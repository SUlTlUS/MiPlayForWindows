package com.suitius.dlnacast.service

import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.media.projection.MediaProjectionManager
import android.os.Build
import android.os.IBinder
import android.os.PowerManager
import android.util.Log
import androidx.core.app.NotificationCompat
import androidx.core.app.ServiceCompat
import com.suitius.dlnacast.MainActivity
import com.suitius.dlnacast.R
import com.suitius.dlnacast.audio.AudioChannelRoute
import com.suitius.dlnacast.miplay.MiPlayCastSession
import com.suitius.dlnacast.model.DlnaRenderer
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.NonCancellable
import kotlinx.coroutines.cancel
import kotlinx.coroutines.cancelAndJoin
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

class CastService : Service() {
    private val scope = CoroutineScope(Job() + Dispatchers.Main.immediate)
    private var session: CastSession? = null
    private var castingJob: Job? = null
    private var castWakeLock: PowerManager.WakeLock? = null

    override fun onCreate() {
        super.onCreate()
        createNotificationChannel()
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        when (intent?.action) {
            ACTION_START, ACTION_ROOT_START -> startCasting(intent)
            ACTION_STOP -> scope.launch { stopCasting(); stopSelf() }
            ACTION_PAUSE -> controlSession { it.pauseReceiver() }
            ACTION_RESUME -> controlSession { it.resumeReceiver() }
            ACTION_SET_VOLUME -> {
                val volume = intent.getIntExtra(EXTRA_VOLUME, -1)
                val receiverIndex = intent.getIntExtra(EXTRA_RECEIVER_INDEX, NO_RECEIVER_INDEX)
                    .takeUnless { it == NO_RECEIVER_INDEX }
                if (volume in 0..100) controlSession { it.setReceiverVolume(volume, receiverIndex) }
            }
            ACTION_SET_CAPTURE_UID -> {
                val uid = intent.getIntExtra(EXTRA_CAPTURE_UID, NO_CAPTURE_UID)
                    .takeUnless { it == NO_CAPTURE_UID }
                controlSession { it.setCaptureUid(uid) }
            }
            ACTION_ADD_RENDERER -> {
                val renderer = intent.singleRendererOrNull() ?: return START_NOT_STICKY
                controlMiPlaySession { it.addReceiver(renderer) }
            }
            ACTION_REMOVE_RENDERER -> {
                val rendererId = intent.getStringExtra(EXTRA_SINGLE_RENDERER_ID) ?: return START_NOT_STICKY
                controlMiPlaySession { it.removeReceiver(rendererId) }
            }
        }
        return START_NOT_STICKY
    }

    private fun startCasting(intent: Intent) {
        val renderers = intent.renderers()
        val renderer = renderers.firstOrNull()
        val protocol = intent.protocolOrDefault()
        val rootCapture = intent.getBooleanExtra(EXTRA_ROOT_CAPTURE, false)
        val projectionData = intent.projectionDataOrNull()
        val captureUid = intent.getIntExtra(EXTRA_CAPTURE_UID, NO_CAPTURE_UID).takeUnless { it == NO_CAPTURE_UID }
        val channelRoutes = intent.channelRoutes(renderers.size)
        val channelRoute = channelRoutes.firstOrNull() ?: AudioChannelRoute.Stereo
        val playbackDelayMicroseconds = intent.getIntExtra(
            EXTRA_PLAYBACK_DELAY_MICROSECONDS,
            DEFAULT_PLAYBACK_DELAY_MICROSECONDS,
        ).coerceIn(0, MAXIMUM_PLAYBACK_DELAY_MICROSECONDS)
        val projectionResultCode = intent.getIntExtra(EXTRA_PROJECTION_RESULT_CODE, Int.MIN_VALUE)
        if (renderer == null || (!rootCapture && (projectionData == null || projectionResultCode == Int.MIN_VALUE))) {
            CastStateStore.update(CastStatus.Error("投送参数不完整，请返回应用后重试"))
            stopSelf()
            return
        }

        ServiceCompat.startForeground(
            this,
            NOTIFICATION_ID,
            buildNotification("正在连接 ${renderers.joinToString { it.name }}"),
            if (rootCapture) {
                ServiceInfo.FOREGROUND_SERVICE_TYPE_MEDIA_PLAYBACK
            } else {
                ServiceInfo.FOREGROUND_SERVICE_TYPE_MEDIA_PROJECTION
            },
        )
        acquireCastWakeLock()
        CastStateStore.update(
            CastStatus.Preparing(if (rootCapture) "正在启动 Root 系统内录…" else "正在准备系统音频捕获…"),
        )
        castingJob?.cancel()
        castingJob = scope.launch {
            var candidate: CastSession? = null
            try {
                session?.let { withContext(Dispatchers.IO) { it.close() } }
                val projection = if (rootCapture) {
                    null
                } else {
                    val projectionManager = getSystemService(MediaProjectionManager::class.java)
                    projectionManager.getMediaProjection(projectionResultCode, projectionData!!)
                        ?: error("Android 未返回可用的录屏授权")
                }
                candidate = withContext(Dispatchers.IO) {
                    val projectionStopped: () -> Unit = {
                        scope.launch {
                            CastStateStore.update(CastStatus.Error("系统已结束音频捕获授权"))
                            stopCasting(resetStatus = false)
                            stopSelf()
                        }
                        Unit
                    }
                    when (protocol) {
                        CastProtocol.DLNA -> ActiveCastSession(
                            applicationContext,
                            renderer,
                            projection,
                            projectionStopped,
                            captureUid,
                            channelRoute,
                            rootCapture,
                        )
                        CastProtocol.MIPLAY -> MiPlayCastSession(
                            applicationContext,
                            renderers,
                            projection,
                            projectionStopped,
                            captureUid,
                            channelRoutes,
                            playbackDelayMicroseconds,
                            rootCapture,
                        ) { phase ->
                            scope.launch {
                                CastStateStore.update(CastStatus.Preparing(phase))
                                updateNotification(phase)
                            }
                            Unit
                        }
                    }
                }
                val newSession = candidate
                withContext(Dispatchers.IO) { newSession.start() }
                session = newSession
                val rendererNames = renderers.joinToString { it.name }
                updateNotification("正在投送到 $rendererNames")
                while (isActive && session === newSession) {
                    newSession.failureMessage?.let { error(it) }
                    Log.i(LOG_TAG, newSession.streamDetail)
                    CastStateStore.update(
                        CastStatus.Streaming(
                            rendererNames,
                            newSession.streamDetail,
                            newSession.supportsPlaybackControl,
                            newSession.receiverPaused,
                            newSession.receiverVolume,
                            newSession.receiverControls,
                            newSession.supportsCaptureSwitch,
                        ),
                    )
                    if (newSession.supportsPlaybackControl) {
                        updateNotification(
                            "正在投送到 $rendererNames · ${if (newSession.receiverPaused) "已暂停" else "播放中"}",
                            newSession.receiverPaused,
                        )
                    }
                    delay(2_000)
                }
            } catch (cancelled: CancellationException) {
                withContext(NonCancellable + Dispatchers.IO) { candidate?.close() }
                if (session === candidate) session = null
                throw cancelled
            } catch (exception: Exception) {
                Log.e(LOG_TAG, "Cast startup failed", exception)
                withContext(Dispatchers.IO) { candidate?.close() }
                session = null
                CastStateStore.update(CastStatus.Error(exception.message ?: exception.javaClass.simpleName))
                ServiceCompat.stopForeground(this@CastService, ServiceCompat.STOP_FOREGROUND_REMOVE)
                releaseCastWakeLock()
                stopSelf()
            }
        }
    }

    private fun controlSession(operation: suspend (CastSession) -> Unit) {
        scope.launch {
            val active = session
            if (active == null) {
                CastStateStore.update(CastStatus.Error("当前没有正在运行的投送会话"))
                return@launch
            }
            runCatching { withContext(Dispatchers.IO) { operation(active) } }
                .onFailure { CastStateStore.update(CastStatus.Error(it.message ?: "MiPlay 控制失败")) }
        }
    }

    private fun controlMiPlaySession(operation: suspend (MiPlayCastSession) -> Unit) {
        scope.launch {
            val active = session as? MiPlayCastSession
            if (active == null) {
                CastStateStore.update(CastStatus.Error("当前没有正在运行的 MiPlay 会话"))
                return@launch
            }
            runCatching { withContext(Dispatchers.IO) { operation(active) } }
                .onFailure { CastStateStore.update(CastStatus.Error(it.message ?: "MiPlay 设备切换失败")) }
        }
    }

    private suspend fun stopCasting(resetStatus: Boolean = true) {
        val job = castingJob
        castingJob = null
        job?.cancelAndJoin()
        val active = session
        session = null
        withContext(Dispatchers.IO) { active?.close() }
        ServiceCompat.stopForeground(this, ServiceCompat.STOP_FOREGROUND_REMOVE)
        releaseCastWakeLock()
        if (resetStatus) CastStateStore.update(CastStatus.Idle)
    }

    private fun acquireCastWakeLock() {
        val active = castWakeLock ?: getSystemService(PowerManager::class.java)
            .newWakeLock(PowerManager.PARTIAL_WAKE_LOCK, "$packageName:cast")
            .apply { setReferenceCounted(false) }
            .also { castWakeLock = it }
        if (!active.isHeld) active.acquire()
    }

    private fun releaseCastWakeLock() {
        castWakeLock?.takeIf { it.isHeld }?.release()
    }

    private fun buildNotification(message: String, receiverPaused: Boolean = false) =
        NotificationCompat.Builder(this, CHANNEL_ID)
        .setSmallIcon(R.drawable.ic_cast)
        .setContentTitle("MiPlay Cast")
        .setContentText(message)
        .setContentIntent(
            PendingIntent.getActivity(
                this,
                0,
                Intent(this, MainActivity::class.java),
                PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
            ),
        )
        .addAction(
            R.drawable.ic_cast,
            if (receiverPaused) "继续" else "暂停",
            PendingIntent.getService(
                this,
                if (receiverPaused) 3 else 2,
                if (receiverPaused) resumeIntent(this) else pauseIntent(this),
                PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
            ),
        )
        .addAction(
            R.drawable.ic_stop,
            "停止",
            PendingIntent.getService(
                this,
                1,
                stopIntent(this),
                PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
            ),
        )
        .setOngoing(true)
        .setCategory(NotificationCompat.CATEGORY_SERVICE)
        .build()

    private fun updateNotification(message: String, receiverPaused: Boolean = false) {
        getSystemService(NotificationManager::class.java)
            .notify(NOTIFICATION_ID, buildNotification(message, receiverPaused))
    }

    private fun createNotificationChannel() {
        val channel = NotificationChannel(
            CHANNEL_ID,
            "音频投送",
            NotificationManager.IMPORTANCE_LOW,
        ).apply { description = "显示正在运行的 MiPlay/DLNA 音频投送" }
        getSystemService(NotificationManager::class.java).createNotificationChannel(channel)
    }

    override fun onDestroy() {
        val active = session
        session = null
        scope.cancel()
        if (active != null) CoroutineScope(Dispatchers.IO).launch { active.close() }
        releaseCastWakeLock()
        super.onDestroy()
    }

    override fun onBind(intent: Intent?): IBinder? = null

    private fun Intent.renderers(): List<DlnaRenderer> {
        val ids = getStringArrayListExtra(EXTRA_RENDERER_ID).orEmpty()
        val names = getStringArrayListExtra(EXTRA_RENDERER_NAME).orEmpty()
        val descriptions = getStringArrayListExtra(EXTRA_RENDERER_DESCRIPTION_URL).orEmpty()
        val hosts = getStringArrayListExtra(EXTRA_RENDERER_HOST).orEmpty()
        val transports = getStringArrayListExtra(EXTRA_RENDERER_AV_TRANSPORT).orEmpty()
        val renderingControls = getStringArrayListExtra(EXTRA_RENDERER_RENDERING_CONTROL).orEmpty()
        val connectionManagers = getStringArrayListExtra(EXTRA_RENDERER_CONNECTION_MANAGER).orEmpty()
        if (listOf(names, descriptions, hosts, transports, renderingControls, connectionManagers).any { it.size != ids.size }) {
            return emptyList()
        }
        return ids.indices.map { index ->
            DlnaRenderer(
                ids[index],
                names[index],
                descriptions[index],
                hosts[index],
                transports[index],
                renderingControls[index].ifEmpty { null },
                connectionManagers[index].ifEmpty { null },
            )
        }
    }

    @Suppress("DEPRECATION")
    private fun Intent.projectionDataOrNull(): Intent? = if (Build.VERSION.SDK_INT >= 33) {
        getParcelableExtra(EXTRA_PROJECTION_DATA, Intent::class.java)
    } else {
        getParcelableExtra(EXTRA_PROJECTION_DATA)
    }

    companion object {
        private const val ACTION_START = "com.suitius.dlnacast.action.START"
        private const val ACTION_ROOT_START = "com.suitius.dlnacast.action.ROOT_START"
        private const val ACTION_STOP = "com.suitius.dlnacast.action.STOP"
        private const val ACTION_PAUSE = "com.suitius.dlnacast.action.PAUSE"
        private const val ACTION_RESUME = "com.suitius.dlnacast.action.RESUME"
        private const val ACTION_SET_VOLUME = "com.suitius.dlnacast.action.SET_VOLUME"
        private const val ACTION_SET_CAPTURE_UID = "com.suitius.dlnacast.action.SET_CAPTURE_UID"
        private const val ACTION_ADD_RENDERER = "com.suitius.dlnacast.action.ADD_RENDERER"
        private const val ACTION_REMOVE_RENDERER = "com.suitius.dlnacast.action.REMOVE_RENDERER"
        private const val EXTRA_VOLUME = "receiver_volume"
        private const val EXTRA_RECEIVER_INDEX = "receiver_index"
        private const val EXTRA_CAPTURE_UID = "capture_uid"
        private const val EXTRA_CHANNEL_ROUTE = "channel_route"
        private const val EXTRA_PLAYBACK_DELAY_MICROSECONDS = "playback_delay_microseconds"
        private const val NO_CAPTURE_UID = -1
        private const val NO_RECEIVER_INDEX = -1
        private const val MAX_MIPLAY_RECEIVERS = 63
        private const val MAX_DLNA_RECEIVERS = 1
        private const val DEFAULT_PLAYBACK_DELAY_MICROSECONDS = 1_000_000
        private const val MAXIMUM_PLAYBACK_DELAY_MICROSECONDS = 2_000_000
        private const val EXTRA_PROJECTION_RESULT_CODE = "projection_result_code"
        private const val EXTRA_PROJECTION_DATA = "projection_data"
        private const val EXTRA_ROOT_CAPTURE = "root_capture"
        private const val EXTRA_RENDERER_ID = "renderer_id"
        private const val EXTRA_RENDERER_NAME = "renderer_name"
        private const val EXTRA_RENDERER_DESCRIPTION_URL = "renderer_description_url"
        private const val EXTRA_RENDERER_HOST = "renderer_host"
        private const val EXTRA_RENDERER_AV_TRANSPORT = "renderer_av_transport"
        private const val EXTRA_RENDERER_RENDERING_CONTROL = "renderer_rendering_control"
        private const val EXTRA_RENDERER_CONNECTION_MANAGER = "renderer_connection_manager"
        private const val EXTRA_PROTOCOL = "cast_protocol"
        private const val EXTRA_SINGLE_RENDERER_ID = "single_renderer_id"
        private const val EXTRA_SINGLE_RENDERER_NAME = "single_renderer_name"
        private const val EXTRA_SINGLE_RENDERER_HOST = "single_renderer_host"
        private const val CHANNEL_ID = "cast-session"
        private const val NOTIFICATION_ID = 1001
        private const val LOG_TAG = "DLNACast.Cast"

        fun startIntent(
            context: Context,
            renderers: List<DlnaRenderer>,
            protocol: CastProtocol,
            projectionResultCode: Int,
            projectionData: Intent,
            captureUid: Int? = null,
            channelRoutes: List<AudioChannelRoute> = List(renderers.size) { AudioChannelRoute.Stereo },
            playbackDelayMicroseconds: Int = DEFAULT_PLAYBACK_DELAY_MICROSECONDS,
        ) = Intent(context, CastService::class.java).apply {
            require(renderers.isNotEmpty()) { "至少选择一台音箱" }
            require(renderers.size <= if (protocol == CastProtocol.MIPLAY) MAX_MIPLAY_RECEIVERS else MAX_DLNA_RECEIVERS) {
                "${protocol.name} 超过可选音箱数量上限"
            }
            require(channelRoutes.size == renderers.size) { "音箱数量和声道路由数量不一致" }
            action = ACTION_START
            putExtra(EXTRA_PROJECTION_RESULT_CODE, projectionResultCode)
            putExtra(EXTRA_PROJECTION_DATA, projectionData)
            putStringArrayListExtra(EXTRA_RENDERER_ID, ArrayList(renderers.map { it.id }))
            putStringArrayListExtra(EXTRA_RENDERER_NAME, ArrayList(renderers.map { it.name }))
            putStringArrayListExtra(EXTRA_RENDERER_DESCRIPTION_URL, ArrayList(renderers.map { it.descriptionUrl }))
            putStringArrayListExtra(EXTRA_RENDERER_HOST, ArrayList(renderers.map { it.host }))
            putStringArrayListExtra(EXTRA_RENDERER_AV_TRANSPORT, ArrayList(renderers.map { it.avTransportControlUrl }))
            putStringArrayListExtra(
                EXTRA_RENDERER_RENDERING_CONTROL,
                ArrayList(renderers.map { it.renderingControlUrl.orEmpty() }),
            )
            putStringArrayListExtra(
                EXTRA_RENDERER_CONNECTION_MANAGER,
                ArrayList(renderers.map { it.connectionManagerControlUrl.orEmpty() }),
            )
            putExtra(EXTRA_PROTOCOL, protocol.name)
            captureUid?.let { putExtra(EXTRA_CAPTURE_UID, it) }
            putStringArrayListExtra(EXTRA_CHANNEL_ROUTE, ArrayList(channelRoutes.map { it.name }))
            putExtra(
                EXTRA_PLAYBACK_DELAY_MICROSECONDS,
                playbackDelayMicroseconds.coerceIn(0, MAXIMUM_PLAYBACK_DELAY_MICROSECONDS),
            )
        }

        fun rootStartIntent(
            context: Context,
            renderers: List<DlnaRenderer>,
            protocol: CastProtocol = CastProtocol.MIPLAY,
            channelRoutes: List<AudioChannelRoute> = List(renderers.size) { AudioChannelRoute.Stereo },
            playbackDelayMicroseconds: Int = DEFAULT_PLAYBACK_DELAY_MICROSECONDS,
        ) = Intent(context, CastService::class.java).apply {
            require(renderers.isNotEmpty()) { "至少选择一台音箱" }
            require(renderers.size <= if (protocol == CastProtocol.MIPLAY) MAX_MIPLAY_RECEIVERS else MAX_DLNA_RECEIVERS) {
                "${protocol.name} 超过可选音箱数量上限"
            }
            require(channelRoutes.size == renderers.size) { "音箱数量和声道路由数量不一致" }
            action = ACTION_ROOT_START
            putExtra(EXTRA_ROOT_CAPTURE, true)
            putStringArrayListExtra(EXTRA_RENDERER_ID, ArrayList(renderers.map { it.id }))
            putStringArrayListExtra(EXTRA_RENDERER_NAME, ArrayList(renderers.map { it.name }))
            putStringArrayListExtra(EXTRA_RENDERER_DESCRIPTION_URL, ArrayList(renderers.map { it.descriptionUrl }))
            putStringArrayListExtra(EXTRA_RENDERER_HOST, ArrayList(renderers.map { it.host }))
            putStringArrayListExtra(EXTRA_RENDERER_AV_TRANSPORT, ArrayList(renderers.map { it.avTransportControlUrl }))
            putStringArrayListExtra(
                EXTRA_RENDERER_RENDERING_CONTROL,
                ArrayList(renderers.map { it.renderingControlUrl.orEmpty() }),
            )
            putStringArrayListExtra(
                EXTRA_RENDERER_CONNECTION_MANAGER,
                ArrayList(renderers.map { it.connectionManagerControlUrl.orEmpty() }),
            )
            putExtra(EXTRA_PROTOCOL, protocol.name)
            putStringArrayListExtra(EXTRA_CHANNEL_ROUTE, ArrayList(channelRoutes.map { it.name }))
            putExtra(
                EXTRA_PLAYBACK_DELAY_MICROSECONDS,
                playbackDelayMicroseconds.coerceIn(0, MAXIMUM_PLAYBACK_DELAY_MICROSECONDS),
            )
        }

        fun stopIntent(context: Context) = Intent(context, CastService::class.java).apply { action = ACTION_STOP }

        fun pauseIntent(context: Context) = Intent(context, CastService::class.java).apply { action = ACTION_PAUSE }

        fun resumeIntent(context: Context) = Intent(context, CastService::class.java).apply { action = ACTION_RESUME }

        fun setVolumeIntent(context: Context, volume: Int, receiverIndex: Int? = null) =
            Intent(context, CastService::class.java).apply {
            action = ACTION_SET_VOLUME
            putExtra(EXTRA_VOLUME, volume.coerceIn(0, 100))
            receiverIndex?.let { putExtra(EXTRA_RECEIVER_INDEX, it) }
        }

        fun setCaptureUidIntent(context: Context, captureUid: Int?) =
            Intent(context, CastService::class.java).apply {
                action = ACTION_SET_CAPTURE_UID
                putExtra(EXTRA_CAPTURE_UID, captureUid ?: NO_CAPTURE_UID)
            }

        fun addRendererIntent(context: Context, renderer: DlnaRenderer) =
            Intent(context, CastService::class.java).apply {
                action = ACTION_ADD_RENDERER
                putExtra(EXTRA_SINGLE_RENDERER_ID, renderer.id)
                putExtra(EXTRA_SINGLE_RENDERER_NAME, renderer.name)
                putExtra(EXTRA_SINGLE_RENDERER_HOST, renderer.host)
            }

        fun removeRendererIntent(context: Context, rendererId: String) =
            Intent(context, CastService::class.java).apply {
                action = ACTION_REMOVE_RENDERER
                putExtra(EXTRA_SINGLE_RENDERER_ID, rendererId)
            }
    }

    private fun Intent.protocolOrDefault(): CastProtocol =
        getStringExtra(EXTRA_PROTOCOL)?.let { value ->
            CastProtocol.entries.firstOrNull { it.name == value }
        } ?: CastProtocol.MIPLAY

    private fun Intent.channelRoutes(rendererCount: Int): List<AudioChannelRoute> {
        val stored = getStringArrayListExtra(EXTRA_CHANNEL_ROUTE).orEmpty()
            .mapNotNull { value -> AudioChannelRoute.entries.firstOrNull { it.name == value } }
        return stored.takeIf { it.size == rendererCount }
            ?: List(rendererCount) { AudioChannelRoute.Stereo }
    }

    private fun Intent.singleRendererOrNull(): DlnaRenderer? {
        val id = getStringExtra(EXTRA_SINGLE_RENDERER_ID) ?: return null
        val name = getStringExtra(EXTRA_SINGLE_RENDERER_NAME) ?: return null
        val host = getStringExtra(EXTRA_SINGLE_RENDERER_HOST) ?: return null
        return DlnaRenderer(id, name, "miplay://$host", host, "", null, null)
    }
}
