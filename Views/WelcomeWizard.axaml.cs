using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ClassIsland.AISmartClass.Models;
using ClassIsland.AISmartClass.Services;

namespace ClassIsland.AISmartClass.Views;

public partial class WelcomeWizard : Window
{
    private const string FullTitle = "AIIsland";

    private int _currentStep;
    private string _chosenPath = "";
    private ApiProviderPreset? _selectedPreset;
    private bool _customMode;
    private bool _platformListInitialized;
    private bool _completionNotified;
    private int _typingIndex;
    private DispatcherTimer? _typingTimer;
    private AISettings _settings = new();

    private static readonly List<string> StepNames = new() { "封面", "功能介绍", "选择方式", "配置 API", "偏好设置", "完成" };

    // 按钮动画：颜色瞬时切换（无 BrushTransition 避免闪烁），代码仅处理缩放
    private readonly List<Button> _animatedButtons = new();
    private readonly Dictionary<Button, ScaleTransform> _buttonTransforms = new();
    private readonly Dictionary<Button, DispatcherTimer> _buttonTimers = new();
    private const double PressScale = 0.98;

    public event Action<AISettings>? WizardCompleted;

    public WelcomeWizard()
    {
        InitializeComponent();
        BuildStepIndicator();
        NavigateTo(1);
        StartTypingAnimation();
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

    private void StartTypingAnimation()
    {
        _typingTimer?.Stop();
        _typingIndex = 0;
        TypingTitle.Text = "";

        _typingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(115) };
        _typingTimer.Tick += (_, _) =>
        {
            if (_typingIndex >= FullTitle.Length)
            {
                TypingTitle.Text = FullTitle;
                _typingTimer?.Stop();
                return;
            }

            _typingIndex++;
            TypingTitle.Text = FullTitle[.._typingIndex] + (_typingIndex < FullTitle.Length ? "_" : "");
        };
        _typingTimer.Start();
    }

    private void BuildStepIndicator()
    {
        var dotColor = new SolidColorBrush(Color.Parse("#30363D"));
        var dotTextColor = new SolidColorBrush(Color.Parse("#8B949E"));
        var activeColor = new SolidColorBrush(Color.Parse("#58A6FF"));

        for (var i = 0; i < StepNames.Count; i++)
        {
            var idx = i;
            var number = i + 1;

            if (i > 0)
            {
                StepIndicator.Children.Add(new Border
                {
                    Width = 24, Height = 1,
                    Background = new SolidColorBrush(Color.Parse("#30363D")),
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                });
            }

            var dot = new Border
            {
                Width = 24, Height = 24, CornerRadius = new CornerRadius(12),
                Background = Brushes.Transparent,
                BorderBrush = dotColor,
                BorderThickness = new Thickness(2),
                Child = new TextBlock
                {
                    Text = number.ToString(), FontSize = 12, FontWeight = FontWeight.SemiBold,
                    Foreground = dotTextColor,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                },
                Tag = number
            };
            ToolTip.SetTip(dot, StepNames[idx]);
            StepIndicator.Children.Add(dot);
        }
    }

    private void UpdateStepIndicator(int step)
    {
        var dotColor = new SolidColorBrush(Color.Parse("#30363D"));
        var dotTextColor = new SolidColorBrush(Color.Parse("#8B949E"));
        var accent = new SolidColorBrush(Color.Parse("#58A6FF"));

        foreach (var child in StepIndicator.Children)
        {
            if (child is not Border dot || dot.Tag is not int n) continue;
            var txt = dot.Child as TextBlock;
            if (n < step)
            {
                dot.Background = accent;
                dot.BorderBrush = accent;
                if (txt != null) txt.Foreground = Brushes.White;
            }
            else if (n == step)
            {
                dot.Background = Brushes.Transparent;
                dot.BorderBrush = accent;
                if (txt != null) txt.Foreground = accent;
            }
            else
            {
                dot.Background = Brushes.Transparent;
                dot.BorderBrush = dotColor;
                if (txt != null) txt.Foreground = dotTextColor;
            }
        }

        StepLabel.Text = $"第 {step} 步，共 {StepNames.Count} 步";
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
            6 => Page5,
            _ => null
        };
    }

    private void NavigateTo(int step, bool forward = true)
    {
        var oldPage = GetPage(_currentStep);
        var newPage = GetPage(step);

        _currentStep = step;
        UpdateStepIndicator(step);
        UpdateButtons();

        if (oldPage != null && newPage != null && oldPage != newPage)
            AnimatePageTransition(oldPage, newPage, forward);
        else if (newPage != null)
        {
            newPage.IsVisible = true;
            newPage.Opacity = 1;
            newPage.RenderTransform = null;
        }

        if (step == 1) StartTypingAnimation();
        if (step == 4 && _chosenPath == "recommended") PopulatePlatformList();
        if (step == 5) UpdateToneSelection();
        if (step == 6) BuildCompletePage();
    }

    private void AnimatePageTransition(Border oldPage, Border newPage, bool forward)
    {
        var startOffset = forward ? 50.0 : -50.0;
        newPage.IsVisible = true;
        newPage.Opacity = 0;
        newPage.RenderTransform = new TranslateTransform(startOffset, 0);

        var oldOpacity = oldPage.Opacity;
        oldPage.RenderTransform ??= new TranslateTransform(0, 0);

        var steps = 14;
        var interval = TimeSpan.FromMilliseconds(16);
        var current = 0;

        var timer = new DispatcherTimer { Interval = interval };

        timer.Tick += (_, _) =>
        {
            current++;
            var t = Math.Min(current / (double)steps, 1);
            var ease = 1 - Math.Pow(1 - t, 3);

            newPage.Opacity = ease;
            if (newPage.RenderTransform is TranslateTransform nt)
                nt.X = startOffset * (1 - ease);

            oldPage.Opacity = Math.Max(0, oldOpacity * (1 - t * 1.2));
            if (oldPage.RenderTransform is TranslateTransform ot)
                ot.X = -startOffset * t * 0.5;

            if (current >= steps)
            {
                timer.Stop();
                newPage.Opacity = 1;
                if (newPage.RenderTransform is TranslateTransform nf) nf.X = 0;
                oldPage.IsVisible = false;
                oldPage.Opacity = 0;
                oldPage.RenderTransform = null;
            }
        };

        timer.Start();
    }

    private void UpdateButtons()
    {
        var isLast = _currentStep == StepNames.Count;
        var isFirst = _currentStep == 1;
        var isApiStep = _currentStep == 4;

        PrevBtn.IsVisible = !isFirst;
        SkipBtn.IsVisible = isApiStep || _currentStep == 5;
        NextBtn.IsVisible = !(_currentStep == 3);
        NextBtn.Content = isLast ? "开始使用" : isApiStep ? "保存并继续" : _currentStep == 5 ? "保存并继续" : "下一步";
    }

    // ---- 事件 ----

    private void OnPathManualClicked(object? sender, RoutedEventArgs e)
    {
        _chosenPath = "manual";
        NavigateTo(4);
    }

    private void OnPathRecommendedClicked(object? sender, RoutedEventArgs e)
    {
        _chosenPath = "recommended";
        NavigateTo(4);
    }

    private void OnPathOfflineClicked(object? sender, RoutedEventArgs e)
    {
        _chosenPath = "offline";
        SaveWizardBasics();
        NavigateTo(5);
    }

    private void OnPrevClicked(object? sender, RoutedEventArgs e)
    {
        if (_currentStep <= 1) return;
        if (_currentStep == 6)
        {
            NavigateTo(5, false);
            return;
        }
        if (_currentStep == 5 && _chosenPath == "offline")
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
        if (_currentStep == 3)
        {
            if (_chosenPath == "offline")
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
            Close();
            return;
        }

        if (_currentStep < StepNames.Count)
            NavigateTo(_currentStep + 1);
    }

    private void OnSkipClicked(object? sender, RoutedEventArgs e)
    {
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
        resultText.Text = "正在测试连接...";
        resultText.Foreground = new SolidColorBrush(Color.Parse("#8B949E"));

        try
        {
            var result = await ApiConnectionTester.FullTestAsync(endpoint, apiKey, model);
            resultText.Text = result.Success ? $"✅ {result.Message}" : $"❌ {result.Message}";
            resultText.Foreground = new SolidColorBrush(
                result.Success ? Color.Parse("#3FB950") : Color.Parse("#F85149"));
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

        resultText.Text = testType == "reminder" ? "正在测试 AI 课前提醒..." : "正在测试 AI 每日总结...";
        resultText.Foreground = new SolidColorBrush(Color.Parse("#8B949E"));

        try
        {
            // 先用临时配置跑一次测试调用
            var svc = Plugin.GetAIService();
            if (svc == null)
            {
                resultText.Text = "AI 服务未初始化，请先保存配置。";
                resultText.Foreground = new SolidColorBrush(Color.Parse("#F85149"));
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

                if (testType == "reminder")
                {
                    var reminder = await svc.GenerateBeforeClassReminder("数学", "英语");
                    resultText.Text = $"✅ AI 提醒测试成功！\n{reminder}";
                    resultText.Foreground = new SolidColorBrush(Color.Parse("#3FB950"));
                }
                else
                {
                    var summary = await svc.GenerateDailySummary(
                        new List<string> { "语文", "数学", "英语", "物理", "体育", "化学" });
                    resultText.Text = $"✅ AI 总结测试成功！\n{summary}";
                    resultText.Foreground = new SolidColorBrush(Color.Parse("#3FB950"));
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
            resultText.Foreground = new SolidColorBrush(Color.Parse("#F85149"));
        }
        finally
        {
            if (activeButton != null) activeButton.IsEnabled = true;
            foreach (var b in siblingButtons)
                if (b != null) b.IsEnabled = true;
        }
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
        var selectedBorder = new SolidColorBrush(Color.Parse("#58A6FF"));
        var defaultBorder = new SolidColorBrush(Color.Parse("#3A414A"));
        var selectedBg = Color.Parse("#1C2F4A");
        var defaultBg = Color.Parse("#1C2128");

        ToneLivelyBtn.BorderBrush = _settings.ToneStyle == 0 ? selectedBorder : defaultBorder;
        ToneLivelyBtn.Background = _settings.ToneStyle == 0
            ? new SolidColorBrush(selectedBg) : new SolidColorBrush(defaultBg);
        ToneNormalBtn.BorderBrush = _settings.ToneStyle == 1 ? selectedBorder : defaultBorder;
        ToneNormalBtn.Background = _settings.ToneStyle == 1
            ? new SolidColorBrush(selectedBg) : new SolidColorBrush(defaultBg);
        ToneSeriousBtn.BorderBrush = _settings.ToneStyle == 2 ? selectedBorder : defaultBorder;
        ToneSeriousBtn.Background = _settings.ToneStyle == 2
            ? new SolidColorBrush(selectedBg) : new SolidColorBrush(defaultBg);
    }

    // ---- 完成页 ----

    private void BuildCompletePage()
    {
        ChecklistPanel.Children.Clear();
        AddCheckItem("API 状态", string.IsNullOrWhiteSpace(_settings.ApiKey) ? "离线体验模式" : "已配置");
        AddCheckItem("配置方式", _settings.SetupMode switch
        {
            "manual" => "手动填写",
            "recommended" => _customMode ? "自定义接口" : _selectedPreset?.Name ?? "推荐平台",
            _ => "先离线体验"
        });
        if (!string.IsNullOrWhiteSpace(_settings.Endpoint))
            AddCheckItem("API 地址", _settings.Endpoint);
        if (!string.IsNullOrWhiteSpace(_settings.Model))
            AddCheckItem("模型名称", _settings.Model);
        AddCheckItem("语气风格", _settings.ToneStyle switch { 0 => "活泼", 1 => "标准", 2 => "严肃", _ => "标准" });

        CompleteSubtitle.Text = string.IsNullOrWhiteSpace(_settings.ApiKey)
            ? "已进入离线体验模式，AI 功能随时可在设置中补配。"
            : "一切就绪，AIIsland 已经开始工作了。";

        if (!_completionNotified)
        {
            _completionNotified = true;
            WizardCompleted?.Invoke(_settings);
        }
    }

    private void AddCheckItem(string label, string value)
    {
        ChecklistPanel.Children.Add(new DockPanel
        {
            Margin = new Thickness(0, 6),
            Children =
            {
                new TextBlock { Text = label, FontSize = 15,
                    Foreground = new SolidColorBrush(Color.Parse("#8B949E")),
                    [DockPanel.DockProperty] = Dock.Left, Width = 110 },
                new TextBlock { Text = value, FontSize = 15, FontWeight = FontWeight.SemiBold,
                    Foreground = new SolidColorBrush(Color.Parse("#E6EDF3")),
                    TextWrapping = TextWrapping.Wrap }
            }
        });
    }

    protected override void OnClosed(EventArgs e)
    {
        _typingTimer?.Stop();
        foreach (var t in _buttonTimers.Values)
            t.Stop();
        _buttonTimers.Clear();
        base.OnClosed(e);
    }
}
