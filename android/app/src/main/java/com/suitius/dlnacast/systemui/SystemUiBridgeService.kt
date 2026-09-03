package com.suitius.dlnacast.systemui

import android.app.Service
import android.content.Intent
import android.os.Binder
import android.os.IBinder
import android.util.Log
import com.suitius.dlnacast.service.CastStateStore
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.launch

/** Keeps the provider available only while SystemUI's output panel is bound. */
class SystemUiBridgeService : Service() {
    private val binder = Binder()
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)

    override fun onCreate() {
        super.onCreate()
        scope.launch {
            CastStateStore.status.collect {
                runCatching {
                    contentResolver.notifyChange(SystemUiBridgeProvider.SPEAKERS_URI, null)
                }.onFailure { error -> Log.w(LOG_TAG, "State notification failed", error) }
            }
        }
    }

    override fun onBind(intent: Intent): IBinder {
        Log.i(LOG_TAG, "Output panel bound")
        return binder
    }

    override fun onUnbind(intent: Intent): Boolean {
        Log.i(LOG_TAG, "Output panel unbound")
        return false
    }

    override fun onDestroy() {
        scope.cancel()
        Log.i(LOG_TAG, "Output panel bridge destroyed")
        super.onDestroy()
    }

    private companion object {
        const val LOG_TAG = "MiPlaySystemUiBridge"
    }
}
