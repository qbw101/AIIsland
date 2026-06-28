namespace ClassIsland.AISmartClass.Models;

/// <summary>
/// 自然语言 → 结构化提醒 的解析结果。
/// 由 AIChatService 的 ParseNaturalLanguage 方法返回。
/// </summary>
public class ReminderParseResult
{
    /// <summary>解析是否成功</summary>
    public bool Success { get; set; }

    /// <summary>解析出的提醒类型</summary>
    public ReminderType Type { get; set; }

    /// <summary>日期（格式 yyyy-MM-dd），固定时间类型填日期部分</summary>
    public string? Date { get; set; }

    /// <summary>时间（格式 HH:mm），固定时间/每日重复类型填时间部分</summary>
    public string? Time { get; set; }

    /// <summary>关联科目名称（科目关联类型填写）</summary>
    public string? SubjectName { get; set; }

    /// <summary>提前分钟数（科目关联类型填写），默认 3</summary>
    public int MinutesBefore { get; set; } = 3;

    /// <summary>解析出的提醒正文</summary>
    public string Content { get; set; } = "";

    /// <summary>如果不能解析，返回给用户的提示信息</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>用于缓存的原始输入</summary>
    public string RawInput { get; set; } = "";

    /// <summary>将解析结果转换为 CustomReminder 对象</summary>
    public CustomReminder ToCustomReminder()
    {
        var reminder = new CustomReminder
        {
            Type = Type,
            Content = Content.Trim(),
            SubjectName = string.IsNullOrWhiteSpace(SubjectName) ? null : SubjectName.Trim().TrimEnd('课'),
            MinutesBefore = Math.Clamp(MinutesBefore, 0, 120),
            IsEnabled = true,
            IsRepeating = Type != ReminderType.FixedTime
        };

        if (!string.IsNullOrWhiteSpace(Time) && TimeSpan.TryParse(Time, out var time))
        {
            if (Type == ReminderType.FixedTime)
            {
                if (!string.IsNullOrWhiteSpace(Date) && DateTime.TryParse(Date, out var date))
                    reminder.FixedDateTime = date.Date + time;
            }
            else if (Type == ReminderType.DailyRepeat)
            {
                reminder.FixedDateTime = new DateTime(2000, 1, 1, time.Hours, time.Minutes, 0);
            }
        }

        return reminder;
    }
}
