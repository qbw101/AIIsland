using System;
using Avalonia.Controls;
using Avalonia.Threading;
using ClassIsland.AISmartClass.Models;
using ClassIsland.Core;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Shared;

namespace ClassIsland.AISmartClass.Services;

/// <summary>
/// 向 ClassIsland 托盘主菜单的“更多选项”区域注册 AIIsland 快捷操作菜单项。
/// 菜单项随 AISettings 中的 trayShow* 开关实时刷新。
/// </summary>
public static class TrayMenuRegistrar
{
    private static ITaskBarIconService? _taskBarService;
    private static NativeMenuItemBase? _separator;
    private static NativeMenuItem? _aiMenu;

    /// <summary>注册 AIIsland 快捷操作菜单。重复调用会更新现有菜单。</summary>
    public static void Register(ITaskBarIconService taskBarService)
    {
        if (taskBarService is null)
        {
            Logger.Warn("[TrayMenu] taskBarService 为空，跳过注册");
            return;
        }

        _taskBarService = taskBarService;
        Refresh(LoadSettings());
    }

    /// <summary>根据最新设置立即重建 AIIsland 托盘菜单。</summary>
    public static void Refresh(AISettings settings)
    {
        if (_taskBarService is null)
        {
            Logger.Info("[TrayMenu] 托盘服务尚未就绪，设置将在注册后应用");
            return;
        }

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => Refresh(settings));
            return;
        }

        try
        {
            var items = _taskBarService.MoreOptionsMenuItems;
            if (items is null)
            {
                Logger.Warn("[TrayMenu] MoreOptionsMenuItems 为空，无法刷新");
                return;
            }

            RemoveExistingItems(items);

            _separator = new NativeMenuItemSeparator();
            _aiMenu = BuildMenu(settings);
            items.Add(_separator);
            items.Add(_aiMenu);

            Logger.Info("[TrayMenu] AIIsland 托盘菜单已按最新设置刷新");
        }
        catch (Exception ex)
        {
            Logger.Error($"[TrayMenu] 刷新托盘菜单失败: {ex}");
        }
    }

    /// <summary>按当前设置与考试模式状态刷新菜单。</summary>
    public static void RefreshCurrent() => Refresh(LoadSettings());

    private static NativeMenuItem BuildMenu(AISettings settings)
    {
        var aiMenu = new NativeMenuItem
        {
            Header = "AIIsland",
            Menu = new NativeMenu()
        };

        var enabledCount = 0;

        // 1. 考试模式
        if (settings.TrayShowExamMode)
        {
            var server = ExamModeServer.GetOrCreate();
            var examItem = new NativeMenuItem
            {
                Header = server.IsRunning ? "停止考试模式" : "启动考试模式"
            };
            examItem.Click += async (_, _) =>
            {
                await AIRegenerationService.ToggleExamModeAsync();
                RefreshCurrent();
            };
            aiMenu.Menu.Items.Add(examItem);
            enabledCount++;
        }

        // 2. 重新生成类
        if (settings.TrayShowRegenerateSummary)
        {
            AddItem(aiMenu.Menu, "重新生成课表总结", AIRegenerationService.RequestRegenerateSummary);
            enabledCount++;
        }

        if (settings.TrayShowRegenerateHint)
        {
            AddItem(aiMenu.Menu, "重新生成学习提示", AIRegenerationService.RequestRegenerateHint);
            enabledCount++;
        }

        if (settings.TrayShowRegenerateHomework)
        {
            AddItem(aiMenu.Menu, "重新生成作业量估算", AIRegenerationService.RequestRegenerateHomeworkEstimate);
            enabledCount++;
        }

        // 3. 触发类
        if (settings.TrayShowBeforeSchoolReminder)
        {
            AddItem(aiMenu.Menu, "生成智能每日简报", AIRegenerationService.RequestTriggerBeforeSchoolReminder);
            enabledCount++;
        }

        if (settings.TrayShowBeforeClassReminder)
        {
            AddItem(aiMenu.Menu, "生成课间贴心提醒", AIRegenerationService.RequestTriggerBeforeClassReminder);
            enabledCount++;
        }

        if (settings.TrayShowAfterSchoolSummary)
        {
            AddItem(aiMenu.Menu, "生成放学贴心总结", AIRegenerationService.RequestTriggerAfterSchoolSummary);
            enabledCount++;
        }

        // 4. 自定义提醒 — 打开设置
        if (settings.TrayShowCustomReminders)
        {
            var reminders = Plugin.SmartClassNotifierInstance?.Settings?.CustomReminders;
            var activeCount = reminders?.Count(r => r.IsEnabled) ?? 0;
            var header = activeCount > 0
                ? $"管理自定义提醒（{activeCount} 条已启用）"
                : "管理自定义提醒";
            var item = new NativeMenuItem { Header = header };
            item.Click += (_, _) => OpenNotificationSettings();
            aiMenu.Menu.Items.Add(item);
            enabledCount++;
        }

        if (enabledCount == 0)
        {
            aiMenu.Menu.Items.Add(new NativeMenuItem
            {
                Header = "请在 AIIsland 设置中启用快捷操作",
                IsEnabled = false
            });
        }

        return aiMenu;
    }

    private static void AddItem(NativeMenu menu, string header, Action action)
    {
        var item = new NativeMenuItem { Header = header };
        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }

    /// <summary>
    /// 打开 ClassIsland 设置窗口的「提醒」页。
    /// ClassIsland 通过 uri 导航打开设置页，格式为 classisland://app/settings/&lt;pageId&gt;，
    /// 提醒设置页的 pageId 为 "notification"（见 NotificationSettingsPage 的 SettingsPageInfo 特性）。
    /// </summary>
    private static void OpenNotificationSettings()
    {
        try
        {
            var nav = IAppHost.TryGetService<IUriNavigationService>();
            if (nav is null)
            {
                Logger.Warn("[TrayMenu] 未能获取 IUriNavigationService，回退到打开主窗口");
                ShowMainWindowFallback();
                return;
            }

            nav.NavigateWrapped(new Uri("classisland://app/settings/notification"), out var exception);
            if (exception is not null)
            {
                Logger.Warn($"[TrayMenu] 导航到提醒设置页失败: {exception.Message}");
                return;
            }

            Logger.Info("[TrayMenu] 已打开 ClassIsland 提醒设置页");
        }
        catch (Exception ex)
        {
            Logger.Error($"[TrayMenu] 打开提醒设置页异常: {ex}");
            ShowMainWindowFallback();
        }
    }

    private static void ShowMainWindowFallback()
    {
        try
        {
            var mainWindow = AppBase.Current?.MainWindow;
            if (mainWindow is null) return;
            mainWindow.Show();
            mainWindow.Activate();
        }
        catch (Exception ex)
        {
            Logger.Warn($"[TrayMenu] 打开主窗口失败: {ex.Message}");
        }
    }

    private static void RemoveExistingItems(IList<NativeMenuItemBase> items)
    {
        if (_aiMenu != null)
        {
            items.Remove(_aiMenu);
            _aiMenu = null;
        }

        if (_separator != null)
        {
            items.Remove(_separator);
            _separator = null;
        }
    }

    private static AISettings LoadSettings()
    {
        try
        {
            var configFolder = Plugin.ConfigFolderPath;
            if (string.IsNullOrEmpty(configFolder)) return new AISettings();

            var configPath = Path.Combine(configFolder, "aisettings.json");
            if (!File.Exists(configPath)) return new AISettings();

            var json = File.ReadAllText(configPath);
            var settings = System.Text.Json.JsonSerializer.Deserialize<AISettings>(json,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new AISettings();
            Plugin.MigrateAISettings(settings, Plugin.IsLegacyTrayMenuSettings(json));
            return settings;
        }
        catch (Exception ex)
        {
            Logger.Error($"[TrayMenu] 读取设置失败，使用默认设置: {ex.Message}");
            return new AISettings();
        }
    }
}
