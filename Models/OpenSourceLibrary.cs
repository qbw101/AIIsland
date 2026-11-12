namespace ClassIsland.AISmartClass.Models;

/// <summary>来自已还原 NuGet 包的版本、上游地址与原始许可声明。</summary>
public sealed record OpenSourceLibrary(
    string Name,
    string Version,
    string License,
    string SourceUrl,
    string LicenseUrl,
    string NugetUrl,
    string NoticeText,
    bool IsDirect)
{
    public string Summary => $"{Version} · {License}";
    public string SourceLabel => Uri.TryCreate(SourceUrl, UriKind.Absolute, out var uri) &&
                                 uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
        ? "GitHub" : "项目主页";
}
