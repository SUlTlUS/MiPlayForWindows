package com.suitius.dlnacast.streaming

import com.suitius.dlnacast.audio.PcmFrameBuffer
import java.io.BufferedInputStream
import java.net.DatagramSocket
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.ServerSocket
import java.net.Socket
import java.net.SocketException
import java.util.UUID
import java.util.concurrent.CopyOnWriteArrayList
import java.util.concurrent.atomic.AtomicBoolean
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.cancel
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.withTimeoutOrNull

class PcmWaveServer private constructor(
    private val rendererAddress: InetAddress,
    private val frames: PcmFrameBuffer,
    private val listener: ServerSocket,
    private val token: String,
    val streamUri: String,
) : AutoCloseable {
    private val scope = CoroutineScope(Job() + Dispatchers.IO)
    private val sockets = CopyOnWriteArrayList<Socket>()
    private val clientConnected = CompletableDeferred<Unit>()
    private val closed = AtomicBoolean(false)

    fun start() {
        scope.launch {
            while (isActive && !closed.get()) {
                try {
                    val socket = listener.accept()
                    sockets += socket
                    launch { handleClient(socket) }
                } catch (exception: SocketException) {
                    if (!closed.get()) throw exception
                }
            }
        }
    }

    suspend fun waitForClient(timeoutMillis: Long = 5_000): Boolean =
        withTimeoutOrNull(timeoutMillis) { clientConnected.await(); true } ?: false

    private fun handleClient(socket: Socket) {
        socket.use { client ->
            try {
                client.tcpNoDelay = true
                if (client.inetAddress != rendererAddress) {
                    writeResponse(client, "403 Forbidden")
                    return
                }
                val request = readHeaders(client) ?: return
                val parts = request.lineSequence().firstOrNull()?.split(' ') ?: return
                val method = parts.getOrNull(0)?.uppercase()
                val path = parts.getOrNull(1).orEmpty()
                if (method != "GET" && method != "HEAD") {
                    writeResponse(client, "405 Method Not Allowed")
                    return
                }
                if (!path.contains(token)) {
                    writeResponse(client, "404 Not Found")
                    return
                }
                val output = client.getOutputStream().buffered(16 * 1024)
                output.write(
                    buildString {
                        append("HTTP/1.1 200 OK\r\n")
                        append("Content-Type: audio/wav\r\n")
                        append("Connection: close\r\n")
                        append("transferMode.dlna.org: Streaming\r\n")
                        append("contentFeatures.dlna.org: DLNA.ORG_OP=00;DLNA.ORG_CI=0\r\n")
                        append("Cache-Control: no-store, no-cache\r\n\r\n")
                    }.toByteArray(Charsets.US_ASCII),
                )
                output.flush()
                if (method == "HEAD") return

                clientConnected.complete(Unit)
                frames.trimToLatest()
                output.write(PcmWaveHeader.createIndefinite())
                while (!closed.get() && !client.isClosed) {
                    output.write(frames.readFrameOrSilence())
                    output.flush()
                }
            } catch (_: Exception) {
                // Renderers commonly close and reopen a live stream while negotiating.
            } finally {
                sockets -= socket
            }
        }
    }

    private fun readHeaders(socket: Socket): String? {
        socket.soTimeout = 3_000
        val input = BufferedInputStream(socket.getInputStream())
        val collected = ArrayList<Byte>(1_024)
        while (collected.size < 16 * 1024) {
            val value = input.read()
            if (value < 0) return null
            collected += value.toByte()
            val size = collected.size
            if (size >= 4 && collected[size - 4] == 13.toByte() && collected[size - 3] == 10.toByte() &&
                collected[size - 2] == 13.toByte() && collected[size - 1] == 10.toByte()
            ) {
                return collected.toByteArray().toString(Charsets.US_ASCII)
            }
        }
        return null
    }

    private fun writeResponse(socket: Socket, status: String) {
        socket.getOutputStream().write(
            "HTTP/1.1 $status\r\nConnection: close\r\nContent-Length: 0\r\n\r\n"
                .toByteArray(Charsets.US_ASCII),
        )
    }

    override fun close() {
        if (!closed.compareAndSet(false, true)) return
        runCatching { listener.close() }
        sockets.forEach { runCatching { it.close() } }
        sockets.clear()
        scope.cancel()
    }

    companion object {
        fun create(rendererHost: String, frames: PcmFrameBuffer): PcmWaveServer {
            val rendererAddress = InetAddress.getByName(rendererHost)
            val localAddress = DatagramSocket().use { socket ->
                socket.connect(InetSocketAddress(rendererAddress, 9))
                socket.localAddress
            }
            val listener = bindFirstAvailable(localAddress)
            val token = UUID.randomUUID().toString().replace("-", "")
            val host = if (localAddress.hostAddress?.contains(':') == true) "[${localAddress.hostAddress}]" else localAddress.hostAddress
            val uri = "http://$host:${listener.localPort}/stream/$token.wav"
            return PcmWaveServer(rendererAddress, frames, listener, token, uri)
        }

        private fun bindFirstAvailable(address: InetAddress): ServerSocket {
            for (port in 49_555..49_565) {
                try {
                    return ServerSocket().apply {
                        reuseAddress = true
                        bind(InetSocketAddress(address, port), 4)
                    }
                } catch (_: Exception) {
                    // Try the next fixed port so firewall and diagnostics stay predictable.
                }
            }
            error("Ports 49555-49565 are unavailable")
        }
    }
}
