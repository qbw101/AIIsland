using System.Net.Http;
using System.Xml;
using System.Text;
using ClassIsland.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace ClassIsland.AISmartClass.Services;

/// <summary>每日简报的节假日和新闻数据。外部数据均采用短超时并允许失败。</summary>
public sealed class DailyBriefingDataService
{
    // 不用 HttpClient 全局超时，改由每个请求的 CancellationTokenSource 独立计时，
    // 这样米哈游候选可以放宽到 20 秒，而 IT之家/36氪 仍保持 5 秒。
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    /// <summary>普通订阅源的单请求超时（IT之家、36氪 等）。</summary>
    private static readonly TimeSpan PerRequestTimeout = TimeSpan.FromSeconds(5);

    /// <summary>米哈游资讯路由的单请求超时，放宽以让慢实例（如 slarker ~18s）有机会返回。</summary>
    private static readonly TimeSpan MihoyoTimeout = TimeSpan.FromSeconds(20);

    public async Task<IReadOnlyList<string>> GetNewsAsync(string pluginFolder, string? configuredFeeds = null, CancellationToken ct = default)
    {
        var feeds = ParseFeedUrls(configuredFeeds ?? "").ToList();
        if (feeds.Count == 0) return Array.Empty<string>();

        var headlines = new List<string>();
        foreach (var feed in feeds.Take(5))
        {
            // 米哈游资讯路由：并发请求所有候选（原地址 + 镜像），取最先成功返回的，取消其余。
            // 其他源：串行回退，失败/超时才切换下一个候选。
            var titles = IsMihoyoFeed(feed)
                ? await FetchMihoyoConcurrentAsync(feed, ct).ConfigureAwait(false)
                : await FetchFirstSuccessAsync(feed, PerRequestTimeout, ct).ConfigureAwait(false);

            foreach (var title in titles)
            {
                if (!headlines.Contains(title, StringComparer.OrdinalIgnoreCase))
                    headlines.Add(title);
            }
            if (headlines.Count >= 5) return headlines;
        }
        return headlines;
    }

    /// <summary>米哈游资讯路由并发请求所有候选，取最先成功返回非空标题的，取消其余。</summary>
    private async Task<IReadOnlyList<string>> FetchMihoyoConcurrentAsync(string feed, CancellationToken ct)
    {
        var candidates = GetFeedCandidates(feed).ToList();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(MihoyoTimeout);
        var token = cts.Token;

        var tasks = candidates
            .Select(c => TryFetchTitlesAsync(c, MihoyoTimeout, token))
            .ToList();

        while (tasks.Count > 0)
        {
            // 取最先完成的任务：成功就返回并取消其余，失败就继续等下一个。
            var done = await Task.WhenAny(tasks).ConfigureAwait(false);
            tasks.Remove(done);
            var (ok, titles) = await done.ConfigureAwait(false);
            if (ok && titles.Count > 0)
            {
                cts.Cancel(); // 取消其余候选
                return titles;
            }
        }
        return Array.Empty<string>();
    }

    /// <summary>串行回退：依次尝试候选，取首个成功返回非空标题的。</summary>
    private async Task<IReadOnlyList<string>> FetchFirstSuccessAsync(string feed, TimeSpan timeout, CancellationToken ct)
    {
        foreach (var candidate in GetFeedCandidates(feed))
        {
            var (ok, titles) = await TryFetchTitlesAsync(candidate, timeout, ct).ConfigureAwait(false);
            if (ok && titles.Count > 0) return titles;
        }
        return Array.Empty<string>();
    }

    /// <summary>抓取单个订阅源，失败返回 (false, 空)。</summary>
    private static async Task<(bool ok, IReadOnlyList<string> titles)> TryFetchTitlesAsync(
        string feed, TimeSpan timeout, CancellationToken ct)
    {
        try
        {
            return (true, await FetchTitlesAsync(feed, timeout, ct).ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or XmlException)
        {
            Logger.Info($"读取新闻 RSS 失败（{feed}）：{ex.Message}");
            return (false, Array.Empty<string>());
        }
    }

    /// <summary>抓取并解析单个订阅源，返回去重前的标题列表（最多 3 条）。</summary>
    private static async Task<IReadOnlyList<string>> FetchTitlesAsync(string feed, TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        var token = cts.Token;

        await using var stream = await Http.GetStreamAsync(feed, token).ConfigureAwait(false);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { Async = true, DtdProcessing = DtdProcessing.Ignore });
        var document = new XmlDocument();
        document.Load(reader);
        return document.SelectNodes("//*[local-name()='item']/*[local-name()='title'] | //*[local-name()='entry']/*[local-name()='title']")
            ?.Cast<XmlNode>()
            .Select(node => node.InnerText.Trim())
            .Where(title => !string.IsNullOrWhiteSpace(title))
            .Take(3)
            .ToArray() ?? Array.Empty<string>();
    }

    /// <summary>判断是否为米哈游官方资讯路由（需并发回退）。</summary>
    private static bool IsMihoyoFeed(string feed)
        => feed.Contains("/mihoyo/bbs/official/", StringComparison.OrdinalIgnoreCase);

    public static string GetHolidayDescription(DateTime date)
    {
        if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            return "周末";
        return (date.Month, date.Day) switch
        {
            (1, 1) => "元旦",
            (5, 1) => "劳动节",
            (10, 1) => "国庆节",
            (12, 25) => "圣诞节",
            _ => ""
        };
    }

    public static IReadOnlyList<string> ParseFeedUrls(string text)
    {
        return text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith("#"))
            .Select(NormalizeFeedUrl)
            .Where(line => line.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string NormalizeFeedUrl(string line)
    {
        var separator = line.IndexOf(':');
        if (separator < 0) separator = line.IndexOf('：');
        if (separator > 0 && !line.StartsWith("http", StringComparison.OrdinalIgnoreCase) &&
            !line[..separator].Contains("/"))
            line = line[(separator + 1)..].Trim();
        if (!line.StartsWith("http", StringComparison.OrdinalIgnoreCase)) line = $"https://{line}";
        return line;
    }

    private static IEnumerable<string> GetFeedCandidates(string feed)
    {
        yield return feed;

        // 36kr.com/feed 当前会返回验证码 HTML，而不是 RSS XML。
        // 保留用户选择的官方地址，解析不到标题时自动尝试兼容镜像。
        if (feed.Contains("36kr.com/feed", StringComparison.OrdinalIgnoreCase))
            yield return "https://rsshub.rssforever.com/36kr/newsflashes";

        // 米哈游官方资讯路由：当前实例 5 秒未响应时，自动用同一路由尝试下一个 RSSHub 实例。
        foreach (var mirror in GetMihoyoMirrors(feed))
            yield return mirror;
    }

    /// <summary>
    /// 米哈游资讯路由依次尝试的 RSSHub 实例（含协议）。首个是用户自建实例，响应最快。
    /// </summary>
    private static readonly string[] MihoyoMirrorBases =
    [
        "http://rss.qbwnas.top",
        "https://rss.watchrss.cn",
        "https://hub.slarker.me",
        "https://rsshub.rssforever.com"
    ];

    /// <summary>
    /// 对米哈游官方资讯路由生成同路由的镜像地址,用于「超时自动切换」。
    /// 非米哈游路由直接返回空。
    /// </summary>
    private static IEnumerable<string> GetMihoyoMirrors(string feed)
    {
        const string marker = "/mihoyo/bbs/official/";
        var idx = feed.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) yield break;

        var route = feed[idx..];
        foreach (var baseUrl in MihoyoMirrorBases)
        {
            var full = baseUrl.TrimEnd('/') + route;
            if (string.Equals(full, feed, StringComparison.OrdinalIgnoreCase)) continue;
            yield return full;
        }
    }

    /// <summary>
    /// 获取今日生日祝福信息。从生日显示组件读取生日列表，返回今日生日的人名列表。
    /// </summary>
    /// <returns>今日生日的人名列表；未找到插件或今日无生日返回空列表</returns>
    public static List<string> GetTodayBirthdays()
    {
        var result = new List<string>();

        try
        {
            // Find the optional service by type to avoid a compile-time dependency on BirthdayIsland.
            var birthdayDataServiceType = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => {
                    try { return a.GetTypes(); }
                    catch { return Array.Empty<Type>(); }
                })
                .FirstOrDefault(t => t.FullName == "BirthdayIsland.Services.BirthdayDataService");

            if (birthdayDataServiceType == null)
            {
                Logger.Info("未找到 BirthdayIsland 插件或 BirthdayDataService，跳过生日祝福");
                return result;
            }

            var service = IAppHost.Host?.Services.GetService(birthdayDataServiceType);

            if (service == null)
            {
                Logger.Info("BirthdayDataService 服务未注册或未初始化");
                return result;
            }

            var getTodayMethod = birthdayDataServiceType.GetMethod("GetTodayBirthdays");
            if (getTodayMethod == null)
            {
                Logger.Info("BirthdayDataService 未找到 GetTodayBirthdays 方法");
                return result;
            }

            var todayBirthdays = getTodayMethod.Invoke(service, new object?[] { null });
            if (todayBirthdays is not System.Collections.IEnumerable list)
            {
                Logger.Info("GetTodayBirthdays 返回值不是可枚举类型");
                return result;
            }

            foreach (var person in list)
            {
                if (person == null) continue;
                
                var nameProp = person.GetType().GetProperty("Name");
                var name = nameProp?.GetValue(person)?.ToString();
                
                if (!string.IsNullOrWhiteSpace(name))
                {
                    result.Add(name);
                }
            }

            if (result.Count > 0)
            {
                Logger.Info($"今日生日（来自 BirthdayIsland）：{string.Join("、", result)}");
            }
            else
            {
                Logger.Info("今日无生日");
            }

            return result;
        }
        catch (Exception ex)
        {
            Logger.Info($"读取 BirthdayIsland 生日数据失败：{ex.Message}");
            return result;
        }
    }

    /// <summary>
    /// 生成生日祝福文本片段,供 AI 简报使用。
    /// </summary>
    /// <returns>生日祝福文本；无生日返回空字符串</returns>
    public static string GetBirthdayGreeting()
    {
        var birthdays = GetTodayBirthdays();
        if (birthdays.Count == 0) return "";

        if (birthdays.Count == 1)
        {
            return $"今天是 {birthdays[0]} 的生日";
        }
        else
        {
            return $"今天是 {string.Join("、", birthdays)} 的生日";
        }
    }

    /// <summary>
    /// 获取今日值日生名单（已弃用，使用 DutyStudentService 替代）
    /// </summary>
    [Obsolete("Use DutyStudentService.GetCurrentDutyStudents() instead")]
    public static List<string> GetTodayDutyStudents()
    {
        var dutyInfo = DutyStudentService.GetCurrentDutyStudents();
        return dutyInfo?.Students ?? new List<string>();
    }

    /// <summary>
    /// 生成值日提醒文本片段，供放学总结使用。
    /// </summary>
    /// <returns>值日提醒文本；无值日返回空字符串</returns>
    public static string GetDutyReminder(IReadOnlySet<string>? allowedPluginIds = null)
    {
        Logger.Info("[DutyReminder] GetDutyReminder 被调用");
        var dutyInfo = DutyStudentService.GetCurrentDutyStudents(allowedPluginIds);
        var result = dutyInfo?.ToFriendlyString() ?? "";
        Logger.Info($"[DutyReminder] 返回结果: '{result}'");
        return result;
    }

}
