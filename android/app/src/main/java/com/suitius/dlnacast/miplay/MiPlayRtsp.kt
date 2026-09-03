package com.suitius.dlnacast.miplay

import android.os.SystemClock
import java.io.BufferedInputStream
import java.io.BufferedOutputStream
import java.io.ByteArrayOutputStream
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.net.Socket
import java.nio.ByteBuffer
import java.nio.ByteOrder
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import java.util.TimeZone
import kotlin.random.Random
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.isActive
import kotlinx.coroutines.withContext

internal data class RtspMessage(
    val startLine: String,
    val headers: Map<String, String>,
    val body: ByteArray,
) {
    fun header(name: String): String? = headers.entries
        .firstOrNull { it.key.equals(name, ignoreCase = true) }
        ?.value

    val cseq: Int?
        get() = header("CSeq")?.toIntOrNull()
}

internal class MiPlayRtspServer(
    private val socket: Socket,
    private val sourceAddress: InetAddress,
    private val timerPort: Int,
    private val playbackDelayMicroseconds: Int,
    private val sharedTimeOffsetProvider: (suspend () -> Long)? = null,
    private val report: (String) -> Unit,
) : AutoCloseable {
    private val sessionId = Random.nextInt(100_000_000, 1_000_000_000).toString()
    private val input = BufferedInputStream(socket.getInputStream(), 64 * 1024)
    private val output = BufferedOutputStream(socket.getOutputStream(), 64 * 1024)

    suspend fun run(onPlay: () -> Unit, onReady: (Long) -> Unit) = withContext(Dispatchers.IO) {
        socket.tcpNoDelay = true
        socket.soTimeout = 10_000
        send(optionsRequest())
        var sourceOptionsAcknowledged = false
        var receiverOptionsObserved = false
        while (!sourceOptionsAcknowledged || !receiverOptionsObserved) {
            val message = readRtspMessage(input)
            when {
                message.startLine == "RTSP/1.0 200 OK" && message.cseq == 1 -> {
                    check(!sourceOptionsAcknowledged) { "Duplicate source OPTIONS acknowledgement" }
                    sourceOptionsAcknowledged = true
                }
                message.startLine == "OPTIONS * RTSP/1.0" && message.cseq == 1 -> {
                    check(!receiverOptionsObserved) { "Duplicate receiver OPTIONS request" }
                    check(message.header("Require") == "org.wfa.wfd1.0") { "Receiver OPTIONS is not WFD" }
                    receiverOptionsObserved = true
                    send(optionsResponse(1))
                }
                else -> error("Unexpected RTSP OPTIONS exchange: ${message.startLine} CSeq=${message.cseq}")
            }
        }

        send(capabilityQuery())
        val capabilities = readRtspMessage(input)
        checkOk(capabilities, 2, "capabilities")
        val capabilityBody = capabilities.body.toString(Charsets.US_ASCII)
        check(capabilityBody.contains("wfd_audio_codecs: AAC 00000001 00")) {
            "Speaker does not advertise the validated AAC WFD profile"
        }

        send(selectedParameters())
        checkOk(readRtspMessage(input), 3, "selected parameters")
        send(setupTrigger())
        checkOk(readRtspMessage(input), 4, "SETUP trigger")

        val setup = readRtspMessage(input)
        check(setup.startLine.startsWith("SETUP ") && setup.cseq == 2) {
            "Expected receiver SETUP CSeq 2, got ${setup.startLine} CSeq=${setup.cseq}"
        }
        val transport = setup.header("Transport")
            ?: error("Receiver SETUP omitted Transport")
        check(transport.contains("RTP/AVP/TCP;interleaved=0-1")) {
            "Receiver requested an unsupported RTSP transport: $transport"
        }
        send(setupResponse(transport))

        val play = readRtspMessage(input)
        check(play.startLine.startsWith("PLAY ") && play.cseq == 3) {
            "Expected receiver PLAY CSeq 3, got ${play.startLine} CSeq=${play.cseq}"
        }
        report("MiPlay RTSP 已收到 PLAY")
        val timeOffsetUs = sharedTimeOffsetProvider?.invoke()
            ?: (monotonicMicroseconds() + playbackDelayMicroseconds)
        send(playResponse())
        onPlay()
        send(timeOffset(timeOffsetUs, 5))
        checkOk(readRtspMessage(input), 5, "TIME_OFFSET")
        report("MiPlay RTSP 与时钟同步已就绪")
        onReady(timeOffsetUs)

        socket.soTimeout = 0
        while (isActive) {
            val message = readRtspMessage(input)
            val cseq = message.cseq ?: error("RTSP runtime request omitted CSeq")
            when {
                message.startLine.startsWith("OPTIONS ") -> send(optionsResponse(cseq, includeSession = true))
                message.startLine.startsWith("GET_PARAMETER ") -> send(genericResponse(cseq))
                message.startLine.startsWith("SET_PARAMETER ") &&
                    message.body.toString(Charsets.US_ASCII).contains("wfd_idr_request") -> send(genericResponse(cseq))
                message.startLine.startsWith("VIDEO_LATENCY ") -> Unit
                message.startLine.startsWith("RTSP/") -> Unit
                else -> error("Unsupported RTSP runtime request: ${message.startLine}")
            }
        }
    }

    private fun optionsRequest(): ByteArray {
        val address = sourceAddress.address
        require(address.size == 4)
        val unsignedAddress = ByteBuffer.wrap(address).order(ByteOrder.BIG_ENDIAN).int.toLong() and 0xffff_ffffL
        return encodeRtsp(
            "OPTIONS * RTSP/1.0",
            listOf(
                "Date" to rtspDate(),
                "Server" to "",
                "CSeq" to "1",
                "Require" to "org.wfa.wfd1.0",
                "wfd_timer_server_port" to "$unsignedAddress:$timerPort",
            ),
            noSpaceHeaders = setOf("wfd_timer_server_port"),
        )
    }

    private fun optionsResponse(cseq: Int, includeSession: Boolean = false): ByteArray {
        val headers = mutableListOf(
            "Date" to rtspDate(),
            "Server" to "",
            "CSeq" to cseq.toString(),
        )
        if (includeSession) headers += "Session" to "$sessionId;timeout=60"
        headers += "Public" to "org.wfa.wfd1.0, SETUP, TEARDOWN, PLAY, PAUSE, GET_PARAMETER, SET_PARAMETER"
        return encodeRtsp("RTSP/1.0 200 OK", headers)
    }

    private fun capabilityQuery(): ByteArray {
        val body = (
            "wfd_video_formats\r\n" +
                "wfd_audio_codecs\r\n" +
                "wfd_client_rtp_ports\r\n" +
                "wfd_tcp_enable\r\n" +
                "wfd_tcp_multi_session_enable\r\n" +
                "wfd_image_enable\r\n" +
                "wfd_dynamic_video_enable\r\n" +
                "wfd_standby_resume_capability\r\n" +
                "wfd_video_bitrate\r\n" +
                "wfd_current_video_info\r\n"
            ).toByteArray(Charsets.US_ASCII)
        return parameterRequest("GET_PARAMETER", 2, body)
    }

    private fun selectedParameters(): ByteArray {
        val body = (
            "wfd_audio_codecs: AAC 00000001 00\r\n" +
                "wfd_client_rtp_ports: RTP/AVP/TCP;interleaved mode=play\r\n" +
                "wfd_presentation_URL: rtsp://${sourceAddress.hostAddress}/wfd1.0/streamid=0 none\r\n"
            ).toByteArray(Charsets.US_ASCII)
        return parameterRequest("SET_PARAMETER", 3, body)
    }

    private fun setupTrigger() = parameterRequest(
        "SET_PARAMETER",
        4,
        "wfd_trigger_method: SETUP\r\n".toByteArray(Charsets.US_ASCII),
    )

    private fun parameterRequest(method: String, cseq: Int, body: ByteArray) = encodeRtsp(
        "$method rtsp://localhost/wfd1.0 RTSP/1.0",
        listOf(
            "Date" to rtspDate(),
            "Server" to "",
            "CSeq" to cseq.toString(),
            "Content-Type" to "text/parameters",
            "Content-Length" to body.size.toString(),
        ),
        body,
    )

    private fun setupResponse(transport: String) = encodeRtsp(
        "RTSP/1.0 200 OK",
        listOf(
            "Date" to rtspDate(),
            "Server" to "",
            "CSeq" to "2",
            "Session" to "$sessionId;timeout=60",
            "Transport" to if (transport.endsWith(';')) transport else "$transport;",
        ),
    )

    private fun playResponse() = encodeRtsp(
        "RTSP/1.0 200 OK",
        listOf(
            "Date" to rtspDate(),
            "Server" to "",
            "CSeq" to "3",
            "Session" to "$sessionId;timeout=60",
            "Range" to "npt=now-",
        ),
    )

    private fun timeOffset(value: Long, cseq: Int) = encodeRtsp(
        "TIME_OFFSET rtsp://localhost/wfd1.0 RTSP/1.0",
        listOf(
            "Date" to rtspDate(),
            "Server" to "",
            "CSeq" to cseq.toString(),
            "Content-Type" to "text/parameters",
            "TimeOffset" to value.toString(),
            "Content-Length" to "0",
        ),
        noSpaceHeaders = setOf("TimeOffset"),
    )

    private fun genericResponse(cseq: Int) = encodeRtsp(
        "RTSP/1.0 200 OK",
        listOf(
            "Date" to rtspDate(),
            "Server" to "",
            "CSeq" to cseq.toString(),
            "Session" to "$sessionId;timeout=60",
            "Content-Length" to "0",
        ),
    )

    private fun send(bytes: ByteArray) {
        output.write(bytes)
        output.flush()
    }

    override fun close() {
        runCatching { socket.close() }
    }
}

internal suspend fun runTimerResponder(
    socket: DatagramSocket,
    receiver: InetAddress,
) = withContext(Dispatchers.IO) {
    val buffer = ByteArray(40)
    while (isActive && !socket.isClosed) {
        val packet = DatagramPacket(buffer, buffer.size)
        socket.receive(packet)
        if (packet.address != receiver || packet.length != 40) continue
        val response = packet.data.copyOfRange(packet.offset, packet.offset + 40)
        val receiveTime = monotonicMicroseconds()
        val sendTime = monotonicMicroseconds()
        ByteBuffer.wrap(response).order(ByteOrder.LITTLE_ENDIAN).apply {
            putLong(16, receiveTime)
            putLong(24, sendTime)
        }
        socket.send(DatagramPacket(response, response.size, packet.socketAddress))
    }
}

private fun encodeRtsp(
    startLine: String,
    headers: List<Pair<String, String>>,
    body: ByteArray = ByteArray(0),
    noSpaceHeaders: Set<String> = emptySet(),
): ByteArray {
    val builder = StringBuilder(startLine).append("\r\n")
    headers.forEach { (name, value) ->
        builder.append(name).append(':')
        if (name !in noSpaceHeaders) builder.append(' ')
        builder.append(value).append("\r\n")
    }
    builder.append("\r\n")
    return builder.toString().toByteArray(Charsets.US_ASCII) + body
}

private fun readRtspMessage(input: BufferedInputStream): RtspMessage {
    val headerBytes = ByteArrayOutputStream(1024)
    var matched = 0
    val terminator = byteArrayOf(13, 10, 13, 10)
    while (matched < terminator.size) {
        val value = input.read()
        if (value < 0) error("Receiver closed RTSP")
        headerBytes.write(value)
        matched = if (value.toByte() == terminator[matched]) matched + 1 else if (value == 13) 1 else 0
        check(headerBytes.size() <= 64 * 1024) { "RTSP headers exceeded 64 KiB" }
    }
    val headerText = headerBytes.toByteArray().toString(Charsets.US_ASCII)
    val lines = headerText.removeSuffix("\r\n\r\n").split("\r\n")
    check(lines.isNotEmpty() && lines.first().isNotBlank()) { "RTSP start line is missing" }
    val headers = linkedMapOf<String, String>()
    lines.drop(1).forEach { line ->
        val separator = line.indexOf(':')
        check(separator > 0) { "Malformed RTSP header: $line" }
        headers[line.substring(0, separator)] = line.substring(separator + 1).trimStart()
    }
    val contentLength = headers.entries.firstOrNull { it.key.equals("Content-Length", true) }
        ?.value?.toIntOrNull() ?: 0
    check(contentLength in 0..(4 * 1024 * 1024)) { "RTSP body is too large" }
    return RtspMessage(lines.first(), headers, input.readExactly(contentLength))
}

private fun checkOk(message: RtspMessage, cseq: Int, stage: String) {
    check(message.startLine == "RTSP/1.0 200 OK" && message.cseq == cseq) {
        "RTSP $stage failed: ${message.startLine} CSeq=${message.cseq}"
    }
}

private fun rtspDate(): String = SimpleDateFormat(
    "EEE, dd MMM yyyy HH:mm:ss '+0000'",
    Locale.US,
).apply { timeZone = TimeZone.getTimeZone("GMT") }.format(Date())

internal fun monotonicMicroseconds(): Long = SystemClock.elapsedRealtimeNanos() / 1_000L
