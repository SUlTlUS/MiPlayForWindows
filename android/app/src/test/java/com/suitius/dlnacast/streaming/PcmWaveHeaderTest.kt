package com.suitius.dlnacast.streaming

import java.nio.ByteBuffer
import java.nio.ByteOrder
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Test

class PcmWaveHeaderTest {
    @Test
    fun createsStereo48KhzPcmHeader() {
        val header = PcmWaveHeader.createIndefinite()
        val littleEndian = ByteBuffer.wrap(header).order(ByteOrder.LITTLE_ENDIAN)

        assertEquals(44, header.size)
        assertArrayEquals("RIFF".toByteArray(), header.copyOfRange(0, 4))
        assertArrayEquals("WAVE".toByteArray(), header.copyOfRange(8, 12))
        assertEquals(2, littleEndian.getShort(22).toInt())
        assertEquals(48_000, littleEndian.getInt(24))
        assertEquals(16, littleEndian.getShort(34).toInt())
    }
}
