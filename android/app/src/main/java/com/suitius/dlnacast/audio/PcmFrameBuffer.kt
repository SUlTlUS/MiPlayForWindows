package com.suitius.dlnacast.audio

import java.util.concurrent.ArrayBlockingQueue
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicLong

class PcmFrameBuffer(
    capacityFrames: Int = 6,
    val frameByteCount: Int = FRAME_BYTES,
    private val preserveFrameSequence: Boolean = false,
) : AutoCloseable {
    init {
        require(frameByteCount > 0 && frameByteCount % (CHANNEL_COUNT * BYTES_PER_SAMPLE) == 0)
    }
    private val frames = ArrayBlockingQueue<ByteArray>(capacityFrames)
    private val pending = ByteArray(frameByteCount)
    private var pendingCount = 0
    private val silenceFrame = ByteArray(frameByteCount)
    private val closed = AtomicBoolean(false)
    private val droppedFrames = AtomicLong(0)
    private val capturedSamples = AtomicLong(0)
    private val nonZeroSamples = AtomicLong(0)
    private val peakSample = AtomicLong(0)
    private val missingFrames = AtomicLong(0)

    val overruns: Long get() = droppedFrames.get()
    val bufferedMilliseconds: Int get() = frames.size * frameByteCount * 1_000 /
        (SAMPLE_RATE * CHANNEL_COUNT * BYTES_PER_SAMPLE)
    val underruns: Long get() = missingFrames.get()
    val sampleCount: Long get() = capturedSamples.get()
    val nonZeroSampleCount: Long get() = nonZeroSamples.get()
    val peakNormalized: Double get() = peakSample.get() / 32768.0

    @Synchronized
    fun offer(frame: ByteArray) {
        if (closed.get() || frame.isEmpty()) return
        observeSignal(frame)
        // Port PcmFrameAssembler: retain short/batched reads instead of padding or truncating.
        var offset = 0
        while (offset < frame.size && !closed.get()) {
            val count = minOf(pending.size - pendingCount, frame.size - offset)
            frame.copyInto(pending, pendingCount, offset, offset + count)
            pendingCount += count
            offset += count
            if (pendingCount == pending.size) {
                enqueue(pending.copyOf())
                pendingCount = 0
            }
        }
    }

    private fun enqueue(frame: ByteArray) {
        if (preserveFrameSequence) {
            // Windows route buffers apply backpressure; neither channel may drop an AU.
            while (!closed.get() && !frames.offer(frame, 50, TimeUnit.MILLISECONDS)) Unit
        } else if (!frames.offer(frame)) {
            frames.poll()
            droppedFrames.incrementAndGet()
            frames.offer(frame)
        }
    }

    private fun observeSignal(frame: ByteArray) {
        var offset = 0
        var localNonZero = 0L
        var localPeak = 0L
        while (offset + 1 < frame.size) {
            val sample = ((frame[offset].toInt() and 0xff) or (frame[offset + 1].toInt() shl 8)).toShort().toInt()
            val absolute = if (sample == Short.MIN_VALUE.toInt()) 32768L else kotlin.math.abs(sample).toLong()
            if (sample != 0) localNonZero++
            if (absolute > localPeak) localPeak = absolute
            offset += 2
        }
        capturedSamples.addAndGet(offset.toLong() / 2L)
        nonZeroSamples.addAndGet(localNonZero)
        peakSample.accumulateAndGet(localPeak, ::maxOf)
    }

    fun readFrameOrSilence(): ByteArray {
        if (closed.get()) return silenceFrame
        return frames.poll(120, TimeUnit.MILLISECONDS) ?: silenceFrame
    }

    /** The shared producer owns cadence and inserts silence once for all routes. */
    fun pollFrameOrSilence(): ByteArray = frames.poll() ?: silenceFrame.also {
        missingFrames.incrementAndGet()
    }

    fun readFramePreservingSequence(): ByteArray {
        check(preserveFrameSequence)
        while (!closed.get()) {
            frames.poll(50, TimeUnit.MILLISECONDS)?.let { return it }
        }
        throw java.io.EOFException("PCM buffer is closed")
    }

    fun trimToLatest(targetFrames: Int = 3) {
        while (frames.size > targetFrames) frames.poll()
    }

    override fun close() {
        closed.set(true)
        frames.clear()
    }

    companion object {
        const val SAMPLE_RATE = 48_000
        const val CHANNEL_COUNT = 2
        const val BYTES_PER_SAMPLE = 2
        const val FRAME_MILLISECONDS = 20
        const val FRAME_BYTES = SAMPLE_RATE * CHANNEL_COUNT * BYTES_PER_SAMPLE * FRAME_MILLISECONDS / 1_000
        const val MIPLAY_SAMPLES_PER_FRAME = 1_024
        const val MIPLAY_FRAME_BYTES = MIPLAY_SAMPLES_PER_FRAME * CHANNEL_COUNT * BYTES_PER_SAMPLE
    }
}
