package com.suitius.dlnacast.network

import com.suitius.dlnacast.model.DlnaRenderer
import java.io.ByteArrayInputStream
import java.net.URI
import javax.xml.parsers.DocumentBuilderFactory
import org.w3c.dom.Element

object DeviceDescriptionParser {
    fun parse(xml: ByteArray, location: String): DlnaRenderer? {
        val factory = DocumentBuilderFactory.newInstance().apply { isNamespaceAware = true }
        configureSafely(factory)
        val document = factory.newDocumentBuilder().parse(ByteArrayInputStream(xml))
        val elements = document.getElementsByTagName("*")
        val device = (0 until elements.length)
            .mapNotNull { elements.item(it) as? Element }
            .firstOrNull { element ->
                element.localName == "device" &&
                    element.childText("deviceType")?.contains("MediaRenderer", ignoreCase = true) == true
            } ?: return null

        val baseUri = document.documentElement.childText("URLBase")
            ?.takeIf(String::isNotBlank)
            ?.let(::URI)
            ?: URI(location)
        val services = device.childElements("serviceList")
            .flatMap { it.childElements("service") }
            .associate { service ->
                service.childText("serviceType").orEmpty() to
                    service.childText("controlURL").orEmpty()
            }

        fun controlUrl(type: String): String? = services.entries
            .firstOrNull { it.key.contains(type, ignoreCase = true) }
            ?.value
            ?.takeIf(String::isNotBlank)
            ?.let(baseUri::resolve)
            ?.toString()

        val avTransport = controlUrl("AVTransport") ?: return null
        val name = device.childText("friendlyName")?.takeIf(String::isNotBlank)
            ?: URI(location).host
        val id = device.childText("UDN")?.takeIf(String::isNotBlank) ?: location
        return DlnaRenderer(
            id = id,
            name = name,
            descriptionUrl = location,
            host = URI(location).host,
            avTransportControlUrl = avTransport,
            renderingControlUrl = controlUrl("RenderingControl"),
            connectionManagerControlUrl = controlUrl("ConnectionManager"),
        )
    }

    private fun Element.childText(name: String): String? = childElements(name)
        .firstOrNull()
        ?.textContent
        ?.trim()

    private fun Element.childElements(name: String): List<Element> = buildList {
        val children = childNodes
        for (index in 0 until children.length) {
            val child = children.item(index) as? Element ?: continue
            if (child.localName == name || child.nodeName == name) add(child)
        }
    }

    private fun configureSafely(factory: DocumentBuilderFactory) {
        // Android vendors ship different XML providers. Unsupported hardening
        // features must not make every otherwise valid UPnP description fail.
        runCatching { factory.setFeature("http://apache.org/xml/features/disallow-doctype-decl", true) }
        runCatching { factory.setFeature("http://xml.org/sax/features/external-general-entities", false) }
        runCatching { factory.setFeature("http://xml.org/sax/features/external-parameter-entities", false) }
        runCatching { factory.setFeature("http://apache.org/xml/features/nonvalidating/load-external-dtd", false) }
        runCatching { factory.isExpandEntityReferences = false }
        runCatching { factory.isXIncludeAware = false }
    }
}
