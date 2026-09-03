package com.suitius.dlnacast

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.selection.selectable
import androidx.compose.foundation.selection.selectableGroup
import androidx.compose.foundation.selection.toggleable
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.RadioButton
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Slider
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.stateDescription
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.suitius.dlnacast.audio.AudioChannelRoute
import com.suitius.dlnacast.service.CastStateStore
import com.suitius.dlnacast.service.CastStatus
import com.suitius.dlnacast.systemui.RootSystemUiController
import com.suitius.dlnacast.systemui.SystemUiCastConfig
import com.suitius.dlnacast.systemui.SystemUiCastPreferences
import com.suitius.dlnacast.ui.theme.DLNACastTheme
import kotlin.math.roundToInt
import kotlinx.coroutines.launch

class SystemUiSettingsActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        setContent {
            DLNACastTheme {
                SystemUiSettingsScreen(onBack = ::finish)
            }
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun SystemUiSettingsScreen(onBack: () -> Unit) {
    val context = LocalContext.current
    val preferences = remember(context) { SystemUiCastPreferences(context) }
    var config by remember { mutableStateOf(preferences.load()) }
    var delayDraft by remember(config.playbackDelayMilliseconds) {
        mutableFloatStateOf(config.playbackDelayMilliseconds.toFloat())
    }
    val status by CastStateStore.status.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    var confirmRestart by rememberSaveable { mutableStateOf(false) }
    var restarting by remember { mutableStateOf(false) }
    var restartMessage by rememberSaveable { mutableStateOf<String?>(null) }
    var restartFailed by rememberSaveable { mutableStateOf(false) }

    fun save(updated: SystemUiCastConfig) {
        preferences.save(updated)
        config = updated
    }

    Scaffold(
        containerColor = MaterialTheme.colorScheme.background,
        topBar = {
            TopAppBar(
                title = { Text("设置") },
                navigationIcon = {
                    IconButton(onClick = onBack) {
                        Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "返回投送界面")
                    }
                },
            )
        },
    ) { insets ->
        Box(Modifier.fillMaxSize().padding(insets), contentAlignment = Alignment.TopCenter) {
            LazyColumn(
                modifier = Modifier.widthIn(max = 720.dp).fillMaxWidth(),
                contentPadding = PaddingValues(start = 16.dp, end = 16.dp, top = 8.dp, bottom = 24.dp),
                verticalArrangement = Arrangement.spacedBy(16.dp),
            ) {
                item {
                    Text("SystemUI 投送", style = MaterialTheme.typography.headlineSmall,
                        fontWeight = FontWeight.Bold)
                    SettingNote("仅用于从系统媒体输出面板发起的 MiPlay 投送，与主界面的参数分别保存。")
                }
                item {
                    SettingsCard {
                        Row(
                            Modifier.fillMaxWidth().heightIn(min = 64.dp)
                                .toggleable(config.enabled, role = Role.Switch) {
                                    save(config.copy(enabled = it))
                                },
                            verticalAlignment = Alignment.CenterVertically,
                            horizontalArrangement = Arrangement.spacedBy(16.dp),
                        ) {
                            Column(Modifier.weight(1f)) {
                                Text("在系统面板显示音箱", style = MaterialTheme.typography.titleMedium)
                                SettingNote(if (config.enabled) "已开启" else "已关闭")
                            }
                            Switch(checked = config.enabled, onCheckedChange = null)
                        }
                        SettingNote("关闭后不再搜索或连接新音箱；已连接的音箱会保留，方便调节音量和停止。不会中断当前投送。")
                    }
                }
                item {
                    SettingsCard {
                        SettingTitle("音频")
                        Text("Root 系统内录", style = MaterialTheme.typography.titleSmall)
                        SettingNote("直接采集系统混音，不弹出系统录屏授权框。首次使用仍需在 Root 管理器中授权本应用；暂不支持只采集某一个应用。")
                        Text("缓冲延迟 · ${delayDraft.roundToInt()} ms",
                            style = MaterialTheme.typography.titleSmall)
                        Slider(
                            value = delayDraft,
                            onValueChange = { delayDraft = (it / 50f).roundToInt() * 50f },
                            onValueChangeFinished = {
                                save(config.copy(playbackDelayMilliseconds = delayDraft.roundToInt()))
                            },
                            valueRange = 0f..2_000f,
                            steps = 39,
                            modifier = Modifier.fillMaxWidth().semantics {
                                contentDescription = "MiPlay 缓冲延迟"
                                stateDescription = "${delayDraft.roundToInt()} 毫秒"
                            },
                        )
                        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
                            SettingNote("0 ms")
                            SettingNote("2000 ms")
                        }
                        SettingNote("默认 1000 ms。数值越小缓冲越少，更容易受网络波动影响；这不是实测的端到端延迟。")
                    }
                }
                item {
                    SettingsCard {
                        SettingTitle("声道输出")
                        Column(Modifier.selectableGroup()) {
                            AudioChannelRoute.entries.forEach { route ->
                                Row(
                                    Modifier.fillMaxWidth().heightIn(min = 56.dp)
                                        .selectable(
                                            selected = config.channelRoute == route,
                                            role = Role.RadioButton,
                                            onClick = { save(config.copy(channelRoute = route)) },
                                        ),
                                    verticalAlignment = Alignment.CenterVertically,
                                    horizontalArrangement = Arrangement.spacedBy(12.dp),
                                ) {
                                    RadioButton(selected = config.channelRoute == route, onClick = null)
                                    Text(when (route) {
                                        AudioChannelRoute.Stereo -> "原始立体声"
                                        AudioChannelRoute.LeftAsMono -> "仅左声道"
                                        AudioChannelRoute.RightAsMono -> "仅右声道"
                                    })
                                }
                            }
                        }
                        SettingNote("此处设置会应用到每台音箱，加入第二台时沿用当前投送的声道。两台分别播放左、右声道请在主界面使用立体声拆分。")
                        SettingNote("设置自动保存。延迟和声道在下次新建投送时生效，不会更改正在进行的连接。")
                        TextButton(onClick = {
                            save(config.copy(playbackDelayMilliseconds = 1_000, channelRoute = AudioChannelRoute.Stereo))
                        }) { Text("恢复默认音频参数") }
                    }
                }
                item {
                    SettingsCard {
                        SettingTitle("模块与 Root")
                        SettingNote("在 LSPosed 中启用 MiPlay Cast，作用域选择“系统界面”（com.android.systemui）。首次启用模块或更新 APK 后，重启 SystemUI 加载新代码。修改上面的设置不需要重启。")
                        Text(when (val current = status) {
                            CastStatus.Idle -> "当前未投送"
                            is CastStatus.Preparing -> "当前正在连接音箱"
                            is CastStatus.Streaming -> if (current.receiverPaused) "当前投送已暂停" else "当前正在投送"
                            is CastStatus.Error -> "上次投送失败：${current.message}"
                        }, style = MaterialTheme.typography.bodyMedium)
                        OutlinedButton(
                            onClick = { confirmRestart = true },
                            enabled = !restarting,
                            modifier = Modifier.fillMaxWidth().heightIn(min = 48.dp),
                        ) {
                            if (restarting) {
                                CircularProgressIndicator(Modifier.size(18.dp), strokeWidth = 2.dp)
                                Text("正在重启…", Modifier.padding(start = 8.dp))
                            } else {
                                Text("请求 Root 并重启 SystemUI")
                            }
                        }
                        restartMessage?.let {
                            Text(it, style = MaterialTheme.typography.bodySmall,
                                color = if (restartFailed) MaterialTheme.colorScheme.error
                                else MaterialTheme.colorScheme.onSurfaceVariant)
                        }
                    }
                }
            }
        }
    }

    if (confirmRestart) {
        AlertDialog(
            onDismissRequest = { confirmRestart = false },
            title = { Text("重启系统界面？") },
            text = { Text("状态栏和媒体输出面板会短暂消失后重新加载，不会重启手机。Root 管理器可能会请求授权。") },
            confirmButton = {
                TextButton(onClick = {
                    confirmRestart = false
                    restarting = true
                    restartFailed = false
                    restartMessage = "正在请求 Root 权限…"
                    scope.launch {
                        RootSystemUiController.restartSystemUi()
                            .onSuccess { restartMessage = "已发送重启命令，请重新打开系统媒体输出面板验证。" }
                            .onFailure {
                                restartFailed = true
                                restartMessage = it.message ?: "重启失败"
                            }
                        restarting = false
                    }
                }) { Text("重启") }
            },
            dismissButton = { TextButton(onClick = { confirmRestart = false }) { Text("取消") } },
        )
    }
}

@Composable
private fun SettingsCard(content: @Composable ColumnScope.() -> Unit) {
    Card(colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surface)) {
        Column(Modifier.fillMaxWidth().padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(10.dp), content = content)
    }
}

@Composable
private fun SettingTitle(text: String) {
    Text(text, style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.SemiBold)
}

@Composable
private fun SettingNote(text: String) {
    Text(text, style = MaterialTheme.typography.bodySmall,
        color = MaterialTheme.colorScheme.onSurfaceVariant)
}
