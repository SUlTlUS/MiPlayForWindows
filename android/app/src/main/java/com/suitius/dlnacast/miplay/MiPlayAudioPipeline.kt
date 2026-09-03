package com.suitius.dlnacast.miplay

import com.suitius.dlnacast.audio.PcmFrameBuffer

/** Constants and sample clock ported from the Windows shared encoder pipeline. */
internal object MiPlayAudioPipeline {
    const val INPUT_WINDOW = 12
    const val OUTPUT_CAPACITY = 12
    const val STARTUP_ACCESS_UNITS = 14
    const val PCM_BUFFER_FRAMES = 4 // floor(100 ms / (1024 / 48000 seconds))
    const val BIT_RATE = 256_000

    fun presentationTimeUs(index: Long): Long {
        require(index >= 0)
        return index * PcmFrameBuffer.MIPLAY_SAMPLES_PER_FRAME * 1_000_000L / PcmFrameBuffer.SAMPLE_RATE
    }

    fun <T> prewarm(read: () -> T): ArrayDeque<T> = ArrayDeque<T>().apply {
        repeat(STARTUP_ACCESS_UNITS) { addLast(read()) }
    }
}

/** PcmFrameBuffer.ReadFrameWithTimingOrSilenceAsync's shared producer clock. */
internal class MiPlayPcmCadence {
    private var next = 0L

    fun nextDeadline(nowNanos: Long): Long {
        val due = if (next == 0L || nowNanos - next > FRAME_NANOS) nowNanos else next
        next = due + FRAME_NANOS
        return due
    }

    companion object {
        const val FRAME_NANOS = 1_024L * 1_000_000_000L / 48_000L
    }
}
