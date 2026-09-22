using System.Globalization;
using System.Text.Json;

namespace ClassIsland.AISmartClass.Services;

/// <summary>某一天在节假日日历中的性质。</summary>
public enum HolidayDayKind
{
    /// <summary>普通工作日。</summary>
    Normal = 0,

    /// <summary>普通周末。</summary>
    Weekend = 1,

    /// <summary>法定节假日 / 放假。</summary>
    OffDay = 2,

    /// <summary>调休补班，需要正常上课。</summary>
    MakeupWorkday = 3
}

/// <summary>节假日日历中的一天。</summary>
/// <param name="Date">日期。</param>
/// <param name="Name">节日名称，例如「国庆节」。</param>
/// <param name="IsOffDay">是否为放假日；<c>false</c> 表示调休补班。</param>
public sealed record HolidayDay(DateOnly Date, string Name, bool IsOffDay);

/// <summary>
/// 中国法定节假日日历（含调休）。
/// 数据来自开源项目 <c>NateScarlet/holiday-cn</c>（每年依据国务院公告整理），
/// 在线抓取失败时退回本地磁盘缓存，再失败退回内置固定日期兜底，保证离线仍可用。
/// </summary>
/// <remarks>
/// 提供两项能力：
/// <list type="bullet">
/// <item><see cref="DescribeToday"/>：判断今天是否为节假日 / 周末 / 调休补班；</item>
/// <item><see cref="GetUpcomingReminders"/>：生成「提前 N 天」的节假日提醒与调休提醒。</item>
/// </list>
/// </remarks>
public sealed class HolidayCalendarService
{
    // 与其他外部数据一致：不用 HttpClient 全局超时，改由每次请求独立计时。
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    /// <summary>单次抓取的超时时间。节假日数据很小，超时后直接退到缓存/兜底。</summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(6);

    /// <summary>按优先级尝试的数据源模板，<c>{0}</c> 为年份。首个为 jsDelivr，国内可达性最好。</summary>
    private static readonly string[] Sources =
    [
        "https://cdn.jsdelivr.net/gh/NateScarlet/holiday-cn@master/{0}.json",
        "https://raw.githubusercontent.com/NateScarlet/holiday-cn/master/{0}.json",
        "https://ghproxy.net/https://raw.githubusercontent.com/NateScarlet/holiday-cn/master/{0}.json"
    ];

    private static readonly Dictionary<int, IReadOnlyList<HolidayDay>> MemoryCache = new();
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private readonly string? _cacheDirectory;

    /// <param name="cacheDirectory">
    /// 磁盘缓存目录；为 <c>null</c> 时使用插件配置目录，仍不可用时仅走内存缓存与在线抓取。
    /// </param>
    public HolidayCalendarService(string? cacheDirectory = null)
        => _cacheDirectory = cacheDirectory ?? Plugin.ConfigFolderPath;

    /// <summary>
    /// 获取指定年份的放假与调休安排。顺序为：内存缓存 → 磁盘缓存 → 在线抓取 → 内置兜底。
    /// 不会抛出异常，最差情况返回内置固定日期节日。
    /// </summary>
    public async Task<IReadOnlyList<HolidayDay>> GetYearAsync(int year, CancellationToken ct = default)
    {
        if (MemoryCache.TryGetValue(year, out var cached)) return cached;

        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (MemoryCache.TryGetValue(year, out cached)) return cached;

            var fromDisk = TryLoadDiskCache(year);
            if (fromDisk.Count > 0)
            {
                MemoryCache[year] = fromDisk;
                return fromDisk;
            }

            var (days, rawJson) = await TryFetchAsync(year, ct).ConfigureAwait(false);
            if (days.Count > 0)
            {
                MemoryCache[year] = days;
                SaveDiskCache(year, rawJson);
                return days;
            }

            var fallback = BuildFallback(year);
            Logger.Info($"[Holiday] {year} 年节假日数据不可用，使用内置兜底（仅固定日期节日）");
            MemoryCache[year] = fallback;
            return fallback;
        }
        finally
        {
            Gate.Release();
        }
    }

    // ========================================
    //  纯逻辑（可单测，不依赖网络）
    // ========================================

    /// <summary>解析 holiday-cn 的年度 JSON。格式不符或解析失败时返回空列表，不抛异常。</summary>
    public static IReadOnlyList<HolidayDay> ParseYearJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<HolidayDay>();

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("days", out var daysNode) ||
                daysNode.ValueKind != JsonValueKind.Array)
                return Array.Empty<HolidayDay>();

            var result = new List<HolidayDay>();
            var seen = new HashSet<DateOnly>();
            foreach (var node in daysNode.EnumerateArray())
            {
                if (node.ValueKind != JsonValueKind.Object) continue;

                var name = node.TryGetProperty("name", out var nameNode) ? nameNode.GetString() ?? "" : "";
                var dateText = node.TryGetProperty("date", out var dateNode) ? dateNode.GetString() : null;
                var isOffDay = node.TryGetProperty("isOffDay", out var offNode) &&
                               offNode.ValueKind == JsonValueKind.True;

                if (!DateOnly.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var date))
                    continue;
                if (!seen.Add(date)) continue;

                result.Add(new HolidayDay(date, name.Trim(), isOffDay));
            }

            result.Sort((a, b) => a.Date.CompareTo(b.Date));
            return result;
        }
        catch (JsonException)
        {
            return Array.Empty<HolidayDay>();
        }
    }

    /// <summary>判断某一天的性质。</summary>
    public static HolidayDayKind Classify(IReadOnlyList<HolidayDay> days, DateTime date)
    {
        var target = DateOnly.FromDateTime(date.Date);
        foreach (var day in days)
        {
            if (day.Date != target) continue;
            return day.IsOffDay ? HolidayDayKind.OffDay : HolidayDayKind.MakeupWorkday;
        }

        return date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday
            ? HolidayDayKind.Weekend
            : HolidayDayKind.Normal;
    }

    /// <summary>
    /// 生成「今天」的节假日描述，供每日简报使用。
    /// 放假返回「国庆节假期」，调休补班返回「国庆节调休（需正常上课）」，
    /// 普通周末返回「周末」，其余返回空串。
    /// </summary>
    public static string DescribeToday(IReadOnlyList<HolidayDay> days, DateTime date)
    {
        var target = DateOnly.FromDateTime(date.Date);
        foreach (var day in days)
        {
            if (day.Date != target) continue;
            return day.IsOffDay
                ? $"{day.Name}假期"
                : $"{day.Name}调休（需正常上课）";
        }

        return date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? "周末" : "";
    }

    /// <summary>
    /// 生成提前提醒：对「未来 1~<paramref name="daysAhead"/> 天内」即将开始的假期与调休日，
    /// 各输出一行提醒文本。假期只在首日提醒一次（多天连休不重复）。
    /// </summary>
    /// <param name="days">该年度的日历数据。</param>
    /// <param name="today">今天。</param>
    /// <param name="daysAhead">提前天数，默认 3。</param>
    /// <param name="includeHoliday">是否包含节假日提醒。</param>
    /// <param name="includeMakeup">是否包含调休提醒。</param>
    public static IReadOnlyList<string> GetUpcomingReminders(
        IReadOnlyList<HolidayDay> days,
        DateTime today,
        int daysAhead = 3,
        bool includeHoliday = true,
        bool includeMakeup = true)
    {
        var results = new List<string>();
        if (days.Count == 0 || daysAhead <= 0) return results;

        var baseDate = DateOnly.FromDateTime(today.Date);
        var offDaySet = new HashSet<DateOnly>();
        foreach (var day in days)
            if (day.IsOffDay) offDaySet.Add(day.Date);

        foreach (var day in days)
        {
            var delta = day.Date.DayNumber - baseDate.DayNumber;
            if (delta < 1 || delta > daysAhead) continue;

            var dateText = $"{day.Date.Month}月{day.Date.Day}日 {WeekdayCn(day.Date.DayOfWeek)}";

            if (day.IsOffDay)
            {
                if (!includeHoliday) continue;
                // 多天连休只在首日提醒一次
                if (offDaySet.Contains(day.Date.AddDays(-1))) continue;
                results.Add($"节假日提醒：{delta} 天后（{dateText}）开始放假，节日为{day.Name}");
            }
            else
            {
                if (!includeMakeup) continue;
                results.Add($"调休提醒：{delta} 天后（{dateText}）为{day.Name}调休，需要正常上课");
            }
        }

        return results;
    }

    /// <summary>
    /// 内置兜底数据：仅覆盖日期固定的节日，用于完全离线且无缓存时。
    /// 与旧版硬编码行为保持一致，保证不倒退。
    /// </summary>
    public static IReadOnlyList<HolidayDay> BuildFallback(int year)
    {
        return
        [
            new HolidayDay(new DateOnly(year, 1, 1), "元旦", true),
            new HolidayDay(new DateOnly(year, 5, 1), "劳动节", true),
            new HolidayDay(new DateOnly(year, 10, 1), "国庆节", true),
            new HolidayDay(new DateOnly(year, 12, 25), "圣诞节", true)
        ];
    }

    /// <summary>中文星期，例如「周五」。</summary>
    public static string WeekdayCn(DayOfWeek dayOfWeek) => dayOfWeek switch
    {
        DayOfWeek.Monday => "周一",
        DayOfWeek.Tuesday => "周二",
        DayOfWeek.Wednesday => "周三",
        DayOfWeek.Thursday => "周四",
        DayOfWeek.Friday => "周五",
        DayOfWeek.Saturday => "周六",
        _ => "周日"
    };

    // ========================================
    //  抓取与缓存
    // ========================================

    private static async Task<(IReadOnlyList<HolidayDay> Days, string Raw)> TryFetchAsync(
        int year, CancellationToken ct)
    {
        foreach (var template in Sources)
        {
            var url = string.Format(CultureInfo.InvariantCulture, template, year);
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(RequestTimeout);
                var json = await Http.GetStringAsync(url, cts.Token).ConfigureAwait(false);

                var days = ParseYearJson(json);
                if (days.Count > 0)
                {
                    Logger.Info($"[Holiday] 已获取 {year} 年节假日安排，共 {days.Count} 条（来源：{HostOf(url)}）");
                    return (days, json);
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                Logger.Info($"[Holiday] 获取 {year} 年节假日超时，尝试下一个数据源");
            }
            catch (Exception ex)
            {
                Logger.Info($"[Holiday] 获取 {year} 年节假日失败（{HostOf(url)}）：{ex.Message}");
            }
        }

        return (Array.Empty<HolidayDay>(), "");
    }

    private static string HostOf(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : url;

    private string? GetCacheFilePath(int year)
    {
        if (string.IsNullOrWhiteSpace(_cacheDirectory)) return null;
        try
        {
            var directory = Path.Combine(_cacheDirectory, "Cache");
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, $"holiday-{year}.json");
        }
        catch (Exception ex)
        {
            Logger.Info($"[Holiday] 准备缓存目录失败：{ex.Message}");
            return null;
        }
    }

    private IReadOnlyList<HolidayDay> TryLoadDiskCache(int year)
    {
        var path = GetCacheFilePath(year);
        if (path == null || !File.Exists(path)) return Array.Empty<HolidayDay>();

        try
        {
            var days = ParseYearJson(File.ReadAllText(path));
            if (days.Count > 0)
            {
                Logger.Info($"[Holiday] 已从本地缓存读取 {year} 年节假日安排，共 {days.Count} 条");
                return days;
            }
        }
        catch (Exception ex)
        {
            Logger.Info($"[Holiday] 读取本地缓存失败：{ex.Message}");
        }

        return Array.Empty<HolidayDay>();
    }

    private void SaveDiskCache(int year, string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson)) return;
        var path = GetCacheFilePath(year);
        if (path == null) return;

        try
        {
            File.WriteAllText(path, rawJson);
        }
        catch (Exception ex)
        {
            Logger.Info($"[Holiday] 写入本地缓存失败：{ex.Message}");
        }
    }
}
