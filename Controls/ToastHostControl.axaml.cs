using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace ClassIsland.AISmartClass.Controls;

/// <summary>轻提示的语义类型，决定边框与图标颜色。</summary>
public enum ToastKind
{
    Info,
    Success,
    Error
}

/// <summary>
/// 右下角轻提示：浮在内容之上、不参与布局，几秒后自动淡出，点击立即关闭。
/// 由宿主页面自行摆放位置（一般是 HorizontalAlignment=Right + VerticalAlignment=Bottom）。
/// </summary>
public partial class ToastHostControl : UserControl
{
    private const int ShortDurationMs = 3200;
    private const int MaxDurationMs = 12000;
    private const int FadeOutMs = 200;

    private readonly Border? _root;
    private readonly TextBlock? _icon;
    private readonly TextBlock? _text;
    private DispatcherTimer? _stayTimer;
    private DispatcherTimer? _hideTimer;

    public ToastHostControl()
    {
        AvaloniaXamlLoader.Load(this);
        _root = this.FindControl<Border>("ToastRoot");
        _icon = this.FindControl<TextBlock>("ToastIcon");
        _text = this.FindControl<TextBlock>("ToastText");
    }

    /// <summary>
    /// 显示一条提示。<paramref name="durationMs"/> 传 0 表示常驻，直到下一条消息覆盖。
    /// </summary>
    public void Show(string message, ToastKind kind = ToastKind.Info, int? durationMs = null)
    {
        if (_root == null || _text == null || _icon == null) return;

        _stayTimer?.Stop();
        StopHideTimer();

        _text.Text = message;
        var (icon, stateClass) = kind switch
        {
            ToastKind.Success => ("\uE73E", "success"),
            ToastKind.Error => ("\uE711", "error"),
            _ => ("\uE946", "")
        };

        _icon.Text = icon;
        foreach (var cls in new[] { "success", "error" })
        {
            _root.Classes.Remove(cls);
            _icon.Classes.Remove(cls);
        }
        if (stateClass.Length > 0)
        {
            _root.Classes.Add(stateClass);
            _icon.Classes.Add(stateClass);
        }

        // 先摘掉 visible 再等一帧挂上，Transitions 每次都有起点可过渡
        _root.Classes.Remove("visible");
        _root.IsVisible = true;
        Dispatcher.UIThread.Post(() =>
        {
            if (_root == null) return;
            _root.Classes.Add("visible");
        }, DispatcherPriority.Render);

        var duration = durationMs ?? EstimateDuration(message);
        if (duration <= 0) return;

        _stayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(duration) };
        _stayTimer.Tick += (_, _) => Hide();
        _stayTimer.Start();
    }

    public void Hide()
    {
        if (_root == null) return;
        _stayTimer?.Stop();
        _stayTimer = null;
        _root.Classes.Remove("visible");

        _hideTimer?.Stop();
        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(FadeOutMs) };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer?.Stop();
            _hideTimer = null;
            if (_root != null && !_root.Classes.Contains("visible"))
                _root.IsVisible = false;
        };
        _hideTimer.Start();
    }

    /// <summary>短消息 3.2 秒，长文本（含 AI 回复）按字数延长，最多 12 秒。</summary>
    private static int EstimateDuration(string message)
    {
        var extra = (message.Length - 24) * 30;
        return Math.Clamp(ShortDurationMs + Math.Max(0, extra), ShortDurationMs, MaxDurationMs);
    }

    private void StopHideTimer()
    {
        _hideTimer?.Stop();
        _hideTimer = null;
    }

    private void OnToastPointerPressed(object? sender, PointerPressedEventArgs e) => Hide();

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _stayTimer?.Stop();
        _stayTimer = null;
        _hideTimer?.Stop();
        _hideTimer = null;
    }
}
