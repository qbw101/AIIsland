using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ClassIsland.AISmartClass.Views;
using ClassIsland.Shared;

namespace ClassIsland.AISmartClass.Services;

/// <summary>
/// Shows the one-time external data authorization prompt when ClassIsland application settings opens.
/// </summary>
public static class ExternalPluginAuthorizationCoordinator
{
    private const string SettingsWindowTypeName = "ClassIsland.Views.SettingsWindowNew";
    private static readonly HashSet<Window> AttachedWindows = new();
    private static IClassicDesktopStyleApplicationLifetime? _desktopLifetime;
    private static bool _initialized;
    private static bool _dialogOpen;

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        Dispatcher.UIThread.Post(InitializeOnUiThread);
    }

    private static void InitializeOnUiThread()
    {
        _desktopLifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        if (_desktopLifetime == null)
        {
            Logger.Warn("[PluginAuthorization] 桌面应用生命周期尚不可用");
            _initialized = false;
            DispatcherTimer.RunOnce(Initialize, TimeSpan.FromSeconds(1));
            return;
        }

        foreach (var window in _desktopLifetime.Windows) AttachIfSettingsWindow(window);
        TryAttachSettingsWindowFromServices();
        if (_desktopLifetime.Windows is INotifyCollectionChanged windows)
            windows.CollectionChanged += OnWindowsChanged;

        Logger.Info("[PluginAuthorization] 已监听 ClassIsland 应用设置窗口");
    }

    private static void TryAttachSettingsWindowFromServices()
    {
        try
        {
            var settingsWindowType = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(assembly =>
                {
                    try { return assembly.GetTypes(); }
                    catch { return Array.Empty<Type>(); }
                })
                .FirstOrDefault(type => type.FullName == SettingsWindowTypeName);
            if (settingsWindowType != null &&
                IAppHost.Host?.Services.GetService(settingsWindowType) is Window settingsWindow)
            {
                AttachIfSettingsWindow(settingsWindow);
            }
        }
        catch (Exception ex)
        {
            Logger.Info($"[PluginAuthorization] 从服务容器获取应用设置窗口失败: {ex.Message}");
        }
    }

    private static void OnWindowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems == null) return;
        foreach (var window in e.NewItems.OfType<Window>()) AttachIfSettingsWindow(window);
    }

    private static void AttachIfSettingsWindow(Window window)
    {
        if (window.GetType().FullName != SettingsWindowTypeName || !AttachedWindows.Add(window)) return;
        window.Opened += OnSettingsWindowOpened;
        window.PropertyChanged += OnSettingsWindowPropertyChanged;
        Logger.Info("[PluginAuthorization] 已绑定应用设置窗口打开事件");

        // SettingsWindowNew is reused: Open() calls Show() only once and later openings
        // are Hide()/Show() cycles. Opened is not guaranteed for those cycles.
        if (window.IsVisible)
            Dispatcher.UIThread.Post(() => OnSettingsWindowOpened(window, EventArgs.Empty), DispatcherPriority.Background);
    }

    private static void OnSettingsWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (sender is not Window window || e.Property.Name != "IsVisible" || !window.IsVisible) return;
        Dispatcher.UIThread.Post(() => OnSettingsWindowOpened(window, EventArgs.Empty), DispatcherPriority.Background);
    }

    private static async void OnSettingsWindowOpened(object? sender, EventArgs e)
    {
        Logger.Info("[PluginAuthorization] 检测到应用设置窗口已打开");
        if (_dialogOpen || sender is not Window owner) return;

        var settings = Plugin.SmartClassNotifierInstance?.Settings;
        if (settings == null)
        {
            Logger.Warn("[PluginAuthorization] SmartClassNotifier 设置实例尚未就绪");
            return;
        }
        if (settings.PluginAuthorizationConfirmed)
        {
            Logger.Info("[PluginAuthorization] 用户已经完成过授权选择，不再弹窗");
            return;
        }

        var installed = PluginIntegrationService.GetInstalledPlugins();
        if (!ShouldPrompt(settings, installed.Count))
        {
            Logger.Info("[PluginAuthorization] 未检测到可读取的插件，本次不显示授权提示");
            return;
        }

        _dialogOpen = true;
        try
        {
            var dialog = new PluginAuthorizationDialog(defaultSelectAll: true);
            var accepted = await dialog.ShowDialog<bool?>(owner) == true;
            Plugin.ApplyExternalPluginAuthorization(
                accepted,
                accepted ? dialog.AuthorizedPluginIds : Array.Empty<string>(),
                confirmed: true);
            Logger.Info(accepted
                ? $"[PluginAuthorization] 用户授权了 {dialog.AuthorizedPluginIds.Count} 个插件"
                : "[PluginAuthorization] 用户拒绝了外部插件数据授权");
        }
        catch (Exception ex)
        {
            Logger.Error($"[PluginAuthorization] 显示授权对话框失败: {ex.Message}");
        }
        finally
        {
            _dialogOpen = false;
        }
    }

    public static bool ShouldPrompt(Models.SmartClassNotifierSettings? settings, int installedPluginCount) =>
        settings != null && !settings.PluginAuthorizationConfirmed && installedPluginCount > 0;
}
