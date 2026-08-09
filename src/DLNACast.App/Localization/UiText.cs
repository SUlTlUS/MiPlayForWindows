using DLNACast.Core.Localization;

namespace DLNACast.App.Localization;

public sealed class UiText
{
    public string PageHeading => SystemLanguage.Select("投送 Windows 音频", "Cast Windows audio");
    public string PageSubtitle => SystemLanguage.Select(
        "选择协议、播放设备与音频来源，然后开始投送。",
        "Choose a protocol, playback devices, and an audio source to start casting.");
    public string CastStatus => SystemLanguage.Select("投送状态", "Cast status");
    public string CastStatusSubtitle => SystemLanguage.Select(
        "实时连接与传输信息",
        "Live connection and transport details");
    public string Transport => SystemLanguage.Select("传输方式", "Transport");
    public string TransportHint => SystemLanguage.Select("根据目标设备选择兼容协议", "Choose a compatible protocol for the target device");
    public string DlnaDescription => SystemLanguage.Select("兼容多数网络音箱", "Compatible with most network speakers");
    public string MiPlay => SystemLanguage.Select("小米妙播", "MiPlay");
    public string MiPlayDescription => SystemLanguage.Select("适配小米音箱", "Low-latency AAC audio");
    public string AudioSource => SystemLanguage.Select("音频来源", "Audio source");
    public string AudioSourceHint => SystemLanguage.Select("选择整个输出设备或单个应用", "Choose an entire output device or a single app");
    public string AllOutputAudio => SystemLanguage.Select("输出设备全部声音", "All output device audio");
    public string AllOutputAudioDescription => SystemLanguage.Select(
        "投送当前输出设备的所有声音",
        "Cast all audio from the current output device");
    public string AppAndChildren => SystemLanguage.Select("应用及其子进程", "App and child processes");
    public string AppAndChildrenDescription => SystemLanguage.Select(
        "仅投送所选应用及其子进程",
        "Cast only the selected app and its child processes");
    public string SpeakersOnly => SystemLanguage.Select("仅音箱播放", "Speakers only");
    public string SpeakersOnlyTooltip => SystemLanguage.Select(
        "开启后自动切换到 DLNA Cast 虚拟扬声器，电脑不出声，仅所选音箱播放",
        "Automatically route audio to the DLNA Cast virtual speaker so only the selected speakers play");
    public string NoAudioSources => SystemLanguage.Select("未找到可用音频来源", "No audio sources found");
    public string Refresh => SystemLanguage.Select("刷新", "Refresh");
    public string RefreshAudioSources => SystemLanguage.Select("刷新音频来源", "Refresh audio sources");
    public string PlaybackDevices => SystemLanguage.Select("播放设备", "Playback devices");
    public string StereoSplit => SystemLanguage.Select("双音箱立体声", "Two-speaker stereo");
    public string StereoSplitTooltip => SystemLanguage.Select(
        "分别把左、右声道投送到两台音箱；开启后切换到 DLNA，建议使用同型号音箱",
        "Cast the left and right channels to separate speakers; enabling it switches to DLNA and works best with matching speakers");
    public string RefreshPlaybackDevices => SystemLanguage.Select("刷新播放设备", "Refresh playback devices");
    public string NoPlaybackDevices => SystemLanguage.Select(
        "未发现播放设备，请确认音箱和电脑位于同一局域网。",
        "No playback devices found. Make sure the speakers and PC are on the same local network.");
    public string TrayHint => SystemLanguage.Select(
        "关闭窗口后，应用仍会在系统托盘运行。",
        "The app keeps running in the system tray after the window is closed.");
}
