package com.suitius.dlnacast.systemui

import android.content.ContentProvider
import android.content.ContentValues
import android.content.pm.PackageManager
import android.database.Cursor
import android.database.MatrixCursor
import android.net.Uri
import android.os.Binder
import android.os.Bundle
import android.os.Process
import android.util.Log
import com.suitius.dlnacast.model.DlnaRenderer
import com.suitius.dlnacast.network.MiPlayMdnsDiscovery
import com.suitius.dlnacast.service.CastPreferences
import com.suitius.dlnacast.service.CastService
import com.suitius.dlnacast.service.CastStateStore
import com.suitius.dlnacast.service.CastStatus
import java.util.UUID
import java.util.concurrent.ConcurrentHashMap
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeoutOrNull

class SystemUiBridgeProvider : ContentProvider() {
    private val cacheLock = Any()
    private var cachedRenderers: List<DlnaRenderer> = emptyList()
    private var cacheTimeMillis: Long = 0

    override fun onCreate(): Boolean = true

    override fun query(
        uri: Uri,
        projection: Array<out String>?,
        selection: String?,
        selectionArgs: Array<out String>?,
        sortOrder: String?,
    ): Cursor {
        enforceAllowedCaller()
        require(uri.authority == AUTHORITY && uri.path == SPEAKERS_PATH) {
            "Unsupported SystemUI bridge URI"
        }
        val appContext = requireNotNull(context).applicationContext
        val enabled = SystemUiCastPreferences(appContext).load().enabled
        val preferences = runCatching { CastPreferences(appContext) }.getOrNull()
        val discovered = if (enabled) refreshSpeakers() else synchronized(cacheLock) { cachedRenderers }
        val status = CastStateStore.status.value
        val activeIds = status.activeRendererIds(preferences?.rendererIds.orEmpty())
        val renderers = systemUiRenderers(discovered, status, activeIds, enabled)
        val receiverVolumes = (status as? CastStatus.Streaming)
            ?.receiverControls
            .orEmpty()
            .associate { it.name to it.volume }

        return MatrixCursor(COLUMNS).apply {
            renderers.forEach { renderer ->
                addRow(
                    arrayOf<Any?>(
                        renderer.id,
                        renderer.name,
                        renderer.host,
                        renderer.descriptionUrl,
                        renderer.avTransportControlUrl,
                        renderer.renderingControlUrl.orEmpty(),
                        renderer.connectionManagerControlUrl.orEmpty(),
                        if (status.matchesActiveRenderer(renderer.id, renderer.host, activeIds)) 1 else 0,
                        status.bridgeStateForRenderer(renderer.id, activeIds, renderer.host),
                        receiverVolumes[renderer.name] ?: -1,
                    ),
                )
            }
            setNotificationUri(appContext.contentResolver, SPEAKERS_URI)
        }
    }

    override fun call(method: String, arg: String?, extras: Bundle?): Bundle {
        enforceAllowedCaller()
        return when (method) {
            METHOD_PREPARE_SELECTION -> prepareSelection(arg)
            METHOD_STATUS -> currentStatus()
            METHOD_STOP -> {
                val appContext = requireNotNull(context).applicationContext
                appContext.startService(CastService.stopIntent(appContext))
                Bundle.EMPTY
            }
            else -> Bundle().apply { putString(KEY_ERROR, "Unsupported method: $method") }
        }
    }

    private fun currentStatus(): Bundle {
        val appContext = requireNotNull(context).applicationContext
        val status = CastStateStore.status.value
        val activeIds = status.activeRendererIds(CastPreferences(appContext).rendererIds)
        return Bundle().apply {
            putBoolean(KEY_ENABLED, SystemUiCastPreferences(appContext).load().enabled)
            putString(KEY_STATE, status.bridgeState)
            putStringArrayList(KEY_RENDERER_IDS, ArrayList(activeIds))
            putStringArrayList(KEY_RENDERER_HOSTS, ArrayList(status.activeRendererHosts()))
        }
    }

    private fun prepareSelection(rendererId: String?): Bundle {
        if (!SystemUiCastPreferences(requireNotNull(context)).load().enabled) {
            return Bundle().apply { putString(KEY_ERROR, "SystemUI casting is disabled") }
        }
        val renderer = synchronized(cacheLock) {
            cachedRenderers.firstOrNull { it.id == rendererId }
        } ?: return Bundle().apply { putString(KEY_ERROR, "MiPlay speaker is no longer available") }
        val nonce = UUID.randomUUID().toString()
        pendingSelections[nonce] = renderer
        return Bundle().apply { putString(KEY_NONCE, nonce) }
    }

    private fun refreshSpeakers(): List<DlnaRenderer> {
        val now = System.currentTimeMillis()
        synchronized(cacheLock) {
            if (cachedRenderers.isNotEmpty() && now - cacheTimeMillis < CACHE_DURATION_MILLISECONDS) {
                return cachedRenderers
            }
        }
        val appContext = requireNotNull(context).applicationContext
        Log.i(LOG_TAG, "Starting MiPlay mDNS discovery for SystemUI")
        val discovered = runCatching {
            runBlocking {
                withContext(Dispatchers.IO) {
                    withTimeoutOrNull(DISCOVERY_TIMEOUT_MILLISECONDS) {
                        MiPlayMdnsDiscovery(appContext).discover(DISCOVERY_REQUEST_MILLISECONDS)
                    }.orEmpty()
                }
            }
        }.onFailure { error ->
            Log.e(LOG_TAG, "MiPlay mDNS discovery failed", error)
        }.getOrDefault(emptyList())
        Log.i(LOG_TAG, "MiPlay mDNS discovery returned ${discovered.size} speaker(s)")
        synchronized(cacheLock) {
            if (discovered.isNotEmpty()) cachedRenderers = discovered
            cacheTimeMillis = now
            return cachedRenderers
        }
    }

    private fun enforceAllowedCaller() {
        val callingUid = Binder.getCallingUid()
        if (callingUid == Process.myUid() || callingUid == 0 || callingUid == SHELL_UID) return
        val packages = requireNotNull(context).packageManager.getPackagesForUid(callingUid).orEmpty()
        if (SYSTEM_UI_PACKAGE !in packages) {
            throw SecurityException("Only SystemUI may use the MiPlay bridge")
        }
    }

    override fun getType(uri: Uri): String = MIME_TYPE

    override fun insert(uri: Uri, values: ContentValues?): Uri? = null

    override fun delete(uri: Uri, selection: String?, selectionArgs: Array<out String>?): Int = 0

    override fun update(
        uri: Uri,
        values: ContentValues?,
        selection: String?,
        selectionArgs: Array<out String>?,
    ): Int = 0

    companion object {
        const val AUTHORITY = "com.suitius.dlnacast.systemui"
        const val ACTION_SYSTEM_UI_SELECTION = "com.suitius.dlnacast.action.SYSTEM_UI_SELECTION"
        const val EXTRA_NONCE = "system_ui_selection_nonce"
        const val EXTRA_RENDERER_ID = "system_ui_renderer_id"
        const val EXTRA_RENDERER_NAME = "system_ui_renderer_name"
        const val EXTRA_RENDERER_HOST = "system_ui_renderer_host"
        const val METHOD_PREPARE_SELECTION = "prepareSelection"
        const val METHOD_STATUS = "status"
        const val METHOD_STOP = "stop"
        const val KEY_NONCE = "nonce"
        const val KEY_ERROR = "error"
        const val KEY_STATE = "state"
        const val KEY_ENABLED = "enabled"
        const val KEY_RENDERER_IDS = "rendererIds"
        const val KEY_RENDERER_HOSTS = "rendererHosts"
        val SPEAKERS_URI: Uri = Uri.parse("content://$AUTHORITY/speakers")

        private const val SYSTEM_UI_PACKAGE = "com.android.systemui"
        private const val SPEAKERS_PATH = "/speakers"
        private const val MIME_TYPE = "vnd.android.cursor.dir/vnd.suitius.miplay-speaker"
        private const val SHELL_UID = 2000
        private const val LOG_TAG = "SystemUiBridge"
        private const val CACHE_DURATION_MILLISECONDS = 5_000L
        private const val DISCOVERY_TIMEOUT_MILLISECONDS = 7_000L
        private const val DISCOVERY_REQUEST_MILLISECONDS = 4_000L
        private val pendingSelections = ConcurrentHashMap<String, DlnaRenderer>()

        val COLUMNS = arrayOf(
            "id",
            "name",
            "host",
            "description_url",
            "av_transport_url",
            "rendering_control_url",
            "connection_manager_url",
            "selected",
            "state",
            "volume",
        )

        @JvmStatic
        fun consumePendingSelection(nonce: String?): DlnaRenderer? =
            nonce?.let(pendingSelections::remove)
    }
}

internal fun systemUiRenderers(
    discovered: List<DlnaRenderer>,
    status: CastStatus,
    activeIds: Set<String>,
    enabled: Boolean,
): List<DlnaRenderer> {
    val renderers = discovered.toMutableList()
    (status as? CastStatus.Streaming)?.receiverControls.orEmpty().forEach { receiver ->
        if (receiver.rendererId.isNotBlank() && receiver.rendererHost.isNotBlank() &&
            renderers.none { it.id == receiver.rendererId || it.host == receiver.rendererHost }
        ) {
            renderers += DlnaRenderer(
                receiver.rendererId, receiver.name, "miplay://${receiver.rendererHost}",
                receiver.rendererHost, "", null, null,
            )
        }
    }
    return if (enabled) renderers else renderers.filter {
        status.matchesActiveRenderer(it.id, it.host, activeIds)
    }
}

internal fun CastStatus.activeRendererIds(savedIds: Collection<String>): Set<String> =
    when (this) {
        is CastStatus.Preparing -> savedIds.toSet()
        is CastStatus.Streaming -> receiverControls.map { it.rendererId }.toSet()
        else -> emptySet()
    }

internal fun CastStatus.activeRendererHosts(): List<String> =
    (this as? CastStatus.Streaming)?.receiverControls?.map { it.rendererHost }.orEmpty()

internal fun CastStatus.matchesActiveRenderer(
    rendererId: String,
    rendererHost: String,
    activeIds: Set<String>,
): Boolean = rendererId in activeIds ||
    (rendererHost.isNotBlank() && rendererHost in activeRendererHosts())

internal fun CastStatus.bridgeStateForRenderer(
    rendererId: String,
    activeIds: Set<String>,
    rendererHost: String = "",
): String = if (matchesActiveRenderer(rendererId, rendererHost, activeIds)) bridgeState else "idle"

private val CastStatus.bridgeState: String
    get() = when (this) {
        CastStatus.Idle -> "idle"
        is CastStatus.Preparing -> "preparing"
        is CastStatus.Streaming -> if (receiverPaused) "paused" else "streaming"
        is CastStatus.Error -> "error"
    }
