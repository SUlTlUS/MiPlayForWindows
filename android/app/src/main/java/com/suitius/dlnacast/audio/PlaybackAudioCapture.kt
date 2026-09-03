package com.suitius.dlnacast.audio

import android.Manifest
import android.content.Context
import android.content.pm.PackageManager
import android.media.AudioAttributes
import android.media.AudioFormat
import android.media.AudioPlaybackCaptureConfiguration
import android.media.AudioRecord
import android.media.projection.MediaProjection
import android.os.Handler
import android.os.Looper
import androidx.core.content.ContextCompat
import java.util.concurrent.atomic.AtomicBoolean
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.cancel
import kotlinx.coroutines.cancelAndJoin
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch

interface PcmAudioCapture : AutoCloseable {
    val supportsUidFilter: Boolean
    val failureMessage: String?
        get() = null
    fun start()
    suspend fun setMatchingUid(matchingUid: Int?)
}

class PlaybackAudioCapture(
    private val context: Context,
    private val projection: MediaProjection,
    private val outputs: List<RoutedPcmOutput>,
    private val onProjectionStopped: () -> Unit,
    matchingUid: Int? = null,
) : PcmAudioCapture {
    init {
        require(outputs.isNotEmpty()) { "至少需要一个 PCM 捕获输出" }
    }
    private val scope = CoroutineScope(Job() + Dispatchers.IO)
    private val closed = AtomicBoolean(false)
    private var audioRecord: AudioRecord? = null
    private var recordJob: Job? = null
    @Volatile private var activeMatchingUid: Int? = matchingUid
    private var callbackRegistered = false

    override val supportsUidFilter: Boolean = true

    private val projectionCallback = object : MediaProjection.Callback() {
        override fun onStop() {
            if (!closed.get()) onProjectionStopped()
        }
    }

    override fun start() {
        check(audioRecord == null) { "Audio capture is already running" }
        check(ContextCompat.checkSelfPermission(context, Manifest.permission.RECORD_AUDIO) == PackageManager.PERMISSION_GRANTED) {
            "录音权限已被撤销，请重新授权"
        }
        if (!callbackRegistered) {
            projection.registerCallback(projectionCallback, Handler(Looper.getMainLooper()))
            callbackRegistered = true
        }
        startRecorder(activeMatchingUid)
    }

    override suspend fun setMatchingUid(matchingUid: Int?) {
        check(!closed.get()) { "音频捕获已经关闭" }
        if (matchingUid == activeMatchingUid) return
        stopRecorder()
        outputs.forEach { it.buffer.trimToLatest(0) }
        activeMatchingUid = matchingUid
        startRecorder(matchingUid)
    }

    private fun startRecorder(matchingUid: Int?) {
        check(
            ContextCompat.checkSelfPermission(context, Manifest.permission.RECORD_AUDIO) ==
                PackageManager.PERMISSION_GRANTED,
        ) { "录音权限已被撤销，请重新授权" }
        val captureBuilder = AudioPlaybackCaptureConfiguration.Builder(projection)
            .addMatchingUsage(AudioAttributes.USAGE_MEDIA)
            .addMatchingUsage(AudioAttributes.USAGE_GAME)
            .addMatchingUsage(AudioAttributes.USAGE_UNKNOWN)
        matchingUid?.let(captureBuilder::addMatchingUid)
        val captureConfig = captureBuilder.build()
        val format = AudioFormat.Builder()
            .setEncoding(AudioFormat.ENCODING_PCM_16BIT)
            .setSampleRate(PcmFrameBuffer.SAMPLE_RATE)
            .setChannelMask(AudioFormat.CHANNEL_IN_STEREO)
            .build()
        val minimumBuffer = AudioRecord.getMinBufferSize(
            PcmFrameBuffer.SAMPLE_RATE,
            AudioFormat.CHANNEL_IN_STEREO,
            AudioFormat.ENCODING_PCM_16BIT,
        )
        val record = AudioRecord.Builder()
            .setAudioFormat(format)
            .setBufferSizeInBytes(maxOf(minimumBuffer, PcmFrameBuffer.FRAME_BYTES * 4))
            .setAudioPlaybackCaptureConfig(captureConfig)
            .build()
        check(record.state == AudioRecord.STATE_INITIALIZED) { "Android playback capture could not be initialized" }
        audioRecord = record
        record.startRecording()
        recordJob = scope.launch {
            val frame = ByteArray(PcmFrameBuffer.FRAME_BYTES)
            while (isActive && !closed.get()) {
                val read = record.read(frame, 0, frame.size, AudioRecord.READ_BLOCKING)
                when {
                    read > 0 -> {
                        val captured = frame.copyOf(read)
                        outputs.forEach { target ->
                            target.buffer.offer(target.route.applyToStereoPcm16(captured))
                        }
                    }
                    read == AudioRecord.ERROR_DEAD_OBJECT -> break
                    read < 0 && isActive -> error("AudioRecord.read failed with code $read")
                }
            }
        }
    }

    private suspend fun stopRecorder() {
        val record = audioRecord
        audioRecord = null
        if (record != null) runCatching { record.stop() }
        recordJob?.cancelAndJoin()
        recordJob = null
        record?.release()
    }

    override fun close() {
        if (!closed.compareAndSet(false, true)) return
        audioRecord?.let { record ->
            runCatching { record.stop() }
            record.release()
        }
        audioRecord = null
        recordJob?.cancel()
        recordJob = null
        scope.cancel()
        if (callbackRegistered) projection.unregisterCallback(projectionCallback)
        projection.stop()
    }
}

data class RoutedPcmOutput(
    val buffer: PcmFrameBuffer,
    val route: AudioChannelRoute,
)
