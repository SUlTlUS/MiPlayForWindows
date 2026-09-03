package com.suitius.dlnacast.miplay

import java.nio.ByteBuffer
import java.nio.ByteOrder
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class MiPlayMediaPacketizerTest {
    @Test
    fun producesValidatedFirstAndSteadyStateWireShapes() {
        val packetizer = MiPlayMediaPacketizer()
        val rawAac = ByteArray(682) { 0x5a }

        val first = packetizer.packetize(rawAac, 9_632_369_733L)
        val second = packetizer.packetize(rawAac, 9_632_391_066L)

        assertTrue(first.containsProgramTables)
        assertEquals(1, first.wireFrames.size)
        assertEquals(1_332, first.wireFrames.single().size)
        assertArrayEquals(
            hex("2400053080A1000000000000DEADBEEF"),
            first.wireFrames.single().copyOfRange(0, 16),
        )
        assertArrayEquals(
            hex("474000110000B00D0000C300000001E1002DF65295"),
            first.wireFrames.single().copyOfRange(16, 37),
        )

        assertFalse(second.containsProgramTables)
        assertEquals(768, second.wireFrames.single().size)
        val secondRtp = ByteBuffer.wrap(second.wireFrames.single(), 4, 12).order(ByteOrder.BIG_ENDIAN)
        assertEquals(0x80.toByte(), secondRtp.get())
        assertEquals(0xa1.toByte(), secondRtp.get())
        assertEquals(1, secondRtp.short.toInt() and 0xffff)
        assertEquals(1_919, secondRtp.int)
        assertEquals(0xdead_beef.toInt(), secondRtp.int)
    }

    @Test
    fun keepsCapturedProgramTableCadence() {
        val packetizer = MiPlayMediaPacketizer()
        val tableIndexes = (0 until 25).filter { index ->
            packetizer.packetize(ByteArray(128), index * 21_333L).containsProgramTables
        }

        assertEquals(listOf(0, 13, 18, 23), tableIndexes)
    }

    @Test
    fun snapshotRestoresSequenceTimestampAndTransportContinuity() {
        val source = MiPlayMediaPacketizer()
        val rawAac = ByteArray(682) { 0x5a }
        repeat(5) { index -> source.packetize(rawAac, index * 21_333L) }

        val snapshot = source.snapshot()
        val restored = MiPlayMediaPacketizer(snapshot)
        assertEquals(snapshot, restored.snapshot())
        val sourceNext = source.packetize(rawAac, 5 * 21_333L)
        val restoredNext = restored.packetize(rawAac, 5 * 21_333L)

        assertEquals(5L, snapshot.accessUnitIndex)
        assertEquals(sourceNext.wireFrames.size, restoredNext.wireFrames.size)
        sourceNext.wireFrames.zip(restoredNext.wireFrames).forEach { (sourceFrame, restoredFrame) ->
            assertArrayEquals(sourceFrame, restoredFrame)
        }
    }

    private fun hex(value: String): ByteArray = value.chunked(2)
        .map { it.toInt(16).toByte() }
        .toByteArray()
}
