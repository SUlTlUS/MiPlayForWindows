package com.suitius.dlnacast.miplay

import java.io.BufferedInputStream
import java.io.BufferedOutputStream
import java.io.EOFException
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.Socket
import java.net.SocketException
import java.nio.ByteBuffer
import java.nio.ByteOrder
import java.nio.charset.StandardCharsets
import java.security.MessageDigest
import java.util.concurrent.TimeoutException
import java.util.concurrent.atomic.AtomicLong
import javax.crypto.Mac
import javax.crypto.spec.SecretKeySpec
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withTimeout

internal data class MiPlayCommandFrame(
    val command: Int,
    val sequence: Int,
    val payload: ByteArray,
)

private data class PendingRuntimeAcknowledgement(
    val command: Int,
    val acknowledgementCommand: Int,
    val sequence: Int,
    val payload: ByteArray,
    val completion: CompletableDeferred<MiPlayCommandFrame>,
)

private fun runtimeAcknowledgementMatches(
    pending: PendingRuntimeAcknowledgement,
    frame: MiPlayCommandFrame,
): Boolean = runtimeAcknowledgementMatches(
    pending.acknowledgementCommand,
    pending.sequence,
    pending.payload,
    frame,
)

internal fun runtimeAcknowledgementMatches(
    acknowledgementCommand: Int,
    sequence: Int,
    payload: ByteArray,
    frame: MiPlayCommandFrame,
): Boolean = frame.command == acknowledgementCommand &&
    frame.sequence == sequence &&
    frame.payload.contentEquals(payload)

internal object MiPlayCommands {
    const val OPEN = 0x0000
    const val CLOSE = 0x0002
    const val PAUSE = 0x0004
    const val PAUSE_ACK = 0x0005
    const val RESUME = 0x0006
    const val RESUME_ACK = 0x0007
    const val SET_VOLUME = 0x000c
    const val SET_VOLUME_ACK = 0x000d
    const val HEARTBEAT = 0x001a
    const val HEARTBEAT_ACK = 0x001b
    const val GET_VOLUME = 0x000e
    const val GET_VOLUME_ACK = 0x000f
    const val SET_MEDIA_INFO = 0x0012
    const val SET_MEDIA_INFO_ACK = 0x0013
    const val GET_MEDIA_INFO = 0x0014
    const val GET_STATE = 0x001c
    const val GET_STATE_ACK = 0x001d
    const val GET_DEVICE_INFO = 0x001e
    const val GET_DEVICE_INFO_ACK = 0x001f
    const val NOTIFY = 0x0022
    const val LEGACY_CHALLENGE = 0x0028
    const val LEGACY_CHALLENGE_ACK = 0x0029
    const val GET_MIRROR_MODE = 0x0034
    const val GET_MIRROR_MODE_ACK = 0x0035
    const val NATIVE_VERSION = 0x0036
    const val NATIVE_VERSION_ACK = 0x0037
    const val SET_PLAY_SOURCE = 0x0040
    const val SET_LOCAL_DEVICE_INFO = 0x0058
    const val SET_LOCAL_DEVICE_INFO_ACK = 0x0059
}

internal object MiPlayCommandCodec {
    private const val MAGIC = 0x24
    private const val HEADER_LENGTH = 9
    private const val MAX_PAYLOAD = 4 * 1024 * 1024

    fun encode(command: Int, sequence: Int, payload: ByteArray = ByteArray(0)): ByteArray {
        require(command in 0..0xffff && sequence in 0..0xffff)
        require(payload.size <= MAX_PAYLOAD)
        return ByteBuffer.allocate(HEADER_LENGTH + payload.size)
            .order(ByteOrder.BIG_ENDIAN)
            .put(MAGIC.toByte())
            .putShort(command.toShort())
            .putShort(sequence.toShort())
            .putInt(payload.size)
            .put(payload)
            .array()
    }

    fun read(input: BufferedInputStream): MiPlayCommandFrame {
        val header = input.readExactly(HEADER_LENGTH)
        check(header[0].toInt() and 0xff == MAGIC) { "MiPlay command magic mismatch" }
        val buffer = ByteBuffer.wrap(header).order(ByteOrder.BIG_ENDIAN)
        buffer.get()
        val command = buffer.short.toInt() and 0xffff
        val sequence = buffer.short.toInt() and 0xffff
        val payloadLength = buffer.int
        check(payloadLength in 0..MAX_PAYLOAD) { "MiPlay command payload is too large" }
        return MiPlayCommandFrame(command, sequence, input.readExactly(payloadLength))
    }
}

internal class MiPlayControlConnection(
    private val target: InetAddress,
    private val report: (String) -> Unit,
) : AutoCloseable {
    private val socket = Socket()
    private lateinit var input: BufferedInputStream
    private lateinit var output: BufferedOutputStream
    private val writeLock = Any()
    private var runtimeSequence = 16
    private var runtimeJob: Job? = null
    private val runtimeCommandMutex = Mutex()
    private val pendingLock = Any()
    private var pendingAcknowledgement: PendingRuntimeAcknowledgement? = null
    private val lastHeartbeatActivityNanos = AtomicLong(System.nanoTime())
    @Volatile var receiverPaused: Boolean = false
        private set
    @Volatile var receiverVolume: Int? = null
        private set

    val localAddress: InetAddress
        get() = socket.localAddress

    fun connectAndBootstrap() {
        socket.tcpNoDelay = true
        socket.soTimeout = 10_000
        socket.connect(InetSocketAddress(target, 8899), 5_000)
        input = BufferedInputStream(socket.getInputStream(), 64 * 1024)
        output = BufferedOutputStream(socket.getOutputStream(), 64 * 1024)
        report("MiPlay 8899 已连接，等待安全挑战")

        val challenge = readExpected("安全挑战") { it.command == MiPlayCommands.LEGACY_CHALLENGE }
        require(challenge.payload.size in 12..17 && challenge.payload.all { it in '0'.code.toByte()..'9'.code.toByte() }) {
            "MiPlay legacy challenge is not the validated numeric shape"
        }
        val version = encodeText(MiPlayCommands.NATIVE_VERSION, 0, "1.0.1123012\u0000")
        val challengeAck = MiPlayCommandCodec.encode(
            MiPlayCommands.LEGACY_CHALLENGE_ACK,
            challenge.sequence,
            legacyChallengeResponse(challenge.payload),
        )
        write(version, challengeAck)
        write(MiPlayCommandCodec.encode(MiPlayCommands.GET_DEVICE_INFO, 1))

        readExpected("版本确认") { it.command == MiPlayCommands.NATIVE_VERSION_ACK && it.sequence == 0 }
        write(sourceNameFrame(2))

        waitForPair(
            "初始设备信息",
            first = { it.command == MiPlayCommands.GET_DEVICE_INFO_ACK && it.sequence == 1 },
            second = { it.command == MiPlayCommands.SET_LOCAL_DEVICE_INFO_ACK && it.sequence == 2 },
        )
        write(accountFrame(3))
        write(MiPlayCommandCodec.encode(MiPlayCommands.GET_MIRROR_MODE, 4))

        var accountReady = false
        var mirrorReady = false
        var volumeReady = false
        var stateReady = false
        var statusSent = false
        while (!(accountReady && mirrorReady && volumeReady && stateReady)) {
            val frame = readSkippingNotify("初始状态")
            when {
                frame.command == MiPlayCommands.SET_LOCAL_DEVICE_INFO_ACK && frame.sequence == 3 -> {
                    accountReady = true
                    if (!statusSent) {
                        statusSent = true
                        write(MiPlayCommandCodec.encode(MiPlayCommands.GET_VOLUME, 5))
                        write(MiPlayCommandCodec.encode(MiPlayCommands.GET_MEDIA_INFO, 6))
                        write(MiPlayCommandCodec.encode(MiPlayCommands.GET_STATE, 7))
                    }
                }
                frame.command == MiPlayCommands.GET_MIRROR_MODE_ACK && frame.sequence == 4 -> mirrorReady = true
                frame.command == MiPlayCommands.GET_VOLUME_ACK && frame.sequence == 5 -> {
                    receiverVolume = decodeStatusScalar(frame.payload)?.takeIf { it in 0..100 }
                    volumeReady = true
                }
                frame.command == MiPlayCommands.GET_STATE_ACK && frame.sequence == 7 -> {
                    receiverPaused = decodeStatusScalar(frame.payload) == 3
                    stateReady = true
                }
                else -> error("MiPlay initial status returned unexpected command ${frame.describe()}")
            }
        }
        report("MiPlay 基础初始化完成")
    }

    fun preparePlayback() {
        write(sourceNameFrame(8))
        write(MiPlayCommandCodec.encode(MiPlayCommands.GET_DEVICE_INFO, 9))
        waitForPair(
            "播放设备信息",
            first = { it.command == MiPlayCommands.SET_LOCAL_DEVICE_INFO_ACK && it.sequence == 8 },
            second = { it.command == MiPlayCommands.GET_DEVICE_INFO_ACK && it.sequence == 9 },
        )
        write(accountFrame(10))
        readExpected("播放账号确认") {
            it.command == MiPlayCommands.SET_LOCAL_DEVICE_INFO_ACK && it.sequence == 10
        }
        write(MiPlayCommandCodec.encode(MiPlayCommands.GET_MIRROR_MODE, 11))
        readExpected("播放模式确认") {
            it.command == MiPlayCommands.GET_MIRROR_MODE_ACK && it.sequence == 11
        }
        write(MiPlayCommandCodec.encode(MiPlayCommands.HEARTBEAT, 12))
        readExpected("播放心跳确认") {
            it.command == MiPlayCommands.HEARTBEAT_ACK && it.sequence == 12
        }
        write(
            encodeText(
                MiPlayCommands.SET_PLAY_SOURCE,
                13,
                "{\"ref_channel\":\"controlcenter\",\"ref_function\":\"single_room\",\"ref_content\":\"music_wangyiyun\"}",
            ),
        )
        report("MiPlay 播放控制已就绪")
    }

    fun sendOpen(sourceAddress: InetAddress, reversePort: Int) {
        val payload = "wfd://${sourceAddress.hostAddress}:$reversePort?mirrorMode=1\u0000"
        write(encodeText(MiPlayCommands.OPEN, 14, payload))
        report("已发送 MiPlay Open，等待音箱反向连接")
    }

    fun sendMediaInfo() {
        val payload = "{\"mArtist\":\"Android\",\"mAlbum\":\"\",\"mTitle\":\"System Audio\",\"mDuration\":2147483647,\"id\":\"\",\"mCoverUrl\":\"\",\"status\":0,\"volume\":24,\"mArt\":\"\",\"mSourceName\":\"MI PAD 4/Plus\",\"mDeviceState\":2}"
        write(encodeText(MiPlayCommands.SET_MEDIA_INFO, 15, payload))
        report("MiPlay 媒体信息已发送")
    }

    fun startRuntime(
        scope: CoroutineScope,
        onReceiverState: (String) -> Unit,
        onFailure: (Throwable) -> Unit = {},
    ) {
        check(runtimeJob == null) { "MiPlay runtime control is already running" }
        socket.soTimeout = 0
        lastHeartbeatActivityNanos.set(System.nanoTime())
        runtimeJob = scope.launch(Dispatchers.IO) {
            try {
                coroutineScope {
                    val heartbeatJob = launch {
                        while (isActive) {
                            delay(5_000)
                            writeRuntime(MiPlayCommands.HEARTBEAT)
                        }
                    }
                    val watchdogJob = launch {
                        while (isActive) {
                            delay(HEARTBEAT_CHECK_INTERVAL_MILLISECONDS)
                            if (System.nanoTime() - lastHeartbeatActivityNanos.get() >
                                HEARTBEAT_INACTIVITY_TIMEOUT_NANOS
                            ) {
                                runCatching { socket.close() }
                                throw TimeoutException("MiPlay 超过 15 秒没有收到心跳活动")
                            }
                        }
                    }
                    try {
                        while (isActive) {
                            val frame = MiPlayCommandCodec.read(input)
                            when (frame.command) {
                                MiPlayCommands.HEARTBEAT,
                                MiPlayCommands.HEARTBEAT_ACK -> {
                                    check(frame.payload.isEmpty()) {
                                        "MiPlay 心跳载荷必须为空"
                                    }
                                    lastHeartbeatActivityNanos.set(System.nanoTime())
                                    if (frame.command == MiPlayCommands.HEARTBEAT) {
                                        writeRuntimeWithSequence(
                                            MiPlayCommands.HEARTBEAT_ACK,
                                            frame.sequence,
                                        )
                                    }
                                }
                                MiPlayCommands.NOTIFY -> decodeNotify(frame.payload)?.let { state ->
                                    when (state) {
                                        "state=2" -> receiverPaused = false
                                        "state=3" -> receiverPaused = true
                                    }
                                    onReceiverState(state)
                                }
                                else -> {
                                    val matched = synchronized(pendingLock) {
                                        pendingAcknowledgement?.let { pending ->
                                            if (runtimeAcknowledgementMatches(pending, frame)) {
                                                pendingAcknowledgement = null
                                                pending.completion.complete(frame)
                                                true
                                            } else {
                                                false
                                            }
                                        } ?: false
                                    }
                                    if (!matched) {
                                        report("MiPlay 运行期收到未匹配的 ${frame.describe()}")
                                    }
                                }
                            }
                        }
                    } finally {
                        heartbeatJob.cancel()
                        watchdogJob.cancel()
                    }
                }
            } catch (cancelled: CancellationException) {
                throw cancelled
            } catch (exception: Exception) {
                if (isActive) {
                    synchronized(pendingLock) {
                        pendingAcknowledgement?.completion?.completeExceptionally(exception)
                        pendingAcknowledgement = null
                    }
                    onFailure(exception)
                    report("MiPlay 控制连接中断：${exception.message ?: exception.javaClass.simpleName}")
                }
            }
        }
    }

    suspend fun pauseReceiver() {
        runtimeCommandMutex.withLock {
            writeRuntime(MiPlayCommands.PAUSE)
        }
    }

    suspend fun resumeReceiver() {
        runtimeCommandMutex.withLock {
            writeRuntime(MiPlayCommands.RESUME)
            delay(CAPTURED_RESUME_REPEAT_DELAY_MILLISECONDS)
            writeRuntime(MiPlayCommands.RESUME)
        }
    }

    suspend fun setReceiverVolume(volume: Int) {
        require(volume in 0..100) { "MiPlay 音量必须在 0 到 100 之间" }
        val acknowledgement = sendRuntimeAndAwaitAcknowledgement(
            command = MiPlayCommands.SET_VOLUME,
            acknowledgementCommand = MiPlayCommands.SET_VOLUME_ACK,
            payload = encodeRawVolume(volume),
        )
        receiverVolume = decodeRawVolume(acknowledgement.payload)
            ?: error("MiPlay 音量 ACK 载荷无效")
    }

    private suspend fun sendRuntimeAndAwaitAcknowledgement(
        command: Int,
        acknowledgementCommand: Int,
        payload: ByteArray,
    ): MiPlayCommandFrame = runtimeCommandMutex.withLock {
        val completion = CompletableDeferred<MiPlayCommandFrame>()
        val sequence: Int
        synchronized(writeLock) {
            sequence = runtimeSequence
            runtimeSequence = (runtimeSequence + 1) and 0xffff
            synchronized(pendingLock) {
                check(pendingAcknowledgement == null) {
                    "MiPlay 已有运行期命令等待 ACK"
                }
                pendingAcknowledgement = PendingRuntimeAcknowledgement(
                    command,
                    acknowledgementCommand,
                    sequence,
                    payload.copyOf(),
                    completion,
                )
            }
            output.write(MiPlayCommandCodec.encode(command, sequence, payload))
            output.flush()
        }
        try {
            withTimeout(COMMAND_ACKNOWLEDGEMENT_TIMEOUT_MILLISECONDS) {
                completion.await()
            }
        } finally {
            synchronized(pendingLock) {
                if (pendingAcknowledgement?.completion === completion) {
                    pendingAcknowledgement = null
                }
            }
        }
    }

    private fun writeRuntime(command: Int, payload: ByteArray = ByteArray(0)) {
        synchronized(writeLock) {
            val sequence = runtimeSequence
            runtimeSequence = (runtimeSequence + 1) and 0xffff
            writeRuntimeWithSequenceLocked(command, sequence, payload)
        }
    }

    private fun writeRuntimeWithSequence(command: Int, sequence: Int, payload: ByteArray = ByteArray(0)) {
        synchronized(writeLock) {
            writeRuntimeWithSequenceLocked(command, sequence, payload)
        }
    }

    private fun writeRuntimeWithSequenceLocked(command: Int, sequence: Int, payload: ByteArray) {
        output.write(MiPlayCommandCodec.encode(command, sequence, payload))
        output.flush()
    }

    private fun waitForPair(
        stage: String,
        first: (MiPlayCommandFrame) -> Boolean,
        second: (MiPlayCommandFrame) -> Boolean,
    ) {
        var firstReady = false
        var secondReady = false
        while (!firstReady || !secondReady) {
            val frame = readSkippingNotify(stage)
            when {
                !firstReady && first(frame) -> firstReady = true
                !secondReady && second(frame) -> secondReady = true
                else -> error("MiPlay $stage returned unexpected command ${frame.describe()}")
            }
        }
    }

    private fun readExpected(stage: String, predicate: (MiPlayCommandFrame) -> Boolean): MiPlayCommandFrame {
        while (true) {
            val frame = MiPlayCommandCodec.read(input)
            if (frame.command == MiPlayCommands.NOTIFY) continue
            check(predicate(frame)) { "MiPlay $stage returned unexpected command ${frame.describe()}" }
            return frame
        }
    }

    private fun readSkippingNotify(stage: String): MiPlayCommandFrame {
        while (true) {
            val frame = MiPlayCommandCodec.read(input)
            if (frame.command != MiPlayCommands.NOTIFY) return frame
            report("MiPlay $stage 收到音箱通知")
        }
    }

    private fun sourceNameFrame(sequence: Int) = encodeText(
        MiPlayCommands.SET_LOCAL_DEVICE_INFO,
        sequence,
        "{\"sourceName\":\"MI PAD 4\\/Plus\"}",
    )

    private fun accountFrame(sequence: Int) = encodeText(
        MiPlayCommands.SET_LOCAL_DEVICE_INFO,
        sequence,
        "{\"isSameAccount\":0}",
    )

    private fun encodeText(command: Int, sequence: Int, text: String) =
        MiPlayCommandCodec.encode(command, sequence, text.toByteArray(StandardCharsets.UTF_8))

    private fun write(vararg frames: ByteArray) {
        synchronized(writeLock) {
            frames.forEach(output::write)
            output.flush()
        }
    }

    override fun close() {
        runtimeJob?.cancel()
        runtimeJob = null
        synchronized(pendingLock) {
            pendingAcknowledgement?.completion?.cancel()
            pendingAcknowledgement = null
        }
        if (::output.isInitialized && !socket.isClosed) {
            runCatching {
                writeRuntime(MiPlayCommands.CLOSE)
            }
        }
        runCatching { socket.close() }
    }

    private fun MiPlayCommandFrame.describe() =
        "0x${command.toString(16).padStart(4, '0')} seq=$sequence len=${payload.size}"

    private companion object {
        const val CAPTURED_RESUME_REPEAT_DELAY_MILLISECONDS = 29L
        const val HEARTBEAT_CHECK_INTERVAL_MILLISECONDS = 1_000L
        const val HEARTBEAT_INACTIVITY_TIMEOUT_NANOS = 15_000_000_000L
        const val COMMAND_ACKNOWLEDGEMENT_TIMEOUT_MILLISECONDS = 3_000L
    }
}

internal fun legacyChallengeResponse(challenge: ByteArray): ByteArray {
    val md5 = MessageDigest.getInstance("MD5")
        .digest("0.0.0.0".toByteArray(StandardCharsets.UTF_8))
        .joinToString("") { "%02x".format(it) }
    val mac = Mac.getInstance("HmacSHA1")
    mac.init(SecretKeySpec(md5.toByteArray(StandardCharsets.UTF_8), "HmacSHA1"))
    return mac.doFinal(challenge).joinToString("") { "%02x".format(it) }
        .toByteArray(StandardCharsets.US_ASCII)
}

private fun decodeNotify(payload: ByteArray): String? {
    if (payload.isEmpty()) return null
    val labelLength = payload[0].toInt() and 0xff
    if (labelLength == 0 || payload.size < labelLength + 3) return null
    val label = String(payload, 1, labelLength, StandardCharsets.US_ASCII)
    val typeOffset = 1 + labelLength
    if ((payload[typeOffset].toInt() and 0xff) != 0x03) return label
    val value = payload[typeOffset + 1].toInt() and 0xff
    return "$label=$value"
}

private fun decodeStatusScalar(payload: ByteArray): Int? {
    if (payload.size != 5 || payload[0] != 0.toByte()) return null
    return ByteBuffer.wrap(payload, 1, 4).order(ByteOrder.BIG_ENDIAN).int
}

private fun decodeRawVolume(payload: ByteArray): Int? {
    if (payload.size != 4) return null
    return ByteBuffer.wrap(payload).order(ByteOrder.BIG_ENDIAN).int.takeIf { it in 0..100 }
}

internal fun encodeRawVolume(volume: Int): ByteArray {
    require(volume in 0..100)
    return ByteBuffer.allocate(Int.SIZE_BYTES)
        .order(ByteOrder.BIG_ENDIAN)
        .putInt(volume)
        .array()
}

internal fun BufferedInputStream.readExactly(length: Int): ByteArray {
    val bytes = ByteArray(length)
    var offset = 0
    while (offset < length) {
        val read = read(bytes, offset, length - offset)
        if (read < 0) throw EOFException("MiPlay socket closed while reading $length bytes")
        offset += read
    }
    return bytes
}
