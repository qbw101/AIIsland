using Avalonia.Controls;
using Avalonia.Media;
using ClassIsland.AISmartClass.Models;
using ClassIsland.AISmartClass.Services;

namespace ClassIsland.AISmartClass.Views;

/// <summary>
/// 把图片识别能力判定渲染成一个彩色小徽章。
/// 设置页和欢迎向导共用同一套配色与文案，避免两处显示不一致。
/// </summary>
internal static class VisionBadgeHelper
{
    private static readonly IBrush SupportedBrush = new SolidColorBrush(Color.Parse("#1B7F3B"));
    private static readonly IBrush UnsupportedBrush = new SolidColorBrush(Color.Parse("#C42B1C"));
    private static readonly IBrush InferredBrush = new SolidColorBrush(Color.Parse("#9D5D00"));
    private static readonly IBrush UnknownBrush = new SolidColorBrush(Color.Parse("#5D5D5D"));
    private static readonly IBrush LabelBrush = Brushes.White;

    /// <summary>按判定结果刷新徽章底色与文字。徽章文字使用「支持 / 可能不支持 / 未检测」这套表述。</summary>
    public static void Apply(Border? badge, TextBlock? label, VisionCapabilityInfo info)
    {
        if (label != null)
        {
            label.Text = VisionCapabilityService.DescribeSupportLabel(info);
            label.Foreground = LabelBrush;
        }

        if (badge == null) return;

        badge.Background = (info.Support, info.Source) switch
        {
            (VisionSupport.Supported, VisionCapabilitySource.Detection) => SupportedBrush,
            (VisionSupport.Unsupported, VisionCapabilitySource.Detection) => UnsupportedBrush,
            // 检测过但没结论：和「推断」一样用琥珀色，表示「不确定」
            (VisionSupport.Unknown, VisionCapabilitySource.DetectionInconclusive) => InferredBrush,
            (VisionSupport.Unknown, _) => UnknownBrush,
            _ => InferredBrush
        };
    }
}
