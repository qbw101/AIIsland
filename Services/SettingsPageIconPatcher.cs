using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ClassIsland.Core.Attributes;
using FluentAvalonia.UI.Controls;

namespace ClassIsland.AISmartClass.Services;

/// <summary>
/// 将 ClassIsland 设置/提醒窗口中 AIIsland 相关的 Fluent 字体图标替换为插件自定义字体图标。
/// SettingsPageInfo / NotificationProviderInfo 只能声明 glyph，不能声明 FontFamily，
/// 因此需在窗口加载后替换实际 IconSource / IconElement。
/// </summary>
public static class SettingsPageIconPatcher
{
    private const string SettingsPageId = "aisettings.aisettingspage";
    private const string SettingsGlyph = "\ue006";
    private const string NotifierGlyph = "\ue007";

    private static readonly FontFamily IconFontFamily =
        new("avares://ClassIsland.AISmartClass/icon#AIIsland Icons");

    private static IDisposable? _windowOpenedSubscription;

    public static void Initialize()
    {
        if (_windowOpenedSubscription != null) return;

        _windowOpenedSubscription = Window.WindowOpenedEvent.AddClassHandler(
            typeof(Window),
            OnWindowOpened,
            RoutingStrategies.Direct);
    }

    private static void OnWindowOpened(object? sender, RoutedEventArgs e)
    {
        if (sender is not Window window) return;

        Dispatcher.UIThread.Post(
            () => PatchSettingsWindow(window),
            DispatcherPriority.Loaded);
    }

    public static void PatchNavigationIcon(Control settingsPage)
    {
        try
        {
            var window = TopLevel.GetTopLevel(settingsPage) as Window;
            if (window == null)
            {
                Logger.Info("[SettingsPageIconPatcher] 未找到设置窗口，跳过图标替换");
                return;
            }

            PatchSettingsWindow(window);
        }
        catch (Exception ex)
        {
            Logger.Error($"[SettingsPageIconPatcher] 设置页图标替换失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 替换设置窗口导航中 AIIsland 设置页的 Fluent 图标为自定义字体图标。
    /// </summary>
    private static void PatchSettingsWindow(Window window)
    {
        try
        {
            var navigationView = window.GetVisualDescendants()
                .OfType<NavigationView>()
                .FirstOrDefault();
            if (navigationView == null) return;

            var item = navigationView.MenuItems
                .OfType<NavigationViewItem>()
                .FirstOrDefault(x => x.Tag is SettingsPageInfo info && info.Id == SettingsPageId);
            if (item == null) return;

            item.IconSource = new FontIconSource
            {
                FontFamily = IconFontFamily,
                Glyph = SettingsGlyph
            };
            Logger.Info("[SettingsPageIconPatcher] AIIsland 设置页图标已替换为自定义 AI_panels 字形");
        }
        catch (Exception ex)
        {
            Logger.Error($"[SettingsPageIconPatcher] 设置窗口图标替换失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 构建提醒提供方的自定义字体图标元素。
    /// 由 SmartClassNotifier 构造函数调用，替换基类默认创建的 FluentIcon。
    /// </summary>
    public static FluentAvalonia.UI.Controls.FontIcon CreateNotifierIcon()
    {
        return new FluentAvalonia.UI.Controls.FontIcon
        {
            FontFamily = IconFontFamily,
            Glyph = NotifierGlyph,
            Width = 24,
            Height = 24,
            FontSize = 24
        };
    }
}
