package com.suitius.dlnacast.audio

import android.content.Context
import android.util.Log
import java.io.BufferedReader
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.ServerSocket
import java.net.Socket
import java.net.SocketTimeoutException
import java.util.concurrent.atomic.AtomicBoolean
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.cancel
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch

class RootPlaybackAudioCapture(
    context: Context,
    private val outputs: List<RoutedPcmOutput>,
) : PcmAudioCapture {
    init {
        require(outputs.isNotEmpty()) { "至少需要一个 PCM 捕获输出" }
    }

    private val apkPath = context.applicationInfo.sourceDir
    private val scope = CoroutineScope(Job() + Dispatchers.IO)
    private val closed = AtomicBoolean(false)
    private var helper: Process? = null
    private var helperLogJob: Job? = null
    private var readJob: Job? = null
    private var server: ServerSocket? = null
    private var socket: Socket? = null
    @Volatile override var failureMessage: String? = null
        private set

    override val supportsUidFilter: Boolean = false

    override fun start() {
        check(helper == null) { "Root audio capture is already running" }
        check(!closed.get()) { "Root audio capture is closed" }

        val listener = ServerSocket().apply {
            reuseAddress = true
            bind(InetSocketAddress(InetAddress.getByName(ROOT_LOOPBACK_HOST), 0), 1)
            soTimeout = ROOT_CONNECT_TIMEOUT_MILLISECONDS
        }
        server = listener
        val command = buildString {
            append("if [ \"\$(id -u)\" != \"0\" ]; then echo root_denied; exit 126; fi; ")
            append("export CLASSPATH=")
            append(shellQuote(apkPath))
            append("; exec /system/bin/app_process /system/bin ")
            append(ROOT_MAIN_CLASS)
            append(' ')
            append(ROOT_LOOPBACK_HOST)
            append(' ')
            append(listener.localPort)
        }
        val process = ProcessBuilder("su", "-c", command)
            .redirectErrorStream(true)
            .start()
        helper = process
        val logReader = process.inputStream.bufferedReader()
        helperLogJob = scope.launch {
            try {
                relayHelperLog(logReader)
            } catch (error: Exception) {
                recordUnexpectedFailure("Root 内录助手日志中断", error)
            }
        }

        val accepted = try {
            listener.accept()
        } catch (timeout: SocketTimeoutException) {
            val exit = if (process.isAlive) null else process.exitValue()
            close()
            if (exit == ROOT_DENIED_EXIT_CODE) {
                error("未获得 Root 授权，请在 KernelSU 中允许 MiPlay Cast")
            }
            error("Root 内录助手未能启动${exit?.let { "（退出码 $it）" }.orEmpty()}")
        } finally {
            runCatching { listener.close() }
            server = null
        }
        accepted.tcpNoDelay = true
        socket = accepted
        readJob = scope.launch {
            try {
                readPcm(accepted)
            } catch (error: Exception) {
                recordUnexpectedFailure("Root 内录数据流中断", error)
            }
        }
        Log.i(LOG_TAG, "Root Remote Submix helper connected")
    }

    override suspend fun setMatchingUid(matchingUid: Int?) {
        if (matchingUid != null) {
            Log.w(LOG_TAG, "Root Remote Submix captures the system mix; UID filter $matchingUid is ignored")
        }
    }

    private suspend fun readPcm(activeSocket: Socket) {
        val input = activeSocket.getInputStream()
        val frame = ByteArray(PcmFrameBuffer.FRAME_BYTES)
        while (scope.isActive && !closed.get()) {
            var read = 0
            while (read < frame.size && scope.isActive && !closed.get()) {
                val count = input.read(frame, read, frame.size - read)
                if (count < 0) break
                read += count
            }
            if (read <= 0) break
            val captured = if (read == frame.size) frame.copyOf() else frame.copyOf(read - read % 4)
            if (captured.isNotEmpty()) {
                outputs.forEach { target ->
                    target.buffer.offer(target.route.applyToStereoPcm16(captured))
                }
            }
        }
        if (!closed.get()) error("Root 内录数据流已中断")
    }

    private fun relayHelperLog(reader: BufferedReader) {
        reader.useLines { lines ->
            lines.forEach { line -> Log.i(LOG_TAG, "helper: $line") }
        }
    }

    private fun recordUnexpectedFailure(stage: String, error: Exception) {
        if (closed.get()) return
        val detail = error.message ?: error.javaClass.simpleName
        failureMessage = "$stage：$detail"
        Log.e(LOG_TAG, failureMessage, error)
    }

    override fun close() {
        if (!closed.compareAndSet(false, true)) return
        runCatching { socket?.close() }
        socket = null
        runCatching { server?.close() }
        server = null
        helper?.destroy()
        helper = null
        readJob?.cancel()
        readJob = null
        helperLogJob?.cancel()
        helperLogJob = null
        scope.cancel()
    }

    private fun shellQuote(value: String): String = "'${value.replace("'", "'\\''")}'"

    private companion object {
        const val LOG_TAG = "DLNACast.RootCapture"
        const val ROOT_MAIN_CLASS = "com.suitius.dlnacast.audio.RootAudioCaptureMain"
        const val ROOT_LOOPBACK_HOST = "127.0.0.1"
        const val ROOT_CONNECT_TIMEOUT_MILLISECONDS = 10_000
        const val ROOT_DENIED_EXIT_CODE = 126
    }
}
