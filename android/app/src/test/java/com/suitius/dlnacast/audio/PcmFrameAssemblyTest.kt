package com.suitius.dlnacast.audio

import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit
import kotlin.concurrent.thread
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class PcmFrameAssemblyTest {
    @Test
    fun shortReadsAreJoinedWithoutPaddingOrDroppingSamples() {
        PcmFrameBuffer(4, PcmFrameBuffer.MIPLAY_FRAME_BYTES).use { buffer ->
            val source = ByteArray(8_192) { (it % 251).toByte() }
            var offset = 0
            for (count in listOf(12, 152, 3_680, 44, 2_000, 2_304)) {
                buffer.offer(source.copyOfRange(offset, offset + count))
                offset += count
            }
            assertEquals(source.size, offset)
            assertArrayEquals(source.copyOfRange(0, 4_096), buffer.readFrameOrSilence())
            assertArrayEquals(source.copyOfRange(4_096, 8_192), buffer.readFrameOrSilence())
            assertEquals(0, buffer.overruns)
        }
    }

    @Test
    fun quarterSecondBatchIsNotTruncatedToOneFrame() {
        PcmFrameBuffer(16, PcmFrameBuffer.MIPLAY_FRAME_BYTES).use { buffer ->
            val source = ByteArray(49_152) { (it % 197).toByte() }
            buffer.offer(source.copyOfRange(0, 48_000))
            buffer.offer(source.copyOfRange(48_000, source.size))
            repeat(12) { index ->
                assertArrayEquals(source.copyOfRange(index * 4_096, (index + 1) * 4_096),
                    buffer.readFrameOrSilence())
            }
            assertEquals(0, buffer.overruns)
        }
    }

    @Test
    fun defaultDlnaFrameSizeAndBoundedLatestBehaviorRemain() {
        PcmFrameBuffer(1).use { buffer ->
            assertEquals(3_840, buffer.frameByteCount)
            buffer.offer(ByteArray(3_840) { 1 })
            buffer.offer(ByteArray(3_840) { 2 })
            assertEquals(1, buffer.overruns)
            assertEquals(20, buffer.bufferedMilliseconds)
            assertArrayEquals(ByteArray(3_840) { 2 }, buffer.readFrameOrSilence())
        }
    }

    @Test(timeout = 3_000)
    fun sequenceBufferBackpressureDoesNotDropFramesAndCloseUnblocksWriter() {
        val buffer = PcmFrameBuffer(1, 4_096, true)
        val entered = CountDownLatch(1)
        buffer.offer(ByteArray(4_096) { 1 })
        val writer = thread(isDaemon = true) {
            entered.countDown()
            buffer.offer(ByteArray(4_096) { 2 })
        }
        assertTrue(entered.await(1, TimeUnit.SECONDS))
        assertArrayEquals(ByteArray(4_096) { 1 }, buffer.readFramePreservingSequence())
        writer.join(500)
        assertFalse(writer.isAlive)
        assertArrayEquals(ByteArray(4_096) { 2 }, buffer.readFramePreservingSequence())
        assertEquals(0, buffer.overruns)
        buffer.offer(ByteArray(4_096))
        val blocked = thread(isDaemon = true) { buffer.offer(ByteArray(4_096)) }
        buffer.close()
        blocked.join(500)
        assertFalse(blocked.isAlive)
    }
}
