package com.suitius.dlnacast.miplay

import com.suitius.dlnacast.audio.PcmFrameBuffer
import java.util.concurrent.Executors
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.locks.LockSupport
import kotlin.concurrent.thread
import kotlin.math.abs

/**
 * Device-only encoder smoke check. Uses synthetic zero PCM, never records or opens a speaker.
 * Can run using app_process with debug APK + androidTest APK on CLASSPATH, without installation.
 */
object MiPlayAudioPipelineDeviceCheck {
    @JvmStatic
    fun main(args: Array<String>) {
        val executor = Executors.newSingleThreadExecutor { task ->
            Thread(task, "MiPlay-device-check").apply { isDaemon = true }
        }
        try {
            executor.submit {
                checkRoutes(1)
                checkRoutes(2)
            }.get(15, TimeUnit.SECONDS)
            println("PASS: real Android AAC codec; one/two route ordered pipeline; no capture/network/playback")
        } finally {
            executor.shutdownNow()
        }
    }

    private fun checkRoutes(routeCount: Int) {
        val buffers = List(routeCount) {
            PcmFrameBuffer(4, PcmFrameBuffer.MIPLAY_FRAME_BYTES, true)
        }
        val encoders = mutableListOf<MiPlayAacEncoder>()
        val closed = AtomicBoolean(false)
        var producer: Thread? = null
        try {
            buffers.forEach { encoders += MiPlayAacEncoder(it) }
            val startedAt = System.nanoTime()
            producer = thread(name = "MiPlay-test-zero-pcm", isDaemon = true) {
                try {
                    var index = 0L
                    while (!closed.get()) {
                        val deadline = startedAt + MiPlayAudioPipeline.presentationTimeUs(index++) * 1_000L
                        while (!closed.get() && System.nanoTime() < deadline) {
                            LockSupport.parkNanos((deadline - System.nanoTime()).coerceAtLeast(1))
                        }
                        buffers.forEach { it.offer(ByteArray(PcmFrameBuffer.MIPLAY_FRAME_BYTES)) }
                    }
                } catch (_: InterruptedException) { }
            }
            val runway = MiPlayAudioPipeline.prewarm { encoders.map { it.nextAccessUnit() } }
            val firstPts = LongArray(routeCount)
            var totalBytes = 0L
            repeat(94) { index ->
                val units = if (runway.isNotEmpty()) runway.removeFirst() else encoders.map { it.nextAccessUnit() }
                units.forEachIndexed { route, au ->
                    check(au.bytes.isNotEmpty())
                    if (index == 0) firstPts[route] = au.presentationTimeUs
                    val expected = MiPlayAudioPipeline.presentationTimeUs(index.toLong())
                    check(abs(au.presentationTimeUs - firstPts[route] - expected) <= 2) {
                        "Codec timestamp discontinuity route=$route AU=$index pts=${au.presentationTimeUs}"
                    }
                    totalBytes += au.bytes.size
                }
            }
            val elapsedMs = (System.nanoTime() - startedAt) / 1_000_000
            check(buffers.all { it.overruns == 0L })
            println("routes=$routeCount AU=94/route bytes=$totalBytes elapsedMs=$elapsedMs drops=0 orderedPts=true")
        } finally {
            closed.set(true)
            buffers.forEach { it.close() }
            producer?.interrupt()
            encoders.forEach { it.close() }
            producer?.join(500)
        }
    }
}
