package com.suitius.dlnacast.systemui

import android.content.Context
import android.content.Intent
import androidx.core.content.edit
import com.suitius.dlnacast.audio.AudioChannelRoute
import com.suitius.dlnacast.model.DlnaRenderer
import com.suitius.dlnacast.service.CastPreferences
import com.suitius.dlnacast.service.CastProtocol
import com.suitius.dlnacast.service.CastService

data class SystemUiCastConfig(
    val enabled: Boolean = true,
    val playbackDelayMilliseconds: Int = 1_000,
    val channelRoute: AudioChannelRoute = AudioChannelRoute.Stereo,
) {
    val playbackDelayMicroseconds: Int
        get() = playbackDelayMilliseconds.coerceIn(0, 2_000) * 1_000

    fun channelRoutes(rendererCount: Int): List<AudioChannelRoute> {
        require(rendererCount in 1..2)
        return List(rendererCount) { channelRoute }
    }
}

/** Independent of the app's device selection and playback form. */
class SystemUiCastPreferences(context: Context) {
    private val appContext = context.applicationContext
    private val preferences = appContext.getSharedPreferences("system_ui_cast", Context.MODE_PRIVATE)

    fun load(): SystemUiCastConfig = SystemUiCastConfig(
        enabled = preferences.getBoolean(KEY_ENABLED, true),
        playbackDelayMilliseconds = preferences.getInt(KEY_DELAY_MS, 1_000).coerceIn(0, 2_000),
        channelRoute = AudioChannelRoute.entries.firstOrNull {
            it.name == preferences.getString(KEY_CHANNEL_ROUTE, null)
        } ?: AudioChannelRoute.Stereo,
    )

    fun save(config: SystemUiCastConfig) {
        preferences.edit {
            putBoolean(KEY_ENABLED, config.enabled)
            putInt(KEY_DELAY_MS, config.playbackDelayMilliseconds.coerceIn(0, 2_000))
            putString(KEY_CHANNEL_ROUTE, config.channelRoute.name)
        }
        appContext.contentResolver.notifyChange(SystemUiBridgeProvider.SPEAKERS_URI, null)
    }

    private companion object {
        const val KEY_ENABLED = "enabled"
        const val KEY_DELAY_MS = "playback_delay_ms"
        const val KEY_CHANNEL_ROUTE = "channel_route"
    }
}

/** Both the background control receiver and Activity fallback use the same settings. */
internal fun systemUiStartIntent(context: Context, renderer: DlnaRenderer): Intent? {
    val config = SystemUiCastPreferences(context).load()
    if (!config.enabled) return null
    CastPreferences(context).saveRendererIds(listOf(renderer.id))
    return CastService.rootStartIntent(
        context = context,
        renderers = listOf(renderer),
        protocol = CastProtocol.MIPLAY,
        channelRoutes = config.channelRoutes(1),
        playbackDelayMicroseconds = config.playbackDelayMicroseconds,
    )
}
