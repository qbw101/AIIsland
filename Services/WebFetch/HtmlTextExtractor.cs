using System.Text.RegularExpressions;

namespace ClassIsland.AISmartClass.Services.WebFetch;

/// <summary>
/// 把 HTML 提炼成模型可读的纯文本。不追求还原排版，只追求「正文完整、噪声少」：
/// 去掉脚本/样式/导航等区块，块级标签换成换行，实体解码后压缩空白。
/// 作为 SmartReader 未识别文章时的兼容回退，同时用于离线对比提取效果。
/// </summary>
internal static partial class HtmlTextExtractor
{
    /// <summary>提取结果：页面标题 + 正文（段落以换行分隔）。</summary>
    internal sealed record Extraction(string Title, string Text);

    private const int MaxTitleLength = 200;

    /// <summary>注释，最早去掉，避免里面的标签干扰后续匹配。</summary>
    [GeneratedRegex(@"<!--.*?-->", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex CommentRegex();

    /// <summary>纯噪声区块：脚本、样式、模板、嵌入对象，内容整段丢弃。</summary>
    [GeneratedRegex(@"<(script|style|noscript|svg|template|iframe|object|embed)[^>]*>.*?</\1\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex InvisibleBlockRegex();

    /// <summary>页面骨架区块：页头/页脚/导航/表单。正文极少住在这些标签里。</summary>
    [GeneratedRegex(@"<(header|footer|nav|aside|form)[^>]*>.*?</\1\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ChromeBlockRegex();

    /// <summary>块级结束标签与换行标签：转成换行，保住段落结构。</summary>
    [GeneratedRegex(@"</(p|div|section|article|main|li|ul|ol|dl|dd|dt|table|thead|tbody|tr|h[1-6]|blockquote|pre)>|<br\s*/?>",
        RegexOptions.IgnoreCase)]
    private static partial Regex BlockEndRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TitleRegex();

    [GeneratedRegex(@"\n\s*\n+")]
    private static partial Regex BlankLineRegex();

    [GeneratedRegex(@"[ \t]{2,}")]
    private static partial Regex SpaceRegex();

    public static Extraction Extract(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return new Extraction("", "");

        var title = System.Net.WebUtility.HtmlDecode(
            TitleRegex().Match(html).Groups[1].Value).Trim();
        if (title.Length > MaxTitleLength) title = title[..MaxTitleLength];

        var body = CommentRegex().Replace(html, " ");
        body = InvisibleBlockRegex().Replace(body, " ");
        body = ChromeBlockRegex().Replace(body, " ");
        body = BlockEndRegex().Replace(body, "\n");
        // 先去标签再解码实体：&lt;div&gt; 这类转义样本会变成可见文本而不是新标签
        body = TagRegex().Replace(body, " ");
        body = System.Net.WebUtility.HtmlDecode(body);
        body = SpaceRegex().Replace(body, " ");
        body = BlankLineRegex().Replace(body, "\n");

        return new Extraction(title, body.Trim());
    }
}
