package com.suitius.dlnacast.miplay

import android.content.Context
import android.media.projection.MediaProjection
import android.util.Log
import com.suitius.dlnacast.audio.AudioChannelRoute
import com.suitius.dlnacast.audio.PcmFrameBuffer
import com.suitius.dlnacast.audio.PcmAudioCapture
import com.suitius.dlnacast.audio.PlaybackAudioCapture
import com.suitius.dlnacast.audio.RootPlaybackAudioCapture
import com.suitius.dlnacast.audio.RoutedPcmOutput
import com.suitius.dlnacast.audio.applyToStereoPcm16
import com.suitius.dlnacast.model.DlnaRenderer
import com.suitius.dlnacast.service.CastSession
import com.suitius.dlnacast.service.ReceiverControlState
import java.io.BufferedOutputStream
import java.net.DatagramSocket
import java.net.ServerSocket
import java.net.Socket
import java.net.SocketTimeoutException
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicLong
import java.util.concurrent.CopyOnWriteArrayList
import java.util.concurrent.atomic.AtomicInteger
import java.util.concurrent.locks.LockSupport
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.async
import kotlinx.coroutines.awaitAll
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.withTimeout

class MiPlayCastSession(
    context: Context,
    private val renderers: List<DlnaRenderer>,
    projection: MediaProjection?,
    private val onProjectionStopped: () -> Unit,
    matchingUid: Int? = null,
    private val channelRoutes: List<AudioChannelRoute> = List(renderers.size) { AudioChannelRoute.Stereo },
    private val playbackDelayMicroseconds: Int = 1_000_000,
    rootCapture: Boolean = false,
    private val report: (String) -> Unit,
) : CastSession {
    init {
        require(renderers.isNotEmpty()) { "至少需要选择一台 MiPlay 音箱" }
        require(renderers.size <= MAX_RECEIVERS) { "MiPlay 音箱数量超过协议组上限" }
        require(channelRoutes.size == renderers.size) { "每台 MiPlay 音箱都必须有对应的声道路由" }
        require(playbackDelayMicroseconds in 0..2_000_000) { "MiPlay 播放延迟超出 0 到 2000 ms" }
    }

    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private val sourceFrames = PcmFrameBuffer(
        MiPlayAudioPipeline.PCM_BUFFER_FRAMES, PcmFrameBuffer.MIPLAY_FRAME_BYTES,
    )
    private val buffersByRoute = channelRoutes.distinct().associateWith {
        PcmFrameBuffer(MiPlayAudioPipeline.PCM_BUFFER_FRAMES,
            PcmFrameBuffer.MIPLAY_FRAME_BYTES, preserveFrameSequence = true)
    }
    private val diagnosticFrames = sourceFrames
    private val capture: PcmAudioCapture = if (rootCapture) {
        RootPlaybackAudioCapture(
            context = context,
            outputs = listOf(RoutedPcmOutput(sourceFrames, AudioChannelRoute.Stereo)),
        )
    } else {
        PlaybackAudioCapture(
            context = context,
            projection = requireNotNull(projection) { "MediaProjection capture requires a projection token" },
            outputs = listOf(RoutedPcmOutput(sourceFrames, AudioChannelRoute.Stereo)),
            onProjectionStopped = onProjectionStopped,
            matchingUid = matchingUid,
        )
    }
    private val closed = AtomicBoolean(false)
    private val accessUnits = AtomicLong(0)
    private val rtpFrames = AtomicLong(0)
    private val wireBytes = AtomicLong(0)
    private val lastAacBytes = AtomicLong(0)
    private val maximumSendSkewMicroseconds = AtomicLong(0)
    private val receivers = CopyOnWriteArrayList<ReceiverConnection>()
    private val sharedTimeOffset = MiPlaySharedTimeOffsetState()
    private val sharedMedia = MiPlaySharedMediaBroadcaster(MAX_RECEIVERS)
    private val sharedSendCompletion = MiPlaySharedSendCompletionCoordinator(MAX_RECEIVERS)
    private val outputTimeline = MiPlaySharedOutputTimeline()
    private val firstMedia = CompletableDeferred<Unit>()
    @Volatile private var failure: String? = null
    private val encoders = mutableMapOf<AudioChannelRoute, MiPlayAacEncoder>()
    private var mediaJob: Job? = null
    private var pcmJob: Job? = null

    override val bufferedMilliseconds: Int get() = buffersByRoute.values.maxOf { it.bufferedMilliseconds }
    override val overruns: Long get() = sourceFrames.overruns
    override val supportsPlaybackControl: Boolean get() = true
    override val supportsCaptureSwitch: Boolean get() = capture.supportsUidFilter
    override val receiverPaused: Boolean
        get() = receivers.isNotEmpty() && receivers.all { it.control.receiverPaused }
    override val receiverVolume: Int? get() = receivers.firstOrNull()?.control?.receiverVolume
    override val receiverControls: List<ReceiverControlState>
        get() = receivers.map { receiver ->
            ReceiverControlState(
                receiver.renderer.name,
                receiver.control.receiverPaused,
                receiver.control.receiverVolume,
                receiver.renderer.id,
                receiver.renderer.host,
            )
        }
    override val failureMessage: String? get() = failure ?: capture.failureMessage

    override val streamDetail: String
        get() {
            val states = receivers.joinToString(" · ") { "${it.renderer.name}: ${it.receiverState}" }
            return "MiPlay ${receivers.size} 台 · AAC ${lastAacBytes.get()} B/AU · AU ${accessUnits.get()} · " +
                "RTP ${rtpFrames.get()} · ${wireBytes.get()} B · 同步偏差≤${maximumSendSkewMicroseconds.get()} µs · " +
                "PCM nonZero ${diagnosticFrames.nonZeroSampleCount}/${diagnosticFrames.sampleCount} · " +
                "peak ${"%.4f".format(diagnosticFrames.peakNormalized)} · " +
                "PCM drop ${sourceFrames.overruns}/empty ${sourceFrames.underruns} · $states"
        }

    override suspend fun start() {
        check(!closed.get()) { "MiPlay session is closed" }
        phaseReport("启动 Android 系统音频捕获与共享 AAC 编码器")
        capture.start()
        buffersByRoute.forEach { (route, buffer) -> encoders[route] = MiPlayAacEncoder(buffer) }
        pcmJob = scope.launch {
            runCatching { fanOutPcm() }.onFailure {
                if (!closed.get()) markFailure("PCM 共享供帧", it)
            }
        }

        val startupMedia = CompletableDeferred<ArrayDeque<Map<AudioChannelRoute, EncodedAccessUnit>>>()
        val timeOffsetBarrier = TimeOffsetBarrier(renderers.size, playbackDelayMicroseconds) {
            // Windows fills a 14-AU runway before selecting TIME_OFFSET and the send origin.
            startupMedia.complete(MiPlayAudioPipeline.prewarm { readSharedAccessUnits() })
        }
        renderers.forEachIndexed { index, renderer ->
            receivers += ReceiverConnection(
                renderer,
                channelRoutes[index],
                timeOffsetProvider = {
                    val value = timeOffsetBarrier.await()
                    sharedTimeOffset.initialize(value)
                    value
                },
            )
        }
        coroutineScope {
            receivers.mapIndexed { index, receiver ->
                async(Dispatchers.IO) {
                    phaseReport("并行连接第 ${index + 1}/${renderers.size} 台音箱：${receiver.renderer.name}")
                    receiver.start()
                }
            }.awaitAll()
        }

        phaseReport("${receivers.size} 台 MiPlay 媒体链路已就绪，正在共享同一 AAC 时间线")
        val runway = startupMedia.await()
        mediaJob = scope.launch {
            runCatching { sendMedia(runway) }
                .onFailure { throwable ->
                    if (!closed.get()) {
                        markFailure("AAC 共享媒体发送", throwable)
                        firstMedia.completeExceptionally(throwable)
                    }
                }
        }
        withTimeout(10_000) { firstMedia.await() }
        receivers.forEach { it.receiverState = "等待 first-audiopcm/state" }
        phaseReport("首个 MiPlay AAC 访问单元已同步发送到 ${receivers.size} 台音箱")
    }

    override suspend fun pauseReceiver() {
        check(receivers.isNotEmpty()) { "MiPlay 控制连接尚未就绪" }
        coroutineScope {
            receivers.map { receiver ->
                async(Dispatchers.IO) {
                    receiver.control.pauseReceiver()
                    receiver.receiverState = "暂停请求已发送"
                }
            }.awaitAll()
        }
    }

    override suspend fun resumeReceiver() {
        check(receivers.isNotEmpty()) { "MiPlay 控制连接尚未就绪" }
        coroutineScope {
            receivers.map { receiver -> async(Dispatchers.IO) { receiver.control.resumeReceiver() } }.awaitAll()
        }
        receivers.forEach { it.receiverState = "继续请求已发送" }
    }

    override suspend fun setReceiverVolume(volume: Int, receiverIndex: Int?) {
        check(receivers.isNotEmpty()) { "MiPlay 控制连接尚未就绪" }
        val targets = receiverIndex?.let { index ->
            listOf(receivers.getOrNull(index) ?: error("找不到第 ${index + 1} 台 MiPlay 音箱"))
        } ?: receivers
        coroutineScope {
            targets.map { receiver ->
                async(Dispatchers.IO) {
                    receiver.control.setReceiverVolume(volume)
                    receiver.receiverState = "音量 $volume"
                }
            }.awaitAll()
        }
    }

    suspend fun addReceiver(renderer: DlnaRenderer) {
        check(!closed.get()) { "MiPlay session is closed" }
        if (receivers.any { it.renderer.id == renderer.id }) return
        check(receivers.size < MAX_RECEIVERS) { "MiPlay 音箱数量超过协议组上限" }
        check(firstMedia.isCompleted) { "MiPlay 媒体时间线尚未开始，不能动态加入" }
        val route = receivers.firstOrNull()?.channelRoute
            ?: channelRoutes.firstOrNull()
            ?: AudioChannelRoute.Stereo
        check(buffersByRoute.containsKey(route)) { "当前音频会话没有可供新音箱使用的声道路由" }
        val receiver = ReceiverConnection(
            renderer,
            route,
            timeOffsetProvider = { sharedTimeOffset.current() },
        )
        phaseReport("正在加入第 ${receivers.size + 1} 台音箱：${renderer.name}")
        try {
            receiver.start()
            receivers += receiver
            phaseReport("${renderer.name} 已加入当前 MiPlay 会话")
        } catch (error: Throwable) {
            receiver.close()
            throw error
        }
    }

    suspend fun removeReceiver(rendererId: String) {
        val receiver = receivers.firstOrNull { it.renderer.id == rendererId } ?: return
        check(receivers.size > 1) { "最后一台音箱应通过停止投送断开" }
        receivers.remove(receiver)
        receiver.close()
        phaseReport("${receiver.renderer.name} 已退出当前 MiPlay 会话")
    }

    override suspend fun setCaptureUid(uid: Int?) {
        capture.setMatchingUid(uid)
        phaseReport(if (uid == null) "已切换为全部可捕获音频" else "已切换到应用 UID $uid")
    }

    private fun fanOutPcm() {
        val cadence = MiPlayPcmCadence()
        while (!closed.get() && scope.isActive) {
            waitUntil(cadence.nextDeadline(System.nanoTime()))
            if (closed.get()) return
            // One capture clock and one underrun decision for both left/right routes.
            val frame = sourceFrames.pollFrameOrSilence()
            buffersByRoute.forEach { (route, buffer) -> buffer.offer(route.applyToStereoPcm16(frame)) }
        }
    }

    private fun readSharedAccessUnits(): Map<AudioChannelRoute, EncodedAccessUnit> =
        channelRoutes.distinct().associateWith { route -> encoders.getValue(route).nextAccessUnit() }

    private suspend fun sendMedia(runway: ArrayDeque<Map<AudioChannelRoute, EncodedAccessUnit>>) {
        check(encoders.size == buffersByRoute.size) { "AAC encoders are not ready" }
        val timelinePacketizers = channelRoutes.distinct()
            .associateWith { MiPlayMediaPacketizer() }
        val startedAt = System.nanoTime()
        var sent = 0L
        while (!closed.get() && scope.isActive) {
            val accessUnitsByRoute = if (runway.isNotEmpty()) runway.removeFirst() else readSharedAccessUnits()
            val sharedPresentationTimeUs = monotonicMicroseconds()
            val outputTimestamp = outputTimeline.getTimestamp(sent, sharedPresentationTimeUs)
            val packetizerSnapshots = accessUnitsByRoute.mapValues { (route, accessUnit) ->
                val packetizer = timelinePacketizers.getValue(route)
                val snapshot = packetizer.snapshot()
                packetizer.packetize(accessUnit.bytes, sharedPresentationTimeUs, outputTimestamp)
                snapshot
            }
            accessUnitsByRoute.values.firstOrNull()?.let { lastAacBytes.set(it.bytes.size.toLong()) }
            val frame = sharedMedia.publish(accessUnitsByRoute, packetizerSnapshots, outputTimestamp)
            val completion = sharedSendCompletion.await(frame.accessUnitIndex)
            maximumSendSkewMicroseconds.accumulateAndGet(completion.skewMicroseconds, ::maxOf)
            outputTimeline.recoverAfterSend(
                frame.accessUnitIndex,
                completion.maximumCompletedMicroseconds,
            )
            accessUnits.incrementAndGet()

            sent++
            val dueNanos = (MiPlayStartupPacing.dueAfterMilliseconds(sent) * 1_000_000.0).toLong()
            waitUntil(startedAt + dueNanos)
        }
    }

    private fun sendReceiverMedia(receiver: ReceiverConnection) {
        val subscription = receiver.mediaSubscription
            ?: error("MiPlay media subscription is not ready")
        val packetizer = MiPlayMediaPacketizer(subscription.initialPacketizerSnapshot)
        while (!closed.get() && scope.isActive) {
            val frame = subscription.take() ?: return
            val accessUnit = frame.routeAccessUnits[receiver.channelRoute]
                ?: error("MiPlay shared frame is missing ${receiver.channelRoute}")
            val packetized = packetizer.packetize(
                accessUnit.bytes,
                accessUnit.presentationTimeUs,
                frame.outputTimestamp,
            )
            packetized.wireFrames.forEach(receiver.audioOutput::write)
            receiver.audioOutput.flush()
            rtpFrames.addAndGet(packetized.wireFrames.size.toLong())
            wireBytes.addAndGet(packetized.wireFrames.sumOf { it.size.toLong() })
            sharedSendCompletion.complete(
                subscription.participantId,
                frame.accessUnitIndex,
                monotonicMicroseconds(),
            )
            if (frame.accessUnitIndex == 0L) firstMedia.complete(Unit)
        }
    }

    private fun waitUntil(deadlineNanos: Long) {
        while (!closed.get()) {
            val remaining = deadlineNanos - System.nanoTime()
            if (remaining <= 0L) return
            if (remaining > 2_000_000L) {
                Thread.sleep((remaining / 1_000_000L - 1L).coerceAtLeast(1L))
            } else {
                LockSupport.parkNanos(remaining)
            }
        }
    }

    private fun phaseReport(message: String) {
        Log.i(LOG_TAG, message)
        report(message)
    }

    private fun markFailure(stage: String, throwable: Throwable) {
        if (closed.get()) return
        val detail = throwable.message ?: throwable.javaClass.simpleName
        failure = "$stage 失败：$detail"
        Log.e(LOG_TAG, failure, throwable)
        report(failure!!)
    }

    override fun close() {
        if (!closed.compareAndSet(false, true)) return
        receivers.forEach(ReceiverConnection::close)
        receivers.clear()
        scope.cancel()
        sourceFrames.close()
        buffersByRoute.values.forEach(PcmFrameBuffer::close)
        encoders.values.forEach { encoder -> runCatching { encoder.close() } }
        encoders.clear()
        runCatching { capture.close() }
    }

    private inner class ReceiverConnection(
        val renderer: DlnaRenderer,
        val channelRoute: AudioChannelRoute,
        private val timeOffsetProvider: suspend () -> Long,
    ) : AutoCloseable {
        lateinit var control: MiPlayControlConnection
            private set
        lateinit var audioOutput: BufferedOutputStream
            private set
        @Volatile var receiverState = "等待音箱"

        private var reverseListener: ServerSocket? = null
        private var timerSocket: DatagramSocket? = null
        private var rtspSocket: Socket? = null
        private var auxiliarySocket: Socket? = null
        private var audioSocket: Socket? = null
        private var rtspServer: MiPlayRtspServer? = null
        private var timerJob: Job? = null
        private var rtspJob: Job? = null
        var mediaSubscription: MiPlaySharedMediaSubscription? = null
            private set
        private var mediaSenderJob: Job? = null

        suspend fun start() {
            val target = java.net.InetAddress.getByName(renderer.host)
            reportReceiver("连接 ${renderer.host}:8899")
            control = MiPlayControlConnection(target, ::reportReceiver)
            control.connectAndBootstrap()
            control.preparePlayback()

            val localAddress = control.localAddress
            val listener = ServerSocket(0, 3, localAddress).apply { soTimeout = 10_000 }
            reverseListener = listener
            val timer = DatagramSocket(0, localAddress)
            timerSocket = timer
            timerJob = scope.launch {
                runCatching { runTimerResponder(timer, target) }
                    .onFailure { if (!closed.get()) markFailure("${renderer.name} UDP 时钟同步", it) }
            }

            control.sendOpen(localAddress, listener.localPort)
            val rtspClient = accept(listener, "RTSP")
            rtspSocket = rtspClient
            val rtspReady = CompletableDeferred<Long>()
            val server = MiPlayRtspServer(
                rtspClient,
                localAddress,
                timer.localPort,
                playbackDelayMicroseconds,
                timeOffsetProvider,
                ::reportReceiver,
            )
            rtspServer = server
            rtspJob = scope.launch {
                runCatching {
                    server.run(
                        onPlay = control::sendMediaInfo,
                        onReady = { rtspReady.complete(it) },
                    )
                }.onFailure { throwable ->
                    if (!closed.get()) {
                        markFailure("${renderer.name} RTSP 控制", throwable)
                        rtspReady.completeExceptionally(throwable)
                    }
                }
            }

            auxiliarySocket = accept(listener, "辅助")
            val audioClient = accept(listener, "音频").apply { tcpNoDelay = true }
            audioSocket = audioClient
            audioOutput = BufferedOutputStream(audioClient.getOutputStream(), 16 * 1024)
            listener.close()
            reverseListener = null
            withTimeout(10_000) { rtspReady.await() }
            control.startRuntime(
                scope,
                onReceiverState = { state -> receiverState = state },
                onFailure = { error ->
                    if (!closed.get()) markFailure("${renderer.name} 运行期控制", error)
                },
            )
            mediaSubscription = sharedMedia.register(channelRoute)
            sharedSendCompletion.register(
                mediaSubscription!!.participantId,
                mediaSubscription!!.liveStartAccessUnitIndex,
            )
            mediaSenderJob = scope.launch {
                runCatching { sendReceiverMedia(this@ReceiverConnection) }
                    .onFailure { error ->
                        if (!closed.get()) {
                            markFailure("${renderer.name} AAC 媒体发送", error)
                            close()
                        }
                    }
            }
            receiverState = "媒体链路已就绪"
        }

        private fun accept(listener: ServerSocket, label: String): Socket = try {
            listener.accept()
        } catch (timeout: SocketTimeoutException) {
            throw IllegalStateException("${renderer.name} 在 10 秒内没有建立 MiPlay $label 反向连接", timeout)
        }

        private fun reportReceiver(message: String) = phaseReport("${renderer.name}：$message")

        override fun close() {
            if (::control.isInitialized) runCatching { control.close() }
            runCatching { reverseListener?.close() }
            runCatching { timerSocket?.close() }
            runCatching { rtspServer?.close() }
            runCatching { rtspSocket?.close() }
            runCatching { auxiliarySocket?.close() }
            runCatching { audioSocket?.close() }
            mediaSubscription?.let { subscription ->
                sharedSendCompletion.unregister(subscription.participantId)
                subscription.close()
            }
            mediaSubscription = null
            mediaSenderJob?.cancel()
            timerJob?.cancel()
            rtspJob?.cancel()
        }
    }

    private companion object {
        const val LOG_TAG = "DLNACast.MiPlay"
        const val MAX_RECEIVERS = 63
    }
}

private class TimeOffsetBarrier(
    private val participantCount: Int,
    private val playbackDelayMicroseconds: Int,
    private val beforeReady: suspend () -> Unit = {},
) {
    private val arrivals = AtomicInteger(0)
    private val sharedValue = CompletableDeferred<Long>()

    suspend fun await(): Long {
        val arrived = arrivals.incrementAndGet()
        check(arrived <= participantCount) { "MiPlay TimeOffset 屏障收到重复参与者" }
        if (arrived == participantCount) {
            try {
                beforeReady()
                sharedValue.complete(monotonicMicroseconds() + playbackDelayMicroseconds)
            } catch (error: Throwable) {
                sharedValue.completeExceptionally(error)
            }
        }
        return sharedValue.await()
    }
}
