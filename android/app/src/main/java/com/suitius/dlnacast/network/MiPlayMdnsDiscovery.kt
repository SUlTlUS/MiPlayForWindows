package com.suitius.dlnacast.network

import android.content.Context
import android.net.ConnectivityManager
import android.net.Network
import android.net.NetworkCapabilities
import android.util.Log
import com.suitius.dlnacast.model.DlnaRenderer
import java.io.ByteArrayOutputStream
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.Inet4Address
import java.net.InetAddress
import java.net.InetSocketAddress
import java.nio.ByteBuffer
import java.nio.ByteOrder
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.async
import kotlinx.coroutines.awaitAll
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.withContext

class MiPlayMdnsDiscovery(context: Context) {
    private val connectivityManager = context.applicationContext
        .getSystemService(ConnectivityManager::class.java)

    suspend fun discover(timeoutMillis: Long): List<DlnaRenderer> = withContext(Dispatchers.IO) {
        coroutineScope {
            bindings().map { binding ->
                async(Dispatchers.IO) {
                    runCatching { search(binding, timeoutMillis) }
                        .onFailure { error ->
                            Log.w(
                                LOG_TAG,
                                "Skipping mDNS interface ${binding.address.hostAddress}: ${error.message}",
                            )
                        }
                        .getOrDefault(emptyList())
                }
            }.awaitAll().flatten()
        }.distinctBy { it.host }
    }

    private fun search(binding: Binding, timeoutMillis: Long): List<DlnaRenderer> {
        val found = linkedMapOf<String, MiPlayMdnsDevice>()
        DatagramSocket(null).use { socket ->
            socket.reuseAddress = true
            binding.network?.bindSocket(socket)
            socket.bind(InetSocketAddress(binding.address, 0))
            socket.soTimeout = 300
            val destination = InetAddress.getByName("224.0.0.251")
            repeat(2) {
                SERVICE_NAMES.forEach { service ->
                    val query = createQuery(service)
                    socket.send(DatagramPacket(query, query.size, destination, MDNS_PORT))
                }
                if (it == 0) Thread.sleep(160)
            }
            val deadline = System.currentTimeMillis() + timeoutMillis
            while (System.currentTimeMillis() < deadline) {
                try {
                    val buffer = ByteArray(16 * 1024)
                    val response = DatagramPacket(buffer, buffer.size)
                    socket.receive(response)
                    SERVICE_NAMES.forEach { service ->
                        parseMdns(
                            response.data.copyOfRange(response.offset, response.offset + response.length),
                            service,
                            response.address,
                        ).forEach { device ->
                            device.address?.hostAddress?.let { found[it] = device }
                        }
                    }
                } catch (_: java.net.SocketTimeoutException) {
                    // Continue until the overall deadline so both service types can answer.
                }
            }
        }
        return found.values.mapNotNull { device ->
            val host = device.address?.hostAddress ?: return@mapNotNull null
            DlnaRenderer(
                id = "miplay://${device.instanceName}",
                name = device.friendlyName,
                descriptionUrl = "miplay://$host",
                host = host,
                avTransportControlUrl = "",
                renderingControlUrl = null,
                connectionManagerControlUrl = null,
            )
        }
    }

    @Suppress("DEPRECATION")
    private fun bindings(): List<Binding> {
        val result = connectivityManager.allNetworks.flatMap { network ->
            val capabilities = connectivityManager.getNetworkCapabilities(network) ?: return@flatMap emptyList()
            if (!capabilities.hasTransport(NetworkCapabilities.TRANSPORT_WIFI) &&
                !capabilities.hasTransport(NetworkCapabilities.TRANSPORT_ETHERNET)
            ) return@flatMap emptyList()
            connectivityManager.getLinkProperties(network)?.linkAddresses.orEmpty()
                .mapNotNull { link -> (link.address as? Inet4Address)?.let { Binding(network, it) } }
        }
        return result.distinctBy { it.address.hostAddress }
    }

    private data class Binding(val network: Network?, val address: InetAddress)

    private companion object {
        const val LOG_TAG = "DLNACast.MiPlayMdns"
        const val MDNS_PORT = 5353
        val SERVICE_NAMES = listOf("_mi-connect._udp.local", "_lyra-mdns._udp.local")
    }
}

internal data class MiPlayMdnsDevice(
    val instanceName: String,
    val friendlyName: String,
    val address: InetAddress?,
)

internal fun createQuery(serviceName: String): ByteArray = ByteArrayOutputStream().use { output ->
    output.write(ByteArray(4))
    output.write(byteArrayOf(0, 1))
    output.write(ByteArray(6))
    serviceName.trimEnd('.').split('.').forEach { label ->
        val bytes = label.toByteArray(Charsets.UTF_8)
        require(bytes.size in 1..63)
        output.write(bytes.size)
        output.write(bytes)
    }
    output.write(0)
    output.write(byteArrayOf(0, 12, 0x80.toByte(), 1))
    output.toByteArray()
}

internal fun parseMdns(
    packet: ByteArray,
    serviceName: String,
    sourceAddress: InetAddress? = null,
): List<MiPlayMdnsDevice> {
    if (packet.size < 12) return emptyList()
    return runCatching {
        var offset = 12
        val questions = readUnsignedShort(packet, 4)
        val records = readUnsignedShort(packet, 6) + readUnsignedShort(packet, 8) + readUnsignedShort(packet, 10)
        repeat(questions) {
            offset = readName(packet, offset).second + 4
            require(offset <= packet.size)
        }
        val instances = linkedSetOf<String>()
        val services = linkedMapOf<String, Pair<String, Int>>()
        val names = linkedMapOf<String, String>()
        val addresses = linkedMapOf<String, InetAddress>()
        repeat(records) {
            val ownerResult = readName(packet, offset)
            val owner = ownerResult.first
            offset = ownerResult.second
            require(offset + 10 <= packet.size)
            val type = readUnsignedShort(packet, offset)
            val length = readUnsignedShort(packet, offset + 8)
            offset += 10
            val end = offset + length
            require(end <= packet.size)
            when (type) {
                1 -> if (length == 4) addresses[owner] = InetAddress.getByAddress(packet.copyOfRange(offset, end))
                12 -> {
                    val instance = readName(packet, offset).first
                    if (owner.equals(serviceName, true)) instances += instance
                }
                16 -> {
                    parseTxtName(packet, offset, end)?.let { names[owner] = it }
                    instances += owner
                }
                33 -> if (length >= 6) {
                    val port = readUnsignedShort(packet, offset + 4)
                    val target = readName(packet, offset + 6).first
                    services[owner] = target to port
                    instances += owner
                }
            }
            offset = end
        }
        val suffix = ".$serviceName"
        instances.filter { it.endsWith(suffix, true) }.map { instance ->
            val target = services[instance]?.first
            MiPlayMdnsDevice(
                instance,
                names[instance]?.takeIf(String::isNotBlank) ?: instance.dropLast(suffix.length),
                target?.let(addresses::get) ?: sourceAddress,
            )
        }
    }.getOrDefault(emptyList())
}

private fun readName(packet: ByteArray, start: Int): Pair<String, Int> {
    val labels = mutableListOf<String>()
    var cursor = start
    var resume = -1
    var pointers = 0
    while (true) {
        require(cursor in packet.indices)
        val length = packet[cursor++].toInt() and 0xff
        if (length == 0) return labels.joinToString(".") to if (resume >= 0) resume else cursor
        if (length and 0xc0 == 0xc0) {
            require(cursor in packet.indices && ++pointers <= 32)
            val pointer = ((length and 0x3f) shl 8) or (packet[cursor++].toInt() and 0xff)
            if (resume < 0) resume = cursor
            cursor = pointer
        } else {
            require(length <= 63 && cursor + length <= packet.size)
            labels += packet.copyOfRange(cursor, cursor + length).toString(Charsets.UTF_8)
            cursor += length
        }
    }
}

private fun parseTxtName(packet: ByteArray, start: Int, end: Int): String? {
    var offset = start
    while (offset < end) {
        val length = packet[offset++].toInt() and 0xff
        require(offset + length <= end)
        val value = packet.copyOfRange(offset, offset + length).toString(Charsets.UTF_8)
        offset += length
        if (value.startsWith("name=", true)) return value.substringAfter('=')
    }
    return null
}

private fun readUnsignedShort(packet: ByteArray, offset: Int): Int =
    ByteBuffer.wrap(packet, offset, 2).order(ByteOrder.BIG_ENDIAN).short.toInt() and 0xffff
