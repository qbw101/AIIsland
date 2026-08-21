using System.Text.Json.Serialization;

namespace ClassIsland.AISmartClass.Models;

/// <summary>
/// AI 作业解析结果
/// </summary>
public class HomeworkParseResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("error")]
    public string? ErrorMessage { get; set; }

    [JsonPropertyName("items")]
    public List<HomeworkParseItem> Items { get; set; } = new();

    /// <summary>原始输入文本</summary>
    [JsonIgnore]
    public string RawInput { get; set; } = "";

    /// <summary>是否由本地规则引擎生成，而不是 AI 直接返回。</summary>
    [JsonIgnore]
    public bool UsedLocalRules { get; set; }
}

/// <summary>
/// AI 解析出的单条作业
/// </summary>
public class HomeworkParseItem
{
    [JsonPropertyName("subject")]
    public string Subject { get; set; } = "";

    [JsonPropertyName("content")]
    public string Content { get; set; } = "";

    [JsonPropertyName("dueDate")]
    public string DueDate { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "书面作业";

    [JsonPropertyName("estimatedMinutes")]
    public int EstimatedMinutes { get; set; } = 30;

    /// <summary>
    /// 将字符串日期解析为 DateTime，失败时默认为明天
    /// </summary>
    [JsonIgnore]
    public DateTime ParsedDueDate
    {
        get
        {
            if (DateTime.TryParse(DueDate, out var date))
            {
                return date.Date;
            }

            // 尝试解析相对日期
            var today = DateTime.Today;
            return DueDate?.ToLower() switch
            {
                "今天" or "today" => today,
                "明天" or "tomorrow" => today.AddDays(1),
                "后天" => today.AddDays(2),
                "大后天" => today.AddDays(3),
                _ when DueDate?.Contains("周") == true || DueDate?.Contains("星期") == true => ParseWeekday(DueDate, today),
                _ => today.AddDays(1)
            };
        }
    }

    private static DateTime ParseWeekday(string input, DateTime today)
    {
        var weekdays = new Dictionary<string, DayOfWeek>
        {
            { "周一", DayOfWeek.Monday }, { "星期一", DayOfWeek.Monday },
            { "周二", DayOfWeek.Tuesday }, { "星期二", DayOfWeek.Tuesday },
            { "周三", DayOfWeek.Wednesday }, { "星期三", DayOfWeek.Wednesday },
            { "周四", DayOfWeek.Thursday }, { "星期四", DayOfWeek.Thursday },
            { "周五", DayOfWeek.Friday }, { "星期五", DayOfWeek.Friday },
            { "周六", DayOfWeek.Saturday }, { "星期六", DayOfWeek.Saturday },
            { "周日", DayOfWeek.Sunday }, { "星期日", DayOfWeek.Sunday }, { "周天", DayOfWeek.Sunday }
        };

        foreach (var kvp in weekdays)
        {
            if (input.Contains(kvp.Key))
            {
                var daysUntil = ((int)kvp.Value - (int)today.DayOfWeek + 7) % 7;
                if (daysUntil == 0) daysUntil = 7; // 如果今天是同一天，算下周
                return today.AddDays(daysUntil);
            }
        }

        return today.AddDays(1);
    }
}
