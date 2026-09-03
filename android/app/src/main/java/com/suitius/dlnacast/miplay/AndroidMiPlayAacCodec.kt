package com.suitius.dlnacast.miplay

import android.media.MediaCodec
import android.media.MediaCodecInfo
import android.media.MediaFormat
import android.util.Log
import com.suitius.dlnacast.audio.PcmFrameBuffer
import java.util.concurrent.atomic.AtomicBoolean

/** Platform adapter only: Windows uses aac_mf; Android uses AAC-LC MediaCodec. */
internal class AndroidMiPlayAacCodec : MiPlayAacCodec {
    private val codec = MediaCodec.createEncoderByType(MediaFormat.MIMETYPE_AUDIO_AAC)
    private val bufferInfo = MediaCodec.BufferInfo()
    private val closed = AtomicBoolean(false)

    init {
        try {
            val format = MediaFormat.createAudioFormat(MediaFormat.MIMETYPE_AUDIO_AAC,
                PcmFrameBuffer.SAMPLE_RATE, PcmFrameBuffer.CHANNEL_COUNT).apply {
                setInteger(MediaFormat.KEY_AAC_PROFILE, MediaCodecInfo.CodecProfileLevel.AACObjectLC)
                setInteger(MediaFormat.KEY_BIT_RATE, MiPlayAudioPipeline.BIT_RATE)
                setInteger(MediaFormat.KEY_MAX_INPUT_SIZE, PcmFrameBuffer.MIPLAY_FRAME_BYTES)
            }
            codec.configure(format, null, null, MediaCodec.CONFIGURE_FLAG_ENCODE)
            codec.start()
            Log.i(LOG_TAG, "AAC encoder started: ${codec.name}; Windows pipeline 48k/1024/256k")
        } catch (error: Throwable) {
            codec.release()
            throw error
        }
    }

    override fun queuePcm(bytes: ByteArray, presentationTimeUs: Long) {
        while (!closed.get() && !Thread.currentThread().isInterrupted) {
            val index = codec.dequeueInputBuffer(10_000)
            if (index < 0) continue
            val input = codec.getInputBuffer(index) ?: error("AAC input buffer is unavailable")
            input.clear()
            check(input.remaining() >= bytes.size)
            input.put(bytes)
            codec.queueInputBuffer(index, 0, bytes.size, presentationTimeUs, 0)
            return
        }
        throw InterruptedException("AAC input stopped")
    }

    override fun readAccessUnit(): EncodedAccessUnit? {
        val index = codec.dequeueOutputBuffer(bufferInfo, 10_000)
        if (index == MediaCodec.INFO_OUTPUT_FORMAT_CHANGED) {
            Log.i(LOG_TAG, "AAC output format: ${codec.outputFormat}")
        }
        if (index < 0) return null
        try {
            if (bufferInfo.flags and MediaCodec.BUFFER_FLAG_CODEC_CONFIG != 0 || bufferInfo.size == 0) return null
            val buffer = codec.getOutputBuffer(index) ?: error("AAC output buffer is unavailable")
            buffer.position(bufferInfo.offset)
            buffer.limit(bufferInfo.offset + bufferInfo.size)
            return EncodedAccessUnit(ByteArray(bufferInfo.size).also(buffer::get), bufferInfo.presentationTimeUs)
        } finally {
            codec.releaseOutputBuffer(index, false)
        }
    }

    override fun close() {
        if (!closed.compareAndSet(false, true)) return
        runCatching { codec.stop() }
        codec.release()
    }

    private companion object { const val LOG_TAG = "DLNACast.MiPlay" }
}
