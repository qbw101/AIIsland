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
    [property: JsonPropertyName("enableBeforeClassReminder")]
    private bool _enableBeforeClassReminder = true;
    // 【课前提醒】课间开始时，AI 根据上节+下节科目生成个性化提醒

    [ObservableProperty]
    [property: JsonPropertyName("enableAfterSchoolSummary")]
    private bool _enableAfterSchoolSummary = true;
    // 【放学总结】放学时，AI 生成本日学习总结全屏遮罩

    [ObservableProperty]
    [property: JsonPropertyName("enableClassChangeAlert")]
    private bool _enableClassChangeAlert = true;
    // 【换课提醒】检测到临时换课时，弹出提示

    // ===== 提醒样式 =====

    [ObservableProperty]
    [property: JsonPropertyName("enableTTS")]
    private bool _enableTTS = false;
    // 是否启用语音播报（默认关闭以免影响课堂）

    [ObservableProperty]
    [property: JsonPropertyName("maskDurationSeconds")]
    private int _maskDurationSeconds = 3;
    // 遮罩显示时长（秒），默认 3 秒

    [ObservableProperty]
    [property: JsonPropertyName("overlayDurationSeconds")]
    private int _overlayDurationSeconds = 5;
    // 正文显示时长（秒），默认 5 秒

    // ===== 自定义定时提醒 =====

    [ObservableProperty]
    [property: JsonPropertyName("customReminders")]
    private ObservableCollection<CustomReminder> _customReminders = new();
    // 用户创建的自定义提醒列表
}
