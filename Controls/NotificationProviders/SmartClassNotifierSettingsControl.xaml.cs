using System.Collections.ObjectModel;
using System.Collections.Specialized;
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

        SubscribeCollectionChanged();
        RefreshReminderList();
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
