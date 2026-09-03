package com.suitius.dlnacast.network

import android.content.Context
import android.net.ConnectivityManager
import android.net.Network
import android.net.NetworkCapabilities
import android.net.wifi.WifiManager
import android.util.Log
import com.suitius.dlnacast.model.DlnaRenderer
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.HttpURLConnection
import java.net.InetAddress
import java.net.Inet4Address
import java.net.InetSocketAddress
import java.net.URI
import java.util.Locale
import kotlinx.coroutines.async
import kotlinx.coroutines.awaitAll
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

class SsdpDiscovery(context: Context) {
    private val miPlayDiscovery = MiPlayMdnsDiscovery(context)
    private val wifiManager = context.applicationContext.getSystemService(WifiManager::class.java)
    private val connectivityManager = context.applicationContext.getSystemService(ConnectivityManager::class.java)
    @Volatile var lastDiagnostics: String = "尚未搜索"
        private set

    suspend fun discover(timeoutMillis: Long = 3_000): List<DlnaRenderer> = withContext(Dispatchers.IO) {
        val lock = wifiManager.createMulticastLock("dlnacast-ssdp").apply {
            setReferenceCounted(false)
            acquire()
        }
        try {
            val search = searchLocations(timeoutMillis)
            val loaded = coroutineScope { search.locations.map { async(Dispatchers.IO) { loadRenderer(it) } }.awaitAll() }
            val dlnaRenderers = loaded.mapNotNull { (it as? LoadResult.Success)?.renderer }
            val miPlayRenderers = miPlayDiscovery.discover(timeoutMillis)
            val renderers = (dlnaRenderers + miPlayRenderers)
                .distinctBy { it.host }
                .sortedBy { it.name.lowercase(Locale.getDefault()) }
            val failures = loaded.filterIsInstance<LoadResult.Failure>()
            val failureDetail = failures.take(4).joinToString(separator = "；") { "${it.host}: ${it.reason}" }
            lastDiagnostics = buildString {
                append("已从 ${search.interfaceAddresses.joinToString().ifBlank { "默认网络" }} 发出 SSDP 与 MiPlay mDNS 查询，收到 ${search.responseCount} 个 SSDP 响应、${search.locations.size} 个设备地址，解析出 ${dlnaRenderers.size} 台 DLNA、${miPlayRenderers.size} 台 MiPlay 音箱")
                if (failureDetail.isNotBlank()) append("。失败详情：$failureDetail")
            }
            Log.i(LOG_TAG, lastDiagnostics)
            renderers
        } finally {
            if (lock.isHeld) lock.release()
        }
    }

    private suspend fun searchLocations(timeoutMillis: Long): SearchResult = coroutineScope {
        val bindings = networkBindings()
        val results = bindings.map { binding -> async(Dispatchers.IO) { searchFromBinding(binding, timeoutMillis) } }.awaitAll()
        SearchResult(
            locations = results.flatMap { it.locations }.distinctBy(FoundLocation::url),
            responseCount = results.sumOf(BindingResult::responseCount),
            interfaceAddresses = bindings.map { it.address.hostAddress.orEmpty() },
        )
    }

    private fun searchFromBinding(binding: NetworkBinding, timeoutMillis: Long): BindingResult {
        fun createRequest(searchTarget: String) = buildString {
            append("M-SEARCH * HTTP/1.1\r\n")
            append("HOST: 239.255.255.250:1900\r\n")
            append("MAN: \"ssdp:discover\"\r\n")
            append("MX: 2\r\n")
            append("ST: $searchTarget\r\n")
            append("USER-AGENT: Android/DLNACast UPnP/1.1\r\n\r\n")
        }.toByteArray(Charsets.US_ASCII)
        val destination = InetAddress.getByName("239.255.255.250")
        val locations = linkedSetOf<FoundLocation>()
        var responseCount = 0
        DatagramSocket(null).use { socket ->
            socket.reuseAddress = true
            binding.network?.bindSocket(socket)
            socket.bind(InetSocketAddress(binding.address, 0))
            socket.soTimeout = 450
            repeat(2) {
                SEARCH_TARGETS.forEach { target ->
                    val request = createRequest(target)
                    socket.send(DatagramPacket(request, request.size, destination, 1900))
                }
                if (it == 0) Thread.sleep(250)
            }
            val deadline = System.currentTimeMillis() + timeoutMillis
            while (System.currentTimeMillis() < deadline) {
                val buffer = ByteArray(16 * 1024)
                try {
                    val response = DatagramPacket(buffer, buffer.size)
                    socket.receive(response)
                    responseCount++
                    parseLocation(buffer.decodeToString(0, response.length))
                        ?.let { locations += FoundLocation(it, binding.network) }
                } catch (_: java.net.SocketTimeoutException) {
                    // Keep listening until the overall discovery deadline.
                }
            }
        }
        return BindingResult(locations, responseCount)
    }

    private fun loadRenderer(found: FoundLocation): LoadResult {
        val uri = URI(found.url)
        val url = uri.toURL()
        val connection = (found.network?.openConnection(url) ?: url.openConnection()) as HttpURLConnection
        try {
            connection.connectTimeout = 2_000
            connection.readTimeout = 2_000
            connection.setRequestProperty("User-Agent", "DLNACast-Android/0.1 UPnP/1.1")
            val status = connection.responseCode
            if (status !in 200..299) return LoadResult.Failure(uri.host, "HTTP $status")
            val renderer = connection.inputStream.use { DeviceDescriptionParser.parse(it.readBytes(), found.url) }
                ?: return LoadResult.Failure(uri.host, "不是 DLNA MediaRenderer，或缺少 AVTransport")
            return LoadResult.Success(renderer)
        } catch (exception: Exception) {
            val reason = exception.message?.takeIf(String::isNotBlank)
                ?.let { "${exception.javaClass.simpleName}: $it" }
                ?: exception.javaClass.simpleName
            return LoadResult.Failure(uri.host, reason)
        } finally {
            connection.disconnect()
        }
    }

    private fun networkBindings(): List<NetworkBinding> {
        val bindings = connectivityManager.allNetworks.flatMap { network ->
            val capabilities = connectivityManager.getNetworkCapabilities(network) ?: return@flatMap emptyList()
            if (!capabilities.hasTransport(NetworkCapabilities.TRANSPORT_WIFI) &&
                !capabilities.hasTransport(NetworkCapabilities.TRANSPORT_ETHERNET)
            ) return@flatMap emptyList()
            connectivityManager.getLinkProperties(network)?.linkAddresses.orEmpty()
                .mapNotNull { link -> (link.address as? Inet4Address)?.let { NetworkBinding(network, it) } }
        }
        if (bindings.isNotEmpty()) return bindings.distinctBy { it.address.hostAddress }
        return listOf(NetworkBinding(connectivityManager.activeNetwork, InetAddress.getByName("0.0.0.0")))
    }

    companion object {
        private const val LOG_TAG = "DLNACast.Discovery"
        private val SEARCH_TARGETS = listOf(
            "urn:schemas-upnp-org:device:MediaRenderer:1",
            "urn:schemas-upnp-org:device:MediaRenderer:2",
            "urn:schemas-upnp-org:device:MediaRenderer:3",
            "ssdp:all",
        )

        internal fun parseLocation(response: String): String? = response
            .lineSequence()
            .map(String::trim)
            .firstOrNull { it.startsWith("location:", ignoreCase = true) }
            ?.substringAfter(':')
            ?.trim()
            ?.takeIf(String::isNotBlank)
    }

    private data class NetworkBinding(val network: Network?, val address: InetAddress)
    private data class FoundLocation(val url: String, val network: Network?)
    private data class BindingResult(val locations: Set<FoundLocation>, val responseCount: Int)
    private data class SearchResult(
        val locations: List<FoundLocation>,
        val responseCount: Int,
        val interfaceAddresses: List<String>,
    )
    private sealed interface LoadResult {
        data class Success(val renderer: DlnaRenderer) : LoadResult
        data class Failure(val host: String, val reason: String) : LoadResult
    }
}
