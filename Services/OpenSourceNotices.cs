using System.Text.Json;
using ClassIsland.AISmartClass.Models;

namespace ClassIsland.AISmartClass.Services;

internal static class OpenSourceNotices
{
    // 随程序集分发，查看清单不依赖网络或用户目录中的外部文件。
    private static readonly Lazy<IReadOnlyList<OpenSourceLibrary>> Catalog = new(() =>
    {
        using var stream = typeof(OpenSourceNotices).Assembly.GetManifestResourceStream(
            "ClassIsland.AISmartClass.ThirdParty.OpenSourceNotices.json")
            ?? throw new InvalidOperationException("未找到随插件分发的开源许可清单。");
        return Array.AsReadOnly(JsonSerializer.Deserialize<OpenSourceLibrary[]>(stream)
            ?? throw new InvalidOperationException("开源许可清单格式无效。"));
    });

    internal static IReadOnlyList<OpenSourceLibrary> Libraries => Catalog.Value;
}
