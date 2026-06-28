using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ClassIsland.AISmartClass.Services;

/// <summary>
/// 主题颜色辅助工具。统一从 ClassIsland/FluentAvalonia 主题字典中获取动态颜色，
/// 找不到时回退到深色主题默认值。
/// </summary>
public static class ThemeHelper
{
    /// <summary>尝试获取 DynamicResource 对应的 Brush，失败返回深色默认值。</summary>
    public static IBrush GetBrush(string key, string fallbackHex)
    {
        if (Application.Current?.TryFindResource(key, out var resource) == true && resource is IBrush brush)
            return brush;
        return new SolidColorBrush(Color.Parse(fallbackHex));
    }

    /// <summary>将 Brush 安全转换为 Color（仅 SolidColorBrush），失败返回深色回退值。</summary>
    public static Color GetColor(IBrush brush, string fallbackHex)
        => (brush as ISolidColorBrush)?.Color ?? Color.Parse(fallbackHex);

    // ===== 常用预设 =====

    public static IBrush BackgroundBase        => GetBrush("SolidBackgroundFillColorBaseBrush", "#161B22");
    public static IBrush BackgroundSecondary   => GetBrush("SolidBackgroundFillColorSecondaryBrush", "#0D1117");
    public static IBrush CardBackground        => GetBrush("CardBackgroundFillColorDefaultBrush", "#1C2128");
    public static IBrush ControlFillDefault    => GetBrush("ControlFillColorDefaultBrush", "#21262D");
    public static IBrush ControlFillSecondary  => GetBrush("ControlFillColorSecondaryBrush", "#30363D");

    public static IBrush CardStrokeDefault     => GetBrush("CardStrokeColorDefaultBrush", "#3A414A");
    public static IBrush ControlStrokeSecondary=> GetBrush("ControlStrokeColorSecondaryBrush", "#484F58");

    public static IBrush TextPrimary           => GetBrush("TextFillColorPrimaryBrush", "#E6EDF3");
    public static IBrush TextSecondary         => GetBrush("TextFillColorSecondaryBrush", "#C9D1D9");
    public static IBrush TextTertiary          => GetBrush("TextFillColorTertiaryBrush", "#8B949E");
    public static IBrush TextDisabled          => GetBrush("TextFillColorDisabledBrush", "#6E7681");

    public static IBrush AccentDefault         => GetBrush("AccentFillColorDefaultBrush", "#58A6FF");
    public static IBrush AccentSecondary       => GetBrush("AccentFillColorSecondaryBrush", "#79C0FF");
    public static IBrush AccentTertiary        => GetBrush("AccentFillColorTertiaryBrush", "#388BFD");
    public static IBrush AccentTextTertiary    => GetBrush("AccentTextFillColorTertiaryBrush", "#1C2F4A");

    public static IBrush SystemSuccess         => GetBrush("SystemFillColorSuccessBrush", "#3FB950");
    public static IBrush SystemCritical        => GetBrush("SystemFillColorCriticalBrush", "#F85149");

    // ===== Color 版本（供需要 Color 而非 Brush 的场景使用） =====

    public static Color AccentTextTertiaryColor => GetColor(AccentTextTertiary, "#1C2F4A");
    public static Color CardBackgroundColor     => GetColor(CardBackground, "#1C2128");
    public static Color SystemSuccessColor      => GetColor(SystemSuccess, "#3FB950");
    public static Color SystemCriticalColor     => GetColor(SystemCritical, "#F85149");
    public static Color AccentDefaultColor      => GetColor(AccentDefault, "#58A6FF");
    public static Color TextTertiaryColor       => GetColor(TextTertiary, "#8B949E");
}
