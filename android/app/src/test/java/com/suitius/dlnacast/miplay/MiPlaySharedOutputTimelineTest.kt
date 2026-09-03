package com.suitius.dlnacast.miplay

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class MiPlaySharedOutputTimelineTest {
    @Test
    fun everyReceiverSharesOneTimestampForAnAccessUnit() {
        val timeline = MiPlaySharedOutputTimeline()

        val first = timeline.getTimestamp(0, 4_000_000L)
        val laterObservation = timeline.getTimestamp(0, 4_900_000L)
        val next = timeline.getTimestamp(1, 4_021_333L)

        assertEquals(108_000_000L, first.programClockReference27Mhz)
        assertEquals(first, laterObservation)
        assertEquals(108_575_991L, next.programClockReference27Mhz)
        assertEquals(1_919L, next.presentationTimestamp90Khz)
    }

    @Test
    fun outputStartsAtTheFirstMediaHandoffAfterPrewarm() {
        val timeline = MiPlaySharedOutputTimeline()

        assertEquals(0L, timeline.getTimestamp(0, 4_000_000L).presentationTimestamp90Khz)
        assertEquals(1_919L, timeline.getTimestamp(1, 4_000_000L).presentationTimestamp90Khz)
        assertEquals(3_839L, timeline.getTimestamp(2, 4_000_812L).presentationTimestamp90Khz)
        assertEquals(5_760L, timeline.getTimestamp(3, 4_000_812L).presentationTimestamp90Khz)
    }

    @Test
    fun correctionIsSharedAndUsesAStrictFiftyMicrosecondThreshold() {
        val timeline = MiPlaySharedOutputTimeline()
        timeline.getTimestamp(0, 1_000_000L)

        val corrected = timeline.getTimestamp(1, 1_100_000L)
        val sameUnit = timeline.getTimestamp(1, 1_125_000L)

        assertEquals(corrected, sameUnit)
        assertTrue(corrected.corrected)
        assertEquals(9_000L, corrected.presentationTimestamp90Khz)
        assertEquals(1L, corrected.correctionCount)
        assertEquals(78_667L, corrected.totalCorrectionMicroseconds)
    }

    @Test
    fun sendRecoveryIsAppliedOnceAndDoesNotDoubleCountAStall() {
        val timeline = MiPlaySharedOutputTimeline()
        timeline.getTimestamp(0, 1_000_000L)

        assertEquals(45_000L, timeline.recoverAfterSend(0, 1_500_000L))
        assertFalse(timeline.getTimestamp(1, 1_521_333L).corrected)
        assertEquals(1_919L, timeline.getTimestamp(1, 1_521_333L).presentationTimestamp90Khz)
        assertEquals(45_000L, timeline.recoverAfterSend(0, 1_500_000L))
    }
}
