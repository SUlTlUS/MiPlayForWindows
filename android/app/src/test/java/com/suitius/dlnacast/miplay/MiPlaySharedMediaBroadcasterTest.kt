package com.suitius.dlnacast.miplay

import com.suitius.dlnacast.audio.AudioChannelRoute
import org.junit.Assert.assertEquals
import org.junit.Test

class MiPlaySharedMediaBroadcasterTest {
    @Test
    fun lateReceiverGetsTheRetainedWindowBeforeTheNextLiveFrame() {
        val broadcaster = MiPlaySharedMediaBroadcaster(capacity = 2)
        repeat(50) { index -> broadcaster.publish(frame(index), snapshot(index), timestamp(index)) }

        val late = broadcaster.register(AudioChannelRoute.Stereo)

        assertEquals(3L, late.initialPacketizerSnapshot.accessUnitIndex)
        assertEquals(
            (3L..49L).toList(),
            (0 until 47).map { requireNotNull(late.take()).accessUnitIndex },
        )

        broadcaster.publish(frame(50), snapshot(50), timestamp(50))
        assertEquals(50L, requireNotNull(late.take()).accessUnitIndex)
        late.close()
    }

    @Test
    fun aLateReceiverDoesNotHoldBackTheExistingMember() {
        val broadcaster = MiPlaySharedMediaBroadcaster(capacity = 2)
        val existing = broadcaster.register(AudioChannelRoute.Stereo)
        broadcaster.publish(frame(0), snapshot(0), timestamp(0))
        assertEquals(0L, requireNotNull(existing.take()).accessUnitIndex)

        val late = broadcaster.register(AudioChannelRoute.Stereo)
        broadcaster.publish(frame(1), snapshot(1), timestamp(1))

        assertEquals(1L, requireNotNull(existing.take()).accessUnitIndex)
        assertEquals(0L, requireNotNull(late.take()).accessUnitIndex)
        assertEquals(1L, requireNotNull(late.take()).accessUnitIndex)
        existing.close()
        late.close()
    }

    private fun frame(index: Int) = mapOf(
        AudioChannelRoute.Stereo to EncodedAccessUnit(
            byteArrayOf(index.toByte()),
            MiPlayAudioPipeline.presentationTimeUs(index.toLong()),
        ),
    )

    private fun snapshot(index: Int) = mapOf(
        AudioChannelRoute.Stereo to MiPlayMediaPacketizerSnapshot(index.toLong()),
    )

    private fun timestamp(index: Int) = MiPlayOutputTimestamp(
        presentationTimestamp90Khz = index.toLong() * 1_920L,
        programClockReference27Mhz = index.toLong() * 51_840L,
        corrected = false,
        correctionCount = 0L,
        totalCorrectionMicroseconds = 0L,
    )
}
