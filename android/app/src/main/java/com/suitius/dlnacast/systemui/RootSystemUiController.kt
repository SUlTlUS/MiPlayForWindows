package com.suitius.dlnacast.systemui

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

object RootSystemUiController {
    suspend fun restartSystemUi(): Result<Unit> = withContext(Dispatchers.IO) {
        runCatching {
            val process = ProcessBuilder("su", "-c", RESTART_COMMAND)
                .redirectErrorStream(true)
                .start()
            val output = process.inputStream.bufferedReader().use { it.readText() }.trim()
            val exitCode = process.waitFor()
            check(exitCode == 0) {
                if (exitCode == ROOT_DENIED_EXIT_CODE) {
                    "未获得 Root 授权，请在 KernelSU 中允许 MiPlay Cast"
                } else {
                    buildString {
                        append("重启系统界面失败（退出码 ")
                        append(exitCode)
                        append('）')
                        if (output.isNotBlank()) append("：${output.take(240)}")
                    }
                }
            }
        }
    }

    private const val ROOT_DENIED_EXIT_CODE = 126
    private const val RESTART_COMMAND =
        "if [ \"\$(id -u)\" != \"0\" ]; then echo root_denied; exit 126; fi; killall com.android.systemui"
}
