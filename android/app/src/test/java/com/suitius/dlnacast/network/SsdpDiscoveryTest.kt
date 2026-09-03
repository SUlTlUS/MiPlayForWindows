package com.suitius.dlnacast.network

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class SsdpDiscoveryTest {
    @Test
    fun parsesCaseInsensitiveLocationHeader() {
        val response = "HTTP/1.1 200 OK\r\nLOCATION: http://192.168.1.8:1400/xml/device.xml\r\n\r\n"

        assertEquals(
            "http://192.168.1.8:1400/xml/device.xml",
            SsdpDiscovery.parseLocation(response),
        )
    }

    @Test
    fun missingLocationReturnsNull() {
        assertNull(SsdpDiscovery.parseLocation("HTTP/1.1 200 OK\r\nST: upnp:rootdevice\r\n\r\n"))
    }
}
