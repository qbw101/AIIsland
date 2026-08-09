using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.AISmartClass.Models;
using ClassIsland.AISmartClass.Services;
using ClassIsland.AISmartClass.Views;

namespace ClassIsland.AISmartClass.Controls.NotificationProviders;

/// <summary>
/// AIIsland 智能提醒的设置控件。
/// 必须继承 NotificationProviderControlBase&lt;T&gt;，由 ClassIsland 通过 SettingsInternal 注入设置实例，
/// 设置变更后由主程序自动持久化到 Settings.json（NotificationProvidersSettings 字典）。
/// 不要在此设置 DataContext = this，axaml 内部用 FindAncestor 绑定，否则 Settings 解析失败。
/// </summary>
public partial class SmartClassNotifierSettingsControl : NotificationProviderControlBase<SmartClassNotifierSettings>
{
    private ListBox? _reminderListBox;
    private TextBox? _nlInputBox;
    private TextBlock? _parseStatusText;
    private TextBlock? _windowsContextTestResult;
    private TextBlock? _rssTestResult;
    private ComboBox? _rssSourceComboBox;
    private TextBox? _customRssTextBox;
    private INotifyCollectionChanged? _subscribedCollection;

    public SmartClassNotifierSettingsControl()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        _reminderListBox = this.FindControl<ListBox>("ReminderListBox");
        _nlInputBox = this.FindControl<TextBox>("NlInputBox");
        _parseStatusText = this.FindControl<TextBlock>("ParseStatusText");
        _windowsContextTestResult = this.FindControl<TextBlock>("WindowsContextTestResult");
        _rssTestResult = this.FindControl<TextBlock>("RssTestResult");
        _rssSourceComboBox = this.FindControl<ComboBox>("RssSourceComboBox");
        _customRssTextBox = this.FindControl<TextBox>("CustomRssTextBox");
        SelectRssSource();

        ApplyWindowsContextAvailability();

        SubscribeCollectionChanged();
        RefreshReminderList();
    }

    private void SelectRssSource()
    {
        if (_rssSourceComboBox == null || Settings == null) return;
        var value = Settings.RssFeedUrls?.Trim() ?? "";
        var isLegacySource = value.Contains("rss.watchrss.cn", StringComparison.OrdinalIgnoreCase) ||
                             value.Equals("https://www.ithome.com/rss", StringComparison.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(value) || isLegacySource)
        {
            value = "http://www.ithome.com/rss/";
            Settings.RssFeedUrls = value;
        }
        var index = value switch
        {
            "http://www.ithome.com/rss/" => 0,
            "https://36kr.com/feed" => 1,
            _ => 2
        };
        _rssSourceComboBox.SelectedIndex = index;
        if (_customRssTextBox != null) _customRssTextBox.IsVisible = index == 2;
    }

    private void OnRssSourceChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_rssSourceComboBox?.SelectedItem is not ComboBoxItem item || Settings == null) return;
        var tag = item.Tag?.ToString() ?? "";
        if (tag == "__custom__")
        {
            if (_customRssTextBox != null) _customRssTextBox.IsVisible = true;
            return;
        }
        if (_customRssTextBox != null) _customRssTextBox.IsVisible = false;
        Settings.RssFeedUrls = tag;
    }

    private async void OnRssTestClicked(object? sender, RoutedEventArgs e)
    {
        if (_rssTestResult == null || Settings == null) return;
        _rssTestResult.Text = "正在读取 RSS...";
        try
        {
            var service = new DailyBriefingDataService();
            var news = await service.GetNewsAsync(Settings.ClassIslandInstallDirectory, Settings.RssFeedUrls);
            _rssTestResult.Text = news.Count == 0
                ? "未读取到新闻，请检查地址或网络。"
                : $"读取成功：{string.Join("；", news.Take(2))}";
        }
        catch (Exception ex)
        {
            _rssTestResult.Text = $"RSS 测试失败：{ex.Message}";
        }
    }

    private void ApplyWindowsContextAvailability()
    {
        if (Settings == null || WindowsSystemContextService.IsWindowsSystemContextSupported) return;

        // Persist a disabled state so an unsupported host cannot repeatedly enter
        // the Windows-only polling paths after restart.
        Settings.EnableWeatherReminder = false;
        Settings.EnableTemperatureReminder = false;
        Settings.EnableWeatherAlertReminder = false;
        Settings.EnableMusicReminder = false;
        if (_windowsContextTestResult != null)
            _windowsContextTestResult.Text = $"Windows 功能已禁用：{WindowsSystemContextService.GetSupportStatus()}";
    }

    private async void OnWindowsContextTestClicked(object? sender, RoutedEventArgs e)
    {
        if (_windowsContextTestResult == null || Settings == null) return;

        _windowsContextTestResult.Text = "正在检测定位、媒体会话和小米天气...";
        try
        {
            var service = new WindowsSystemContextService();
            var music = await service.GetCurrentMusicAsync();

            var locationService = new LocationService(
                () => Settings.ClassIslandInstallDirectory);
            var location = await locationService.GetLocationAsync();
            var locationText = location == null
                ? "定位：不可用"
                : $"定位：{location.Address} ({location.Latitude:F4}, {location.Longitude:F4}) [{location.Provider}]";

            var weather = await service.GetCurrentWeatherAsync(location);
            var weatherText = weather == null
                ? "小米天气：未取得天气数据"
                : $"小米天气：{WindowsSystemContextService.DescribeWeatherCode(weather.WeatherCode)}，" +
                  $"{weather.TemperatureC:0.#}°C" +
                  (weather.ApparentTemperatureC is double apparent ? $" / 体感 {apparent:0.#}°C" : "") +
                  (weather.Alerts.Count > 0
                      ? $" / 预警：{string.Join("、", weather.Alerts.Select(a => string.IsNullOrWhiteSpace(a.Level) ? a.Title : $"{a.Title}({a.Level})"))}"
                      : "");

            var musicText = music == null ? "未检测到正在播放的音乐" : $"音乐：{music.Title}";
            _windowsContextTestResult.Text = $"{locationText}；{musicText}；{weatherText}";
        }
        catch (Exception ex)
        {
            _windowsContextTestResult.Text = $"Windows 功能测试失败：{ex.Message}";
        }
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        if (_subscribedCollection != null)
            _subscribedCollection.CollectionChanged -= OnCustomRemindersChanged;
        _subscribedCollection = null;
    }

    private void SubscribeCollectionChanged()
    {
        if (_subscribedCollection != null)
            _subscribedCollection.CollectionChanged -= OnCustomRemindersChanged;

        _subscribedCollection = Settings?.CustomReminders;
        if (_subscribedCollection != null)
            _subscribedCollection.CollectionChanged += OnCustomRemindersChanged;
    }

    private void OnCustomRemindersChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshReminderList();
    }

    private void RefreshReminderList()
    {
        if (_reminderListBox == null || Settings == null) return;
        _reminderListBox.ItemsSource = null;
        _reminderListBox.ItemsSource = Settings.CustomReminders;
    }

    private void OnAddClicked(object? sender, RoutedEventArgs e)
    {
        ShowDialog(new CustomReminderEditDialog(new CustomReminder()));
    }

    private void OnEditClicked(object? sender, RoutedEventArgs e)
    {
        if (_reminderListBox?.SelectedItem is not CustomReminder reminder)
        {
            SetStatus("请先选择一条提醒。", true);
            return;
        }

        ShowDialog(new CustomReminderEditDialog(reminder), reminder);
    }

    private void OnDeleteClicked(object? sender, RoutedEventArgs e)
    {
        if (_reminderListBox?.SelectedItem is not CustomReminder reminder || Settings == null)
        {
            SetStatus("请先选择要删除的提醒。", true);
            return;
        }

        Settings.CustomReminders.Remove(reminder);
        SetStatus("已删除选中的提醒。", false);
        RefreshReminderList();
    }

    private async void OnExportRemindersClicked(object? sender, RoutedEventArgs e)
    {
        if (Settings == null || Settings.CustomReminders.Count == 0)
        {
            SetStatus("当前没有可导出的自定义提醒。", true);
            return;
        }

        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "导出自定义提醒",
                SuggestedFileName = "aiisland-reminders.json",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("JSON 文件") { Patterns = new[] { "*.json" } }
                }
            });

            if (file == null) return;

            var json = JsonSerializer.Serialize(Settings.CustomReminders, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            await using var stream = await file.OpenWriteAsync();
            await using var writer = new System.IO.StreamWriter(stream, System.Text.Encoding.UTF8);
            await writer.WriteAsync(json);

            SetStatus($"已导出 {Settings.CustomReminders.Count} 条提醒到：{file.Path.LocalPath}", false);
        }
        catch (Exception ex)
        {
            Logger.Error($"导出提醒失败: {ex.Message}");
            SetStatus($"导出失败: {ex.Message}", true);
        }
    }

    private async void OnImportRemindersClicked(object? sender, RoutedEventArgs e)
    {
        if (Settings == null)
        {
            SetStatus("设置尚未加载，无法导入提醒。", true);
            return;
        }

        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "导入自定义提醒",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("JSON 文件") { Patterns = new[] { "*.json" } }
                }
            });

            if (files.Count == 0) return;

            await using var stream = await files[0].OpenReadAsync();
            var imported = await JsonSerializer.DeserializeAsync<List<CustomReminder>>(stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (imported == null || imported.Count == 0)
            {
                SetStatus("配置文件为空或没有有效提醒。", true);
                return;
            }

            // 重置触发状态，避免导入的提醒因旧状态跳过触发
            foreach (var r in imported)
            {
                r.LastTriggeredDate = null;
                r.LastTriggeredKey = null;
            }

            // 去重合并：以 Id 为键，相同 Id 覆盖，新增则追加
            var existing = Settings.CustomReminders.ToDictionary(r => r.Id);
            int added = 0, updated = 0;
            foreach (var r in imported)
            {
                if (existing.TryGetValue(r.Id, out var target))
                {
                    target.CopyFrom(r);
                    updated++;
                }
                else
                {
                    Settings.CustomReminders.Add(r);
                    added++;
                }
            }

            RefreshReminderList();
            SetStatus($"导入完成：新增 {added} 条，更新 {updated} 条。", false);
        }
        catch (Exception ex)
        {
            Logger.Error($"导入提醒失败: {ex.Message}");
            SetStatus($"导入失败: {ex.Message}", true);
        }
    }

    private async void OnParseClicked(object? sender, RoutedEventArgs e)
    {
        var input = _nlInputBox?.Text?.Trim();
        if (string.IsNullOrWhiteSpace(input))
        {
            SetStatus("请输入提醒内容。", true);
            return;
        }

        try
        {
            var parser = Plugin.GetReminderParser();
            if (parser == null)
            {
                SetStatus("自然语言解析服务尚未初始化，请稍后重试。", true);
                return;
            }

            SetStatus("正在解析...", false);
            var (reminder, error) = await parser.ParseAsync(input);
            if (reminder == null)
            {
                SetStatus(error ?? "无法解析这条提醒，请改用手动添加。", true);
                return;
            }

            Settings?.CustomReminders.Add(reminder);
            RefreshReminderList();
            if (_nlInputBox != null) _nlInputBox.Text = "";
            SetStatus($"已添加：{reminder.DisplaySubtitle}", false);
        }
        catch (Exception ex)
        {
            Logger.Error($"自然语言解析失败: {ex.Message}");
            SetStatus("解析失败，请尝试更直接的表述或手动添加。", true);
        }
    }

    private async void ShowDialog(CustomReminderEditDialog dialog, CustomReminder? target = null)
    {
        if (this.VisualRoot is Window owner)
            await dialog.ShowDialog(owner);
        else
            dialog.Show();

        if (!dialog.Confirmed) return;

        if (Settings == null)
        {
            SetStatus("设置尚未加载，无法保存提醒。", true);
            return;
        }

        if (target == null)
            Settings.CustomReminders.Add(dialog.Result);
        else
            target.CopyFrom(dialog.Result);

        RefreshReminderList();
        SetStatus("提醒已保存。", false);
    }

    private void SetStatus(string message, bool isError)
    {
        if (_parseStatusText == null) return;
        _parseStatusText.Text = message;
        if (isError)
            _parseStatusText.Classes.Add("error");
        else
            _parseStatusText.Classes.Remove("error");
        _parseStatusText.IsVisible = true;
    }
}
