using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ClassIsland.AISmartClass.Services.WebSearch;

/// <summary>
/// OpenSERP 搜索提供者。自托管实例的接口是 <c>GET {base}/{engine}/search?text=...&amp;limit=N</c>，
/// 无需鉴权，支持 google / bing / baidu / yandex / duckduckgo / ecosia / mega。
/// <para>
/// 默认引擎为 <c>bing</c>：实测中文结果质量明显好于百度，响应也稳定。首选引擎失败时自动换一个重试。
/// 地址里也可以直接写引擎名（如 <c>http://host/baidu</c>）。
/// </para>
/// <para>
/// 两道结果防线（都针对自建实例的实测问题）：
/// ① 请求 URL 附带 <c>_sig</c>（query 的短哈希），即使服务端或反代缓存 key 漏算了查询词，
///    也不会把上一次搜其他内容的结果串过来；
/// ② 解析后剔除广告、CSS/JS 碎片、纯数字标题，并做一次轻量相关性校验——
///    结果文本必须与查询词有词项重叠，否则视为「没搜到」，避免把风马牛不相及的条目喂给模型。
/// </para>
/// </summary>
internal sealed class OpenSerpSearchProvider : IWebSearchProvider
{
    private static readonly HttpClient SharedHttp = new() { Timeout = Timeout.InfiniteTimeSpan };

    /// <summary>
    /// OpenSERP 支持的引擎名，用于识别地址里附带的引擎段。
    /// 刻意不含 <c>mega</c>：它会等待无法连通的引擎超时（约 90 秒），
    /// 表现很像「服务抽风」；把 <c>/mega</c> 当地址路径处理反而更安全。
    /// </summary>
    private static readonly HashSet<string> KnownEngines = new(StringComparer.OrdinalIgnoreCase)
    {
        "google", "bing", "baidu", "yandex", "duckduckgo", "ecosia"
    };

    private readonly HttpClient _http;

    /// <param name="http">为 null 时使用共享实例；测试可注入带假消息处理器的 HttpClient。</param>
    public OpenSerpSearchProvider(HttpClient? http = null) => _http = http ?? SharedHttp;

    public string Name => "OpenSERP";

    public bool IsConfigured(WebSearchConfig config) => !string.IsNullOrWhiteSpace(config.Endpoint);

    public async Task<WebSearchResponse> SearchAsync(string query, WebSearchConfig config, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
            return WebSearchResponse.Fail("搜索关键词为空");

        var errors = new List<string>();
        var hadValidButEmpty = false;

        foreach (var engine in BuildEngineChain(config))
        {
            if (!TryBuildSearchUri(config.Endpoint, engine, query, config.EffectiveMaxResults, out var uri, out var usedEngine))
                return WebSearchResponse.Fail("搜索地址格式不正确，应形如 http://search.qbwnas.top");

            var stopwatch = Stopwatch.StartNew();
            var (result, timedOut) = await SearchEngineAsync(uri, usedEngine, config, query, ct).ConfigureAwait(false);
            stopwatch.Stop();

            if (result.Success && result.Items.Count > 0)
            {
                Logger.Info($"联网检索成功：引擎={usedEngine} 关键词=\"{query}\" 有效 {result.Items.Count} 条，耗时 {stopwatch.ElapsedMilliseconds}ms");
                return result;
            }

            if (result.Success)
            {
                // 拿到了响应但一条都不相关：多见于实例缓存了错误内容（实测出现过
                // 搜「甘雨怎么养」返回 WeTransfer、搜「上海理工大学招生」返回新浪汽车）。
                // 换引擎再试，比直接告诉模型「没搜到」更有机会救回来。
                hadValidButEmpty = true;
                errors.Add($"{usedEngine}: 结果与查询无关");
                Logger.Info($"联网检索无有效结果：引擎={usedEngine} 关键词=\"{query}\"，换引擎重试");
                continue;
            }

            errors.Add($"{usedEngine}: {result.Error}");
            Logger.Info($"联网检索失败：引擎={usedEngine} 关键词=\"{query}\" 耗时 {stopwatch.ElapsedMilliseconds}ms 原因={result.Error}");

            // 超时说明上游就是慢，换引擎大概率同样慢，直接放弃避免总耗时翻倍
            if (timedOut) break;
        }

        return hadValidButEmpty
            ? WebSearchResponse.Ok(Array.Empty<WebSearchItem>())
            : WebSearchResponse.Fail(string.Join("；", errors));
    }

    /// <summary>首选引擎 + 一个备选引擎，去重后依次尝试。</summary>
    private static IEnumerable<string> BuildEngineChain(WebSearchConfig config)
    {
        var primary = NormalizeEngine(config.EffectiveEngine);
        yield return primary;

        foreach (var fallback in new[] { "baidu", "bing" })
        {
            if (!fallback.Equals(primary, StringComparison.OrdinalIgnoreCase))
                yield return fallback;
        }
    }

    /// <summary>向单个引擎发起检索。返回的 <c>TimedOut</c> 用于决定是否值得换引擎重试。</summary>
    private async Task<(WebSearchResponse Response, bool TimedOut)> SearchEngineAsync(
        Uri uri, string engine, WebSearchConfig config, string query, CancellationToken ct)
    {
        var timeoutSeconds = config.EffectiveTimeoutSeconds;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        try
        {
            using var response = await _http
                .GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cts.Token)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return (WebSearchResponse.Fail(DescribeStatus(response.StatusCode)), false);

            var body = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            return (Parse(body, query, config.EffectiveMaxResults), false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // 调用方主动取消（例如用户关掉了对话），原样抛出结束整次对话
            throw;
        }
        catch (OperationCanceledException)
        {
            return (WebSearchResponse.Fail($"超过 {timeoutSeconds} 秒未返回"), true);
        }
        catch (HttpRequestException ex)
        {
            return (WebSearchResponse.Fail($"无法连接：{ex.Message}"), false);
        }
    }

    /// <summary>
    /// 由用户填写的服务地址拼出搜索 URL。允许三种写法：
    /// 根地址 <c>http://host</c>、带引擎 <c>http://host/baidu</c>、完整 <c>http://host/baidu/search</c>。
    /// 反代子路径（如 <c>https://host/search-api</c>）会原样保留。
    /// <para>
    /// 额外带上 <c>_sig</c>：query 的短哈希。自建实例与其前置反代可能按 URL 做缓存，
    /// 一旦缓存键漏算了查询词，不同查询就会互相串结果（实测出现过这类现象）。
    /// OpenSERP 会忽略未知参数，但任何按完整 URL 建键的缓存都会被正确区分开。
    /// </para>
    /// </summary>
    internal static bool TryBuildSearchUri(
        string endpoint, string fallbackEngine, string query, int maxResults, out Uri uri, out string engine)
    {
        uri = null!;
        engine = NormalizeEngine(fallbackEngine);

        if (string.IsNullOrWhiteSpace(endpoint)) return false;
        if (!Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out var baseUri)) return false;
        if (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps) return false;

        var segments = baseUri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .ToList();

        // 地址里已带 /search 时先去掉，再看上一段是不是引擎名
        if (segments.Count > 0 && segments[^1].Equals("search", StringComparison.OrdinalIgnoreCase))
            segments.RemoveAt(segments.Count - 1);

        if (segments.Count > 0 && KnownEngines.Contains(segments[^1]))
        {
            engine = segments[^1].ToLowerInvariant();
            segments.RemoveAt(segments.Count - 1);
        }

        var builder = new UriBuilder(baseUri)
        {
            Path = "/" + string.Join('/', segments.Concat(new[] { engine, "search" })),
            Query = $"text={Uri.EscapeDataString(query)}&limit={Math.Clamp(maxResults, 1, 10)}" +
                    $"&_sig={QuerySignature(query)}"
        };

        uri = builder.Uri;
        return true;
    }

    /// <summary>查询词指纹，用来让任何按 URL 建键的缓存无法把不同查询混在一起。</summary>
    private static string QuerySignature(string query)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(query ?? ""));
        return Convert.ToHexString(hash)[..8].ToLowerInvariant();
    }

    /// <summary>
    /// 解析 OpenSERP 的 JSON 响应并做质量过滤。
    /// 结果数组里混有广告位（<c>type = "ad"</c> 或 <c>is_ad = true</c>）、
    /// 页面样式碎片（标题是数字、摘要里是 CSS）以及与查询毫无关系的条目，全部剔除。
    /// 过滤后为空即视为「没搜到」，由上层回传「无结果」而不是把垃圾当资料。
    /// </summary>
    internal static WebSearchResponse Parse(string body, string query, int maxResults)
    {
        if (string.IsNullOrWhiteSpace(body))
            return WebSearchResponse.Fail("返回内容为空");

        try
        {
            using var doc = JsonDocument.Parse(body);

            if (doc.RootElement.TryGetProperty("results", out var results) &&
                results.ValueKind == JsonValueKind.Array)
            {
                // 先看服务端回显的查询词：如果它已经变成乱码，搜索结果必然不可信
                if (TryDetectCorruptedEcho(doc.RootElement, query, out var echo))
                {
                    Logger.Info($"联网检索回显异常：请求=\"{query}\" 服务端收到=\"{echo}\"，本次结果作废");
                    return WebSearchResponse.Fail($"查询词在传输中被损坏（服务端收到「{echo}」）");
                }

                return WebSearchResponse.Ok(ReadItems(results, query, maxResults));
            }

            // 部分错误响应把原因放在 error 字段里
            var error = ReadString(doc.RootElement, "error");
            return WebSearchResponse.Fail(
                error.Length > 0 ? error : "返回 JSON 缺少 results 数组");
        }
        catch (JsonException)
        {
            return WebSearchResponse.Fail("返回内容不是 JSON，请确认地址指向 OpenSERP 实例");
        }
    }

    /// <summary>
    /// OpenSERP 会在 <c>query.text</c> 里回显服务端实际收到的查询词，正好可以用来校验编码链路。
    /// 一旦回显出现「未解码的百分号串」「UTF-8 替换字符」或「整串问号」，说明请求在某一层
    /// 被按错误编码解码——引擎只能拿乱码做模糊匹配，结果必然风马牛不相及，因此直接判失败。
    /// <para>
    /// 只识别明确的损坏特征，不做严格相等比较：服务端可能对空白做归一化，
    /// 严格比较会误伤正常请求。
    /// </para>
    /// </summary>
    internal static bool TryDetectCorruptedEcho(JsonElement root, string query, out string echo)
    {
        echo = "";
        if (!root.TryGetProperty("query", out var queryElement)) return false;
        if (queryElement.ValueKind != JsonValueKind.Object) return false;
        if (!queryElement.TryGetProperty("text", out var textElement)) return false;
        if (textElement.ValueKind != JsonValueKind.String) return false;

        echo = textElement.GetString()?.Trim() ?? "";
        if (echo.Length == 0) return false;

        var original = (query ?? "").Trim();

        // 服务端收到的是没解码的百分号串（原文本身含 % 时跳过，避免误伤「涨了 50%」这类查询）
        if (echo.Contains('%') && !original.Contains('%')) return true;

        // UTF-8 解码失败的典型产物
        if (echo.Contains('\uFFFD')) return true;

        // 整串问号：编码不匹配时的另一种表现
        if (echo.All(c => c == '?') && original.Any(c => c != '?')) return true;

        return false;
    }

    private static List<WebSearchItem> ReadItems(JsonElement results, string query, int maxResults)
    {
        var tokens = SearchResultFilter.Tokenize(query);
        var items = new List<WebSearchItem>();
        var dropped = 0;

        foreach (var item in results.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;

            if (IsAdvertisement(item)) { dropped++; continue; }

            var accepted = SearchResultFilter.Accept(
                ReadString(item, "title"),
                ReadString(item, "url"),
                ReadString(item, "snippet"),
                null,
                tokens);

            if (accepted is null) { dropped++; continue; }

            items.Add(accepted);
            if (items.Count >= Math.Max(1, maxResults)) break;
        }

        if (dropped > 0)
            Logger.Info($"联网检索过滤：关键词=\"{query}\" 丢弃 {dropped} 条广告/碎片/无关结果，保留 {items.Count} 条");

        return items;
    }

    private static bool IsAdvertisement(JsonElement item)
    {
        if (ReadString(item, "type").Equals("ad", StringComparison.OrdinalIgnoreCase)) return true;
        return item.TryGetProperty("is_ad", out var isAd) && isAd.ValueKind == JsonValueKind.True;
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value)) return "";
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString()?.Trim() ?? "",
            JsonValueKind.Number => value.ToString(),
            _ => ""
        };
    }

    private static string NormalizeEngine(string? engine)
    {
        var normalized = (engine ?? "").Trim().ToLowerInvariant();
        return normalized.Length > 0 && KnownEngines.Contains(normalized) ? normalized : "bing";
    }

    private static string DescribeStatus(HttpStatusCode status) => status switch
    {
        HttpStatusCode.NotFound => "接口不存在（404）：请检查地址是否指向 OpenSERP 实例",
        HttpStatusCode.TooManyRequests => "被搜索引擎限流（429）：请稍后重试",
        HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout =>
            $"上游搜索引擎暂时不可用（{(int)status}）",
        _ => $"HTTP {(int)status} 状态异常"
    };
}
