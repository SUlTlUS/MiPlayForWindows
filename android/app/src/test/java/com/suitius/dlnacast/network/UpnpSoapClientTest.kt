package com.suitius.dlnacast.network

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class UpnpSoapClientTest {
    private val client = UpnpSoapClient()

    @Test
    fun escapesTransportUriExactlyOnceInDidlMetadata() {
        val metadata = client.createDidlMetadata("http://192.168.1.2/stream/a.wav?x=1&y=2")

        assertTrue(metadata.contains("x=1&amp;y=2"))
        assertFalse(metadata.contains("&amp;amp;"))
    }

    @Test
    fun envelopeEscapesEmbeddedMetadata() {
        val envelope = client.createEnvelope(
            "urn:test",
            "SetValue",
            mapOf("Value" to "<item id=\"a\">x & y</item>"),
        )

        assertTrue(envelope.contains("&lt;item id=&quot;a&quot;&gt;x &amp; y&lt;/item&gt;"))
    }
}
