package com.suitius.dlnacast.systemui

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import androidx.core.content.ContextCompat
import com.suitius.dlnacast.model.DlnaRenderer
import com.suitius.dlnacast.service.CastPreferences
import com.suitius.dlnacast.service.CastService

class SystemUiControlReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        val preferences = CastPreferences(context)
        if ((intent.action == ACTION_ROOT_START || intent.action == ACTION_ADD_RENDERER) &&
            !SystemUiCastPreferences(context).load().enabled
        ) {
            context.contentResolver.notifyChange(SystemUiBridgeProvider.SPEAKERS_URI, null)
            return
        }
        val serviceIntent = when (intent.action) {
            ACTION_ROOT_START -> rendererFrom(intent)?.let {
                systemUiStartIntent(context, it)
            }
            ACTION_ADD_RENDERER -> rendererFrom(intent)?.let {
                preferences.saveRendererIds((preferences.rendererIds + it.id).distinct())
                CastService.addRendererIntent(context, it)
            }
            ACTION_REMOVE_RENDERER -> intent.getStringExtra(EXTRA_RENDERER_ID)?.let {
                preferences.saveRendererIds(preferences.rendererIds.filterNot { id -> id == it })
                CastService.removeRendererIntent(context, it)
            }
            ACTION_SET_VOLUME -> CastService.setVolumeIntent(
                context,
                intent.getIntExtra(EXTRA_VOLUME, 50),
                intent.getIntExtra(EXTRA_RECEIVER_INDEX, NO_RECEIVER_INDEX)
                    .takeUnless { it == NO_RECEIVER_INDEX },
            )
            ACTION_STOP -> {
                preferences.saveRendererIds(emptyList())
                CastService.stopIntent(context)
            }
            else -> null
        } ?: return
        if (intent.action == ACTION_ROOT_START) {
            ContextCompat.startForegroundService(context, serviceIntent)
        } else {
            context.startService(serviceIntent)
        }
    }

    private fun rendererFrom(intent: Intent): DlnaRenderer? {
        val id = intent.getStringExtra(EXTRA_RENDERER_ID) ?: return null
        val name = intent.getStringExtra(EXTRA_RENDERER_NAME) ?: return null
        val host = intent.getStringExtra(EXTRA_RENDERER_HOST) ?: return null
        return DlnaRenderer(
            id = id,
            name = name,
            descriptionUrl = "miplay://$host",
            host = host,
            avTransportControlUrl = "",
            renderingControlUrl = null,
            connectionManagerControlUrl = null,
        )
    }

    companion object {
        const val ACTION_ADD_RENDERER = "com.suitius.dlnacast.action.SYSTEM_UI_ADD_RENDERER"
        const val ACTION_ROOT_START = "com.suitius.dlnacast.action.SYSTEM_UI_ROOT_START"
        const val ACTION_REMOVE_RENDERER = "com.suitius.dlnacast.action.SYSTEM_UI_REMOVE_RENDERER"
        const val ACTION_SET_VOLUME = "com.suitius.dlnacast.action.SYSTEM_UI_SET_VOLUME"
        const val ACTION_STOP = "com.suitius.dlnacast.action.SYSTEM_UI_STOP"
        const val EXTRA_RENDERER_ID = "system_ui_renderer_id"
        const val EXTRA_RENDERER_NAME = "system_ui_renderer_name"
        const val EXTRA_RENDERER_HOST = "system_ui_renderer_host"
        const val EXTRA_VOLUME = "system_ui_volume"
        const val EXTRA_RECEIVER_INDEX = "system_ui_receiver_index"
        private const val NO_RECEIVER_INDEX = -1
    }
}
