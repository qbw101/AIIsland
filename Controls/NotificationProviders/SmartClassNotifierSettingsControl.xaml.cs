using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
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
        _parseStatusText.Foreground = isError ? ThemeHelper.SystemCritical : ThemeHelper.TextTertiary;
        _parseStatusText.IsVisible = true;
    }
}
