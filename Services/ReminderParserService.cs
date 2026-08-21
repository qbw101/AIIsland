using System.Text.RegularExpressions;
using ClassIsland.AISmartClass.Models;

namespace ClassIsland.AISmartClass.Services;

/// <summary>
/// 自然语言 → CustomReminder 的转换层。
/// 先用本地规则覆盖常见输入；规则无法识别时再调用 AI 解析。
/// </summary>
public class ReminderParserService
{
    private readonly AIChatService _ai;

    public ReminderParserService(AIChatService ai)
    {
        _ai = ai;
    }

    public async Task<(CustomReminder? reminder, string? error)> ParseAsync(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return (null, "请输入提醒内容");

        input = NormalizeInput(input);

        var local = TryLocalParse(input);
        if (local != null)
            return (local, null);

        var result = await _ai.ParseNaturalLanguage(input);
        if (!result.Success)
            return (null, result.ErrorMessage ?? "无法理解这条提醒");

        var reminder = result.ToCustomReminder();
        var validationError = ValidateReminder(reminder);
        if (validationError != null)
            return (null, validationError);

        return (reminder, null);
    }

    private static CustomReminder? TryLocalParse(string input)
    {
        return TryParseSubjectLinked(input)
            ?? TryParseDailyRepeat(input)
            ?? TryParseFixedTime(input);
    }

    private static CustomReminder? TryParseSubjectLinked(string input)
    {
        // 例：每节数学课前5分钟提醒我带作业本 / 数学课前提醒我拿草稿纸
        var match = Regex.Match(
            input,
            @"^(?:每节|每次)?(?<subject>[\u4e00-\u9fa5A-Za-z0-9]+?)课前(?:(?<mins>\d{1,3})分钟?)?(?:提醒我?|记得|提示我?)?(?<content>.+)$");
        if (!match.Success) return null;

        var subject = NormalizeSubject(match.Groups["subject"].Value);
        var content = CleanContent(match.Groups["content"].Value);
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(content)) return null;

        return new CustomReminder
        {
            Type = ReminderType.SubjectLinked,
            SubjectName = subject,
            MinutesBefore = match.Groups["mins"].Success ? ClampMinutes(match.Groups["mins"].Value, 3) : 3,
            Content = content,
            IsEnabled = true,
            IsRepeating = true
        };
    }

    private static CustomReminder? TryParseDailyRepeat(string input)
    {
        // 例：每天7点提醒我背单词 / 每天晚上8:30提醒我检查书包
        var match = Regex.Match(
            input,
            @"^(?:每天|每日|天天)(?<period>上午|早上|中午|下午|晚上|晚间|凌晨)?(?<hour>\d{1,2})(?:[:：点时](?<minute>\d{1,2})?)?分?(?:提醒我?|记得|提示我?)?(?<content>.+)$");
        if (!match.Success) return null;

        var content = CleanContent(match.Groups["content"].Value);
        if (string.IsNullOrWhiteSpace(content)) return null;

        var hour = NormalizeHour(match.Groups["hour"].Value, match.Groups["period"].Value);
        var minute = match.Groups["minute"].Success && !string.IsNullOrWhiteSpace(match.Groups["minute"].Value)
            ? int.Parse(match.Groups["minute"].Value) : 0;
        if (!IsValidTime(hour, minute)) return null;

        return new CustomReminder
        {
            Type = ReminderType.DailyRepeat,
            FixedDateTime = new DateTime(2000, 1, 1, hour, minute, 0),
            Content = content,
            IsEnabled = true,
            IsRepeating = true
        };
    }

    private static CustomReminder? TryParseFixedTime(string input)
    {
        // 例：明天下午3点提醒我交数学作业 / 今天18:30提醒我带饭卡 / 后天早上7点10分提醒我早读
        var match = Regex.Match(
            input,
            @"^(?<date>今天|明天|后天|大后天|周[一二三四五六日天]|星期[一二三四五六日天]|\d{1,2}月\d{1,2}[日号]?|\d{4}-\d{1,2}-\d{1,2})\s*(?<period>上午|早上|中午|下午|晚上|晚间|凌晨)?(?<hour>\d{1,2})(?:[:：点时](?<minute>\d{1,2})?)?分?(?:提醒我?|记得|提示我?)?(?<content>.+)$");
        if (!match.Success) return null;

        var date = ParseDate(match.Groups["date"].Value, DateTime.Today);
        if (!date.HasValue) return null;

        var hour = NormalizeHour(match.Groups["hour"].Value, match.Groups["period"].Value);
        var minute = match.Groups["minute"].Success && !string.IsNullOrWhiteSpace(match.Groups["minute"].Value)
            ? int.Parse(match.Groups["minute"].Value) : 0;
        if (!IsValidTime(hour, minute)) return null;

        var content = CleanContent(match.Groups["content"].Value);
        if (string.IsNullOrWhiteSpace(content)) return null;

        return new CustomReminder
        {
            Type = ReminderType.FixedTime,
            FixedDateTime = date.Value.Date.AddHours(hour).AddMinutes(minute),
            Content = content,
            IsEnabled = true,
            IsRepeating = false
        };
    }

    private static string? ValidateReminder(CustomReminder reminder)
    {
        if (string.IsNullOrWhiteSpace(reminder.Content)) return "提醒内容为空";

        return reminder.Type switch
        {
            ReminderType.FixedTime when !reminder.FixedDateTime.HasValue => "固定时间提醒缺少日期时间",
            ReminderType.DailyRepeat when !reminder.FixedDateTime.HasValue => "每日重复提醒缺少时间",
            ReminderType.SubjectLinked when string.IsNullOrWhiteSpace(reminder.SubjectName) => "科目关联提醒缺少科目名称",
            _ => null
        };
    }

    private static DateTime? ParseDate(string value, DateTime today)
    {
        return value switch
        {
            "今天" => today,
            "明天" => today.AddDays(1),
            "后天" => today.AddDays(2),
            "大后天" => today.AddDays(3),
            _ when value.StartsWith("周") || value.StartsWith("星期") => ParseWeekday(value, today),
            _ when Regex.IsMatch(value, @"^\d{1,2}月\d{1,2}[日号]?$") => ParseMonthDay(value, today),
            _ when DateTime.TryParse(value, out var parsed) => parsed,
            _ => null
        };
    }

    private static DateTime ParseWeekday(string value, DateTime today)
    {
        var ch = value.Last();
        var target = ch switch
        {
            '一' => DayOfWeek.Monday,
            '二' => DayOfWeek.Tuesday,
            '三' => DayOfWeek.Wednesday,
            '四' => DayOfWeek.Thursday,
            '五' => DayOfWeek.Friday,
            '六' => DayOfWeek.Saturday,
            '日' or '天' => DayOfWeek.Sunday,
            _ => today.DayOfWeek
        };
        var diff = ((int)target - (int)today.DayOfWeek + 7) % 7;
        if (diff == 0) diff = 7;
        return today.AddDays(diff);
    }

    private static DateTime? ParseMonthDay(string value, DateTime today)
    {
        var match = Regex.Match(value, @"^(?<month>\d{1,2})月(?<day>\d{1,2})[日号]?$");
        if (!match.Success) return null;

        var month = int.Parse(match.Groups["month"].Value);
        var day = int.Parse(match.Groups["day"].Value);
        try
        {
            var date = new DateTime(today.Year, month, day);
            return date < today ? date.AddYears(1) : date;
        }
        catch
        {
            return null;
        }
    }

    private static int NormalizeHour(string hourText, string period)
    {
        var hour = int.Parse(hourText);
        if ((period is "下午" or "晚上" or "晚间") && hour is >= 1 and <= 11)
            hour += 12;
        if (period == "中午" && hour is >= 1 and <= 10)
            hour += 12;
        if ((period is "早上" or "上午" or "凌晨") && hour == 12)
            hour = 0;
        return hour;
    }

    private static bool IsValidTime(int hour, int minute) => hour is >= 0 and <= 23 && minute is >= 0 and <= 59;

    private static int ClampMinutes(string value, int fallback)
    {
        return int.TryParse(value, out var minutes) ? Math.Clamp(minutes, 0, 120) : fallback;
    }

    private static string NormalizeInput(string value)
    {
        return value.Trim()
            .Replace("：", ":")
            .Replace("　", " ");
    }

    private static string NormalizeSubject(string value)
    {
        return value.Trim().TrimEnd('课');
    }

    private static string CleanContent(string value)
    {
        return value.Trim().TrimStart('，', ',', '。', '.', '：', ':', ' ');
    }
}
