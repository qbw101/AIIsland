using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClassIsland.AISmartClass.Models;

/// <summary>
/// 用户自定义提醒的一条记录。
/// 支持固定时间、每日重复时间、关联科目课前 N 分钟三种触发方式。
/// </summary>
public partial class CustomReminder : ObservableObject
{
    [ObservableProperty]
    [property: JsonPropertyName("id")]
    private Guid _id = Guid.NewGuid();

    [ObservableProperty]
    [property: JsonPropertyName("type")]
    private ReminderType _type = ReminderType.FixedTime;

    /// <summary>
    /// 固定时间：完整日期时间；每日重复：仅使用 TimeOfDay，日期部分无意义。
    /// </summary>
    [ObservableProperty]
    [property: JsonPropertyName("fixedDateTime")]
    private DateTime? _fixedDateTime;

    /// <summary>关联科目名称，例如“数学”。仅 SubjectLinked 使用。</summary>
    [ObservableProperty]
    [property: JsonPropertyName("subjectName")]
    private string? _subjectName;

    /// <summary>课前提前多少分钟提醒。仅 SubjectLinked 使用。</summary>
    [ObservableProperty]
    [property: JsonPropertyName("minutesBefore")]
    private int _minutesBefore = 3;

    [ObservableProperty]
    [property: JsonPropertyName("content")]
    private string _content = "";

    [ObservableProperty]
    [property: JsonPropertyName("isEnabled")]
    private bool _isEnabled = true;

    /// <summary>
    /// 兼容旧配置字段。新逻辑中：DailyRepeat/SubjectLinked 天然重复，FixedTime 天然一次性。
    /// </summary>
    [ObservableProperty]
    [property: JsonPropertyName("isRepeating")]
    private bool _isRepeating = false;

    [ObservableProperty]
    [property: JsonPropertyName("lastTriggeredDate")]
    private DateTime? _lastTriggeredDate;

    /// <summary>
    /// 最近一次触发去重键。用于区分同一天内多节同科目的 SubjectLinked 提醒。
    /// </summary>
    [ObservableProperty]
    [property: JsonPropertyName("lastTriggeredKey")]
    private string? _lastTriggeredKey;

    [JsonIgnore]
    public string DisplayTitle => string.IsNullOrWhiteSpace(Content) ? "未命名提醒" : Content;

    [JsonIgnore]
    public string DisplaySubtitle
    {
        get
        {
            var enabled = IsEnabled ? "已启用" : "已停用";
            return Type switch
            {
                ReminderType.FixedTime => FixedDateTime.HasValue
                    ? $"固定时间 · {FixedDateTime.Value:yyyy-MM-dd HH:mm} · {enabled}"
                    : $"固定时间 · 未设置时间 · {enabled}",
                ReminderType.DailyRepeat => FixedDateTime.HasValue
                    ? $"每天重复 · {FixedDateTime.Value:HH:mm} · {enabled}"
                    : $"每天重复 · 未设置时间 · {enabled}",
                ReminderType.SubjectLinked => $"{NormalizeSubjectForDisplay(SubjectName)}课前 {MinutesBefore} 分钟 · {enabled}",
                _ => enabled
            };
        }
    }

    public CustomReminder Clone()
    {
        return new CustomReminder
        {
            Id = Id,
            Type = Type,
            FixedDateTime = FixedDateTime,
            SubjectName = SubjectName,
            MinutesBefore = MinutesBefore,
            Content = Content,
            IsEnabled = IsEnabled,
            IsRepeating = IsRepeating,
            LastTriggeredDate = LastTriggeredDate,
            LastTriggeredKey = LastTriggeredKey
        };
    }

    public void CopyFrom(CustomReminder source)
    {
        Type = source.Type;
        FixedDateTime = source.FixedDateTime;
        SubjectName = source.SubjectName;
        MinutesBefore = source.MinutesBefore;
        Content = source.Content;
        IsEnabled = source.IsEnabled;
        IsRepeating = source.IsRepeating;
        LastTriggeredDate = source.LastTriggeredDate;
        LastTriggeredKey = source.LastTriggeredKey;
    }

    private static string NormalizeSubjectForDisplay(string? subject)
    {
        return string.IsNullOrWhiteSpace(subject) ? "未设置科目" : subject.Trim().TrimEnd('课');
    }
}

/// <summary>提醒类型枚举</summary>
public enum ReminderType
{
    FixedTime = 0,
    SubjectLinked = 1,
    DailyRepeat = 2
}
