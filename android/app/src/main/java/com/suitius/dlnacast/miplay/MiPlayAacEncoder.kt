package com.suitius.dlnacast.miplay

import com.suitius.dlnacast.audio.PcmFrameBuffer
import java.util.concurrent.ArrayBlockingQueue
import java.util.concurrent.Semaphore
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicBoolean
import kotlin.concurrent.thread

internal data class EncodedAccessUnit(
    val bytes: ByteArray,
    val presentationTimeUs: Long,
)

internal interface MiPlayAacCodec : AutoCloseable {
    fun queuePcm(bytes: ByteArray, presentationTimeUs: Long)
    fun readAccessUnit(): EncodedAccessUnit?
}

/** Port of MiPlaySharedRouteEncoder: independent input/output with bounded ordered AUs. */
internal class MiPlayAacEncoder(
    private val pcm: PcmFrameBuffer,
    private val codec: MiPlayAacCodec = AndroidMiPlayAacCodec(),
) : AutoCloseable {
    private val closed = AtomicBoolean(false)
    private val inputSlots = Semaphore(MiPlayAudioPipeline.INPUT_WINDOW)
    private val output = ArrayBlockingQueue<EncodedAccessUnit>(MiPlayAudioPipeline.OUTPUT_CAPACITY)
    @Volatile private var failure: Throwable? = null
    val bufferedAccessUnits: Int get() = output.size

    private val writer = worker("MiPlay-aac-input") {
        var index = 0L
        while (!closed.get()) {
            inputSlots.acquire()
            val frame = pcm.readFramePreservingSequence()
            check(frame.size == PcmFrameBuffer.MIPLAY_FRAME_BYTES)
            codec.queuePcm(frame, MiPlayAudioPipeline.presentationTimeUs(index++))
        }
    }
    private val reader = worker("MiPlay-aac-output") {
        while (!closed.get()) {
            codec.readAccessUnit()?.let { output.put(it) }
        }
    }

    private fun worker(name: String, action: () -> Unit): Thread = thread(name = name, isDaemon = true) {
        try {
            action()
        } catch (error: Throwable) {
            if (!closed.get()) failure = error
        }
    }

    fun nextAccessUnit(): EncodedAccessUnit {
        while (!closed.get()) {
            failure?.let { throw IllegalStateException("AAC encoder worker failed", it) }
            output.poll(50, TimeUnit.MILLISECONDS)?.let {
                // Keep ownership until the AAC is consumed, not merely encoded.
                inputSlots.release()
                return it
            }
        }
        error("AAC encoder is closed")
    }

    override fun close() {
        if (!closed.compareAndSet(false, true)) return
        writer.interrupt()
        reader.interrupt()
        writer.join(500)
        reader.join(500)
        codec.close()
        output.clear()
    }
}
