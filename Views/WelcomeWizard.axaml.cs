using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Platform.Storage;
using ClassIsland.AISmartClass.Models;
using ClassIsland.AISmartClass.Services;
using ClassIsland.AISmartClass.Services.NotificationProviders;

namespace ClassIsland.AISmartClass.Views;

public partial class WelcomeWizard : Window
{
    private int _currentStep;
    private string _chosenPath = "";
    private ApiProviderPreset? _selectedPreset;
    private bool _customMode;
    private bool _platformListInitialized;
    private bool _completionNotified;
    private bool _reminderSettingsInitialized;
    private bool _pluginListInitialized;
    private SmartClassNotifierSettings _welcomeReminderSettings = new();
    private DispatcherTimer? _welcomeAnimationTimer;
    private DispatcherTimer? _contentAnimationTimer;
    private DispatcherTimer? _transitionTimer;
    private DispatcherTimer? _completionAnimationTimer;
    private AISettings _settings = new();

    // 导航防抖：上次导航时间，200ms 内的重复点击被忽略
    private DateTime _lastNavigateTime = DateTime.MinValue;
    private const int NavigateDebounceMs = 200;

    private static readonly List<string> StepNames = new() { "开始", "功能预览", "接入 AI", "填写配置", "选择语气", "贴心提醒", "完成" };

    // 按钮动画：颜色瞬时切换（无 BrushTransition 避免闪烁），代码仅处理缩放
    private readonly List<Button> _animatedButtons = new();
    private readonly Dictionary<Button, ScaleTransform> _buttonTransforms = new();
    private readonly Dictionary<Button, DispatcherTimer> _buttonTimers = new();
    private const double PressScale = 0.98;

    private sealed class ContentAnimationState
    {
        public required Control Control { get; init; }
        public required TranslateTransform Offset { get; init; }
        public Transform? OriginalTransform { get; init; }
    }

    private readonly List<ContentAnimationState> _contentAnimationStates = new();

    // 封面打字机效果：逐字输出 "AIIsland"，打完后光标闪烁
    private const string HeroTitleText = "AIIsland";
    private const double TypingStartDelay = 620;      // 等 logo 入场稳定后再开始敲字
    private const double TypingCharInterval = 105;    // 每个字符间隔
    private const int HeroTitleLength = 8;            // "AIIsland".Length，const 表达式不能用 .Length
    private const double TypingEndDelay = TypingStartDelay + TypingCharInterval * HeroTitleLength;
    private const double CaretBlinkPeriod = 1100;     // 光标一次明暗完整周期

    public event Action<AISettings>? WizardCompleted;

    public WelcomeWizard()
    {
        InitializeComponent();
        BuildStepIndicator();
        NavigateTo(1);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        RegisterButtonAnimations(this);
    }

    private void RegisterButtonAnimations(Visual root)
    {
        if (root is Button btn && !_animatedButtons.Contains(btn))
        {
            _animatedButtons.Add(btn);
            var st = new ScaleTransform(1.0, 1.0);
            _buttonTransforms[btn] = st;
            btn.RenderTransform = st;
            btn.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);

            btn.PointerPressed += (_, _) => AnimateScale(btn, PressScale);
            btn.PointerReleased += (_, _) => AnimateScale(btn, 1.0);
            btn.PointerExited += (_, _) => AnimateScale(btn, 1.0);
            btn.PointerCaptureLost += (_, _) => AnimateScale(btn, 1.0);
        }
        foreach (var child in root.GetVisualChildren())
            RegisterButtonAnimations(child);
    }

    private void AnimateScale(Button btn, double target)
    {
        // 取消上一个未完成的 timer，避免多个 timer 同时写 ScaleTransform
        if (_buttonTimers.TryGetValue(btn, out var oldTimer))
        {
            oldTimer.Stop();
            _buttonTimers.Remove(btn);
        }

        if (!_buttonTransforms.TryGetValue(btn, out var st))
        {
            st = new ScaleTransform(1.0, 1.0);
            _buttonTransforms[btn] = st;
            btn.RenderTransform = st;
        }

        var cur = st.ScaleX;
        if (Math.Abs(cur - target) < 0.001) return;

        var steps = 5;
        var elapsed = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _buttonTimers[btn] = timer;

        timer.Tick += (_, _) =>
        {
            elapsed++;
            var t = (double)elapsed / steps;
            t = t * t * (3 - 2 * t);
            var s = cur + (target - cur) * t;
            // 修改现有对象的属性，不创建新 ScaleTransform（避免渲染闪烁）
            st.ScaleX = s;
            st.ScaleY = s;
            if (elapsed >= steps)
            {
                timer.Stop();
                _buttonTimers.Remove(btn);
            }
        };
        timer.Start();
    }

    public WelcomeWizard(AISettings existingSettings) : this()
    {
        _settings = existingSettings;
        ManualEndpointBox.Text = existingSettings.Endpoint;
        ManualModelBox.Text = existingSettings.Model;
        ManualKeyBox.Text = existingSettings.ApiKey;
        RecommendedEndpointBox.Text = existingSettings.Endpoint;
        RecommendedModelBox.Text = existingSettings.Model;
        RecommendedKeyBox.Text = existingSettings.ApiKey;
    }

    private void StartWelcomeAnimation()
    {
        StopWelcomeAnimation();

        var riseItems = new (Control Control, double Delay, double Distance)[]
        {
            (HeroTitleRow, TypingStartDelay - 120, 14),
            (HeroSubtitle, TypingEndDelay + 60, 16)
        };

        HeroTitle.Text = string.Empty;
        HeroCaret.Opacity = 0;

        foreach (var item in riseItems)
        {
            item.Control.Opacity = 0;
            item.Control.RenderTransform = new TranslateTransform(0, item.Distance);
        }

        HeroVisual.Opacity = 0;
        HeroVisual.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        var visualScale = new ScaleTransform(0.82, 0.82);
        HeroVisual.RenderTransform = visualScale;

        var characterOffset = new TranslateTransform(0, 14);
        HeroCharacter.RenderTransform = characterOffset;

        var startedAt = Environment.TickCount64;
        _welcomeAnimationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _welcomeAnimationTimer.Tick += (_, _) =>
        {
            var elapsed = Environment.TickCount64 - startedAt;

            foreach (var item in riseItems)
            {
                var progress = EaseOutCubic(Progress(elapsed, item.Delay, 420));
                item.Control.Opacity = progress;
                if (item.Control.RenderTransform is TranslateTransform transform)
                    transform.Y = item.Distance * (1 - progress);
            }

            var visualProgress = Progress(elapsed, 40, 620);
            var visualEase = EaseOutBack(visualProgress);
            HeroVisual.Opacity = EaseOutCubic(visualProgress);
            visualScale.ScaleX = visualScale.ScaleY = 0.82 + 0.18 * visualEase;

            // 图标仅做一次入场上移，入场结束后固定不动
            characterOffset.Y = 14 * (1 - visualEase);

            UpdateTypewriter(elapsed);
        };
        _welcomeAnimationTimer.Start();
    }

    /// <summary>
    /// 按经过时间推进封面标题的打字机效果。
    /// 打字阶段：光标常亮并跟着字符走；打完后：光标进入平滑闪烁。
    /// </summary>
    private void UpdateTypewriter(long elapsed)
    {
        if (elapsed < TypingStartDelay)
        {
            HeroTitle.Text = string.Empty;
            // 敲字前先让光标露出来，暗示"马上要打字了"
            HeroCaret.Opacity = EaseOutCubic(Progress(elapsed, TypingStartDelay - 260, 240));
            return;
        }

        var typed = (int)Math.Floor((elapsed - TypingStartDelay) / TypingCharInterval) + 1;
        typed = Math.Clamp(typed, 0, HeroTitleText.Length);

        if (HeroTitle.Text?.Length != typed)
            HeroTitle.Text = HeroTitleText[..typed];

        if (typed < HeroTitleText.Length)
        {
            HeroCaret.Opacity = 1;
            return;
        }

        // 打完了，光标转入闪烁。用 sin 做平滑呼吸而不是硬切换，视觉上更贵一点
        var since = elapsed - TypingEndDelay;
        var wave = (Math.Sin(since / CaretBlinkPeriod * Math.PI * 2) + 1) / 2;
        HeroCaret.Opacity = 0.12 + wave * 0.88;
    }

    private void StopWelcomeAnimation()
    {
        _welcomeAnimationTimer?.Stop();
        _welcomeAnimationTimer = null;

        // 中断时把标题补全，避免停在残缺状态
        HeroTitle.Text = HeroTitleText;
        HeroCaret.Opacity = 1;
    }

    private static double Progress(long elapsed, double delay, double duration)
        => Math.Clamp((elapsed - delay) / duration, 0, 1);

    private static double EaseOutCubic(double value)
        => 1 - Math.Pow(1 - value, 3);

    private static double EaseOutBack(double value)
    {
        const double overshoot = 1.70158;
        var shifted = value - 1;
        return 1 + (overshoot + 1) * Math.Pow(shifted, 3) + overshoot * Math.Pow(shifted, 2);
    }

    private void StartContentEntrance(Border page)
    {
        StopContentEntrance();
        if (page.Child is not StackPanel panel) return;

        var targets = new List<Control>();
        foreach (var child in panel.Children)
        {
            if (ReferenceEquals(child, FeatureTimeline) && child is Grid featureGrid)
                targets.AddRange(featureGrid.Children);
            else
                targets.Add(child);
        }

        foreach (var child in targets)
        {
            var offset = new TranslateTransform(0, 14);
            var original = child.RenderTransform as Transform;
            child.Opacity = 0;
            child.RenderTransform = original == null
                ? offset
                : new TransformGroup { Children = { original, offset } };

            _contentAnimationStates.Add(new ContentAnimationState
            {
                Control = child,
                Offset = offset,
                OriginalTransform = original
            });
        }

        if (_contentAnimationStates.Count == 0) return;

        var startedAt = Environment.TickCount64;
        _contentAnimationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _contentAnimationTimer.Tick += (_, _) =>
        {
            var elapsed = Environment.TickCount64 - startedAt;
            var complete = true;

            for (var i = 0; i < _contentAnimationStates.Count; i++)
            {
                var state = _contentAnimationStates[i];
                var progress = EaseOutCubic(Progress(elapsed, 90 + i * 55, 330));
                state.Control.Opacity = progress;
                state.Offset.Y = 14 * (1 - progress);
                complete &= progress >= 1;
            }

            if (complete) StopContentEntrance();
        };
        _contentAnimationTimer.Start();
    }

    /// <summary>
    /// 完成页图标的收尾动画：光环由小到大铺开，图标带回弹落位，之后维持极轻的呼吸感。
    /// </summary>
    private void StartCompletionAnimation()
    {
        StopCompletionAnimation();

        DonePanel.RenderTransformOrigin = DoneHalo.RenderTransformOrigin =
            DoneIcon.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);

        var haloScale = new ScaleTransform(0.6, 0.6);
        var iconScale = new ScaleTransform(0.7, 0.7);
        DoneHalo.RenderTransform = haloScale;
        DoneIcon.RenderTransform = iconScale;
        DoneHalo.Opacity = 0;
        DoneIcon.Opacity = 0;

        var startedAt = Environment.TickCount64;
        _completionAnimationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _completionAnimationTimer.Tick += (_, _) =>
        {
            var elapsed = Environment.TickCount64 - startedAt;

            var haloProgress = Progress(elapsed, 40, 480);
            DoneHalo.Opacity = EaseOutCubic(haloProgress);
            haloScale.ScaleX = haloScale.ScaleY = 0.6 + 0.4 * EaseOutBack(haloProgress);

            var iconProgress = Progress(elapsed, 140, 520);
            DoneIcon.Opacity = EaseOutCubic(iconProgress);

            if (elapsed < 900)
            {
                iconScale.ScaleX = iconScale.ScaleY = 0.7 + 0.3 * EaseOutBack(iconProgress);
            }
            else
            {
                var phase = (elapsed - 900) / 1000.0;
                var breathe = Math.Sin(phase * Math.PI * 2 / 3.4);
                iconScale.ScaleX = iconScale.ScaleY = 1 + breathe * 0.014;
                haloScale.ScaleX = haloScale.ScaleY = 1 - breathe * 0.01;
            }
        };
        _completionAnimationTimer.Start();
    }

    private void StopCompletionAnimation()
    {
        _completionAnimationTimer?.Stop();
        _completionAnimationTimer = null;
    }

    private void StopContentEntrance()
    {
        _contentAnimationTimer?.Stop();
        _contentAnimationTimer = null;
        foreach (var state in _contentAnimationStates)
        {
            state.Control.Opacity = 1;
            state.Control.RenderTransform = state.OriginalTransform;
        }
        _contentAnimationStates.Clear();
    }

    private void BuildStepIndicator()
    {
        for (var i = 0; i < StepNames.Count; i++)
        {
            var idx = i;
            var number = i + 1;

            if (i > 0)
            {
                var connector = new Border
                {
                    Width = 24, Height = 1,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    Tag = $"connector-{i}"
                };
                connector.Classes.Add("step-connector");
                StepIndicator.Children.Add(connector);
            }

            var numberText = new TextBlock
            {
                Text = number.ToString(), FontSize = 12, FontWeight = FontWeight.SemiBold,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            numberText.Classes.Add("step-number");

            var dot = new Border
            {
                Width = 24, Height = 24, CornerRadius = new CornerRadius(12),
                BorderThickness = new Thickness(2),
                Child = numberText,
                Tag = number
            };
            dot.Classes.Add("step-dot");
            ToolTip.SetTip(dot, StepNames[idx]);
            StepIndicator.Children.Add(dot);
        }
    }

    private void UpdateStepIndicator(int step)
    {
        foreach (var child in StepIndicator.Children)
        {
            if (child is Border connector && connector.Tag is string connectorTag &&
                connectorTag.StartsWith("connector-") &&
                int.TryParse(connectorTag[10..], out var connectorStep))
            {
                connector.Classes.Set("completed", connectorStep < step);
                continue;
            }

            if (child is not Border dot || dot.Tag is not int n) continue;
            var numberText = dot.Child as TextBlock;

            dot.Classes.Remove("completed");
            dot.Classes.Remove("current");
            numberText?.Classes.Remove("completed");
            numberText?.Classes.Remove("current");

            if (n < step)
            {
                dot.Classes.Add("completed");
                numberText?.Classes.Add("completed");
            }
            else if (n == step)
            {
                dot.Classes.Add("current");
                numberText?.Classes.Add("current");
            }
        }

        StepIndicator.IsVisible = step > 1;
        StepLabel.IsVisible = step > 1;
        StepLabel.Text = $"{StepNames[step - 1]}  ·  第 {step} 步，共 {StepNames.Count} 步";
    }

    private Border? GetPage(int step)
    {
        return step switch
        {
            1 => Page1,
            2 => Page2,
            3 => Page3,
            4 => _chosenPath switch
            {
                "manual" => Page4Manual,
                "recommended" => Page4Recommended,
                _ => Page4Recommended
            },
            5 => PagePreferences,
            6 => PagePluginAuth,
            7 => Page5,
            _ => null
        };
    }

    /// <summary>
    /// 所有页面控件列表（用于导航时统一隐藏）。
    /// </summary>
    private IEnumerable<Border> AllPages => new[]
    {
        Page1, Page2, Page3, Page4Manual, Page4Recommended, PagePreferences, PagePluginAuth, Page5
    };

    private void NavigateTo(int step, bool forward = true)
    {
        StopContentEntrance();
        if (step != 6) StopCompletionAnimation();

        // ★ 中断旧动画：Stop timer + 把所有页面归零
        // 这样旧动画的中间状态不会残留，新动画从干净状态开始
        if (_transitionTimer != null)
        {
            _transitionTimer.Stop();
            _transitionTimer = null;
            foreach (var p in AllPages)
            {
                p.IsVisible = false;
                p.IsHitTestVisible = true;
                p.Opacity = 0;
                p.RenderTransform = null;
            }
        }

        var oldPage = GetPage(_currentStep);
        var newPage = GetPage(step);

        _currentStep = step;
        UpdateStepIndicator(step);
        UpdateButtons();

        PageScrollViewer.Offset = Vector.Zero;

        if (newPage == null) return;

        // 先完成插件检测和动态控件创建，再开始页面过渡，避免动画期间发生布局重排。
        if (step == 6) UpdatePluginAuthSelection();

        // 确保 oldPage 处于完全可见状态（动画中断或异常状态修复）
        if (oldPage != null && oldPage != newPage)
        {
            if (!oldPage.IsVisible || oldPage.Opacity < 0.99)
            {
                oldPage.IsVisible = true;
                oldPage.Opacity = 1;
                oldPage.RenderTransform = null;
            }
            AnimatePageTransition(oldPage, newPage, forward);
        }
        else
        {
            // 同一页面或无 oldPage，直接显示
            foreach (var p in AllPages)
            {
                p.IsVisible = false;
                p.Opacity = 0;
                p.RenderTransform = null;
            }
            newPage.IsVisible = true;
            newPage.Opacity = 1;
            newPage.RenderTransform = null;
        }

        if (step == 1)
            StartWelcomeAnimation();
        else
        {
            StopWelcomeAnimation();
            StartContentEntrance(newPage);
        }
        if (step == 3) UpdatePathSelection();
        if (step == 4 && _chosenPath == "recommended") PopulatePlatformList();
        if (step == 5) UpdateToneSelection();
        if (step == 7)
        {
            BuildCompletePage();
            StartCompletionAnimation();
        }
    }

    /// <summary>
    /// 安全的页面过渡动画。
    /// 设计原则：
    /// 1. 只操作 oldPage 和 newPage 的 Opacity/RenderTransform，不动 IsVisible（直到动画结束才设 IsVisible=false）
    /// 2. 如果被 NavigateTo 中断，旧 timer 被 Stop，所有页面归零——不会有残留中间状态
    /// 3. 动画结束时同步设置最终状态
    /// </summary>
    private void AnimatePageTransition(Border oldPage, Border newPage, bool forward)
    {
        var startOffset = forward ? 48.0 : -48.0;

        newPage.IsVisible = true;
        newPage.IsHitTestVisible = true;
        newPage.Opacity = 0;
        newPage.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        var newTranslate = new TranslateTransform(startOffset, 0);
        var newScale = new ScaleTransform(0.985, 0.985);
        newPage.RenderTransform = new TransformGroup { Children = { newScale, newTranslate } };

        oldPage.IsVisible = true;
        oldPage.IsHitTestVisible = false;
        oldPage.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        var oldTranslate = new TranslateTransform();
        var oldScale = new ScaleTransform(1, 1);
        oldPage.RenderTransform = new TransformGroup { Children = { oldScale, oldTranslate } };

        const double duration = 280;
        var startedAt = Environment.TickCount64;
        _transitionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };

        _transitionTimer.Tick += (_, _) =>
        {
            var progress = Progress(Environment.TickCount64 - startedAt, 0, duration);
            var ease = EaseOutCubic(progress);

            newPage.Opacity = Math.Min(1, ease * 1.2);
            newTranslate.X = startOffset * (1 - ease);
            newScale.ScaleX = newScale.ScaleY = 0.985 + 0.015 * ease;

            oldPage.Opacity = Math.Max(0, 1 - progress * 1.35);
            oldTranslate.X = -startOffset * ease * 0.28;
            oldScale.ScaleX = oldScale.ScaleY = 1 - 0.012 * ease;

            if (progress >= 1)
            {
                _transitionTimer.Stop();
                _transitionTimer = null;
                newPage.Opacity = 1;
                newPage.RenderTransform = null;
                oldPage.IsVisible = false;
                oldPage.IsHitTestVisible = true;
                oldPage.Opacity = 0;
                oldPage.RenderTransform = null;
            }
        };

        _transitionTimer.Start();
    }

    private void UpdateButtons()
    {
        var isLast = _currentStep == StepNames.Count;
        var isFirst = _currentStep == 1;
        var isApiStep = _currentStep == 4;

        PrevBtn.IsVisible = !isFirst;
        SkipBtn.IsVisible = isApiStep || _currentStep == 5 || _currentStep == 6;
        NextBtn.IsVisible = !(_currentStep == 3);
        NextBtn.Content = isLast
            ? "开始使用"
            : isFirst
                ? "开始设置"
                : isApiStep || _currentStep == 5 || _currentStep == 6
                    ? "保存并继续"
                    : "下一步";
    }

    // ---- 事件 ----

    /// <summary>防抖检查：距上次导航不足 200ms 则忽略点击。</summary>
    private bool IsDebounced => (DateTime.Now - _lastNavigateTime).TotalMilliseconds < NavigateDebounceMs;

    private void OnPathManualClicked(object? sender, RoutedEventArgs e)
    {
        if (IsDebounced) return;
        _lastNavigateTime = DateTime.Now;
        _chosenPath = "manual";
        UpdatePathSelection();
        NavigateTo(4);
    }

    private void OnPathRecommendedClicked(object? sender, RoutedEventArgs e)
    {
        if (IsDebounced) return;
        _lastNavigateTime = DateTime.Now;
        _chosenPath = "recommended";
        UpdatePathSelection();
        NavigateTo(4);
    }

    private void OnPathOfflineClicked(object? sender, RoutedEventArgs e)
    {
        if (IsDebounced) return;
        _lastNavigateTime = DateTime.Now;
        _chosenPath = "offline";
        UpdatePathSelection();
        SaveWizardBasics();
        NavigateTo(5);
    }

    /// <summary>同步第 3 页四个配置方式卡片的选中态，便于用户退回时看到上次的选择。</summary>
    private void UpdatePathSelection()
    {
        SetSelectedState(PathRecommendedBtn, _chosenPath == "recommended");
        SetSelectedState(PathManualBtn, _chosenPath == "manual");
        SetSelectedState(PathImportBtn, _chosenPath == "import");
        SetSelectedState(PathOfflineBtn, _chosenPath == "offline");
    }

    private async void OnPathImportClicked(object? sender, RoutedEventArgs e)
    {
        if (IsDebounced) return;
        _lastNavigateTime = DateTime.Now;

        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "导入已有配置",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("JSON 文件") { Patterns = new[] { "*.json" } }
                }
            });

            if (files.Count == 0) return;

            await using var stream = await files[0].OpenReadAsync();
            using var reader = new System.IO.StreamReader(stream, System.Text.Encoding.UTF8);
            var json = await reader.ReadToEndAsync();

            var imported = System.Text.Json.JsonSerializer.Deserialize<AISettings>(json,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (imported == null)
            {
                StepLabel.Text = "配置文件为空或格式不正确。";
                return;
            }

            _settings = imported;
            _chosenPath = "import";
            UpdatePathSelection();
            SaveWizardBasics();

            // 同步到手动/推荐输入框，便于用户继续编辑
            ManualEndpointBox.Text = _settings.Endpoint;
            ManualModelBox.Text = _settings.Model;
            ManualKeyBox.Text = _settings.ApiKey;
            RecommendedEndpointBox.Text = _settings.Endpoint;
            RecommendedModelBox.Text = _settings.Model;
            RecommendedKeyBox.Text = _settings.ApiKey;

            NavigateTo(5);
        }
        catch (Exception ex)
        {
            Logger.Error($"欢迎向导导入配置失败: {ex.Message}");
            StepLabel.Text = $"导入失败: {ex.Message}";
        }
    }

    private void OnPrevClicked(object? sender, RoutedEventArgs e)
    {
        if (IsDebounced) return;
        _lastNavigateTime = DateTime.Now;
        if (_currentStep <= 1) return;
        if (_currentStep == 6)
        {
            NavigateTo(5, false);
            return;
        }
        if (_currentStep == 5 && (_chosenPath == "offline" || _chosenPath == "import"))
        {
            NavigateTo(3, false);
            return;
        }
        if (_currentStep == 5 && (_chosenPath == "manual" || _chosenPath == "recommended"))
        {
            NavigateTo(4, false);
            return;
        }
        if (_currentStep == 4)
        {
            NavigateTo(3, false);
            return;
        }
        NavigateTo(_currentStep - 1, false);
    }

    private void OnNextClicked(object? sender, RoutedEventArgs e)
    {
        if (IsDebounced) return;
        _lastNavigateTime = DateTime.Now;
        if (_currentStep == 3)
        {
            if (_chosenPath == "offline" || _chosenPath == "import")
            {
                SaveWizardBasics();
                NavigateTo(5);
            }
            else if (_chosenPath == "manual" || _chosenPath == "recommended")
            {
                NavigateTo(4);
            }
            else
            {
                StepLabel.Text = "请先选择一种配置方式";
            }
            return;
        }

        if (_currentStep == 4)
        {
            if (_chosenPath == "manual")
            {
                _settings.Endpoint = ManualEndpointBox.Text?.Trim() ?? "";
                _settings.ApiKey = ManualKeyBox.Text?.Trim() ?? "";
                _settings.Model = ManualModelBox.Text?.Trim() ?? "";
            }
            else if (_chosenPath == "recommended")
            {
                _settings.Endpoint = RecommendedEndpointBox.Text?.Trim() ?? "";
                _settings.ApiKey = RecommendedKeyBox.Text?.Trim() ?? "";
                _settings.Model = RecommendedModelBox.Text?.Trim() ?? "";
            }
            SaveWizardBasics();
            NavigateTo(5);
            return;
        }

        if (_currentStep == 5)
        {
            SaveWizardBasics();
            NavigateTo(6);
            return;
        }

        if (_currentStep == 6)
        {
            SavePluginAuthSettings();
            NavigateTo(7);
            return;
        }

        if (_currentStep == 7)
        {
            Close();
            return;
        }

        if (_currentStep < StepNames.Count)
            NavigateTo(_currentStep + 1);
    }

    private void OnSkipClicked(object? sender, RoutedEventArgs e)
    {
        if (IsDebounced) return;
        _lastNavigateTime = DateTime.Now;
        if (_currentStep == 3)
        {
            _chosenPath = "offline";
            SaveWizardBasics();
            NavigateTo(5);
        }
        else if (_currentStep == 4)
        {
            SaveWizardBasics();
            NavigateTo(5);
        }
        else if (_currentStep == 5)
        {
            SaveWizardBasics();
            NavigateTo(6);
        }
        else if (_currentStep == 6)
        {
            SavePluginAuthSettings();
            NavigateTo(7);
        }
    }

    private void OnCustomToggleClicked(object? sender, RoutedEventArgs e)
    {
        _customMode = !_customMode;
        CustomToggleBtn.Content = _customMode
            ? "- 已启用自定义，请手动填写地址和模型"
            : "+ 自定义（本地部署 / API 中转站）";

        if (_customMode)
        {
            _selectedPreset = null;
            PlatformListBox.SelectedIndex = -1;
            RecommendedEndpointBox.Text = "";
            RecommendedModelBox.Text = "";
            RecommendedTestResult.Text = "已切换到自定义模式。";
        }
        else
        {
            RecommendedTestResult.Text = "";
        }
    }

    private void OnOpenConsoleClicked(object? sender, RoutedEventArgs e)
    {
        var url = _selectedPreset?.ConsoleUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            RecommendedTestResult.Text = _customMode
                ? "自定义模式没有固定注册链接，请打开你的本地部署或中转站后台。"
                : "请先选择一个平台。";
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            RecommendedTestResult.Text = $"无法打开链接：{ex.Message}";
        }
    }

    // ---- 推荐平台列表 ----

    private void PopulatePlatformList()
    {
        if (!_platformListInitialized)
        {
            PlatformListBox.ItemsSource = ApiProviderPreset.All;
            PlatformListBox.SelectionChanged += OnPlatformSelected;
            _platformListInitialized = true;
        }

        if (_selectedPreset == null && !_customMode)
            PlatformListBox.SelectedIndex = -1;
    }

    private void OnPlatformSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (PlatformListBox.SelectedItem is ApiProviderPreset preset)
        {
            _selectedPreset = preset;
            _customMode = false;
            CustomToggleBtn.Content = "+ 自定义（本地部署 / API 中转站）";
            RecommendedEndpointBox.Text = preset.Endpoint;
            RecommendedModelBox.Text = preset.Model;
            RecommendedTestResult.Text = "";
        }
    }

    // ---- API 连接测试 ----

    private async void OnManualTestClicked(object? sender, RoutedEventArgs e)
    {
        await RunTest(
            ManualEndpointBox.Text?.Trim() ?? "",
            ManualKeyBox.Text?.Trim() ?? "",
            ManualModelBox.Text?.Trim() ?? "",
            ManualTestResult,
            ManualTestBtn);
    }

    private async void OnManualReminderTestClicked(object? sender, RoutedEventArgs e)
    {
        await RunAiTest(
            ManualEndpointBox.Text?.Trim() ?? "",
            ManualKeyBox.Text?.Trim() ?? "",
            ManualModelBox.Text?.Trim() ?? "",
            "reminder",
            ManualTestResult,
            ManualReminderTestBtn, ManualTestBtn, ManualSummaryTestBtn);
    }

    private async void OnManualBeforeSchoolTestClicked(object? sender, RoutedEventArgs e)
    {
        await RunAiTest(
            ManualEndpointBox.Text?.Trim() ?? "",
            ManualKeyBox.Text?.Trim() ?? "",
            ManualModelBox.Text?.Trim() ?? "",
            "before-school",
            ManualTestResult,
            ManualBeforeSchoolTestBtn, ManualTestBtn, ManualReminderTestBtn, ManualSummaryTestBtn);
    }

    private async void OnManualSummaryTestClicked(object? sender, RoutedEventArgs e)
    {
        await RunAiTest(
            ManualEndpointBox.Text?.Trim() ?? "",
            ManualKeyBox.Text?.Trim() ?? "",
            ManualModelBox.Text?.Trim() ?? "",
            "summary",
            ManualTestResult,
            ManualSummaryTestBtn, ManualTestBtn, ManualReminderTestBtn);
    }

    private async void OnRecommendedTestClicked(object? sender, RoutedEventArgs e)
    {
        await RunTest(
            RecommendedEndpointBox.Text?.Trim() ?? "",
            RecommendedKeyBox.Text?.Trim() ?? "",
            RecommendedModelBox.Text?.Trim() ?? "",
            RecommendedTestResult,
            RecommendedTestBtn);
    }

    private async void OnRecommendedReminderTestClicked(object? sender, RoutedEventArgs e)
    {
        await RunAiTest(
            RecommendedEndpointBox.Text?.Trim() ?? "",
            RecommendedKeyBox.Text?.Trim() ?? "",
            RecommendedModelBox.Text?.Trim() ?? "",
            "reminder",
            RecommendedTestResult,
            RecommendedReminderTestBtn, RecommendedTestBtn, RecommendedSummaryTestBtn);
    }

    private async void OnRecommendedBeforeSchoolTestClicked(object? sender, RoutedEventArgs e)
    {
        await RunAiTest(
            RecommendedEndpointBox.Text?.Trim() ?? "",
            RecommendedKeyBox.Text?.Trim() ?? "",
            RecommendedModelBox.Text?.Trim() ?? "",
            "before-school",
            RecommendedTestResult,
            RecommendedBeforeSchoolTestBtn, RecommendedTestBtn, RecommendedReminderTestBtn, RecommendedSummaryTestBtn);
    }

    private async void OnRecommendedSummaryTestClicked(object? sender, RoutedEventArgs e)
    {
        await RunAiTest(
            RecommendedEndpointBox.Text?.Trim() ?? "",
            RecommendedKeyBox.Text?.Trim() ?? "",
            RecommendedModelBox.Text?.Trim() ?? "",
            "summary",
            RecommendedTestResult,
            RecommendedSummaryTestBtn, RecommendedTestBtn, RecommendedReminderTestBtn);
    }

    private static async System.Threading.Tasks.Task RunTest(
        string endpoint, string apiKey, string model,
        TextBlock resultText, Button? testButton)
    {
        if (testButton != null) testButton.IsEnabled = false;
        SetTestResultState(resultText);
        resultText.Text = "正在测试连接...";

        try
        {
            var result = await ApiConnectionTester.FullTestAsync(endpoint, apiKey, model);
            resultText.Text = result.Success ? $"✅ {result.Message}" : $"❌ {result.Message}";
            SetTestResultState(resultText, result.Success ? "success" : "error");
        }
        finally
        {
            if (testButton != null) testButton.IsEnabled = true;
        }
    }

    private static async System.Threading.Tasks.Task RunAiTest(
        string endpoint, string apiKey, string model,
        string testType,
        TextBlock resultText, Button? activeButton,
        params Button?[] siblingButtons)
    {
        if (activeButton != null) activeButton.IsEnabled = false;
        foreach (var b in siblingButtons)
            if (b != null) b.IsEnabled = false;

        resultText.Text = testType switch
        {
            "before-school" => "正在测试智能每日简报...",
            "reminder" => "正在测试课间贴心提醒...",
            _ => "正在测试放学贴心总结..."
        };
        SetTestResultState(resultText);

        try
        {
            // 先用临时配置跑一次测试调用
            var svc = Plugin.GetAIService();
            if (svc == null)
            {
                resultText.Text = "AI 服务未初始化，请先保存配置。";
                SetTestResultState(resultText, "error");
                return;
            }

            // 临时切换为向导中填写的配置进行测试
            var savedEndpoint = svc.Endpoint;
            var savedKey = svc.ApiKey;
            var savedModel = svc.Model;
            try
            {
                svc.Endpoint = endpoint;
                svc.ApiKey = apiKey;
                svc.Model = model;

                if (testType is "before-school" or "reminder")
                {
                    var context = Plugin.SmartClassNotifierInstance == null
                        ? null
                        : await Plugin.SmartClassNotifierInstance.BuildThoughtfulContextAsync(
                            testType == "before-school" ? ThoughtfulScene.DailyBriefing : ThoughtfulScene.BreakStart);
                    var reminder = testType == "before-school"
                        ? await svc.GenerateDailyBriefing(new List<string> { "第1节：语文", "第2节：数学" }, throwOnError: true, context: context)
                        : await svc.GenerateBeforeClassReminder("数学", "英语", throwOnError: true, context: context);
                    resultText.Text = testType == "before-school"
                        ? $"✅ 智能每日简报测试成功！\n{reminder}"
                        : $"✅ 课间贴心提醒测试成功！\n{reminder}";
                    SetTestResultState(resultText, "success");
                }
                else
                {
                    var context = Plugin.SmartClassNotifierInstance == null
                        ? null
                        : await Plugin.SmartClassNotifierInstance.BuildThoughtfulContextAsync(ThoughtfulScene.AfterSchool);
                    var summary = await svc.GenerateDailySummary(
                        new List<string> { "语文", "数学", "英语", "物理", "体育", "化学" },
                        throwOnError: true,
                        context: context);
                    resultText.Text = $"✅ 放学贴心总结测试成功！\n{summary}";
                    SetTestResultState(resultText, "success");
                }
            }
            finally
            {
                svc.Endpoint = savedEndpoint;
                svc.ApiKey = savedKey;
                svc.Model = savedModel;
            }
        }
        catch (Exception ex)
        {
            resultText.Text = $"❌ 测试失败: {ex.Message}";
            SetTestResultState(resultText, "error");
        }
        finally
        {
            if (activeButton != null) activeButton.IsEnabled = true;
            foreach (var b in siblingButtons)
                if (b != null) b.IsEnabled = true;
        }
    }

    private static void SetTestResultState(TextBlock resultText, string? state = null)
    {
        resultText.Classes.Remove("success");
        resultText.Classes.Remove("error");
        if (!string.IsNullOrWhiteSpace(state))
            resultText.Classes.Add(state);
    }

    // ---- 保存与完成 ----

    private void SaveWizardBasics()
    {
        _settings.WizardCompleted = true;
        _settings.SetupMode = string.IsNullOrWhiteSpace(_chosenPath) ? "offline" : _chosenPath;
    }

    // ---- 偏好设置：语气风格 ----

    private void OnToneLivelyClicked(object? sender, RoutedEventArgs e)
    {
        _settings.ToneStyle = 0;
        UpdateToneSelection();
    }

    private void OnToneNormalClicked(object? sender, RoutedEventArgs e)
    {
        _settings.ToneStyle = 1;
        UpdateToneSelection();
    }

    private void OnToneSeriousClicked(object? sender, RoutedEventArgs e)
    {
        _settings.ToneStyle = 2;
        UpdateToneSelection();
    }

    private void UpdateToneSelection()
    {
        SetSelectedState(ToneLivelyBtn, _settings.ToneStyle == 0);
        SetSelectedState(ToneNormalBtn, _settings.ToneStyle == 1);
        SetSelectedState(ToneSeriousBtn, _settings.ToneStyle == 2);
    }

    // ---- 插件授权管理 ----

    private void UpdatePluginAuthSelection()
    {
        InitializeReminderSettings();
        if (_pluginListInitialized) return;
        _pluginListInitialized = true;
        DetectAvailablePlugins();
    }

    private void InitializeReminderSettings()
    {
        if (_reminderSettingsInitialized) return;
        _reminderSettingsInitialized = true;
        _welcomeReminderSettings = Plugin.SmartClassNotifierInstance?.Settings ?? new SmartClassNotifierSettings();

        WizardEnableThoughtfulReminderCheckBox.IsChecked = _welcomeReminderSettings.EnableThoughtfulReminder;
        WizardBeforeSchoolCheckBox.IsChecked = _welcomeReminderSettings.EnableBeforeSchoolReminder;
        WizardBreakStartCheckBox.IsChecked = _welcomeReminderSettings.EnableBeforeClassReminder;
        WizardAfterSchoolCheckBox.IsChecked = _welcomeReminderSettings.EnableAfterSchoolSummary;
        WizardClassChangeCheckBox.IsChecked = _welcomeReminderSettings.EnableClassChangeAlert;
        WizardWeatherCheckBox.IsChecked = _welcomeReminderSettings.EnableWeatherReminder;
        WizardTemperatureCheckBox.IsChecked = _welcomeReminderSettings.EnableTemperatureReminder;
        WizardWeatherAlertCheckBox.IsChecked = _welcomeReminderSettings.EnableWeatherAlertReminder;
        WizardMusicCheckBox.IsChecked = _welcomeReminderSettings.EnableMusicReminder;
        WizardCustomReminderCheckBox.IsChecked = _welcomeReminderSettings.EnableCustomReminder;
        WizardHolidayCheckBox.IsChecked = _welcomeReminderSettings.EnableDailyBriefingHoliday;
        WizardNewsCheckBox.IsChecked = _welcomeReminderSettings.EnableDailyBriefingNews;
        EnablePluginIntegrationCheckBox.IsChecked = _welcomeReminderSettings.EnableExternalPluginIntegration ||
                                                     !_welcomeReminderSettings.PluginAuthorizationConfirmed;
    }

    private void DetectAvailablePlugins()
    {
        AvailablePluginsPanel.Children.Clear();
        var detectedPlugins = PluginIntegrationService.GetInstalledPlugins();

        if (detectedPlugins.Count == 0)
        {
            NoPluginsHint.IsVisible = true;
            EnablePluginIntegrationCheckBox.IsEnabled = false;
            EnablePluginIntegrationCheckBox.IsChecked = false;
            return;
        }

        NoPluginsHint.IsVisible = false;
        EnablePluginIntegrationCheckBox.IsEnabled = true;

        var savedIds = Plugin.SmartClassNotifierInstance?.Settings.AuthorizedPluginIds;
        foreach (var plugin in detectedPlugins)
        {
            var checkBox = new CheckBox
            {
                Content = new StackPanel
                {
                    Spacing = 2,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = plugin.Name,
                            FontSize = 13.5,
                            FontWeight = FontWeight.SemiBold,
                            Classes = { "plugin-auth-name" }
                        },
                        new TextBlock
                        {
                            Text = plugin.Description,
                            FontSize = 12,
                            Classes = { "plugin-auth-description" }
                        }
                    }
                },
                IsChecked = savedIds == null || savedIds.Count == 0 || savedIds.Contains(plugin.Id),
                Tag = plugin.Id
            };
            AvailablePluginsPanel.Children.Add(checkBox);
        }
    }

    private void SavePluginAuthSettings()
    {
        _welcomeReminderSettings.EnableThoughtfulReminder = WizardEnableThoughtfulReminderCheckBox.IsChecked == true;
        _welcomeReminderSettings.EnableBeforeSchoolReminder = WizardBeforeSchoolCheckBox.IsChecked == true;
        _welcomeReminderSettings.EnableBeforeClassReminder = WizardBreakStartCheckBox.IsChecked == true;
        _welcomeReminderSettings.EnableAfterSchoolSummary = WizardAfterSchoolCheckBox.IsChecked == true;
        _welcomeReminderSettings.EnableClassChangeAlert = WizardClassChangeCheckBox.IsChecked == true;
        _welcomeReminderSettings.EnableWeatherReminder = WizardWeatherCheckBox.IsChecked == true;
        _welcomeReminderSettings.EnableTemperatureReminder = WizardTemperatureCheckBox.IsChecked == true;
        _welcomeReminderSettings.EnableWeatherAlertReminder = WizardWeatherAlertCheckBox.IsChecked == true;
        _welcomeReminderSettings.EnableMusicReminder = WizardMusicCheckBox.IsChecked == true;
        _welcomeReminderSettings.EnableCustomReminder = WizardCustomReminderCheckBox.IsChecked == true;
        _welcomeReminderSettings.EnableDailyBriefingHoliday = WizardHolidayCheckBox.IsChecked == true;
        _welcomeReminderSettings.EnableDailyBriefingNews = WizardNewsCheckBox.IsChecked == true;
        Plugin.ApplyWelcomeReminderSettings(_welcomeReminderSettings);

        var selectedIds = AvailablePluginsPanel.Children
            .OfType<CheckBox>()
            .Where(checkBox => checkBox.IsChecked == true && checkBox.Tag is string)
            .Select(checkBox => (string)checkBox.Tag!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var enabled = EnablePluginIntegrationCheckBox.IsChecked == true && selectedIds.Count > 0;
        _welcomeReminderSettings.AuthorizedPluginIds = enabled
            ? selectedIds
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _welcomeReminderSettings.EnableExternalPluginIntegration = enabled;
        _welcomeReminderSettings.PluginAuthorizationConfirmed = AvailablePluginsPanel.Children.Count > 0;
        Plugin.ApplyExternalPluginAuthorization(
            enabled,
            enabled ? selectedIds : Array.Empty<string>(),
            confirmed: _welcomeReminderSettings.PluginAuthorizationConfirmed);
    }

    private static void SetSelectedState(Button button, bool isSelected)
    {
        if (isSelected)
            button.Classes.Add("selected");
        else
            button.Classes.Remove("selected");
    }

    // ---- 完成页 ----

    private void BuildCompletePage()
    {
        ChecklistPanel.Children.Clear();
        AddCheckItem("AI 连接", string.IsNullOrWhiteSpace(_settings.ApiKey) ? "未连接（离线模式）" : "已连接");
        AddCheckItem("接入方式", _settings.SetupMode switch
        {
            "manual" => "自己填写",
            "recommended" => _customMode ? "自定义接口" : _selectedPreset?.Name ?? "推荐平台",
            "import" => "导入已有配置",
            _ => "暂未接入"
        });
        if (!string.IsNullOrWhiteSpace(_settings.Endpoint))
            AddCheckItem("接口地址", _settings.Endpoint);
        if (!string.IsNullOrWhiteSpace(_settings.Model))
            AddCheckItem("使用模型", _settings.Model);
        AddCheckItem("说话语气", _settings.ToneStyle switch { 0 => "活泼", 1 => "标准", 2 => "严肃", _ => "标准" });

        var notifierSettings = Plugin.SmartClassNotifierInstance?.Settings ?? _welcomeReminderSettings;
        var pluginAuthStatus = notifierSettings.EnableExternalPluginIntegration
            ? $"已授权 {notifierSettings.AuthorizedPluginIds.Count} 个插件"
            : notifierSettings.PluginAuthorizationConfirmed ? "未启用" : "未检测到可授权插件";
        AddCheckItem("插件集成", pluginAuthStatus);

        CompleteSubtitle.Text = string.IsNullOrWhiteSpace(_settings.ApiKey)
            ? "当前为离线模式，将使用本地预设文案。接入 Key 后可启用 AI 功能。"
            : "从下一节课开始，它会在相应的时间自动提醒。无需额外操作。";

        if (!_completionNotified)
        {
            _completionNotified = true;
            WizardCompleted?.Invoke(_settings);
        }
    }

    private void AddCheckItem(string label, string value)
    {
        var labelText = new TextBlock
        {
            Text = label,
            FontSize = 13,
            [DockPanel.DockProperty] = Dock.Left,
            Width = 96
        };
        labelText.Classes.Add("check-label");

        var valueText = new TextBlock
        {
            Text = value,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };
        valueText.Classes.Add("check-value");

        ChecklistPanel.Children.Add(new DockPanel
        {
            Margin = new Thickness(0, 6),
            Children = { labelText, valueText }
        });
    }

    protected override void OnClosed(EventArgs e)
    {
        StopWelcomeAnimation();
        StopContentEntrance();
        StopCompletionAnimation();
        _transitionTimer?.Stop();
        _transitionTimer = null;
        foreach (var t in _buttonTimers.Values)
            t.Stop();
        _buttonTimers.Clear();
        base.OnClosed(e);
    }
}
