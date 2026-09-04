using System;
using System.Collections.Generic;
using System.Linq;

namespace ClassIsland.AISmartClass.Services;

/// <summary>
/// 贴心提示上下文的结构化快照，用于增量播报：与上次快照做 Diff，
/// 只返回发生变化的数据项，避免课间每次刷新都重复播报相同内容。
/// </summary>
public sealed class ThoughtfulContextSnapshot
{
    /// <summary>日期（yyyy-MM-dd），用于跨天重置缓存。</summary>
    public string DateLabel { get; init; } = "";

    /// <summary>当前时段（清晨/上午/中午/下午/晚上/深夜）。</summary>
    public string TimePeriod { get; init; } = "";

    /// <summary>天气描述文本（如「晴」「多云」），由天气代码转换而来。</summary>
    public string? WeatherDescription { get; init; }

    /// <summary>当前温度（摄氏度）。</summary>
    public double? TemperatureC { get; init; }

    /// <summary>天气预警标题集合。</summary>
    public IReadOnlyList<string> WeatherAlerts { get; init; } = Array.Empty<string>();

    /// <summary>今日新闻标题列表。</summary>
    public IReadOnlyList<string> NewsTitles { get; init; } = Array.Empty<string>();

    /// <summary>生日信息。</summary>
    public string BirthdayGreeting { get; init; } = "";

    /// <summary>值日提醒。</summary>
    public string DutyReminder { get; init; } = "";

    /// <summary>节假日描述。</summary>
    public string HolidayDescription { get; init; } = "";

    /// <summary>当前播放的歌曲（标题·歌手）。</summary>
    public string MusicKey { get; init; } = "";

    private const double TemperatureChangeThreshold = 2.0;
    private const int MaxNewsInChange = 3;

    /// <summary>
    /// 与上一次快照比较，返回「发生变化」的数据项文本行列表。
    /// previous 为 null（首次或跨天）时，所有非空项都视为变化。
    /// 无变化时返回空列表，调用方据此跳过贴心提示生成。
    /// </summary>
    public IReadOnlyList<string> Diff(ThoughtfulContextSnapshot? previous)
    {
        var changes = new List<string>();

        if (previous == null)
        {
            if (!string.IsNullOrWhiteSpace(TimePeriod))
                changes.Add($"当前时段：{TimePeriod}");
            if (!string.IsNullOrWhiteSpace(WeatherDescription))
                changes.Add($"当前天气：{DescribeWeather()}");
            if (WeatherAlerts.Count > 0)
                changes.Add($"天气预警：{string.Join("；", WeatherAlerts)}");
            if (NewsTitles.Count > 0)
                changes.Add($"今日新闻：{string.Join("；", NewsTitles.Take(MaxNewsInChange))}");
            if (!string.IsNullOrWhiteSpace(BirthdayGreeting))
                changes.Add($"生日信息：{BirthdayGreeting}");
            if (!string.IsNullOrWhiteSpace(DutyReminder))
                changes.Add($"值日提醒：{DutyReminder}");
            if (!string.IsNullOrWhiteSpace(HolidayDescription))
                changes.Add($"节假日信息：{HolidayDescription}");
            if (!string.IsNullOrWhiteSpace(MusicKey))
                changes.Add($"当前媒体：{MusicKey}");
            return changes;
        }

        if (!string.Equals(TimePeriod, previous.TimePeriod, StringComparison.Ordinal))
            changes.Add($"当前时段：{TimePeriod}");

        if (WeatherChanged(previous))
            changes.Add($"当前天气：{DescribeWeather()}");

        if (!WeatherAlerts.SequenceEqual(previous.WeatherAlerts, StringComparer.Ordinal))
            changes.Add($"天气预警：{string.Join("；", WeatherAlerts)}");

        var newNews = NewsTitles.Except(previous.NewsTitles, StringComparer.OrdinalIgnoreCase).ToList();
        if (newNews.Count > 0)
            changes.Add($"新增新闻：{string.Join("；", newNews.Take(MaxNewsInChange))}");

        if (!string.Equals(BirthdayGreeting, previous.BirthdayGreeting, StringComparison.Ordinal))
            changes.Add($"生日信息：{BirthdayGreeting}");

        if (!string.Equals(DutyReminder, previous.DutyReminder, StringComparison.Ordinal))
            changes.Add($"值日提醒：{DutyReminder}");

        if (!string.Equals(HolidayDescription, previous.HolidayDescription, StringComparison.Ordinal))
            changes.Add($"节假日信息：{HolidayDescription}");

        if (!string.IsNullOrWhiteSpace(MusicKey) &&
            !string.Equals(MusicKey, previous.MusicKey, StringComparison.Ordinal))
            changes.Add($"当前媒体：{MusicKey}");

        return changes;
    }

    private bool WeatherChanged(ThoughtfulContextSnapshot previous)
    {
        if (!string.Equals(WeatherDescription, previous.WeatherDescription, StringComparison.Ordinal))
            return true;

        if (TemperatureC == null && previous.TemperatureC == null) return false;
        if (TemperatureC == null || previous.TemperatureC == null) return true;
        return Math.Abs(TemperatureC.Value - previous.TemperatureC.Value) >= TemperatureChangeThreshold;
    }

    private string DescribeWeather()
    {
        if (TemperatureC == null) return WeatherDescription ?? "";
        return $"{WeatherDescription}，{TemperatureC:0.#}°C";
    }
}
