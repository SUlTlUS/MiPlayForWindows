package com.suitius.dlnacast.network

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Test

class DeviceDescriptionParserTest {
    @Test
    fun resolvesRendererControlUrlsAgainstDescriptionLocation() {
        val xml = """
            <?xml version="1.0"?>
            <root xmlns="urn:schemas-upnp-org:device-1-0">
              <device>
                <deviceType>urn:schemas-upnp-org:device:MediaRenderer:1</deviceType>
                <friendlyName>Living Room</friendlyName>
                <UDN>uuid:test-renderer</UDN>
                <serviceList>
                  <service>
                    <serviceType>urn:schemas-upnp-org:service:AVTransport:1</serviceType>
                    <controlURL>/MediaRenderer/AVTransport/Control</controlURL>
                  </service>
                  <service>
                    <serviceType>urn:schemas-upnp-org:service:RenderingControl:1</serviceType>
                    <controlURL>/MediaRenderer/RenderingControl/Control</controlURL>
                  </service>
                </serviceList>
              </device>
            </root>
        """.trimIndent().toByteArray()

        val renderer = DeviceDescriptionParser.parse(xml, "http://192.168.1.8:1400/xml/device.xml")

        assertNotNull(renderer)
        assertEquals("Living Room", renderer?.name)
        assertEquals("192.168.1.8", renderer?.host)
        assertEquals(
            "http://192.168.1.8:1400/MediaRenderer/AVTransport/Control",
            renderer?.avTransportControlUrl,
        )
    }
}
