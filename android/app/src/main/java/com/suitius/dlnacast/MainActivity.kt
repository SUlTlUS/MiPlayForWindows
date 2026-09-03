package com.suitius.dlnacast

import android.content.Intent
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawing
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.layout.windowInsetsPadding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material3.Button
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.Checkbox
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.RadioButton
import androidx.compose.material3.Slider
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.mutableFloatStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.role
import androidx.compose.ui.semantics.selected
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.core.content.ContextCompat
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.suitius.dlnacast.model.DlnaRenderer
import com.suitius.dlnacast.audio.CaptureSourceCatalog
import com.suitius.dlnacast.audio.AudioChannelRoute
import com.suitius.dlnacast.network.SsdpDiscovery
import com.suitius.dlnacast.service.CastService
import com.suitius.dlnacast.service.CastProtocol
import com.suitius.dlnacast.service.CastPreferences
import com.suitius.dlnacast.service.CastStateStore
import com.suitius.dlnacast.service.CastStatus
import com.suitius.dlnacast.systemui.SystemUiBridgeProvider
import com.suitius.dlnacast.systemui.systemUiStartIntent
import com.suitius.dlnacast.ui.theme.DLNACastTheme
import kotlinx.coroutines.launch

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        setContent {
            DLNACastTheme {
                CastScreen(
                    onStartCast = ::requestCastPermissions,
                    onStopCast = { startService(CastService.stopIntent(this)) },
                    onPauseCast = { startService(CastService.pauseIntent(this)) },
                    onResumeCast = { startService(CastService.resumeIntent(this)) },
                    onSetVolume = { volume, receiverIndex ->
                        startService(CastService.setVolumeIntent(this, volume, receiverIndex))
                    },
                    onAddRenderer = { renderer ->
                        startService(CastService.addRendererIntent(this, renderer))
                    },
                    onRemoveRenderer = { rendererId ->
                        startService(CastService.removeRendererIntent(this, rendererId))
                    },
                    onSetCaptureUid = { uid ->
                        startService(CastService.setCaptureUidIntent(this, uid))
                    },
                    onOpenSettings = { startActivity(Intent(this, SystemUiSettingsActivity::class.java)) },
                )
            }
        }
        handleSystemUiSelection(intent)
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        setIntent(intent)
        handleSystemUiSelection(intent)
    }

    private fun handleSystemUiSelection(intent: Intent?) {
        if (intent?.action != SystemUiBridgeProvider.ACTION_SYSTEM_UI_SELECTION) return
        val renderer = SystemUiBridgeProvider.consumePendingSelection(
            intent.getStringExtra(SystemUiBridgeProvider.EXTRA_NONCE),
        ) ?: rendererFromSystemUiIntent(intent) ?: return
        val serviceIntent = systemUiStartIntent(this, renderer) ?: return
        ContextCompat.startForegroundService(this, serviceIntent)
    }

    private fun rendererFromSystemUiIntent(intent: Intent): DlnaRenderer? {
        val id = intent.getStringExtra(SystemUiBridgeProvider.EXTRA_RENDERER_ID) ?: return null
        val name = intent.getStringExtra(SystemUiBridgeProvider.EXTRA_RENDERER_NAME) ?: return null
        val host = intent.getStringExtra(SystemUiBridgeProvider.EXTRA_RENDERER_HOST) ?: return null
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

    private fun requestCastPermissions(
        renderers: List<DlnaRenderer>,
        protocol: CastProtocol,
        captureUid: Int?,
        channelRoutes: List<AudioChannelRoute>,
        playbackDelayMicroseconds: Int,
    ) {
        ContextCompat.startForegroundService(
            this,
            CastService.rootStartIntent(
                context = this,
                renderers = renderers,
                protocol = protocol,
                channelRoutes = channelRoutes,
                playbackDelayMicroseconds = playbackDelayMicroseconds,
            ),
        )
    }
}

@Composable
private fun CastScreen(
    onStartCast: (List<DlnaRenderer>, CastProtocol, Int?, List<AudioChannelRoute>, Int) -> Unit,
    onStopCast: () -> Unit,
    onPauseCast: () -> Unit,
    onResumeCast: () -> Unit,
    onSetVolume: (Int, Int?) -> Unit,
    onAddRenderer: (DlnaRenderer) -> Unit,
    onRemoveRenderer: (String) -> Unit,
    onSetCaptureUid: (Int?) -> Unit,
    onOpenSettings: () -> Unit,
) {
    val context = androidx.compose.ui.platform.LocalContext.current
    val scope = rememberCoroutineScope()
    val discovery = remember(context) { SsdpDiscovery(context) }
    val preferences = remember(context) { CastPreferences(context) }
    val castStatus by CastStateStore.status.collectAsStateWithLifecycle()
    var renderers by remember { mutableStateOf<List<DlnaRenderer>>(emptyList()) }
    var selectedRendererIds by rememberSaveable {
        mutableStateOf(
            if (CastStateStore.status.value is CastStatus.Streaming ||
                CastStateStore.status.value is CastStatus.Preparing
            ) {
                preferences.rendererIds
            } else {
                emptyList()
            },
        )
    }
    var scanning by remember { mutableStateOf(false) }
    var discoveryError by remember { mutableStateOf<String?>(null) }
    var protocol by rememberSaveable { mutableStateOf(preferences.protocol) }
    val captureSources = remember(context) { CaptureSourceCatalog.load(context) }
    var selectedCaptureUid by rememberSaveable { mutableStateOf(preferences.captureUid) }
    var captureMenuExpanded by remember { mutableStateOf(false) }
    var channelRoute by rememberSaveable { mutableStateOf(preferences.channelRoute) }
    var stereoSplit by rememberSaveable { mutableStateOf(preferences.stereoSplit) }
    var playbackDelayMilliseconds by rememberSaveable {
        mutableFloatStateOf(preferences.playbackDelayMilliseconds)
    }

    fun discover() {
        if (scanning) return
        scanning = true
        discoveryError = null
        scope.launch {
            runCatching { discovery.discover() }
                .onSuccess { found ->
                    renderers = found
                    val foundIds = found.map(DlnaRenderer::id).toSet()
                    selectedRendererIds = selectedRendererIds.filter { it in foundIds }
                }
                .onFailure { discoveryError = it.message ?: "搜索失败，请确认手机已连接 Wi-Fi" }
            if (renderers.isEmpty() && discoveryError == null) discoveryError = discovery.lastDiagnostics
            scanning = false
        }
    }

    LaunchedEffect(Unit) { discover() }
    LaunchedEffect(captureSources) {
        if (captureSources.none { it.uid == selectedCaptureUid }) selectedCaptureUid = null
    }
    LaunchedEffect(castStatus) {
        if (castStatus is CastStatus.Idle || castStatus is CastStatus.Error) {
            selectedRendererIds = emptyList()
        } else if (selectedRendererIds.isEmpty()) {
            selectedRendererIds = preferences.rendererIds
        }
    }
    LaunchedEffect(
        protocol,
        selectedCaptureUid,
        channelRoute,
        playbackDelayMilliseconds,
        selectedRendererIds,
        stereoSplit,
    ) {
        preferences.save(
            protocol,
            selectedCaptureUid,
            channelRoute,
            playbackDelayMilliseconds.toInt(),
            selectedRendererIds,
            stereoSplit,
        )
    }

    fun startSelection(rendererIds: List<String>) {
        val selected = rendererIds.mapNotNull { id -> renderers.firstOrNull { it.id == id } }
        if (selected.isEmpty()) {
            onStopCast()
            return
        }
        onStartCast(
            selected,
            protocol,
            selectedCaptureUid,
            if (stereoSplit && selected.size == 2) {
                listOf(AudioChannelRoute.LeftAsMono, AudioChannelRoute.RightAsMono)
            } else {
                List(selected.size) { channelRoute }
            },
            playbackDelayMilliseconds.toInt() * 1_000,
        )
    }

    fun toggleRenderer(renderer: DlnaRenderer) {
        if (castStatus is CastStatus.Preparing) return
        val selected = renderer.id in selectedRendererIds
        if (castStatus is CastStatus.Streaming && protocol == CastProtocol.MIPLAY) {
            if (selected) {
                if (selectedRendererIds.size <= 1) {
                    selectedRendererIds = emptyList()
                    onStopCast()
                } else {
                    selectedRendererIds = selectedRendererIds - renderer.id
                    onRemoveRenderer(renderer.id)
                }
            } else {
                selectedRendererIds += renderer.id
                onAddRenderer(renderer)
            }
            return
        }
        val updated = when {
            selected -> selectedRendererIds - renderer.id
            protocol == CastProtocol.DLNA -> listOf(renderer.id)
            selectedRendererIds.size < MAX_MIPLAY_RECEIVERS -> selectedRendererIds + renderer.id
            else -> selectedRendererIds.drop(1) + renderer.id
        }
        selectedRendererIds = updated
        startSelection(updated)
    }

    Surface(modifier = Modifier.fillMaxSize(), color = MaterialTheme.colorScheme.background) {
        Box(
            modifier = Modifier
                .fillMaxSize()
                .windowInsetsPadding(WindowInsets.safeDrawing)
                .padding(horizontal = 16.dp),
            contentAlignment = Alignment.TopCenter,
        ) {
            LazyColumn(
                modifier = Modifier.fillMaxWidth().widthIn(max = 720.dp),
                verticalArrangement = Arrangement.spacedBy(16.dp),
            ) {
                item {
                    Spacer(Modifier.height(8.dp))
                    Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                        Text("MiPlay Cast", modifier = Modifier.weight(1f),
                            style = MaterialTheme.typography.headlineLarge, fontWeight = FontWeight.Bold)
                        TextButton(onClick = onOpenSettings, modifier = Modifier.heightIn(min = 48.dp)) {
                            Text("设置")
                        }
                    }
                    Text(
                        "把手机正在播放的声音投到同一 Wi-Fi 下的小爱音箱",
                        style = MaterialTheme.typography.bodyLarge,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                }

                item {
                    StatusCard(
                        status = castStatus,
                        protocol = protocol,
                    )
                }

                item {
                    Card(colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surface)) {
                        Column(
                            modifier = Modifier.fillMaxWidth().padding(16.dp),
                            verticalArrangement = Arrangement.spacedBy(12.dp),
                        ) {
                            Text("传输方式", style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.SemiBold)
                            Text(
                                "选择音箱使用的投送协议",
                                style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant,
                            )
                            Row(
                                modifier = Modifier.fillMaxWidth(),
                                horizontalArrangement = Arrangement.spacedBy(8.dp),
                            ) {
                                ProtocolButton(
                                    label = "DLNA",
                                    selected = protocol == CastProtocol.DLNA,
                                    modifier = Modifier.weight(1f),
                                    enabled = castStatus !is CastStatus.Streaming && castStatus !is CastStatus.Preparing,
                                ) {
                                    protocol = CastProtocol.DLNA
                                    selectedRendererIds = emptyList()
                                }
                                ProtocolButton(
                                    label = "MiPlay",
                                    selected = protocol == CastProtocol.MIPLAY,
                                    modifier = Modifier.weight(1f),
                                    enabled = castStatus !is CastStatus.Streaming && castStatus !is CastStatus.Preparing,
                                ) {
                                    protocol = CastProtocol.MIPLAY
                                    selectedRendererIds = emptyList()
                                }
                            }
                            Text(
                                if (protocol == CastProtocol.MIPLAY) {
                                    "小米原生 8899 + WFD 音频链路"
                                } else {
                                    "UPnP PCM/WAV 兼容链路"
                                },
                                style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant,
                            )

                            if (protocol == CastProtocol.MIPLAY) {
                                HorizontalDivider()
                                Text("MiPlay 播放提前量", fontWeight = FontWeight.SemiBold)
                                Text(
                                    "${playbackDelayMilliseconds.toInt()} ms",
                                    style = MaterialTheme.typography.labelLarge,
                                )
                                Slider(
                                    value = playbackDelayMilliseconds,
                                    onValueChange = { playbackDelayMilliseconds = it },
                                    enabled = castStatus !is CastStatus.Streaming && castStatus !is CastStatus.Preparing,
                                    valueRange = 0f..2_000f,
                                    steps = 39,
                                    modifier = Modifier.fillMaxWidth().height(40.dp),
                                )
                                Text(
                                    "默认 1000 ms；双音箱使用同一时间基准。",
                                    style = MaterialTheme.typography.bodySmall,
                                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                                )
                            }
                        }
                    }
                }

                item {
                    val selectedSource = captureSources.firstOrNull { it.uid == selectedCaptureUid }
                        ?: CaptureSourceCatalog.systemWide
                    Card(colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surface)) {
                        Column(
                            modifier = Modifier.fillMaxWidth().padding(16.dp),
                            verticalArrangement = Arrangement.spacedBy(10.dp),
                        ) {
                            Text("音频来源", style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.SemiBold)
                            Text(
                                "投送全部可捕获音频，或只捕获一个应用",
                                style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant,
                            )
                            Box {
                                OutlinedButton(
                                    onClick = { captureMenuExpanded = true },
                                    modifier = Modifier.fillMaxWidth().height(48.dp),
                                ) {
                                    Text(selectedSource.label, maxLines = 1)
                                }
                                DropdownMenu(
                                    expanded = captureMenuExpanded,
                                    onDismissRequest = { captureMenuExpanded = false },
                                    modifier = Modifier.heightIn(max = 420.dp),
                                ) {
                                    captureSources.forEach { source ->
                                        DropdownMenuItem(
                                            text = {
                                                Column {
                                                    Text(source.label)
                                                    source.packageName?.let {
                                                        Text(
                                                            it,
                                                            style = MaterialTheme.typography.bodySmall,
                                                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                                                        )
                                                    }
                                                }
                                            },
                                            onClick = {
                                                selectedCaptureUid = source.uid
                                                if ((castStatus as? CastStatus.Streaming)?.supportsCaptureSwitch == true) {
                                                    onSetCaptureUid(source.uid)
                                                }
                                                captureMenuExpanded = false
                                            },
                                        )
                                    }
                                }
                            }
                        }
                    }
                }

                item {
                    val visibleRenderers = if (protocol == CastProtocol.MIPLAY) {
                        renderers
                    } else {
                        renderers.filter { it.avTransportControlUrl.isNotBlank() }
                    }
                    val streaming = castStatus as? CastStatus.Streaming
                    Card(colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surface)) {
                        Column(
                            modifier = Modifier.fillMaxWidth().padding(16.dp),
                            verticalArrangement = Arrangement.spacedBy(12.dp),
                        ) {
                            Row(
                                modifier = Modifier.fillMaxWidth(),
                                horizontalArrangement = Arrangement.SpaceBetween,
                                verticalAlignment = Alignment.CenterVertically,
                            ) {
                                Column(Modifier.weight(1f)) {
                                    Text("播放设备", style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.SemiBold)
                                    Text(
                                        if (protocol == CastProtocol.MIPLAY) "勾选音箱即可开始；最多选择两台"
                                        else "勾选一台 DLNA 音箱即可开始",
                                        style = MaterialTheme.typography.bodySmall,
                                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                                    )
                                }
                                OutlinedButton(onClick = ::discover, enabled = !scanning) {
                                    if (scanning) {
                                        CircularProgressIndicator(modifier = Modifier.height(20.dp), strokeWidth = 2.dp)
                                    } else {
                                        Text("刷新")
                                    }
                                }
                            }

                            if (protocol == CastProtocol.MIPLAY) {
                                ProtocolButton(
                                    label = if (stereoSplit) "立体声拆分：第一台左，第二台右" else "立体声拆分",
                                    selected = stereoSplit,
                                    modifier = Modifier.fillMaxWidth(),
                                    enabled = castStatus !is CastStatus.Streaming && castStatus !is CastStatus.Preparing,
                                ) { stereoSplit = !stereoSplit }
                                if (!stereoSplit) {
                                    Row(
                                        modifier = Modifier.fillMaxWidth(),
                                        horizontalArrangement = Arrangement.spacedBy(8.dp),
                                    ) {
                                        listOf(
                                            AudioChannelRoute.Stereo to "立体声",
                                            AudioChannelRoute.LeftAsMono to "左声道",
                                            AudioChannelRoute.RightAsMono to "右声道",
                                        ).forEach { (route, label) ->
                                            ProtocolButton(
                                                label = label,
                                                selected = channelRoute == route,
                                                modifier = Modifier.weight(1f),
                                                enabled = castStatus !is CastStatus.Streaming && castStatus !is CastStatus.Preparing,
                                            ) { channelRoute = route }
                                        }
                                    }
                                }
                            }

                            if (streaming != null) {
                                Row(
                                    modifier = Modifier.fillMaxWidth(),
                                    horizontalArrangement = Arrangement.spacedBy(8.dp),
                                ) {
                                    if (streaming.supportsPlaybackControl) {
                                        OutlinedButton(
                                            onClick = if (streaming.receiverPaused) onResumeCast else onPauseCast,
                                            modifier = Modifier.weight(1f),
                                        ) {
                                            Text(if (streaming.receiverPaused) "继续播放" else "暂停音箱")
                                        }
                                    }
                                    OutlinedButton(onClick = onStopCast, modifier = Modifier.weight(1f)) {
                                        Text("停止投送")
                                    }
                                }
                            } else if (castStatus is CastStatus.Preparing) {
                                OutlinedButton(onClick = onStopCast, modifier = Modifier.fillMaxWidth()) {
                                    Text("取消连接")
                                }
                            }

                            streaming?.receiverVolume?.let { currentVolume ->
                                if (streaming.receiverControls.size > 1) {
                                    var groupVolume by remember(currentVolume) {
                                        mutableFloatStateOf(currentVolume.toFloat())
                                    }
                                    Text("全部音箱音量 ${groupVolume.toInt()}", style = MaterialTheme.typography.labelLarge)
                                    Slider(
                                        value = groupVolume,
                                        onValueChange = { groupVolume = it },
                                        onValueChangeFinished = { onSetVolume(groupVolume.toInt(), null) },
                                        valueRange = 0f..100f,
                                        steps = 99,
                                        modifier = Modifier.fillMaxWidth().height(40.dp),
                                    )
                                }
                            }

                            discoveryError?.let { message ->
                                MessageCard(message, isError = true)
                            }
                            if (!scanning && visibleRenderers.isEmpty() && discoveryError == null) {
                                MessageCard("没有找到音箱。请确认手机和音箱在同一 Wi-Fi，并关闭访客网络隔离。")
                            }
                            visibleRenderers.forEach { renderer ->
                                val selected = renderer.id in selectedRendererIds
                                val selectedIndex = selectedRendererIds.indexOf(renderer.id)
                                val receiver = streaming?.receiverControls?.getOrNull(selectedIndex)
                                DeviceCard(
                                    renderer = renderer,
                                    selected = selected,
                                    allowsMultiple = protocol == CastProtocol.MIPLAY,
                                    enabled = castStatus !is CastStatus.Preparing,
                                    channelLabel = if (stereoSplit && selectedIndex >= 0) {
                                        if (selectedIndex == 0) "左声道" else "右声道"
                                    } else {
                                        null
                                    },
                                    receiverVolume = receiver?.volume,
                                    onSetVolume = if (receiver?.volume != null) {
                                        { volume -> onSetVolume(volume, selectedIndex) }
                                    } else {
                                        null
                                    },
                                ) { toggleRenderer(renderer) }
                            }

                            Text(
                                if (castStatus is CastStatus.Streaming) {
                                    if (protocol == CastProtocol.MIPLAY) {
                                        "MiPlay 播放中更改勾选会在当前会话加入或移除音箱。"
                                    } else {
                                        "更改勾选会重新请求 Android 音频捕获授权，并按新设备组合重启投送。"
                                    }
                                } else {
                                    "首次勾选时，Android 会依次请求录音权限和系统音频捕获授权。"
                                },
                                style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant,
                            )
                        }
                    }
                }

                item {
                    Text(
                        "Android 只允许捕获同意被录制的媒体/游戏音频；DRM 内容、通话和部分应用会保持静音。",
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                    Spacer(Modifier.height(24.dp))
                }
            }
        }
    }
}

private const val MAX_MIPLAY_RECEIVERS = 63

@Composable
private fun ProtocolButton(
    label: String,
    selected: Boolean,
    modifier: Modifier = Modifier,
    enabled: Boolean = true,
    onClick: () -> Unit,
) {
    val semantics = modifier
        .height(48.dp)
        .semantics {
            this.selected = selected
            role = Role.RadioButton
        }
    if (selected) {
        Button(onClick = onClick, enabled = enabled, modifier = semantics) { Text(label) }
    } else {
        OutlinedButton(onClick = onClick, enabled = enabled, modifier = semantics) { Text(label) }
    }
}

@Composable
private fun DeviceCard(
    renderer: DlnaRenderer,
    selected: Boolean,
    allowsMultiple: Boolean,
    enabled: Boolean,
    channelLabel: String?,
    receiverVolume: Int?,
    onSetVolume: ((Int) -> Unit)?,
    onClick: () -> Unit,
) {
    Card(
        onClick = onClick,
        enabled = enabled,
        modifier = Modifier
            .fillMaxWidth()
            .semantics {
                this.selected = selected
                role = if (allowsMultiple) Role.Checkbox else Role.RadioButton
            },
        colors = CardDefaults.cardColors(
            containerColor = if (selected) MaterialTheme.colorScheme.primaryContainer else MaterialTheme.colorScheme.surface,
        ),
    ) {
        Row(
            modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 14.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            if (allowsMultiple) {
                Checkbox(checked = selected, onCheckedChange = null)
            } else {
                RadioButton(selected = selected, onClick = null)
            }
            Column(Modifier.padding(start = 12.dp).weight(1f)) {
                Text(renderer.name, style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.Medium)
                Text(
                    listOfNotNull(channelLabel, renderer.host).joinToString(" · "),
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
        }
        if (receiverVolume != null && onSetVolume != null) {
            var pendingVolume by remember(receiverVolume) { mutableFloatStateOf(receiverVolume.toFloat()) }
            Column(
                modifier = Modifier.fillMaxWidth().padding(start = 16.dp, end = 16.dp, bottom = 10.dp),
            ) {
                Text("音量 ${pendingVolume.toInt()}", style = MaterialTheme.typography.labelLarge)
                Slider(
                    value = pendingVolume,
                    onValueChange = { pendingVolume = it },
                    onValueChangeFinished = { onSetVolume(pendingVolume.toInt()) },
                    valueRange = 0f..100f,
                    steps = 99,
                    modifier = Modifier.fillMaxWidth().height(40.dp),
                )
            }
        }
    }
}

@Composable
private fun StatusCard(
    status: CastStatus,
    protocol: CastProtocol,
) {
    val (title, detail) = when (status) {
        CastStatus.Idle -> "等待投送" to "勾选一台音箱即可开始"
        is CastStatus.Preparing -> "正在连接" to status.message
        is CastStatus.Streaming -> "正在投送到 ${status.rendererName}" to status.detail
        is CastStatus.Error -> "投送未启动" to status.message
    }
    Card(
        colors = CardDefaults.cardColors(
            containerColor = when (status) {
                is CastStatus.Error -> MaterialTheme.colorScheme.errorContainer
                is CastStatus.Streaming -> MaterialTheme.colorScheme.primaryContainer
                else -> MaterialTheme.colorScheme.surface
            },
        ),
    ) {
        Column(Modifier.fillMaxWidth().padding(20.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Text("投送状态", style = MaterialTheme.typography.titleMedium, fontWeight = FontWeight.SemiBold)
                Text(
                    if (protocol == CastProtocol.MIPLAY) "MiPlay" else "DLNA",
                    style = MaterialTheme.typography.labelLarge,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
            HorizontalDivider()
            Text(title, style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.SemiBold)
            Text(detail, color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
    }
}

@Composable
private fun MessageCard(message: String, isError: Boolean = false) {
    Card(
        colors = CardDefaults.cardColors(
            containerColor = if (isError) MaterialTheme.colorScheme.errorContainer else MaterialTheme.colorScheme.surfaceVariant,
        ),
    ) {
        Text(message, modifier = Modifier.fillMaxWidth().padding(16.dp), style = MaterialTheme.typography.bodyMedium)
    }
}
