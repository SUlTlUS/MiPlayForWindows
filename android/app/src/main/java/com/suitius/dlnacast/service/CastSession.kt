package com.suitius.dlnacast.service

interface CastSession : AutoCloseable {
    val bufferedMilliseconds: Int
    val overruns: Long
    val streamDetail: String
    val failureMessage: String?
        get() = null
    val supportsPlaybackControl: Boolean
        get() = false
    val receiverPaused: Boolean
        get() = false
    val receiverVolume: Int?
        get() = null
    val receiverControls: List<ReceiverControlState>
        get() = emptyList()
    val supportsCaptureSwitch: Boolean
        get() = false

    suspend fun start()
    suspend fun pauseReceiver(): Unit = error("当前投送协议不支持暂停音箱")
    suspend fun resumeReceiver(): Unit = error("当前投送协议不支持继续音箱")
    suspend fun setReceiverVolume(volume: Int, receiverIndex: Int? = null): Unit =
        error("当前投送协议不支持设置音箱音量")
    suspend fun setCaptureUid(uid: Int?): Unit = error("当前投送协议不支持运行中切换音源")
}

data class ReceiverControlState(
    val name: String,
    val paused: Boolean,
    val volume: Int?,
    val rendererId: String = "",
    val rendererHost: String = "",
)
