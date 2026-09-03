package com.suitius.dlnacast.network

import com.suitius.dlnacast.model.DlnaRenderer
import java.net.HttpURLConnection
import java.net.URI

class UpnpSoapClient {
    fun setTransportUri(renderer: DlnaRenderer, streamUri: String) {
        val metadata = createDidlMetadata(streamUri)
        invoke(
            renderer.avTransportControlUrl,
            AV_TRANSPORT_SERVICE,
            "SetAVTransportURI",
            mapOf(
                "InstanceID" to "0",
                "CurrentURI" to streamUri,
                "CurrentURIMetaData" to metadata,
            ),
        )
    }

    fun play(renderer: DlnaRenderer) {
        invoke(
            renderer.avTransportControlUrl,
            AV_TRANSPORT_SERVICE,
            "Play",
            mapOf("InstanceID" to "0", "Speed" to "1"),
        )
    }

    fun stop(renderer: DlnaRenderer) {
        runCatching {
            invoke(
                renderer.avTransportControlUrl,
                AV_TRANSPORT_SERVICE,
                "Stop",
                mapOf("InstanceID" to "0"),
            )
        }
    }

    internal fun createDidlMetadata(streamUri: String): String {
        val escapedUri = escapeXml(streamUri)
        return """<DIDL-Lite xmlns="urn:schemas-upnp-org:metadata-1-0/DIDL-Lite/" xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:upnp="urn:schemas-upnp-org:metadata-1-0/upnp/"><item id="android-audio" parentID="0" restricted="1"><dc:title>Android Audio</dc:title><upnp:class>object.item.audioItem.musicTrack</upnp:class><res protocolInfo="http-get:*:audio/wav:DLNA.ORG_OP=00;DLNA.ORG_CI=0">$escapedUri</res></item></DIDL-Lite>"""
    }

    internal fun createEnvelope(service: String, action: String, arguments: Map<String, String>): String {
        val body = arguments.entries.joinToString(separator = "") { (name, value) ->
            "<$name>${escapeXml(value)}</$name>"
        }
        return """<?xml version="1.0" encoding="utf-8"?><s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/" s:encodingStyle="http://schemas.xmlsoap.org/soap/encoding/"><s:Body><u:$action xmlns:u="$service">$body</u:$action></s:Body></s:Envelope>"""
    }

    private fun invoke(url: String, service: String, action: String, arguments: Map<String, String>) {
        val bytes = createEnvelope(service, action, arguments).toByteArray(Charsets.UTF_8)
        val connection = URI(url).toURL().openConnection() as HttpURLConnection
        try {
            connection.requestMethod = "POST"
            connection.connectTimeout = 3_000
            connection.readTimeout = 3_000
            connection.doOutput = true
            connection.setFixedLengthStreamingMode(bytes.size)
            connection.setRequestProperty("Content-Type", "text/xml; charset=\"utf-8\"")
            connection.setRequestProperty("SOAPACTION", "\"$service#$action\"")
            connection.outputStream.use { it.write(bytes) }
            val responseCode = connection.responseCode
            if (responseCode !in 200..299) {
                val detail = connection.errorStream?.bufferedReader()?.use { it.readText() }.orEmpty()
                error("UPnP $action failed with HTTP $responseCode${if (detail.isBlank()) "" else ": $detail"}")
            }
            connection.inputStream.use { it.readBytes() }
        } finally {
            connection.disconnect()
        }
    }

    private fun escapeXml(value: String): String = buildString(value.length) {
        value.forEach { character ->
            append(
                when (character) {
                    '&' -> "&amp;"
                    '<' -> "&lt;"
                    '>' -> "&gt;"
                    '"' -> "&quot;"
                    '\'' -> "&apos;"
                    else -> character
                },
            )
        }
    }

    private companion object {
        const val AV_TRANSPORT_SERVICE = "urn:schemas-upnp-org:service:AVTransport:1"
    }
}
