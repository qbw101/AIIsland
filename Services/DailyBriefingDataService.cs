using System.Net.Http;
using System.Xml;

namespace ClassIsland.AISmartClass.Services;

/// <summary>每日简报的节假日和新闻数据。外部数据均采用短超时并允许失败。</summary>
public sealed class DailyBriefingDataService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(4) };

    public async Task<IReadOnlyList<string>> GetNewsAsync(string pluginFolder, string? configuredFeeds = null, CancellationToken ct = default)
    {
        // 新闻源由设置页选择；不再读取外部 rss.txt 列表。
        var feeds = ParseFeedUrls(configuredFeeds ?? "").ToList();
        if (feeds.Count == 0) return Array.Empty<string>();

        var headlines = new List<string>();
        foreach (var feed in feeds.Take(5).SelectMany(GetFeedCandidates))
        {
            try
            {
                await using var stream = await Http.GetStreamAsync(feed, ct).ConfigureAwait(false);
                using var reader = XmlReader.Create(stream, new XmlReaderSettings { Async = true, DtdProcessing = DtdProcessing.Ignore });
                var document = new XmlDocument();
                document.Load(reader);
                var titles = document.SelectNodes("//*[local-name()='item']/*[local-name()='title'] | //*[local-name()='entry']/*[local-name()='title']")
                    ?.Cast<XmlNode>()
                    .Select(node => node.InnerText.Trim())
                    .Where(title => !string.IsNullOrWhiteSpace(title))
                    .Take(3) ?? Enumerable.Empty<string>();
                foreach (var title in titles)
                {
                    if (!string.IsNullOrWhiteSpace(title) && !headlines.Contains(title, StringComparer.OrdinalIgnoreCase))
                        headlines.Add(title);
                    if (headlines.Count >= 5) return headlines;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or XmlException)
            {
                Logger.Info($"读取新闻 RSS 失败（{feed}）：{ex.Message}");
            }
        }
        return headlines;
    }

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
    }

}
