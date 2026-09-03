package com.suitius.dlnacast.audio

enum class AudioChannelRoute {
    Stereo,
    LeftAsMono,
    RightAsMono,
}

internal fun AudioChannelRoute.applyToStereoPcm16(frame: ByteArray): ByteArray {
    if (this == AudioChannelRoute.Stereo) return frame
    val routed = frame.copyOf()
    var offset = 0
    while (offset + 3 < routed.size) {
        val sourceOffset = if (this == AudioChannelRoute.LeftAsMono) offset else offset + 2
        val low = routed[sourceOffset]
        val high = routed[sourceOffset + 1]
        routed[offset] = low
        routed[offset + 1] = high
        routed[offset + 2] = low
        routed[offset + 3] = high
        offset += 4
    }
    return routed
}
