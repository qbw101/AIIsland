using System.Text.RegularExpressions;

namespace ClassIsland.AISmartClass.Services.WebSearch;

/// <summary>
/// 搜索结果质量过滤。自建 OpenSERP 实测会返回三类垃圾：广告位、页面解析残留
/// （标题为数字、摘要为 CSS）、以及与查询毫无关系的条目（疑似实例缓存了错误内容）。
/// 各搜索提供者共用这套规则，避免一边过滤一边漏。
/// </summary>
internal static class SearchResultFilter
{
    /// <summary>参与相关性匹配前先剔除的低信息量词，避免「最新」这类词单独造成误命中。</summary>
    private static readonly string[] StopWords =
    {
        "最新", "今天", "现在", "目前", "什么", "怎么", "如何", "多少", "请问", "一下",
        "帮我", "告诉", "介绍", "相关", "内容", "情况", "有没有", "是不是", "哪些", "为什么"
    };

    /// <summary>
    /// 把查询词切成用于相关性判断的词项：英文/数字按词切，中文按 2 字滑窗切。
    /// 先剔除无检索价值的词。返回空列表表示无法可靠判断，调用方应跳过相关性校验。
    /// </summary>
    public static List<string> Tokenize(string? query)
    {
        var normalized = query ?? "";
        foreach (var word in StopWords)
            normalized = normalized.Replace(word, " ", StringComparison.Ordinal);

        var tokens = new List<string>();

        foreach (Match match in Regex.Matches(normalized, "[A-Za-z0-9]{2,}"))
            tokens.Add(match.Value.ToLowerInvariant());

        foreach (Match match in Regex.Matches(normalized, @"[\u4e00-\u9fff]{2,}"))
        {
            var chunk = match.Value;
            if (chunk.Length <= 4)
            {
                tokens.Add(chunk);
                continue;
            }

            for (var i = 0; i + 2 <= chunk.Length; i++)
                tokens.Add(chunk.Substring(i, 2));
        }

        return tokens.Distinct().Take(16).ToList();
    }

    /// <summary>剔除页面解析残留：空标题、纯数字标题、CSS/JS 片段摘要。</summary>
    public static bool IsLowQuality(string title, string snippet)
    {
        var trimmedTitle = (title ?? "").Trim();
        if (trimmedTitle.Length < 2) return true;
        if (trimmedTitle.All(char.IsDigit)) return true;

        var text = snippet ?? "";
        if (text.Contains('{') &&
            (text.Contains("margin", StringComparison.OrdinalIgnoreCase) ||
             text.Contains("padding", StringComparison.OrdinalIgnoreCase) ||
             text.Contains("font-size", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return false;
    }

    /// <summary>标题或摘要里至少要出现一个查询词项。词项为空时视为通过。</summary>
    public static bool IsRelevant(string title, string snippet, IReadOnlyList<string> tokens)
    {
        if (tokens.Count == 0) return true;
        var text = (title ?? "") + " " + (snippet ?? "");

        foreach (var token in tokens)
        {
            if (text.Contains(token, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// 完整判定一条原始结果是否可用。可用时返回规范化后的 <see cref="WebSearchItem"/>，
    /// 否则返回 null，由调用方计入丢弃统计。
    /// </summary>
    public static WebSearchItem? Accept(
        string title, string url, string snippet, string? publishedDate, IReadOnlyList<string> tokens)
    {
        title = (title ?? "").Trim();
        snippet = (snippet ?? "").Trim();
        url = (url ?? "").Trim();

        if (url.Length == 0) return null;
        if (IsLowQuality(title, snippet)) return null;
        if (!IsRelevant(title, snippet, tokens)) return null;

        return new WebSearchItem(title, url, snippet, publishedDate);
    }
}
