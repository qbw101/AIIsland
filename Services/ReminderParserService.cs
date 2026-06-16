using System.Text.RegularExpressions;
using ClassIsland.AISmartClass.Models;

namespace ClassIsland.AISmartClass.Services;

/// <summary>
/// 自然语言 → CustomReminder 的转换层。
/// 先尝试本地正则匹配，失败后调用 AI 解析。
/// </summary>
public class ReminderParserService
{
    private readonly AIChatService _ai;

    public ReminderParserService(AIChatService ai)
    {
        _ai = ai;
    }

    /// <summary>
    /// 解析用户输入的自然语言提醒。
    /// 返回 (reminder, error)，error 非空表示解析失败。
    /// </summary>
    public async Task<(CustomReminder? reminder, string? error)> ParseAsync(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return (null, "请输入提醒内容");

        // 1. 尝试本地规则匹配
        var local = TryLocalParse(input);
        if (local != null)
            return (local, null);

        // 2. AI 解析
        var result = await _ai.ParseNaturalLanguage(input);

        if (!result.Success)
            return (null, result.ErrorMessage ?? "无法理解");

        // 3. 将 ParseResult 转换为 CustomReminder
        var reminder = new CustomReminder
        {
            Type = result.Type,
            Content = result.Content
        };

        switch (result.Type)
        {
            case ReminderType.FixedTime:
                if (DateTime.TryParse($"{result.Date} {result.Time}", out var dt))
                    reminder.FixedDateTime = dt;
                else
                    return (null, $"无法理解日期时间: {result.Date} {result.Time}");
                break;

            case ReminderType.DailyRepeat:
                if (TimeSpan.TryParse(result.Time, out var ts))
                    reminder.FixedDateTime = DateTime.Today.Add(ts);
                else
                    return (null, "无法理解时间");
                reminder.IsRepeating = true;
                break;

            case ReminderType.SubjectLinked:
                reminder.SubjectName = result.SubjectName;
                reminder.MinutesBefore = result.MinutesBefore;
                break;
        }

        return (reminder, null);
    }

    /// <summary>本地正则匹配，减少 AI 调用</summary>
    private CustomReminder? TryLocalParse(string input)
    {
        // 模式 1：「明天/今天/后天 X点/X时/X分 提醒我 XXX」
        var dateMatch = Regex.Match(
            input,
            @"(今天|明天|后天|周[一二三四五六日])\s*(\d{1,2})[点时](\d{1,2})?分?\s*提醒我?\s*(.+)");
        if (dateMatch.Success)
        {
            var dayOffset = dateMatch.Groups[1].Value switch
            {
                "今天" => 0, "明天" => 1, "后天" => 2,
                _ => 0 // 周X 暂不做精确偏移，让 AI 处理
            };
            if (dayOffset > 0)
            {
                var hour = int.Parse(dateMatch.Groups[2].Value);
                var minute = dateMatch.Groups[3].Success ? int.Parse(dateMatch.Groups[3].Value) : 0;
                var content = dateMatch.Groups[4].Value.Trim();

                return new CustomReminder
                {
                    Type = ReminderType.FixedTime,
                    FixedDateTime = DateTime.Today.AddDays(dayOffset).AddHours(hour).AddMinutes(minute),
                    Content = content
                };
            }
        }

        // 模式 2：「每节XX课前 N 分钟提醒 XXX」
        var subjectMatch = Regex.Match(
            input,
            @"每节(.+?)课前?\s*(\d{1,2})?\s*分钟?\s*提醒我?\s*(.+)");
        if (subjectMatch.Success)
        {
            var subject = subjectMatch.Groups[1].Value.Trim();
            var mins = subjectMatch.Groups[2].Success
                ? int.Parse(subjectMatch.Groups[2].Value) : 3;
            var content = subjectMatch.Groups[3].Value.Trim();

            return new CustomReminder
            {
                Type = ReminderType.SubjectLinked,
                SubjectName = subject,
                MinutesBefore = mins,
                Content = content
            };
        }

        return null;
    }
}
