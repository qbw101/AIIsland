using System.Text.Json.Serialization;

namespace ClassIsland.AISmartClass.Models.ExamMode;

/// <summary>
/// 考试模式全局偏好。考试科目与起止时间始终来自 ClassIsland 课表服务。
/// </summary>
public sealed class ExamModeSettings
{
    [JsonPropertyName("enableScheduler")]
    public bool EnableScheduler { get; set; } = true;

    [JsonPropertyName("reminderEnabled")]
    public bool ReminderEnabled { get; set; } = true;

    /// <summary>提醒预设：compact、standard、full 或 custom。</summary>
    [JsonPropertyName("reminderPreset")]
    public string ReminderPreset { get; set; } = "standard";

    /// <summary>考前提醒档位（分钟）。保留原字段名以兼容旧配置。</summary>
    [JsonPropertyName("reminderMinutes")]
    public List<int> ReminderMinutes { get; set; } = new() { 1, 5, 15 };

    [JsonPropertyName("startNotificationEnabled")]
    public bool StartNotificationEnabled { get; set; } = true;

    [JsonPropertyName("remainingNotificationEnabled")]
    public bool RemainingNotificationEnabled { get; set; } = true;

    /// <summary>考试进行中的剩余时间提醒档位（分钟）。</summary>
    [JsonPropertyName("remainingMinutes")]
    public List<int> RemainingMinutes { get; set; } = new() { 1, 5, 15 };

    [JsonPropertyName("endNotificationEnabled")]
    public bool EndNotificationEnabled { get; set; } = true;

    /// <summary>考试模式运行时，仅保留 AIIsland 考试提醒和地震预警提醒。</summary>
    [JsonPropertyName("suppressOtherNotifications")]
    public bool SuppressOtherNotifications { get; set; }

    [JsonPropertyName("audioPath")]
    public string AudioPath { get; set; } = "";

    [JsonPropertyName("autoPlayEnabled")]
    public bool AutoPlayEnabled { get; set; }

    /// <summary>听力播放相对考试开始的偏移分钟数；负数提前，正数延后。</summary>
    [JsonPropertyName("autoPlayOffsetMinutes")]
    public int AutoPlayOffsetMinutes { get; set; }

    [JsonPropertyName("audioVolume")]
    public double AudioVolume { get; set; } = 0.8;
}
