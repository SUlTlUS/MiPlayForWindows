package com.suitius.dlnacast.miplay

import com.suitius.dlnacast.audio.PcmFrameBuffer
import java.util.concurrent.LinkedBlockingQueue
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicInteger
import kotlin.concurrent.thread
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class MiPlayAudioPipelineTest {
    @Test
    fun matchesWindowsFormatAndBoundedPipelineConstants() {
        assertEquals(4_096, PcmFrameBuffer.MIPLAY_FRAME_BYTES)
        assertEquals(12, MiPlayAudioPipeline.INPUT_WINDOW)
        assertEquals(12, MiPlayAudioPipeline.OUTPUT_CAPACITY)
        assertEquals(14, MiPlayAudioPipeline.STARTUP_ACCESS_UNITS)
        assertEquals(256_000, MiPlayAudioPipeline.BIT_RATE)
    }

    @Test
    fun sampleClockUsesTotalSamplesWithoutAccumulatingRoundingError() {
        assertEquals(0, MiPlayAudioPipeline.presentationTimeUs(0))
        assertEquals(21_333, MiPlayAudioPipeline.presentationTimeUs(1))
        assertEquals(42_666, MiPlayAudioPipeline.presentationTimeUs(2))
        assertEquals(64_000, MiPlayAudioPipeline.presentationTimeUs(3))
        assertEquals(3_600_000_000L, MiPlayAudioPipeline.presentationTimeUs(168_750))
    }

    @Test
    fun captureCadenceRebasesAfterBlockingInsteadOfBurstingOldReads() {
        val clock = MiPlayPcmCadence()
        val origin = 5_000_000_000L
        assertEquals(origin, clock.nextDeadline(origin))
        assertEquals(origin + MiPlayPcmCadence.FRAME_NANOS, clock.nextDeadline(origin + 1))
        val resumed = origin + 1_000_000_000
        assertEquals(resumed, clock.nextDeadline(resumed))
        assertEquals(resumed + MiPlayPcmCadence.FRAME_NANOS, clock.nextDeadline(resumed + 1))
    }

    @Test
    fun startupConsumesFourteenOrderedFramesBeforeSendOriginStarts() {
        var index = 0
        val runway = MiPlayAudioPipeline.prewarm { index++ }
        assertEquals((0 until 14).toList(), runway.toList())
        assertEquals(14, index)
    }

    @Test(timeout = 5_000)
    fun batchedCodecDrainsIndependentlyAndRetainsEveryAccessUnit() {
        val pcm = PcmFrameBuffer(4, PcmFrameBuffer.MIPLAY_FRAME_BYTES, true)
        val codec = BatchedCodec()
        val encoder = MiPlayAacEncoder(pcm, codec)
        val producer = thread(isDaemon = true) {
            try {
                repeat(100) { index -> pcm.offer(ByteArray(pcm.frameByteCount) { index.toByte() }) }
            } catch (_: InterruptedException) { }
        }
        try {
            // No media consumer yet: only 12 frames may enter the codec.
            val deadline = System.nanoTime() + 2_000_000_000L
            while (encoder.bufferedAccessUnits < 12 && System.nanoTime() < deadline) Thread.yield()
            assertEquals(12, encoder.bufferedAccessUnits)
            assertEquals(12, codec.inputCount.get())
            val runway = MiPlayAudioPipeline.prewarm(encoder::nextAccessUnit)
            val actual = runway.toMutableList()
            repeat(46) { actual += encoder.nextAccessUnit() }
            assertEquals(60, actual.size)
            actual.forEachIndexed { index, au ->
                assertArrayEquals(byteArrayOf(index.toByte()), au.bytes)
                assertEquals(MiPlayAudioPipeline.presentationTimeUs(index.toLong()), au.presentationTimeUs)
            }
            assertEquals(0, pcm.overruns)
        } finally {
            pcm.close()
            encoder.close()
            producer.interrupt()
            producer.join(500)
        }
        assertFalse(producer.isAlive)
    }

    private class BatchedCodec : MiPlayAacCodec {
        val inputCount = AtomicInteger()
        private val pending = mutableListOf<EncodedAccessUnit>()
        private val ready = LinkedBlockingQueue<EncodedAccessUnit>()

        override fun queuePcm(bytes: ByteArray, presentationTimeUs: Long) {
            inputCount.incrementAndGet()
            pending += EncodedAccessUnit(byteArrayOf(bytes[0]), presentationTimeUs)
            // Models an encoder that emits only after accepting several input frames.
            if (pending.size == 4) {
                ready.addAll(pending)
                pending.clear()
            }
        }

        override fun readAccessUnit(): EncodedAccessUnit? = ready.poll(10, TimeUnit.MILLISECONDS)
        override fun close() { ready.clear() }
    }
}
