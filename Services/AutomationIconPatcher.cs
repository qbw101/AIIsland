using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using FluentAvalonia.UI.Controls;

namespace ClassIsland.AISmartClass.Services;

/// <summary>
/// 为 AIIsland 自动化动作使用的自定义码点指定图标字体。
/// <see cref="ClassIsland.Core.Attributes.ActionInfo"/> 只能声明 glyph，ClassIsland 默认用 Fluent
/// 字体创建 FontIcon 和 FontIconSource，因此需在 glyph 绑定完成时改用 AIIsland Icons。
/// </summary>
public static class AutomationIconPatcher
{
    private static readonly FontFamily IconFontFamily =
        new("avares://ClassIsland.AISmartClass/icon/#AIIsland Icons");

    private static readonly HashSet<string> CustomGlyphs = new()
    {
        "\U000F0058", // 生成 AIIsland 贴心提醒（ai_edit）
        "\U000F00DC", // 刷新 AIIsland 组件（arrow_2_circlepath）
        "\U000F01D5", // 触发 AIIsland 贴心提醒（bell）
        "\U000F0129", // 设置考试模式（slider_horizontal_2_1）
    };

    private static bool _initialized;

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        FontIcon.GlyphProperty.Changed.AddClassHandler<FontIcon>(OnFontIconGlyphChanged);
        FontIconSource.GlyphProperty.Changed.AddClassHandler<FontIconSource>(OnFontIconSourceGlyphChanged);
    }

    private static void OnFontIconGlyphChanged(FontIcon icon, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is string glyph && CustomGlyphs.Contains(glyph))
        {
            icon.FontFamily = IconFontFamily;
        }
    }

    private static void OnFontIconSourceGlyphChanged(FontIconSource source, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is string glyph && CustomGlyphs.Contains(glyph))
        {
            source.FontFamily = IconFontFamily;
        }
    }
}
