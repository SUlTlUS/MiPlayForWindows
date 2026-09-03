package com.suitius.dlnacast.service

import android.content.Context
import android.media.projection.MediaProjection
import android.util.Log
import com.suitius.dlnacast.audio.PcmFrameBuffer
import com.suitius.dlnacast.audio.PcmAudioCapture
import com.suitius.dlnacast.audio.PlaybackAudioCapture
import com.suitius.dlnacast.audio.RootPlaybackAudioCapture
import com.suitius.dlnacast.audio.AudioChannelRoute
import com.suitius.dlnacast.audio.RoutedPcmOutput
import com.suitius.dlnacast.model.DlnaRenderer
import com.suitius.dlnacast.network.UpnpSoapClient
import com.suitius.dlnacast.streaming.PcmWaveServer

class ActiveCastSession(
    context: Context,
    private val renderer: DlnaRenderer,
    projection: MediaProjection?,
    private val onProjectionStopped: () -> Unit,
    matchingUid: Int? = null,
    channelRoute: AudioChannelRoute = AudioChannelRoute.Stereo,
    rootCapture: Boolean = false,
) : CastSession {
    private val frames = PcmFrameBuffer()
    private val soapClient = UpnpSoapClient()
    private val capture: PcmAudioCapture = if (rootCapture) {
        RootPlaybackAudioCapture(
            context = context,
            outputs = listOf(RoutedPcmOutput(frames, channelRoute)),
        )
    } else {
        PlaybackAudioCapture(
            context = context,
            projection = requireNotNull(projection) { "MediaProjection capture requires a projection token" },
            outputs = listOf(RoutedPcmOutput(frames, channelRoute)),
            onProjectionStopped = onProjectionStopped,
            matchingUid = matchingUid,
        )
    }
    private val server = PcmWaveServer.create(renderer.host, frames)

    override val bufferedMilliseconds: Int get() = frames.bufferedMilliseconds
    override val overruns: Long get() = frames.overruns
    override val streamDetail: String
        get() = "DLNA · PCM/WAV · 缓冲 $bufferedMilliseconds ms · 丢帧 $overruns"
    override val failureMessage: String? get() = capture.failureMessage
    override val supportsCaptureSwitch: Boolean get() = capture.supportsUidFilter

    override suspend fun setCaptureUid(uid: Int?) = capture.setMatchingUid(uid)

    override suspend fun start() {
        server.start()
        Log.i(LOG_TAG, "PCM/WAV server ready at ${server.streamUri}")
        capture.start()
        Log.i(LOG_TAG, "Playback capture started")
        soapClient.setTransportUri(renderer, server.streamUri)
        Log.i(LOG_TAG, "SetAVTransportURI accepted by ${renderer.name}")
        soapClient.play(renderer)
        Log.i(LOG_TAG, "Play accepted by ${renderer.name}; waiting for stream client")
        check(server.waitForClient()) { "音箱在 5 秒内没有请求手机的直播地址" }
        Log.i(LOG_TAG, "Renderer connected to PCM/WAV stream")
    }

    override fun close() {
        server.close()
        capture.close()
        frames.close()
        soapClient.stop(renderer)
    }

    private companion object {
        const val LOG_TAG = "DLNACast.Cast"
    }
}
