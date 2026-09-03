package com.suitius.dlnacast.audio

import org.junit.Assert.assertArrayEquals
import org.junit.Test

class AudioChannelRouteTest {
    private val stereo = byteArrayOf(1, 2, 3, 4, 5, 6, 7, 8)

    @Test
    fun duplicatesLeftChannelIntoBothOutputs() {
        assertArrayEquals(
            byteArrayOf(1, 2, 1, 2, 5, 6, 5, 6),
            AudioChannelRoute.LeftAsMono.applyToStereoPcm16(stereo),
        )
    }

    @Test
    fun duplicatesRightChannelIntoBothOutputs() {
        assertArrayEquals(
            byteArrayOf(3, 4, 3, 4, 7, 8, 7, 8),
            AudioChannelRoute.RightAsMono.applyToStereoPcm16(stereo),
        )
    }
}
