using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClassIsland.AISmartClass.Models;

/// <summary>
/// 提醒提供方的用户设置。
/// 在 ClassIsland 提醒设置页面中可见、可编辑。
/// 使用 ObservableObject（非 ObservableRecipient）以避免序列化时包含 IsActive/Messenger 等多余属性。
/// </summary>
public partial class SmartClassNotifierSettings : ObservableObject
{
    // ===== 自动提醒开关 =====

    [ObservableProperty]
    [property: JsonPropertyName("enableThoughtfulReminder")]
    private bool _enableThoughtfulReminder = true;
    // 贴心提醒总开关：统一协调课程、时间、天气和媒体情境。

    [ObservableProperty]
    [property: JsonPropertyName("enableBeforeSchoolReminder")]
    private bool _enableBeforeSchoolReminder = true;
    // 第一节课开始前 5 分钟触发一次。

    [ObservableProperty]
    [property: JsonPropertyName("enableBeforeClassReminder")]
    private bool _enableBeforeClassReminder = true;
    // 【课间开始】AI 根据上节+下节科目生成个性化提醒（保留旧字段兼容）。

    [ObservableProperty]
    [property: JsonPropertyName("enableAfterSchoolSummary")]
    private bool _enableAfterSchoolSummary = true;
    // 【最后一节课结束】放学时，AI 生成本日学习总结全屏遮罩

    [ObservableProperty]
    [property: JsonPropertyName("enableClassChangeAlert")]
    private bool _enableClassChangeAlert = true;
    // 【换课提醒】检测到临时换课时，弹出提示

    [ObservableProperty]
    [property: JsonPropertyName("enableWeatherReminder")]
    private bool _enableWeatherReminder = true;
    // 允许贴心提醒使用 ClassIsland 天气位置设置和小米天气 API 取得天气上下文。

    [ObservableProperty]
    [property: JsonPropertyName("enableTemperatureReminder")]
    private bool _enableTemperatureReminder = true;
    // 允许贴心提醒使用温度和体感温度上下文。

    [ObservableProperty]
    [property: JsonPropertyName("enableWeatherAlertReminder")]
    private bool _enableWeatherAlertReminder = true;
    // 允许贴心提醒使用天气预警上下文（如暴雨、高温、台风预警）。

    [ObservableProperty]
    [property: JsonPropertyName("enableMusicReminder")]
    private bool _enableMusicReminder = true;
    // 允许贴心提醒使用当前播放音乐上下文，并保留播放时独立提醒。

    [ObservableProperty]
    [property: JsonPropertyName("enableCustomReminder")]
    private bool _enableCustomReminder = true;
    // 自定义定时提醒开关，保留原有自定义提醒配置与数据。

    [ObservableProperty]
    [property: JsonPropertyName("enableDailyBriefingHoliday")]
    private bool _enableDailyBriefingHoliday = true;

    [ObservableProperty]
    [property: JsonPropertyName("enableDailyBriefingNews")]
    private bool _enableDailyBriefingNews = true;

    [ObservableProperty]
    [property: JsonPropertyName("enableExternalPluginIntegration")]
    private bool _enableExternalPluginIntegration = false;
    // 允许 AIIsland 读取其他插件的数据（如生日、值日），为用户提供更智能的提醒。

    [ObservableProperty]
    [property: JsonPropertyName("authorizedPluginIds")]
    private HashSet<string> _authorizedPluginIds = new();
    // 用户已授权 AIIsland 读取数据的插件 ID 列表

    [ObservableProperty]
    [property: JsonPropertyName("pluginAuthorizationConfirmed")]
    private bool _pluginAuthorizationConfirmed = false;
    // 用户是否已经明确允许或拒绝过插件数据授权。即使授权列表为空，确认后也不再自动弹窗。

    [ObservableProperty]
    [property: JsonPropertyName("rssFeedUrls")]
    private string _rssFeedUrls = "http://www.ithome.com/rss/";

    // ===== 地理位置来源 =====
    // 当前固定使用 ClassIsland 本地天气定位设置，自动分辨城市选择或坐标定位。

    [ObservableProperty]
    [property: JsonPropertyName("locationProvider")]
    private LocationProviderType _locationProvider = LocationProviderType.ClassIslandSettings;
    // 天气、地址相关功能使用的定位来源。当前仅支持 ClassIsland 本地设置。

    [ObservableProperty]
    [property: JsonPropertyName("classIslandInstallDirectory")]
    private string _classIslandInstallDirectory = "";
    // ClassIsland 安装目录，留空时自动探测。用于读取其 data/Settings.json 中的天气定位数据。

    // 以下字段为历史兼容保留，不再在 UI 中展示，也不再作为定位来源。

    [ObservableProperty]
    [property: JsonPropertyName("manualAddress")]
    private string _manualAddress = "";

    [ObservableProperty]
    [property: JsonPropertyName("manualLatitude")]
    private double _manualLatitude;

    [ObservableProperty]
    [property: JsonPropertyName("manualLongitude")]
    private double _manualLongitude;

    // ===== 提醒样式 =====

    [ObservableProperty]
    [property: JsonPropertyName("enableTTS")]
    private bool _enableTTS = false;
    // 已废弃：语音播报改为跟随 ClassIsland 全局设置（「启用提醒语音」），此字段不再被读取。
    // 保留字段仅为兼容历史 Settings.json，避免旧配置反序列化报错。

    [ObservableProperty]
    [property: JsonPropertyName("maskDurationSeconds")]
    private int _maskDurationSeconds = 3;
    // 遮罩显示时长（秒），默认 3 秒

    [ObservableProperty]
    [property: JsonPropertyName("overlayDurationSeconds")]
    private int _overlayDurationSeconds = 5;
    // 正文显示时长（秒），默认 5 秒

    [ObservableProperty]
    [property: JsonPropertyName("rollingSpeed")]
    private int _rollingSpeed = 7;
    // 滚动正文的滚动速度（字/秒），值越小滚动越慢、正文停留越久，可缓解长句子语音播报不完。

    // ===== 自定义定时提醒 =====

    [ObservableProperty]
    [property: JsonPropertyName("customReminders")]
    private ObservableCollection<CustomReminder> _customReminders = new();
    // 用户创建的自定义提醒列表
}
