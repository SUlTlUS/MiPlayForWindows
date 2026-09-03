package com.suitius.dlnacast.network

import java.io.ByteArrayOutputStream
import java.net.InetAddress
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class MiPlayMdnsDiscoveryTest {
    @Test
    fun queryRequestsMiConnectPtrWithUnicastResponse() {
        val query = createQuery("_mi-connect._udp.local")

        assertEquals(0, query[0].toInt())
        assertEquals(1, query[5].toInt())
        assertEquals(12, query[query.size - 3].toInt())
        assertEquals(0x80, query[query.size - 2].toInt() and 0xff)
        assertEquals(1, query.last().toInt())
    }

    @Test
    fun parsesPtrSrvTxtAndAddressRecords() {
        val packet = mdnsPacket()
        val devices = parseMdns(packet, SERVICE)

        assertEquals(1, devices.size)
        assertEquals("小爱音箱-6333", devices.single().friendlyName)
        assertEquals("192.168.10.4", devices.single().address?.hostAddress)
        assertTrue(devices.single().instanceName.endsWith(SERVICE))
    }

    private fun mdnsPacket(): ByteArray = ByteArrayOutputStream().use { output ->
        output.write(byteArrayOf(0, 0, 0x84.toByte(), 0, 0, 0, 0, 4, 0, 0, 0, 0))
        val instance = "speaker.$SERVICE"
        val host = "speaker.local"
        record(output, SERVICE, 12, dnsName(instance))
        record(output, instance, 33, byteArrayOf(0, 0, 0, 0, 0x04, 0x83.toByte()) + dnsName(host))
        val txt = "name=小爱音箱-6333".toByteArray(Charsets.UTF_8)
        record(output, instance, 16, byteArrayOf(txt.size.toByte()) + txt)
        record(output, host, 1, InetAddress.getByName("192.168.10.4").address)
        output.toByteArray()
    }

    private fun record(output: ByteArrayOutputStream, owner: String, type: Int, data: ByteArray) {
        output.write(dnsName(owner))
        output.write(byteArrayOf(0, type.toByte(), 0, 1, 0, 0, 0, 120, 0, data.size.toByte()))
        output.write(data)
    }

    private fun dnsName(value: String): ByteArray = ByteArrayOutputStream().use { output ->
        value.split('.').forEach { label ->
            val bytes = label.toByteArray(Charsets.UTF_8)
            output.write(bytes.size)
            output.write(bytes)
        }
        output.write(0)
        output.toByteArray()
    }

    private companion object {
        const val SERVICE = "_mi-connect._udp.local"
    }
}
