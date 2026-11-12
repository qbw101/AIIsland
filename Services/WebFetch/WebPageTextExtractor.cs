using SmartReader;

namespace ClassIsland.AISmartClass.Services.WebFetch;

/// <summary>只解析已下载的 HTML；Readability 优先，无法识别文章时回落到原有提取器。</summary>
internal static class WebPageTextExtractor
{
    internal sealed record Extraction(string Title, string Text, string Method);

    public static async Task<Extraction> ExtractAsync(string html, Uri uri, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            // 必须使用传入 HTML 的重载，不让库自行下载页面或图片，保留 WebFetchService 的地址校验。
            using var reader = new Reader(uri.AbsoluteUri, html)
            {
                CharThreshold = WebFetchService.MinMeaningfulChars,
                ContinueIfNotReadable = true,
                MaxElemsToParse = 20_000
            };
            // 保持旧提取器对语义导航区块的过滤；短文章的 Readability 回退可能把它们并入正文。
            reader.AddCustomOperationStart(root =>
            {
                foreach (var element in root.QuerySelectorAll("nav, aside, form").ToArray())
                    element.Remove();
            });
            var article = await reader.GetArticleAsync(ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            // IsReadable 包含面向长段落的预判；中文样本即使已提取完整正文也可能为 false。
            // 继续执行提取后，按实际正文与既有有效字符门槛决定是否采用结果。
            if (article.Completed &&
                CountLetters(article.TextContent) >= WebFetchService.MinMeaningfulChars)
            {
                var title = article.Title?.Trim() ?? "";
                if (title.Length > 200) title = title[..200];
                return new Extraction(title, article.TextContent.Trim(), "SmartReader");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 第三方提取器异常不应使已下载的页面丢失；旧提取器继续承担兼容回退。
            Logger.Info($"SmartReader 正文提取失败，使用兼容提取：{ex.GetType().Name}");
        }

        ct.ThrowIfCancellationRequested();
        var fallback = HtmlTextExtractor.Extract(html);
        return new Extraction(fallback.Title, fallback.Text, "Legacy");
    }

    private static int CountLetters(string text) => text.Count(char.IsLetter);
}
