using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClassIsland.AISmartClass.Models;

/// <summary>
/// 用户自定义提醒的一条记录。
/// 支持三种来源：固定时间、关联科目、自然语言解析生成。
/// </summary>
public partial class CustomReminder : ObservableObject
{
    // ===== 唯一标识 =====

    [ObservableProperty]
    [property: JsonPropertyName("id")]
    private Guid _id = Guid.NewGuid();

    // ===== 类型标记 =====

    [ObservableProperty]
    [property: JsonPropertyName("type")]
    private ReminderType _type = ReminderType.FixedTime;

    // ===== 时间/条件 =====

    [ObservableProperty]
    [property: JsonPropertyName("fixedDateTime")]
    private DateTime? _fixedDateTime;

    [ObservableProperty]
    [property: JsonPropertyName("subjectName")]
    private string? _subjectName;

    [ObservableProperty]
    [property: JsonPropertyName("minutesBefore")]
    private int _minutesBefore = 3;

    // ===== 内容 =====

    [ObservableProperty]
    [property: JsonPropertyName("content")]
    private string _content = "";

    // ===== 状态 =====

    [ObservableProperty]
    [property: JsonPropertyName("isEnabled")]
    private bool _isEnabled = true;

    [ObservableProperty]
    [property: JsonPropertyName("isRepeating")]
    private bool _isRepeating = false;

    [ObservableProperty]
    [property: JsonPropertyName("lastTriggeredDate")]
    private DateTime? _lastTriggeredDate;
}

/// <summary>
/// 提醒类型枚举
/// </summary>
public enum ReminderType
{
    FixedTime = 0,      // 固定日期+时间
    SubjectLinked = 1,  // 关联科目（课前 N 分钟）
    DailyRepeat = 2     // 每日重复（仅时间，不含日期）
}
