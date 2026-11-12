using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FluentAvalonia.UI.Controls;

namespace ClassIsland.AISmartClass.Controls;

/// <summary>
/// 按钮忙碌态：Windows 原生转圈（Fluent ProgressRing）。
/// 忙碌时按钮整体变灰不可点，内容换成居中的转圈；转圈颜色沿用按钮原来的前景色，
/// 这样蓝色主按钮上是白圈，不会被背景吞掉。
/// </summary>
public static class BusyContent
{
    // 记录按钮原始的 MinWidth：XAML 里设了下限的按钮（如「测试」=104），
    // 忙碌结束必须还原，否则第一次点击后按钮会永久变窄。
    private static readonly ConditionalWeakTable<Button, StrongBox<double>> OriginalMinWidth = new();

    /// <summary>单独一个转圈，颜色跟随给定的前景色。</summary>
    public static Control Ring(double size = 16, IBrush? foreground = null)
    {
        var ring = new ProgressRing
        {
            IsActive = true,
            Width = size,
            Height = size,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        if (foreground != null)
            ring.Foreground = foreground;

        return ring;
    }

    /// <summary>
    /// 把按钮切到忙碌态：变灰不可点，内容换成居中转圈。
    /// 以当前宽度作为 <see cref="Layoutable.MinWidth"/> 下限，避免按钮在忙碌前后抖动。
    /// 结束时调用 <see cref="End"/> 还原。
    /// </summary>
    public static void Begin(Button button, double ringSize = 16)
    {
        if (!OriginalMinWidth.TryGetValue(button, out _))
            OriginalMinWidth.Add(button, new StrongBox<double>(button.MinWidth));

        if (button.Bounds.Width > 0 && button.MinWidth < button.Bounds.Width)
            button.MinWidth = button.Bounds.Width;

        // 先取前景色：一旦置为 Disabled，模板会把前景换成灰白
        var brush = button.Foreground;
        button.Content = Ring(ringSize, brush);
        button.IsEnabled = false;
    }

    /// <summary>还原按钮内容、可点击状态、原始 MinWidth 与自适应宽度。</summary>
    public static void End(Button button, object? originalContent)
    {
        button.Content = originalContent;

        if (OriginalMinWidth.TryGetValue(button, out var originalMinWidth))
        {
            button.MinWidth = originalMinWidth.Value;
            OriginalMinWidth.Remove(button);
        }

        button.IsEnabled = true;
    }
}
