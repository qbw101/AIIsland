using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using ClassIsland.AISmartClass.Models;

namespace ClassIsland.AISmartClass.Services;

/// <summary>
/// 本地规则作业解析器。当 AI 不可用时，按关键词拆分并识别科目、日期、类型和预计耗时。
/// </summary>
public static partial class HomeworkRuleParser
{
    private static readonly HashSet<string> SubjectKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "数学", "语文", "英语", "物理", "化学", "生物",
        "历史", "地理", "政治", "道德与法治", "道法",
        "体育", "美术", "音乐", "信息技术", "信息", "通用技术",
        "科学", "自然", "劳技", "劳动", "心理", "心理健康"
    };

    private static readonly IReadOnlyList<(string[] Keywords, string Type, int BaseMinutes)> TypePatterns = new List<(string[], string, int)>
    {
        (["背诵", "默写", "朗读", "诵读"], "背诵", 20),
        (["练习册", "习题", "练习"], "书面作业", 25),
        (["试卷", "卷子", "考试卷", "测验卷"], "书面作业", 40),
        (["抄写", "抄"], "书面作业", 15),
        (["预习"], "预习", 25),
        (["复习"], "复习", 30),
        (["作文"], "书面作业", 50),
        (["实验", "实践", "手工"], "实践", 30)
    };

    /// <summary>
    /// 用本地规则解析用户输入的作业描述。
    /// </summary>
    /// <param name="input">原始输入文本</param>
    /// <param name="referenceDate">解析日期时的参考日期，默认今天。</param>
    /// <returns>解析结果，成功时 Items 不为空。</returns>
    public static HomeworkParseResult Parse(string input, DateTime? referenceDate = null)
    {
        var result = new HomeworkParseResult
        {
            Success = false,
            ErrorMessage = "本地规则未能识别任何作业条目",
            RawInput = input,
            UsedLocalRules = true
        };

        if (string.IsNullOrWhiteSpace(input))
            return result;

        var today = referenceDate ?? DateTime.Now;
        var items = new List<HomeworkParseItem>();
        var segments = SplitIntoSegments(input);

        foreach (var segment in segments)
        {
            var trimmed = segment.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            var item = ParseSegment(trimmed, today);
            if (item != null && !string.IsNullOrWhiteSpace(item.Subject) && !string.IsNullOrWhiteSpace(item.Content))
            {
                items.Add(item);
            }
        }

        if (items.Count == 0)
            return result;

        result.Success = true;
        result.ErrorMessage = null;
        result.Items = items;
        return result;
    }

    private static IReadOnlyList<string> SplitIntoSegments(string input)
    {
        // 按常见分隔符拆分：中英文逗号、分号、句号、换行、竖线
        var parts = Regex.Split(input, @"[,，;；。！!\|\r\n]+");
        return parts
            .Select(p => p.Trim())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList();
    }

    private static HomeworkParseItem? ParseSegment(string segment, DateTime today)
    {
        var (subject, remaining) = ExtractSubject(segment);
        if (string.IsNullOrWhiteSpace(subject))
        {
            // 没识别到科目，整条作为内容，科目标记为待确认
            subject = "其他";
        }

        var (dueDate, dueDateText) = ExtractDueDate(remaining, today);
        var type = DetermineType(remaining, out var baseMinutes);
        var estimated = EstimateMinutes(remaining, baseMinutes, type);
        var content = CleanContent(remaining, dueDateText);

        if (string.IsNullOrWhiteSpace(content))
            content = remaining.Trim();

        return new HomeworkParseItem
        {
            Subject = subject,
            Content = content,
            DueDate = dueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Type = type,
            EstimatedMinutes = estimated
        };
    }

    private static (string Subject, string Remaining) ExtractSubject(string text)
    {
        // 优先匹配开头：科目名在最前面
        var firstWord = text.Trim().Split(new[] { ' ', '：', ':', '，', ',', '；', ';', 'P', 'p', '第' }, 2)[0];
        if (!string.IsNullOrWhiteSpace(firstWord) && SubjectKeywords.Contains(firstWord.Trim()))
        {
            return (firstWord.Trim(), text.Substring(firstWord.Length).TrimStart('：', ':', ' ', '，', ',', '；', ';'));
        }

        // 全局匹配科目关键词
        foreach (var subject in SubjectKeywords.OrderByDescending(s => s.Length))
        {
            if (text.Contains(subject, StringComparison.OrdinalIgnoreCase))
            {
                var remaining = text.Replace(subject, "", StringComparison.OrdinalIgnoreCase).Trim();
                return (subject, remaining);
            }
        }

        return (string.Empty, text);
    }

    private static (DateTime Date, string MatchedText) ExtractDueDate(string text, DateTime today)
    {
        var remaining = text;
        var match = DateRegex().Match(remaining);
        if (match.Success && TryParseDateString(match.Value, today, out var date))
        {
            return (date, match.Value);
        }

        return (today.AddDays(1), string.Empty); // 默认明天
    }

    private static bool TryParseDateString(string value, DateTime today, out DateTime date)
    {
        date = today.AddDays(1);
        var normalized = value.Trim();

        // 今天/明天/后天/大后天。先判断更长的词，避免“大后天”被“后天”提前命中。
        if (normalized.Contains("大后天", StringComparison.OrdinalIgnoreCase))
        {
            date = today.AddDays(3);
            return true;
        }

        if (normalized.Contains("今天", StringComparison.OrdinalIgnoreCase))
        {
            date = today;
            return true;
        }

        if (normalized.Contains("明天", StringComparison.OrdinalIgnoreCase))
        {
            date = today.AddDays(1);
            return true;
        }

        if (normalized.Contains("后天", StringComparison.OrdinalIgnoreCase))
        {
            date = today.AddDays(2);
            return true;
        }

        // 周 X / 星期 X
        var dayOfWeekMatch = DayOfWeekRegex().Match(normalized);
        if (dayOfWeekMatch.Success)
        {
            var dayName = dayOfWeekMatch.Groups[1].Value.Trim();
            var targetDay = ParseDayOfWeekChinese(dayName);
            if (targetDay.HasValue)
            {
                var currentIndex = ((int)today.DayOfWeek + 6) % 7; // 周一 = 0
                var targetIndex = ((int)targetDay.Value + 6) % 7;
                int offset;

                if (normalized.Contains("下周", StringComparison.OrdinalIgnoreCase))
                {
                    // “下周 X”表示下一个自然周中的目标日，而不是“下一次目标日”再加七天。
                    offset = 7 - currentIndex + targetIndex;
                }
                else if (normalized.Contains("本周", StringComparison.OrdinalIgnoreCase))
                {
                    offset = targetIndex - currentIndex;
                    if (offset < 0) offset += 7;
                }
                else
                {
                    offset = (targetIndex - currentIndex + 7) % 7;
                    if (offset == 0) offset = 7;
                }

                date = today.AddDays(offset);
                return true;
            }
        }

        // yyyy-M-d / M-d，兼容用户常用的一位数月日。
        if (DateTime.TryParseExact(
                normalized,
                new[] { "yyyy-M-d", "yyyy-MM-dd", "M-d", "MM-dd" },
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            var hasExplicitYear = normalized.Count(c => c == '-') == 2;
            date = hasExplicitYear
                ? parsed
                : new DateTime(today.Year, parsed.Month, parsed.Day);
            return true;
        }

        return false;
    }

    private static DayOfWeek? ParseDayOfWeekChinese(string dayName)
    {
        return dayName switch
        {
            "一" or "1" or "周一" => DayOfWeek.Monday,
            "二" or "2" or "周二" => DayOfWeek.Tuesday,
            "三" or "3" or "周三" => DayOfWeek.Wednesday,
            "四" or "4" or "周四" => DayOfWeek.Thursday,
            "五" or "5" or "周五" => DayOfWeek.Friday,
            "六" or "6" or "周六" => DayOfWeek.Saturday,
            "日" or "7" or "周日" or "天" or "星期天" => DayOfWeek.Sunday,
            _ => null
        };
    }

    private static string DetermineType(string text, out int baseMinutes)
    {
        foreach (var (keywords, type, minutes) in TypePatterns)
        {
            foreach (var keyword in keywords)
            {
                if (text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    baseMinutes = minutes;
                    return type;
                }
            }
        }

        baseMinutes = 25;
        return "书面作业";
    }

    private static int EstimateMinutes(string text, int baseMinutes, string type)
    {
        var minutes = baseMinutes;

        // 页码范围 P23-25 => +10 分钟
        var pageRangeMatch = PageRangeRegex().Match(text);
        if (pageRangeMatch.Success)
        {
            if (int.TryParse(pageRangeMatch.Groups[1].Value, out var start) &&
                int.TryParse(pageRangeMatch.Groups[2].Value, out var end))
            {
                var pages = Math.Max(1, end - start + 1);
                minutes += pages * 8;
            }
        }
        // 单个页码 P23 / 第23页。页码是位置，不代表作业页数，只按一页估算。
        else if (SinglePageRegex().Match(text) is { Success: true } singlePageMatch)
        {
            var pageText = singlePageMatch.Groups[1].Success
                ? singlePageMatch.Groups[1].Value
                : singlePageMatch.Groups[2].Value;
            if (int.TryParse(pageText, out _))
            {
                minutes += 8;
            }
        }

        // 单元 Unit3 / 第三单元
        if (UnitRegex().Match(text) is { Success: true } unitMatch)
        {
            minutes += 5;
        }

        // 限制范围
        return Math.Clamp(minutes, 5, 180);
    }

    private static string CleanContent(string text, string dueDateText)
    {
        var result = text;

        // 去掉日期词
        if (!string.IsNullOrWhiteSpace(dueDateText))
        {
            result = result.Replace(dueDateText, "", StringComparison.OrdinalIgnoreCase);
        }

        // 去掉科目词（前面已去，这里兜底）
        foreach (var subject in SubjectKeywords.OrderByDescending(s => s.Length))
        {
            result = result.Replace(subject, "", StringComparison.OrdinalIgnoreCase);
        }

        // 去掉多余的标点和空格
        result = Regex.Replace(result, @"^[\s：:,，;；]+|[\s：:,，;；]+$", "");
        return result.Trim();
    }

    [GeneratedRegex(@"大后天|今天|明天|后天|下周[一二三四五六日天]|本周[一二三四五六日天]|星期[一二三四五六日天]|周[一二三四五六日天]|\d{4}-\d{1,2}-\d{1,2}|\d{1,2}-\d{1,2}", RegexOptions.IgnoreCase, "zh-CN")]
    private static partial Regex DateRegex();

    [GeneratedRegex(@"(?:下周|本周)?(?:星期|周)([一二三四五六日天1-7])", RegexOptions.IgnoreCase, "zh-CN")]
    private static partial Regex DayOfWeekRegex();

    [GeneratedRegex(@"[Pp]\s*(\d+)\s*[-—~～]\s*(\d+)", RegexOptions.IgnoreCase, "zh-CN")]
    private static partial Regex PageRangeRegex();

    [GeneratedRegex(@"[Pp]\s*(\d+)|第\s*(\d+)\s*[页页]", RegexOptions.IgnoreCase, "zh-CN")]
    private static partial Regex SinglePageRegex();

    [GeneratedRegex(@"[Uu]nit\s*(\d+)|第\s*(\d+)\s*单元", RegexOptions.IgnoreCase, "zh-CN")]
    private static partial Regex UnitRegex();
}
