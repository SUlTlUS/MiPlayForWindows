package com.suitius.dlnacast.miplay

import java.io.BufferedInputStream
import java.io.ByteArrayInputStream
import java.nio.ByteBuffer
import java.nio.ByteOrder
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class MiPlayCommandCodecTest {
    @Test
    fun usesNineByteBigEndianCommandHeader() {
        val frame = MiPlayCommandCodec.encode(0x1234, 0xabcd, byteArrayOf(1, 2, 3))

        assertArrayEquals(
            byteArrayOf(0x24, 0x12, 0x34, 0xab.toByte(), 0xcd.toByte(), 0, 0, 0, 3),
            frame.copyOfRange(0, 9),
        )
        val decoded = MiPlayCommandCodec.read(BufferedInputStream(ByteArrayInputStream(frame)))
        assertEquals(0x1234, decoded.command)
        assertEquals(0xabcd, decoded.sequence)
        assertArrayEquals(byteArrayOf(1, 2, 3), decoded.payload)
    }

    @Test
    fun openPayloadIsNullTerminated() {
        val text = "wfd://192.168.31.8:7236?mirrorMode=1\u0000"
        val frame = MiPlayCommandCodec.encode(0, 0x1234, text.toByteArray())
        val header = ByteBuffer.wrap(frame).order(ByteOrder.BIG_ENDIAN)

        assertEquals(0x24, header.get().toInt())
        assertEquals(0, header.short.toInt())
        assertEquals(0x1234, header.short.toInt() and 0xffff)
        assertEquals(text.toByteArray().size, header.int)
        assertEquals(0, frame.last().toInt())
    }

    @Test
    fun legacyChallengeMatchesWindowsVerifiedResponse() {
        val response = legacyChallengeResponse("legacy-challenge".toByteArray())

        assertEquals(
            "1bfbbecf1244c16add4362959aa0ccc7b6e8a0c4",
            response.toString(Charsets.US_ASCII),
        )
    }

    @Test
    fun receiverVolumeUsesRawFourByteBigEndianPayload() {
        assertArrayEquals(byteArrayOf(0, 0, 0, 24), encodeRawVolume(24))
    }

    @Test
    fun runtimeAcknowledgementRequiresCommandSequenceAndPayloadMatch() {
        val expectedPayload = encodeRawVolume(36)
        val expected = MiPlayCommandFrame(0x000d, 0x0010, expectedPayload)

        assertTrue(runtimeAcknowledgementMatches(0x000d, 0x0010, expectedPayload, expected))
        assertFalse(runtimeAcknowledgementMatches(0x001b, 0x0010, expectedPayload, expected))
        assertFalse(runtimeAcknowledgementMatches(0x000d, 0x0011, expectedPayload, expected))
        assertFalse(runtimeAcknowledgementMatches(0x000d, 0x0010, encodeRawVolume(37), expected))
    }
}
