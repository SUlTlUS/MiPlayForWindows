package com.suitius.dlnacast.audio

import android.content.Context
import android.content.pm.LauncherApps
import android.os.Process

data class CaptureSource(
    val label: String,
    val packageName: String?,
    val uid: Int?,
) {
    val isSystemWide: Boolean get() = uid == null
}

object CaptureSourceCatalog {
    val systemWide = CaptureSource("全部可捕获音频", null, null)

    fun load(context: Context): List<CaptureSource> {
        val launcherApps = context.getSystemService(LauncherApps::class.java)
        val packageManager = context.packageManager
        val user = Process.myUserHandle()
        val applications = launcherApps.getActivityList(null, user)
            .map { it.applicationInfo }
            .distinctBy { it.packageName }
            .map { info ->
                CaptureSource(
                    label = packageManager.getApplicationLabel(info).toString(),
                    packageName = info.packageName,
                    uid = info.uid,
                )
            }
            .filterNot { it.packageName == context.packageName }
            .sortedBy { it.label.lowercase() }
        return listOf(systemWide) + applications
    }
}
