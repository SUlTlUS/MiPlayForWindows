package com.suitius.dlnacast.streaming

import com.suitius.dlnacast.audio.PcmFrameBuffer
import java.nio.ByteBuffer
import java.nio.ByteOrder

object PcmWaveHeader {
    fun createIndefinite(): ByteArray {
        val byteRate = PcmFrameBuffer.SAMPLE_RATE * PcmFrameBuffer.CHANNEL_COUNT * PcmFrameBuffer.BYTES_PER_SAMPLE
        val blockAlign = PcmFrameBuffer.CHANNEL_COUNT * PcmFrameBuffer.BYTES_PER_SAMPLE
        return ByteBuffer.allocate(44).order(ByteOrder.LITTLE_ENDIAN).apply {
            put("RIFF".toByteArray(Charsets.US_ASCII))
            putInt(Int.MAX_VALUE)
            put("WAVE".toByteArray(Charsets.US_ASCII))
            put("fmt ".toByteArray(Charsets.US_ASCII))
            putInt(16)
            putShort(1)
            putShort(PcmFrameBuffer.CHANNEL_COUNT.toShort())
            putInt(PcmFrameBuffer.SAMPLE_RATE)
            putInt(byteRate)
            putShort(blockAlign.toShort())
            putShort((PcmFrameBuffer.BYTES_PER_SAMPLE * 8).toShort())
            put("data".toByteArray(Charsets.US_ASCII))
            putInt(Int.MAX_VALUE - 36)
        }.array()
    }
}
