using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Platform.Storage;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using ClassIsland.AISmartClass.Controls;
using ClassIsland.AISmartClass.Models;
using ClassIsland.AISmartClass.Views;
using ClassIsland.AISmartClass.PublicApi;
using ClassIsland.AISmartClass.Services;
using ClassIsland.AISmartClass.Services.NotificationProviders;
using ClassIsland.AISmartClass.Services.WebSearch;
using FluentAvalonia.UI.Controls;

namespace ClassIsland.AISmartClass.Views.SettingsPages;

[SettingsPageInfo(
    "aisettings.aisettingspage",
    "AIIsland",
    "\ue006",
    "\ue006"
)]
public partial class AISettingsPage : SettingsPageBase
{
    private readonly string _configFolder;
    private AISettings _settings = new();

    // ===== XAML 控件引用 =====

    private TextBox? _endpointBox;
    private TextBox? _apiKeyBox;
    private TextBox? _modelBox;
    private Button? _fetchModelsButton;
    private ComboBox? _toneStyleComboBox;
    private NumericUpDown? _maxTokensBox;
    private NumericUpDown? _timeoutBox;
    private NumericUpDown? _cacheBox;
    private NumericUpDown? _maxRetriesBox;
    private NumericUpDown? _aiLogRetentionDaysBox;
    private ToggleSwitch? _deepThinkingSwitch;
    private ComboBox? _thinkingEffortComboBox;
    private ToggleSwitch? _webSearchSwitch;
    private ToggleSwitch? _webFetchSwitch;
    private ToggleSwitch? _deepSeekFallbackSwitch;
    private bool _suppressDeepSeekToggle;
    private NumericUpDown? _webSearchMaxResultsBox;
    private NumericUpDown? _webSearchTimeoutBox;
    private Button? _testWebSearchButton;
    private NumericUpDown? _customTemperatureBox;
    private Border? _visionSupportBadge;
    private TextBlock? _visionSupportText;
    private TextBlock? _visionSupportHint;
    private Button? _detectVisionButton;
    private Button? _testButton;
    private Button? _testBeforeSchoolButton;
    private Button? _testReminderButton;
    private Button? _testSummaryButton;
    private Button? _testPromptButton;
    private Button? _testContextButton;
    private ToastHostControl? _toast;
    private Button? _examModeButton;
    private TextBlock? _versionLabel;
    private OpenSourceLicensesWindow? _openSourceWindow;

    // 功能开关
    private ToggleSwitch? _enableCacheCb;
    private ToggleSwitch? _examSeriousToneCb;

    // 时间来源（全局）
    private ComboBox? _timeSourceComboBox;

    // 角色语气
    private ToggleSwitch? _personaEnabledSwitch;
    private ComboBox? _personaComboBox;
    private TextBox? _customPersonaBox;
    private const string CustomPersonaId = "__custom__";

    // 托盘菜单快捷操作复选框
    private CheckBox? _trayBeforeClassReminderCb;
    private CheckBox? _trayBeforeSchoolReminderCb;
    private CheckBox? _trayAfterSchoolSummaryCb;
    private CheckBox? _trayCustomRemindersCb;
    private CheckBox? _trayRegenerateHomeworkCb;
    private CheckBox? _trayExamModeCb;
    private CheckBox? _trayRegenerateSummaryCb;
    private CheckBox? _trayRegenerateHintCb;

    // 外部插件授权管理
    private ToggleSwitch? _defaultAuthTrustedSwitch;
    private ItemsControl? _pluginAuthList;
    private TextBlock? _noPluginsText;

    // 诊断与遥测
    private ToggleSwitch? _telemetryEnabledSwitch;
    private ToggleSwitch? _telemetryLogTailSwitch;

    // 按钮缩放动画（复用 WelcomeWizard 风格）
    private readonly List<Button> _animatedButtons = new();
    private readonly Dictionary<Button, ScaleTransform> _buttonTransforms = new();
    private readonly Dictionary<Button, DispatcherTimer> _buttonTimers = new();
    private const double PressScale = 0.98;

    private bool _controlsInitialized;
    private bool _applyingSettings;

    public AISettingsPage(AiIslandConfigPath configPath)
    {
        _configFolder = configPath.Value;
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        SettingsPageIconPatcher.PatchNavigationIcon(this);
        InitializeControls();
        LoadSettings();
        RegisterButtonAnimations(this);

        // 订阅外部设置变更事件（例如欢迎向导保存、其他入口修改），自动刷新 UI
        Plugin.AISettingsChanged += OnExternalSettingsChanged;
        Plugin.PluginAuthorizationsChanged += OnPluginAuthorizationsChanged;
    }

    /// <summary>递归注册所有 Button 的缩放动画（复用 WelcomeWizard 风格）</summary>
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
            btn.PointerCaptureLost += (_, _) => AnimateScale(btn, 1.0);
        }
        foreach (var child in root.GetVisualChildren())
            RegisterButtonAnimations(child);
    }

    private void AnimateScale(Button btn, double target)
    {
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
            t = t * t * (3 - 2 * t); // ease-in-out
            st.ScaleX = cur + (target - cur) * t;
            st.ScaleY = st.ScaleX;
            if (elapsed >= steps)
            {
                timer.Stop();
                _buttonTimers.Remove(btn);
            }
        };
        timer.Start();
    }

    #region 控件查找与事件绑定

    private void InitializeControls()
    {
        if (_controlsInitialized) return;
        _controlsInitialized = true;

        // ---- 文本 / 下拉 / 数字框 ----
        _endpointBox = this.FindControl<TextBox>("EndpointBox");
        _apiKeyBox = this.FindControl<TextBox>("ApiKeyBox");
        _modelBox = this.FindControl<TextBox>("ModelBox");
        _toneStyleComboBox = this.FindControl<ComboBox>("ToneStyleComboBox");
        _maxTokensBox = this.FindControl<NumericUpDown>("MaxTokensBox");
        _timeoutBox = this.FindControl<NumericUpDown>("TimeoutBox");
        _cacheBox = this.FindControl<NumericUpDown>("CacheBox");
        _maxRetriesBox = this.FindControl<NumericUpDown>("MaxRetriesBox");
        _aiLogRetentionDaysBox = this.FindControl<NumericUpDown>("AiLogRetentionDaysBox");
        _deepThinkingSwitch = this.FindControl<ToggleSwitch>("DeepThinkingSwitch");
        _thinkingEffortComboBox = this.FindControl<ComboBox>("ThinkingEffortComboBox");
        _webSearchSwitch = this.FindControl<ToggleSwitch>("WebSearchSwitch");
        _webFetchSwitch = this.FindControl<ToggleSwitch>("WebFetchSwitch");
        _deepSeekFallbackSwitch = this.FindControl<ToggleSwitch>("DeepSeekFallbackSwitch");
        _webSearchMaxResultsBox = this.FindControl<NumericUpDown>("WebSearchMaxResultsBox");
        _webSearchTimeoutBox = this.FindControl<NumericUpDown>("WebSearchTimeoutBox");
        _testWebSearchButton = this.FindControl<Button>("TestWebSearchButton");

        if (_webSearchSwitch != null)
        {
            _webSearchSwitch.IsCheckedChanged += (_, _) => AutoSaveSettings();
        }

        if (_webFetchSwitch != null)
        {
            _webFetchSwitch.IsCheckedChanged += (_, _) => AutoSaveSettings();
        }

        if (_deepSeekFallbackSwitch != null)
        {
            // 开启前必须确认费用。用隧道事件在控件切换状态之前拦截，
            // 避免「先变成开、弹窗取消后再设回关」——那样 Fluent 的过渡动画会残留，
            // 出现「文字是关、滑块停在开的位置」的错位。
            _deepSeekFallbackSwitch.AddHandler(
                InputElement.PointerPressedEvent,
                OnDeepSeekFallbackPointerPressed,
                RoutingStrategies.Tunnel);

            _deepSeekFallbackSwitch.IsCheckedChanged += (_, _) =>
            {
                if (_applyingSettings || _suppressDeepSeekToggle) return;
                AutoSaveSettings();
            };
        }

        if (_testWebSearchButton != null)
        {
            _testWebSearchButton.Click += async (_, _) =>
            {
                try
                {
                    await TestWebSearchAsync();
                }
                catch (Exception ex)
                {
                    // 事件处理器不能抛：按钮点击异常会直接冒到 UI 线程
                    Logger.Error(ex, "测试联网搜索失败");
                }
            };
        }
        if (_deepThinkingSwitch != null)
        {
            _deepThinkingSwitch.IsCheckedChanged += (_, _) =>
            {
                if (_applyingSettings) return;
                UpdateThinkingControls();
                AutoSaveSettings();
            };
        }

        if (_thinkingEffortComboBox != null)
        {
            _thinkingEffortComboBox.SelectionChanged += (_, _) =>
            {
                if (_applyingSettings) return;
                AutoSaveSettings();
            };
        }
        _customTemperatureBox = this.FindControl<NumericUpDown>("CustomTemperatureBox");

        // ---- 按钮 ----
        _testButton = WireButton("TestButton", OnTestClicked);
        _fetchModelsButton = WireButton("FetchModelsButton", OnFetchModelsClicked);
        _detectVisionButton = WireButton("DetectVisionButton", OnDetectVisionClicked);
        _visionSupportBadge = this.FindControl<Border>("VisionSupportBadge");
        _visionSupportText = this.FindControl<TextBlock>("VisionSupportText");
        _visionSupportHint = this.FindControl<TextBlock>("VisionSupportHint");
        _testBeforeSchoolButton = WireButton("TestBeforeSchoolButton", OnTestBeforeSchoolClicked);
        _testReminderButton = WireButton("TestReminderButton", OnTestReminderClicked);
        _testSummaryButton = WireButton("TestSummaryButton", OnTestSummaryClicked);
        _testPromptButton = WireButton("TestPromptButton", OnTestPromptClicked);
        _testContextButton = WireButton("TestContextButton", OnTestContextClicked);
        _examModeButton = WireButton("ExamModeButton", OnExamModeClicked);
        WireButton("SaveButton", OnSaveClicked);
        WireButton("GitHubBtn", OnGitHubClicked);
        WireButton("OpenSourceLicensesBtn", OnOpenSourceLicensesClicked);
        WireButton("IssuesBtn", OnIssuesClicked);
        WireButton("WelcomeWizardButton", OnWelcomeWizardClicked);
        WireButton("OpenAiLogsBtn", OnOpenAiLogsFolderClicked);
        WireButton("OpenPromptsBtn", OnOpenPromptsFolderClicked);
        WireButton("ImportConfigBtn", OnImportConfigClicked);
        WireButton("ExportConfigBtn", OnExportConfigClicked);
        WireButton("RestoreDefaultsBtn", OnRestoreDefaultsClicked);

        // ---- 功能开关 ----
        _enableCacheCb = this.FindControl<ToggleSwitch>("EnableCacheCb");
        if (_enableCacheCb != null) _enableCacheCb.IsCheckedChanged += (_, _) => AutoSaveSettings();

        _examSeriousToneCb = this.FindControl<ToggleSwitch>("ExamSeriousToneCb");
        if (_examSeriousToneCb != null) _examSeriousToneCb.IsCheckedChanged += (_, _) => AutoSaveSettings();

        // ---- 时间来源（全局，所有组件共用） ----
        _timeSourceComboBox = this.FindControl<ComboBox>("TimeSourceComboBox");
        if (_timeSourceComboBox != null)
        {
            _timeSourceComboBox.SelectionChanged += (_, _) =>
            {
                if (_applyingSettings) return;
                AutoSaveSettings();
            };
        }

        // ---- 角色语气 ----
        _personaEnabledSwitch = this.FindControl<ToggleSwitch>("PersonaEnabledSwitch");
        if (_personaEnabledSwitch != null)
            _personaEnabledSwitch.IsCheckedChanged += (_, _) => { UpdatePersonaControls(); AutoSaveSettings(); };

        _personaComboBox = this.FindControl<ComboBox>("PersonaComboBox");
        if (_personaComboBox != null)
        {
            _personaComboBox.Items.Clear();
            foreach (var preset in PersonaCatalog.BuiltInPersonas)
                _personaComboBox.Items.Add(new ComboBoxItem { Tag = preset.Id, Content = preset.Name });
            _personaComboBox.Items.Add(new ComboBoxItem { Tag = CustomPersonaId, Content = "自定义角色" });
            _personaComboBox.SelectionChanged += (_, _) => { UpdatePersonaControls(); AutoSaveSettings(); };
        }

        _customPersonaBox = this.FindControl<TextBox>("CustomPersonaBox");
        WireTextBoxAutoSave(_customPersonaBox);

        // 托盘菜单快捷操作复选框
        _trayBeforeSchoolReminderCb = this.FindControl<CheckBox>("TrayBeforeSchoolReminderCb");
        if (_trayBeforeSchoolReminderCb != null)
            _trayBeforeSchoolReminderCb.IsCheckedChanged += (_, _) => AutoSaveSettings();

        _trayBeforeClassReminderCb = this.FindControl<CheckBox>("TrayBeforeClassReminderCb");
        if (_trayBeforeClassReminderCb != null)
        {
            _trayBeforeClassReminderCb.IsCheckedChanged += (_, _) => AutoSaveSettings();
        }

        _trayAfterSchoolSummaryCb = this.FindControl<CheckBox>("TrayAfterSchoolSummaryCb");
        if (_trayAfterSchoolSummaryCb != null)
        {
            _trayAfterSchoolSummaryCb.IsCheckedChanged += (_, _) => AutoSaveSettings();
        }

        _trayCustomRemindersCb = this.FindControl<CheckBox>("TrayCustomRemindersCb");
        if (_trayCustomRemindersCb != null)
        {
            _trayCustomRemindersCb.IsCheckedChanged += (_, _) => AutoSaveSettings();
        }

        _trayRegenerateHomeworkCb = this.FindControl<CheckBox>("TrayRegenerateHomeworkCb");
        if (_trayRegenerateHomeworkCb != null)
        {
            _trayRegenerateHomeworkCb.IsCheckedChanged += (_, _) => AutoSaveSettings();
        }

        _trayExamModeCb = this.FindControl<CheckBox>("TrayExamModeCb");
        if (_trayExamModeCb != null)
        {
            _trayExamModeCb.IsCheckedChanged += (_, _) => AutoSaveSettings();
        }

        _trayRegenerateSummaryCb = this.FindControl<CheckBox>("TrayRegenerateSummaryCb");
        if (_trayRegenerateSummaryCb != null)
        {
            _trayRegenerateSummaryCb.IsCheckedChanged += (_, _) => AutoSaveSettings();
        }

        _trayRegenerateHintCb = this.FindControl<CheckBox>("TrayRegenerateHintCb");
        if (_trayRegenerateHintCb != null)
        {
            _trayRegenerateHintCb.IsCheckedChanged += (_, _) => AutoSaveSettings();
        }

        // ---- 文本 / 数字 / 语气风格（自动保存） ----
        WireTextBoxAutoSave(_endpointBox);
        WireTextBoxAutoSave(_apiKeyBox);
        WireTextBoxAutoSave(_modelBox);
        WireNumericAutoSave(_maxTokensBox);
        WireNumericAutoSave(_timeoutBox);
        WireNumericAutoSave(_cacheBox);
        WireNumericAutoSave(_maxRetriesBox);
        WireNumericAutoSave(_aiLogRetentionDaysBox);
        WireNumericAutoSave(_customTemperatureBox);
        WireNumericAutoSave(_webSearchMaxResultsBox);
        WireNumericAutoSave(_webSearchTimeoutBox);

        if (_toneStyleComboBox != null)
            _toneStyleComboBox.SelectionChanged += (_, _) => AutoSaveSettings();

        // ---- Toast 浮层 ----
        _toast = this.FindControl<ToastHostControl>("Toast");

        // ---- 版本号 ----
        _versionLabel = this.FindControl<TextBlock>("VersionLabel");
        if (_versionLabel != null)
            _versionLabel.Text = $"v{ReadManifestVersion()}";

        // ---- 外部插件授权管理 ----
        _defaultAuthTrustedSwitch = this.FindControl<ToggleSwitch>("DefaultAuthTrustedSwitch");
        if (_defaultAuthTrustedSwitch != null)
            _defaultAuthTrustedSwitch.IsCheckedChanged += (_, _) => AutoSaveSettings();

        _pluginAuthList = this.FindControl<ItemsControl>("PluginAuthList");
        _noPluginsText = this.FindControl<TextBlock>("NoPluginsText");
        RefreshPluginAuthList();

        // ---- 诊断与遥测 ----
        _telemetryEnabledSwitch = this.FindControl<ToggleSwitch>("TelemetryEnabledSwitch");
        if (_telemetryEnabledSwitch != null)
            _telemetryEnabledSwitch.IsCheckedChanged += (_, _) => AutoSaveSettings();

        _telemetryLogTailSwitch = this.FindControl<ToggleSwitch>("TelemetryLogTailSwitch");
        if (_telemetryLogTailSwitch != null)
            _telemetryLogTailSwitch.IsCheckedChanged += (_, _) => AutoSaveSettings();

        // ---- 图片识别能力：模型或地址一变，上次的检测结论就失效，需要立刻重新显示 ----
        if (_endpointBox != null) _endpointBox.TextChanged += (_, _) => UpdateVisionSupportDisplay();
        if (_modelBox != null) _modelBox.TextChanged += (_, _) => UpdateVisionSupportDisplay();
        UpdateVisionSupportDisplay();
    }

    /// <summary>按当前接口地址与模型刷新图片识别徽章。未检测时退回按模型名称推断。</summary>
    private void UpdateVisionSupportDisplay()
    {
        var endpoint = _endpointBox?.Text?.Trim() ?? _settings.Endpoint;
        var model = _modelBox?.Text?.Trim() ?? _settings.Model;
        var info = VisionCapabilityService.Evaluate(
            endpoint, model, _settings.VisionSupport, _settings.VisionSupportSignature);

        VisionBadgeHelper.Apply(_visionSupportBadge, _visionSupportText, info);
        if (_visionSupportHint != null)
            _visionSupportHint.Text = VisionCapabilityService.DescribeSupportDetail(info);
    }

    /// <summary>发送一张测试图片，真实确认当前模型能否识别图片，并把结论写入配置。</summary>
    private async void OnDetectVisionClicked(object? sender, RoutedEventArgs e)
    {
        if (_detectVisionButton == null) return;

        var endpoint = _endpointBox?.Text?.Trim() ?? "";
        var apiKey = _apiKeyBox?.Text?.Trim() ?? "";
        var model = _modelBox?.Text?.Trim() ?? "";

        var validation = ApiConnectionTester.ValidateFields(endpoint, apiKey, model);
        if (!validation.Success)
        {
            ShowToast(validation.Message, "error");
            return;
        }

        // 先把当前填写内容写入配置，保证检测结论绑定的是即将保存的模型
        AutoSaveSettings();

        var originalContent = _detectVisionButton.Content;
        BusyContent.Begin(_detectVisionButton);
        ShowToast("正在检测图片识别能力...", durationMs: 0);

        try
        {
            var result = await VisionCapabilityService.DetectAsync(endpoint, apiKey, model);
            // 只有形成结论才落盘；网络错误、超时等「没测成」不影响上次结论
            if (result.IsConclusion)
            {
                VisionCapabilityService.ApplyDetection(_settings, result.Support, endpoint, model);
                SaveSettingsFile();
                Plugin.SyncAISettings(_settings);
            }

            UpdateVisionSupportDisplay();
            ShowToast(result.Message, result.Support switch
            {
                VisionSupport.Supported => "success",
                VisionSupport.Unsupported => "error",
                _ => ""
            });
        }
        catch (Exception ex)
        {
            Logger.Error($"检测图片识别能力失败: {ex.Message}");
            ShowToast($"检测失败：{ex.Message}", "error");
        }
        finally
        {
            BusyContent.End(_detectVisionButton, originalContent);
        }
    }

    /// <summary>刷新已授权插件列表的显示</summary>
    private void RefreshPluginAuthList()
    {
        if (_pluginAuthList != null)
        {
            // 授权项不是可观察对象，重建 ItemsSource 才能立即刷新模式和调用统计。
            _pluginAuthList.ItemsSource = null;
            _pluginAuthList.ItemsSource = _settings.PluginAuthEntries.ToList();
        }
        if (_noPluginsText != null)
            _noPluginsText.IsVisible = _settings.PluginAuthEntries.Count == 0;
    }

    /// <summary>
    /// 在开关切换前拦截「开启」动作：先校验地址、再弹费用确认，用户同意后才把开关设为开。
    /// 取消时开关从未改变过状态，不会留下「文字关了、滑块还在开的位置」的动画残影。
    /// </summary>
    private async void OnDeepSeekFallbackPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_applyingSettings || _suppressDeepSeekToggle) return;

        // 已经是开启状态：交给控件自己处理（直接关闭，无需确认费用）
        if (_deepSeekFallbackSwitch?.IsChecked == true) return;

        e.Handled = true;

        try
        {
            var endpoint = _endpointBox?.Text?.Trim() ?? _settings.Endpoint;
            if (!DeepSeekSearchProvider.TryBuildSearchUri(endpoint, out _))
            {
                ShowToast("当前 API 地址不是 DeepSeek 官方地址（api.deepseek.com），无法使用官方联网兜底。", "error");
                return;
            }

            if (!await ShowDeepSeekSearchConsentAsync()) return;

            _suppressDeepSeekToggle = true;
            try
            {
                if (_deepSeekFallbackSwitch != null) _deepSeekFallbackSwitch.IsChecked = true;
            }
            finally
            {
                _suppressDeepSeekToggle = false;
            }

            AutoSaveSettings();
        }
        catch (Exception ex)
        {
            // 事件处理器不能抛：异常会直接冒到 UI 线程
            Logger.Error(ex, "启用 DeepSeek 官方联网兜底失败");
        }
    }

    /// <summary>
    /// 启用前的费用告知：说明会使用用户自己的账号，并列出官方定价、免计费情形与参考用量。
    /// 对话框异常时按「不启用」处理，避免用户在不知情的情况下产生费用。
    /// </summary>
    private async Task<bool> ShowDeepSeekSearchConsentAsync()
    {
        try
        {
            var content = new StackPanel { Spacing = 12, MaxWidth = 520 };

            content.Children.Add(new TextBlock
            {
                Text = "这个开关只在你开启联网搜索，并且 OpenSERP 没有有效结果、或网页抓不开时才会触发。",
                TextWrapping = TextWrapping.Wrap
            });

            content.Children.Add(new TextBlock
            {
                Text = "触发的搜索或抓取会使用你当前填写的 DeepSeek API Key，从你自己的账号余额中扣费。",
                TextWrapping = TextWrapping.Wrap,
                FontWeight = FontWeight.SemiBold
            });

            content.Children.Add(BuildDeepSeekCostTable());

            content.Children.Add(new TextBlock
            {
                Text = "正常的搜索与抓取都在自建服务或本地完成，不会产生这笔费用；" +
                       "关闭本开关后也不再发起任何 DeepSeek 请求。价格以 DeepSeek 官方最新公布为准。",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.75,
                FontSize = 12
            });

            var dialog = new ContentDialog
            {
                Title = "启用 DeepSeek 官方联网兜底？",
                Content = content,
                PrimaryButtonText = "同意并启用",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close
            };

            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "显示 DeepSeek 联网兜底费用确认对话框失败");
            return false;
        }
    }

    /// <summary>费用明细：两列四行，列出官方单价、免计费情形与常见用量换算。</summary>
    private static Control BuildDeepSeekCostTable()
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto"),
            ColumnSpacing = 18,
            RowSpacing = 8
        };

        void AddRow(int row, string label, string value, bool emphasize = false)
        {
            var labelBlock = new TextBlock
            {
                Text = label,
                FontSize = 12,
                Opacity = 0.75
            };
            var valueBlock = new TextBlock
            {
                Text = value,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                FontWeight = emphasize ? FontWeight.SemiBold : FontWeight.Normal
            };

            Grid.SetRow(labelBlock, row);
            Grid.SetColumn(labelBlock, 0);
            Grid.SetRow(valueBlock, row);
            Grid.SetColumn(valueBlock, 1);
            grid.Children.Add(labelBlock);
            grid.Children.Add(valueBlock);
        }

        AddRow(0, "每次成功请求", "约 0.005 元（搜索与抓取同价，官方 0.75 美元 / 千次）", emphasize: true);
        AddRow(1, "不计费", "空结果、无正文、请求失败");
        AddRow(2, "触发条件", "OpenSERP 无有效结果，或网页抓取失败时");
        AddRow(3, "参考用量", "每天 10 次 ≈ 每月 1.5 元；每天 30 次 ≈ 每月 4.5 元");

        return grid;
    }

    /// <summary>
    /// 设置页「测试搜索」：先测内置 OpenSERP 是否连通；若勾选了 DeepSeek 官方联网兜底，
    /// 再单独测一次 DeepSeek（该请求按次计费）。结果统一走轻提示，与页面其他操作保持一致。
    /// </summary>
    private async Task TestWebSearchAsync()
    {
        if (_testWebSearchButton == null) return;

        // 按钮进入忙碌态：变灰不可点 + 原生转圈
        var originalContent = _testWebSearchButton.Content;
        BusyContent.Begin(_testWebSearchButton);

        try
        {
            var service = Plugin.GetWebSearchService();
            if (service == null)
            {
                ShowToast("搜索服务尚未初始化，请先重启 ClassIsland。", "error", 6000);
                return;
            }

            var config = new WebSearchConfig(
                WebSearchConfig.DefaultEndpoint,
                _settings.SearchEngine,
                (int)(_webSearchMaxResultsBox?.Value ?? 5),
                (int)(_webSearchTimeoutBox?.Value ?? 8));

            var (openSerpOk, openSerpText) = await TestOpenSerpAsync(service, config);
            var allOk = openSerpOk;
            var parts = new List<string> { openSerpText };

            if (_deepSeekFallbackSwitch?.IsChecked == true)
            {
                var (deepSeekOk, deepSeekText) = await TestDeepSeekAsync(config);
                allOk &= deepSeekOk;
                parts.Add(deepSeekText);
            }

            ShowToast(string.Join("；", parts), allOk ? "success" : "error", 8000);
        }
        catch (OperationCanceledException)
        {
            ShowToast("检索超时，请检查搜索服务是否可达。", "error", 8000);
        }
        catch (Exception ex)
        {
            ShowToast($"检索出错：{ex.Message}", "error", 8000);
        }
        finally
        {
            BusyContent.End(_testWebSearchButton, originalContent);
        }
    }

    /// <summary>测试 OpenSERP 通道。返回「是否可用」与一句可直接展示的结论。</summary>
    private static async Task<(bool Ok, string Text)> TestOpenSerpAsync(
        WebSearchService service, WebSearchConfig config)
    {
        using var cts = new CancellationTokenSource(
            TimeSpan.FromSeconds(config.EffectiveTimeoutSeconds + 5));

        var outcome = await service.SearchWithConfigAsync("上海理工大学", config, cts.Token);

        if (!outcome.Success)
            return (false, $"OpenSERP 失败：{outcome.Error}");

        return outcome.Items.Count == 0
            ? (false, "OpenSERP 无结果")
            : (true, $"OpenSERP 正常（{outcome.Items.Count} 条）");
    }

    /// <summary>
    /// 测试 DeepSeek 官方搜索。只在用户勾选兜底时调用——该请求会从用户账号扣费（约 0.005 元/次）。
    /// </summary>
    private async Task<(bool Ok, string Text)> TestDeepSeekAsync(WebSearchConfig config)
    {
        var endpoint = _endpointBox?.Text?.Trim() ?? _settings.Endpoint;
        var apiKey = _apiKeyBox?.Text?.Trim() ?? _settings.ApiKey;

        if (!DeepSeekSearchProvider.TryBuildSearchUri(endpoint, out _))
            return (false, "DeepSeek 未测试：当前接口地址不是官方域名");

        if (string.IsNullOrWhiteSpace(apiKey))
            return (false, "DeepSeek 未测试：未填 API Key");

        using var cts = new CancellationTokenSource(
            TimeSpan.FromSeconds(config.EffectiveTimeoutSeconds + 5));

        var provider = new DeepSeekSearchProvider();
        var response = await provider.SearchAsync(
            "上海理工大学",
            config with
            {
                AiEndpoint = endpoint,
                AiApiKey = apiKey,
                EnableDeepSeekFallback = true
            },
            cts.Token);

        if (!response.Success)
            return (false, $"DeepSeek 失败：{response.Error}");

        return response.Items.Count == 0
            ? (false, "DeepSeek 无结果")
            : (true, $"DeepSeek 正常（{response.Items.Count} 条，约 0.005 元）");
    }

    /// <summary>深度思考关闭时，思考强度没有意义，置灰避免误解。</summary>
    private void UpdateThinkingControls()    {
        if (_thinkingEffortComboBox == null) return;
        _thinkingEffortComboBox.IsEnabled = _deepThinkingSwitch?.IsChecked == true;
    }

    /// <summary>根据开关与预设选择刷新角色语气控件的可用状态。</summary>
    private void UpdatePersonaControls()
    {
        var enabled = _personaEnabledSwitch?.IsChecked == true;
        var isCustom = (_personaComboBox?.SelectedItem as ComboBoxItem)?.Tag?.ToString() == CustomPersonaId;

        if (_personaComboBox != null)
        {
            _personaComboBox.IsEnabled = enabled;
            _personaComboBox.Opacity = enabled ? 1.0 : 0.5;
        }

        if (_customPersonaBox != null)
        {
            _customPersonaBox.IsEnabled = enabled && isCustom;
            _customPersonaBox.Opacity = _customPersonaBox.IsEnabled ? 1.0 : 0.5;
        }
    }

    /// <summary>按 Id 选中角色预设下拉项；未命中内置角色时落到「自定义角色」。</summary>
    private void SelectPersonaComboItem(string? id)
    {
        if (_personaComboBox == null) return;

        for (var i = 0; i < _personaComboBox.Items.Count; i++)
        {
            if (_personaComboBox.Items[i] is ComboBoxItem item &&
                string.Equals(item.Tag?.ToString(), id, StringComparison.OrdinalIgnoreCase))
            {
                _personaComboBox.SelectedIndex = i;
                return;
            }
        }

        // 未命中内置角色 → 落到最后一项「自定义角色」
        if (_personaComboBox.Items.Count > 0)
            _personaComboBox.SelectedIndex = _personaComboBox.Items.Count - 1;
    }

    /// <summary>按时间来源选中下拉项。</summary>
    private void SelectTimeSourceItem(BusinessTimeSource source)
    {
        if (_timeSourceComboBox == null) return;

        var tag = source.ToString();
        for (var i = 0; i < _timeSourceComboBox.Items.Count; i++)
        {
            if (_timeSourceComboBox.Items[i] is ComboBoxItem item &&
                string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
            {
                _timeSourceComboBox.SelectedIndex = i;
                return;
            }
        }

        _timeSourceComboBox.SelectedIndex = 0;
    }

    /// <summary>读取下拉框当前选中的时间来源。</summary>
    private BusinessTimeSource ReadSelectedTimeSource()
    {
        var tag = (_timeSourceComboBox?.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        return Enum.TryParse<BusinessTimeSource>(tag, ignoreCase: true, out var source)
            ? source
            : BusinessTimeSource.ClassIslandTime;
    }

    /// <summary>「AI 生成」按钮：打开角色设定生成向导，生成结果回填到自定义角色设定。</summary>
    private async void OnGeneratePersonaClicked(object? sender, RoutedEventArgs e)
    {
        AutoSaveSettings();

        if (this.VisualRoot is not Avalonia.Controls.Window owner)
        {
            ShowToast("无法打开生成向导：找不到宿主窗口。", "error");
            return;
        }

        var wizard = new PersonaWizardDialog(_customPersonaBox?.Text);
        await wizard.ShowDialog(owner);

        if (!wizard.Confirmed || string.IsNullOrWhiteSpace(wizard.Result))
            return;

        // 回填生成结果，并自动切到「自定义角色」使其立即生效。
        if (_customPersonaBox != null) _customPersonaBox.Text = wizard.Result;
        if (_personaEnabledSwitch != null) _personaEnabledSwitch.IsChecked = true;
        SelectPersonaComboItem("");
        UpdatePersonaControls();
        AutoSaveSettings();
    }

    /// <summary>「切换授权」按钮：在直接授权与每次确认之间切换</summary>
    private void OnTogglePluginAuthClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string pluginId) return;
        var authGuard = Plugin.GetAuthGuard();
        if (authGuard == null) return;

        var entry = _settings.PluginAuthEntries.FirstOrDefault(e => e.PluginId == pluginId);
        if (entry == null) return;

        if (entry.AuthMode == AIIslandAuthMode.Trusted)
        {
            authGuard.RevokeTrust(pluginId);
            Logger.Info($"[AuthUI] 插件 {pluginId} 切换为每次确认");
        }
        else
        {
            authGuard.Trust(pluginId, entry.PluginName);
            Logger.Info($"[AuthUI] 插件 {pluginId} 切换为直接授权");
        }

    }

    /// <summary>「撤销」按钮：完全移除插件的授权记录</summary>
    private void OnRevokePluginAuthClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string pluginId) return;
        var authGuard = Plugin.GetAuthGuard();
        if (authGuard == null) return;

        authGuard.RemoveEntry(pluginId);
        Logger.Info($"[AuthUI] 已撤销插件 {pluginId} 的授权记录");
    }

    private Button? WireButton(string name, EventHandler<RoutedEventArgs> handler)
    {
        var btn = this.FindControl<Button>(name);
        if (btn != null) btn.Click += handler;
        return btn;
    }

    private void WireTextBoxAutoSave(TextBox? textBox)
    {
        if (textBox != null)
            textBox.LostFocus += (_, _) => AutoSaveSettings();
    }

    private void WireNumericAutoSave(NumericUpDown? numericBox)
    {
        if (numericBox != null)
            numericBox.ValueChanged += (_, _) => AutoSaveSettings();
    }

    #endregion

    #region 设置加载与保存

    private void LoadSettings()
    {
        try
        {
            var sharedSettings = Plugin.GetAISettings();
            if (sharedSettings != null)
            {
                _settings = sharedSettings;
                ApplySettingsToControls();
                return;
            }

            var configPath = System.IO.Path.Combine(_configFolder, "aisettings.json");
            if (System.IO.File.Exists(configPath))
            {
                var json = System.IO.File.ReadAllText(configPath);
                _settings = System.Text.Json.JsonSerializer.Deserialize<AISettings>(json,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new AISettings();
                if (Plugin.MigrateAISettings(_settings, Plugin.IsLegacyTrayMenuSettings(json)))
                {
                    SaveSettingsFile();
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Info($"加载设置失败: {ex.Message}");
            _settings = new AISettings();
        }
        ApplySettingsToControls();
    }

    private void ApplySettingsToControls()
    {
        _applyingSettings = true;
        try
        {
            ApplySettingsToControlsCore();
        }
        finally
        {
            _applyingSettings = false;
        }
    }

    private void ApplySettingsToControlsCore()
    {
        if (_endpointBox != null) _endpointBox.Text = _settings.Endpoint;
        if (_apiKeyBox != null) _apiKeyBox.Text = _settings.ApiKey;
        if (_modelBox != null) _modelBox.Text = _settings.Model;
        if (_toneStyleComboBox != null) _toneStyleComboBox.SelectedIndex = _settings.ToneStyle;
        if (_maxTokensBox != null) _maxTokensBox.Value = _settings.MaxTokens;
        if (_timeoutBox != null) _timeoutBox.Value = _settings.TimeoutSeconds;
        if (_cacheBox != null) _cacheBox.Value = _settings.CacheMinutes;
        if (_maxRetriesBox != null) _maxRetriesBox.Value = _settings.MaxRetries;
        if (_aiLogRetentionDaysBox != null) _aiLogRetentionDaysBox.Value = _settings.AiLogRetentionDays;
        if (_customTemperatureBox != null) _customTemperatureBox.Value = (decimal)(_settings.CustomTemperature ?? 1.0);

        // 联网搜索（检索地址为内置值，不再由设置页改写）
        if (_webSearchSwitch != null) _webSearchSwitch.IsChecked = _settings.EnableWebSearch;
        if (_webFetchSwitch != null) _webFetchSwitch.IsChecked = _settings.EnableWebFetch;

        // DeepSeek 官方接口只能用官方地址的 Key：配置里残留的「开」在换模型后不会生效，
        // 加载时直接归零，避免界面上显示一个假的开启状态。
        var deepSeekFallbackEnabled = _settings.EnableDeepSeekSearchFallback &&
                                      DeepSeekSearchProvider.TryBuildSearchUri(_settings.Endpoint, out _);
        _settings.EnableDeepSeekSearchFallback = deepSeekFallbackEnabled;
        if (_deepSeekFallbackSwitch != null) _deepSeekFallbackSwitch.IsChecked = deepSeekFallbackEnabled;
        if (_webSearchMaxResultsBox != null) _webSearchMaxResultsBox.Value = _settings.WebSearchMaxResults;
        if (_webSearchTimeoutBox != null) _webSearchTimeoutBox.Value = _settings.WebSearchTimeoutSeconds;

        // 深度思考与思考强度
        if (_deepThinkingSwitch != null) _deepThinkingSwitch.IsChecked = _settings.EnableDeepThinking;
        if (_thinkingEffortComboBox != null) _thinkingEffortComboBox.SelectedIndex = (int)_settings.ThinkingEffort;
        UpdateThinkingControls();

        // 角色语气
        if (_personaEnabledSwitch != null) _personaEnabledSwitch.IsChecked = _settings.PersonaEnabled;
        if (_personaComboBox != null) SelectPersonaComboItem(_settings.PersonaPresetId);
        if (_customPersonaBox != null) _customPersonaBox.Text = _settings.CustomPersona;
        UpdatePersonaControls();

        // 功能开关
        if (_enableCacheCb != null) _enableCacheCb.IsChecked = _settings.EnableApiCache;
        if (_examSeriousToneCb != null) _examSeriousToneCb.IsChecked = _settings.UseSeriousToneInExamMode;

        // 时间来源
        SelectTimeSourceItem(_settings.TimeSource);

        // 托盘菜单快捷操作开关
        if (_trayBeforeSchoolReminderCb != null) _trayBeforeSchoolReminderCb.IsChecked = _settings.TrayShowBeforeSchoolReminder;
        if (_trayBeforeClassReminderCb != null) _trayBeforeClassReminderCb.IsChecked = _settings.TrayShowBeforeClassReminder;
        if (_trayAfterSchoolSummaryCb != null) _trayAfterSchoolSummaryCb.IsChecked = _settings.TrayShowAfterSchoolSummary;
        if (_trayRegenerateHomeworkCb != null) _trayRegenerateHomeworkCb.IsChecked = _settings.TrayShowRegenerateHomework;
        if (_trayExamModeCb != null) _trayExamModeCb.IsChecked = _settings.TrayShowExamMode;
        if (_trayRegenerateSummaryCb != null) _trayRegenerateSummaryCb.IsChecked = _settings.TrayShowRegenerateSummary;
        if (_trayRegenerateHintCb != null) _trayRegenerateHintCb.IsChecked = _settings.TrayShowRegenerateHint;
        if (_trayCustomRemindersCb != null) _trayCustomRemindersCb.IsChecked = _settings.TrayShowCustomReminders;

        // 外部插件授权管理
        if (_defaultAuthTrustedSwitch != null) _defaultAuthTrustedSwitch.IsChecked = _settings.DefaultAuthMode == 1;
        RefreshPluginAuthList();

        // 诊断与遥测
        _settings.Telemetry ??= new TelemetrySettings();
        if (_telemetryEnabledSwitch != null) _telemetryEnabledSwitch.IsChecked = _settings.Telemetry.Enabled;
        if (_telemetryLogTailSwitch != null) _telemetryLogTailSwitch.IsChecked = _settings.Telemetry.IncludeLogTail;

        // 考试模式状态恢复：如果服务器正在运行，刷新按钮文案并锁定语气下拉
        RefreshExamModeState();

        // 图片识别能力徽章跟随最新的接口地址与模型
        UpdateVisionSupportDisplay();
    }

    /// <summary>根据 ExamModeServer 运行状态刷新 UI</summary>
    private void RefreshExamModeState()
    {
        try
        {
            var server = ExamModeServer.GetOrCreate();
            var isRunning = server.IsRunning;

            if (_examModeButton != null)
                _examModeButton.Content = isRunning ? "停止考试模式" : "启动考试模式";

            if (_toneStyleComboBox != null && isRunning && _settings.UseSeriousToneInExamMode)
            {
                _toneStyleComboBox.SelectedIndex = 2;          // 视觉上切换为"严肃"
                _toneStyleComboBox.IsEnabled = false;          // 考试期间禁止修改语气
            }
            else if (_toneStyleComboBox != null)
            {
                _toneStyleComboBox.IsEnabled = true;
                _toneStyleComboBox.SelectedIndex = _settings.ToneStyle;
            }
        }
        catch { /* ExamModeServer 可能尚未创建，忽略 */ }
    }

    /// <summary>收集 UI 控件值 → 持久化到 JSON → 实时同步到 AIChatService</summary>
    private bool AutoSaveSettings()
    {
        // 正在把设置值写回控件时，禁止反向读取保存，避免递归和事件风暴
        if (_applyingSettings) return false;

        try
        {
            _settings.Endpoint = _endpointBox?.Text ?? "";
            _settings.ApiKey = _apiKeyBox?.Text ?? "";
            _settings.Model = _modelBox?.Text ?? "";

            // 考试模式下语气下拉框被锁定为"严肃"，此时不覆写用户偏好
            var examRunning = ExamModeServer.GetOrCreate().IsRunning;
            if (!examRunning || !_settings.UseSeriousToneInExamMode)
                _settings.ToneStyle = _toneStyleComboBox?.SelectedIndex ?? 1;

            _settings.MaxTokens = (int)(_maxTokensBox?.Value ?? 200);
            _settings.TimeoutSeconds = (int)(_timeoutBox?.Value ?? 10);
            _settings.CacheMinutes = (int)(_cacheBox?.Value ?? 5);
            _settings.MaxRetries = (int)(_maxRetriesBox?.Value ?? 1);
            _settings.EnableDeepThinking = _deepThinkingSwitch?.IsChecked ?? false;
            _settings.ThinkingEffort = (ThinkingEffort)Math.Clamp(
                _thinkingEffortComboBox?.SelectedIndex ?? 0,
                (int)ThinkingEffort.Auto,
                (int)ThinkingEffort.High);
            _settings.AiLogRetentionDays = (int)(_aiLogRetentionDaysBox?.Value ?? 30);

            // 联网搜索（检索地址为内置值，设置页不再改写）
            _settings.EnableWebSearch = _webSearchSwitch?.IsChecked ?? false;
            _settings.EnableWebFetch = _webFetchSwitch?.IsChecked ?? true;
            _settings.EnableDeepSeekSearchFallback = _deepSeekFallbackSwitch?.IsChecked ?? false;
            _settings.WebSearchMaxResults = (int)(_webSearchMaxResultsBox?.Value ?? 5);
            _settings.WebSearchTimeoutSeconds = (int)(_webSearchTimeoutBox?.Value ?? 8);

            // 官方搜索只能用 DeepSeek 官方地址的 Key。切换到其他模型/接口后自动关闭，
            // 否则界面上会留下一个永远不会生效的「开」，还容易让人以为它在计费。
            var deepSeekFallbackDisabled = false;
            if (_settings.EnableDeepSeekSearchFallback &&
                !DeepSeekSearchProvider.TryBuildSearchUri(_settings.Endpoint, out _))
            {
                _settings.EnableDeepSeekSearchFallback = false;
                _suppressDeepSeekToggle = true;
                try
                {
                    if (_deepSeekFallbackSwitch != null) _deepSeekFallbackSwitch.IsChecked = false;
                }
                finally
                {
                    _suppressDeepSeekToggle = false;
                }

                deepSeekFallbackDisabled = true;
            }

            // 自定义温度：如果用户设置为默认值1.0，则视为未自定义，使用语气风格映射
            var tempValue = _customTemperatureBox?.Value;
            _settings.CustomTemperature = (tempValue.HasValue && Math.Abs(tempValue.Value - 1.0m) > 0.01m) ? (double)tempValue.Value : null;

            // 角色语气
            _settings.PersonaEnabled = _personaEnabledSwitch?.IsChecked ?? false;
            var personaTag = (_personaComboBox?.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            _settings.PersonaPresetId = (personaTag == CustomPersonaId) ? "" : (personaTag ?? "");
            _settings.CustomPersona = _customPersonaBox?.Text ?? "";

            // 功能开关
            _settings.EnableApiCache = _enableCacheCb?.IsChecked ?? true;
            _settings.UseSeriousToneInExamMode = _examSeriousToneCb?.IsChecked ?? true;

            // 时间来源
            if (_timeSourceComboBox != null) _settings.TimeSource = ReadSelectedTimeSource();

            // 托盘菜单快捷操作开关
            _settings.TrayShowBeforeSchoolReminder = _trayBeforeSchoolReminderCb?.IsChecked ?? false;
            _settings.TrayShowBeforeClassReminder = _trayBeforeClassReminderCb?.IsChecked ?? false;
            _settings.TrayShowAfterSchoolSummary = _trayAfterSchoolSummaryCb?.IsChecked ?? false;
            _settings.TrayShowRegenerateHomework = _trayRegenerateHomeworkCb?.IsChecked ?? false;
            _settings.TrayShowExamMode = _trayExamModeCb?.IsChecked ?? true;
            _settings.TrayShowRegenerateSummary = _trayRegenerateSummaryCb?.IsChecked ?? true;
            _settings.TrayShowRegenerateHint = _trayRegenerateHintCb?.IsChecked ?? true;
            _settings.TrayShowCustomReminders = _trayCustomRemindersCb?.IsChecked ?? false;

            // 外部插件授权管理
            _settings.DefaultAuthMode = (_defaultAuthTrustedSwitch?.IsChecked ?? false) ? 1 : 0;

            // 诊断与遥测（服务器地址、端口与令牌为内置接入点，不再由设置页改写）
            _settings.Telemetry ??= new TelemetrySettings();
            _settings.Telemetry.Enabled = _telemetryEnabledSwitch?.IsChecked ?? true;
            _settings.Telemetry.IncludeLogTail = _telemetryLogTailSwitch?.IsChecked ?? true;

            SaveSettingsFile();
            Plugin.SyncAISettings(_settings);

            if (deepSeekFallbackDisabled)
                ShowToast("当前接口地址不是 DeepSeek 官方地址，「DeepSeek 官方联网兜底」已自动关闭。", "error");

            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"保存设置失败: {ex.Message}");
            return false;
        }
    }

    private void SaveSettingsFile()
    {
        Plugin.SaveAISettingsFile(_settings);
        var configPath = System.IO.Path.Combine(_configFolder, "aisettings.json");
        Logger.Info($"设置已保存到 {configPath}");
    }

    #endregion

    #region 按钮事件

    private void OnSaveClicked(object? sender, RoutedEventArgs e)
    {
        if (AutoSaveSettings())
            ShowToast("设置已保存", "success");
        else
            ShowToast("保存失败，请查看插件日志", "error");
    }

    private async void OnRestoreDefaultsClicked(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (!await ConfirmRestoreDefaultsAsync()) return;

            _settings = new AISettings();
            ApplySettingsToControls();
            AutoSaveSettings();
            ShowToast("已恢复默认设置并保存", "success");
        }
        catch (Exception ex)
        {
            Logger.Error($"恢复默认设置失败: {ex.Message}");
            ShowToast($"恢复默认失败：{ex.Message}", "error");
        }
    }

    /// <summary>恢复默认会清空 API Key 等全部 AI 配置，执行前先确认，避免误点后无从找回。</summary>
    private async Task<bool> ConfirmRestoreDefaultsAsync()
    {
        try
        {
            var dialog = new ContentDialog
            {
                Title = "恢复默认设置？",
                Content = new TextBlock
                {
                    Text = "会把 AIIsland 的全部配置恢复为初始状态：API 地址和 Key、模型、语气与角色、"
                         + "提醒内容、生成参数、托盘菜单显示项、插件授权和遥测选项都会重置，并立即保存。\n\n"
                         + "此操作无法撤销；作业清单等外部插件的数据不受影响。",
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 520
                },
                PrimaryButtonText = "恢复默认",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close
            };
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        catch (Exception ex)
        {
            Logger.Error($"显示恢复默认确认对话框失败: {ex.Message}");
            // 对话框打不开时按取消处理，绝不能静默清空配置
            return false;
        }
    }

    private async void OnExportConfigClicked(object? sender, RoutedEventArgs e)
    {
        var confirmed = await ShowApiKeyLeakWarningAsync();
        if (!confirmed) return;

        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "导出 API 配置",
                SuggestedFileName = "aiisland-api-config.json",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("JSON 文件") { Patterns = new[] { "*.json" } }
                }
            });

            if (file == null) return;

            var json = System.Text.Json.JsonSerializer.Serialize(_settings, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });

            await using var stream = await file.OpenWriteAsync();
            await using var writer = new System.IO.StreamWriter(stream, System.Text.Encoding.UTF8);
            await writer.WriteAsync(json);

            await ShowConfigResultDialogAsync(
                "导出配置",
                $"配置已成功导出。\n\n保存位置：{file.Path.LocalPath}");
        }
        catch (Exception ex)
        {
            Logger.Error($"导出配置失败: {ex.Message}");
            await ShowConfigResultDialogAsync(
                "导出配置失败",
                $"无法导出配置：{ex.Message}");
        }
    }

    private async void OnImportConfigClicked(object? sender, RoutedEventArgs e)
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "导入 API 配置",
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
                await ShowConfigResultDialogAsync(
                    "导入配置失败",
                    "配置文件为空或格式不正确。");
                return;
            }

            _settings = imported;
            // 导入的旧配置可能带着已下线的遥测接入点，先迁移再写回
            Plugin.MigrateAISettings(_settings, false);
            ApplySettingsToControls();
            AutoSaveSettings();
            await ShowConfigResultDialogAsync(
                "导入配置",
                "配置已成功导入并保存，新的设置已经生效。");
        }
        catch (Exception ex)
        {
            Logger.Error($"导入配置失败: {ex.Message}");
            await ShowConfigResultDialogAsync(
                "导入配置失败",
                $"无法导入配置：{ex.Message}");
        }
    }

    private async Task ShowConfigResultDialogAsync(string title, string message)
    {
        try
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = new TextBlock
                {
                    Text = message,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 520
                },
                CloseButtonText = "确定",
                DefaultButton = ContentDialogButton.Close
            };
            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            Logger.Error($"显示配置操作结果对话框失败: {ex.Message}");
        }
    }

    private async Task<bool> ShowApiKeyLeakWarningAsync()
    {
        try
        {
            var dialog = new ContentDialog
            {
                Title = "API Key 泄露风险",
                Content = "导出的配置文件包含你的 API Key。请妥善保管，不要上传到公共仓库、网盘或发送给他人，否则可能导致密钥泄露和额外费用。",
                PrimaryButtonText = "仍要导出",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close
            };
            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary;
        }
        catch (Exception ex)
        {
            Logger.Error($"显示导出确认对话框失败: {ex.Message}");
            // 对话框失败时默认允许导出，避免功能不可用
            return true;
        }
    }

    private async void OnTestClicked(object? sender, RoutedEventArgs e)
    {
        if (_testButton == null) return;
        var originalContent = _testButton.Content;
        BusyContent.Begin(_testButton);
        ShowToast("正在测试 API 连接...", durationMs: 0);

        try
        {
            var endpoint = _endpointBox?.Text ?? "";
            var apiKey = _apiKeyBox?.Text ?? "";
            var model = _modelBox?.Text ?? "";
            // 使用用户设置的自定义温度，如果没有设置则使用默认值1.0
            var temperature = _settings.CustomTemperature ?? 1.0;
            var result = await Services.ApiConnectionTester.FullTestAsync(endpoint, apiKey, model, temperature);
            ShowToast(result.Message, result.Success ? "success" : "error");
        }
        finally { BusyContent.End(_testButton, originalContent); }
    }

    private async void OnFetchModelsClicked(object? sender, RoutedEventArgs e)
    {
        var button = _fetchModelsButton;
        if (button == null) return;

        var endpoint = _endpointBox?.Text?.Trim() ?? "";
        var apiKey = _apiKeyBox?.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            ShowToast("请先填写 API 地址", "error");
            return;
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            ShowToast("请先填写 API Key", "error");
            return;
        }

        var originalContent = button.Content;
        BusyContent.Begin(button);
        ShowToast("正在读取模型列表...", durationMs: 0);

        try
        {
            var result = await ModelListFetcher.FetchAsync(endpoint, apiKey);
            if (!result.Success)
            {
                ShowToast(result.Error ?? "没有可用的模型", "error");
                return;
            }

            ShowToast($"获取到 {result.Models.Count} 个模型，请在列表中选择", "success");
            ShowModelPicker(result.Models, button);
        }
        catch (Exception ex)
        {
            Logger.Error($"[设置页] 获取模型列表失败: {ex}");
            ShowToast($"获取模型失败: {ex.Message}", "error");
        }
        finally
        {
            BusyContent.End(button, originalContent);
        }
    }

    /// <summary>在按钮下方弹出模型列表，选中后写入模型名称并自动保存。</summary>
    private void ShowModelPicker(IReadOnlyList<string> models, Button anchor)
    {
        var list = new ListBox
        {
            ItemsSource = models,
            MaxHeight = 300,
            MinWidth = 260
        };

        var panel = new StackPanel { Spacing = 8, Width = 300 };
        panel.Children.Add(new TextBlock
        {
            Text = $"共 {models.Count} 个模型，点击填入",
            Classes = { "row-description" }
        });
        panel.Children.Add(new ScrollViewer
        {
            Content = list,
            MaxHeight = 300
        });

        var flyout = new Avalonia.Controls.Flyout
        {
            Content = panel,
            Placement = PlacementMode.BottomEdgeAlignedRight
        };

        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is not string model) return;
            if (_modelBox != null) _modelBox.Text = model;
            AutoSaveSettings();
            flyout.Hide();
            ShowToast($"已选择模型：{model}", "success");
        };

        flyout.ShowAt(anchor);
    }

    private async void OnTestReminderClicked(object? sender, RoutedEventArgs e)
    {
        if (_testReminderButton == null) return;
        AutoSaveSettings();
        var originalContent = _testReminderButton.Content;
        BusyContent.Begin(_testReminderButton);
        ShowToast("正在调用 AI 生成课间贴心提醒...", durationMs: 0);

        try
        {
            var aiService = Plugin.GetAIService();
            if (aiService == null) { ShowToast("AI 服务未初始化，请先保存配置", "error"); return; }
            aiService.ClearCache();
            var context = Plugin.SmartClassNotifierInstance == null
                ? null
                : await Plugin.SmartClassNotifierInstance.BuildThoughtfulContextAsync(ThoughtfulScene.BreakStart);
            var result = await aiService.GenerateBeforeClassReminder(
                "数学", "英语", throwOnError: true, context: context);
            ShowToast($"课间贴心提醒测试成功！\n\n模拟：数学 → 英语\n\nAI 回复：{result}", "success");
        }
        catch (Exception ex) { ShowToast($"测试失败: {ex.Message}", "error"); }
        finally { BusyContent.End(_testReminderButton, originalContent); }
    }

    private async void OnTestBeforeSchoolClicked(object? sender, RoutedEventArgs e)
    {
        if (_testBeforeSchoolButton == null) return;
        AutoSaveSettings();
        var originalContent = _testBeforeSchoolButton.Content;
        BusyContent.Begin(_testBeforeSchoolButton);
        ShowToast("正在调用 AI 生成智能每日简报...", durationMs: 0);

        try
        {
            var aiService = Plugin.GetAIService();
            if (aiService == null) { ShowToast("AI 服务未初始化，请先保存配置", "error"); return; }
            aiService.ClearCache();
            var context = Plugin.SmartClassNotifierInstance == null
                ? null
                : await Plugin.SmartClassNotifierInstance.BuildThoughtfulContextAsync(ThoughtfulScene.DailyBriefing);
            var result = await aiService.GenerateDailyBriefing(
                new List<string> { "第1节 08:00-08:45：语文", "第2节 08:55-09:40：数学" },
                throwOnError: true, context: context);
            ShowToast($"智能每日简报测试成功！\n\nAI 回复：{result}", "success");
        }
        catch (Exception ex) { ShowToast($"测试失败: {ex.Message}", "error"); }
        finally { BusyContent.End(_testBeforeSchoolButton, originalContent); }
    }

    private async void OnTestPromptClicked(object? sender, RoutedEventArgs e)
    {
        if (_testPromptButton == null) return;
        AutoSaveSettings();
        var originalContent = _testPromptButton.Content;
        BusyContent.Begin(_testPromptButton);
        try
        {
            var aiService = Plugin.GetAIService();
            if (aiService == null)
            {
                ShowToast("AI 服务未初始化，请先保存配置", "error");
                return;
            }

            var context = Plugin.SmartClassNotifierInstance == null
                ? null
                : await Plugin.SmartClassNotifierInstance.BuildThoughtfulContextAsync(ThoughtfulScene.BreakStart);
            var (systemPrompt, userPrompt) = aiService.PreviewBeforeClassPrompt("数学", "英语", context);
            var preview = $"[System Prompt]\n{systemPrompt}\n\n[User Prompt]\n{userPrompt}";
            ShowPromptPreview("测试 AI 提示词", preview);
        }
        catch (Exception ex)
        {
            Logger.Error($"生成 AI 提示词预览失败: {ex.Message}");
            ShowToast($"生成提示词预览失败: {ex.Message}", "error");
        }
        finally
        {
            BusyContent.End(_testPromptButton, originalContent);
        }
    }

    private async void OnTestContextClicked(object? sender, RoutedEventArgs e)
    {
        if (_testContextButton == null) return;
        AutoSaveSettings();
        var originalContent = _testContextButton.Content;
        BusyContent.Begin(_testContextButton);
        try
        {
            var notifier = Plugin.SmartClassNotifierInstance;
            if (notifier == null)
            {
                ShowToast("智能课表服务未初始化，暂时无法获取上下文", "error");
                return;
            }

            var context = await notifier.BuildThoughtfulContextAsync(ThoughtfulScene.BreakStart);
            ShowPromptPreview("测试上下文", string.IsNullOrWhiteSpace(context)
                ? "当前没有可用的上下文数据。\n\n这通常表示天气、时间、课程、新闻等数据源暂时没有返回内容。"
                : context);
        }
        catch (Exception ex)
        {
            Logger.Error($"生成 AI 上下文预览失败: {ex.Message}");
            ShowToast($"生成上下文预览失败: {ex.Message}", "error");
        }
        finally
        {
            BusyContent.End(_testContextButton, originalContent);
        }
    }

    private void ShowPromptPreview(string title, string content)
    {
        var window = new PromptPreviewWindow(title, content);
        if (VisualRoot is Avalonia.Controls.Window owner)
            _ = window.ShowDialog(owner);
        else
            window.Show();
    }

    private async void OnTestSummaryClicked(object? sender, RoutedEventArgs e)
    {
        if (_testSummaryButton == null) return;
        AutoSaveSettings();
        var originalContent = _testSummaryButton.Content;
        BusyContent.Begin(_testSummaryButton);
        ShowToast("正在调用 AI 生成放学贴心总结...", durationMs: 0);

        try
        {
            var aiService = Plugin.GetAIService();
            if (aiService == null) { ShowToast("AI 服务未初始化，请先保存配置", "error"); return; }
            aiService.ClearCache();
            var subjects = new List<string> { "语文", "数学", "英语", "物理", "体育", "化学" };
            var context = Plugin.SmartClassNotifierInstance == null
                ? null
                : await Plugin.SmartClassNotifierInstance.BuildThoughtfulContextAsync(ThoughtfulScene.AfterSchool);
            var result = await aiService.GenerateDailySummary(
                subjects, throwOnError: true, context: context);
            ShowToast($"放学贴心总结测试成功！\n\n模拟：语数英理化体\n\nAI 回复：{result}", "success");
        }
        catch (Exception ex) { ShowToast($"测试失败: {ex.Message}", "error"); }
        finally { BusyContent.End(_testSummaryButton, originalContent); }
    }

    private async void OnExamModeClicked(object? sender, RoutedEventArgs e)
    {
        if (_examModeButton == null) return;
        _examModeButton.IsEnabled = false;

        try
        {
            var server = ExamModeServer.GetOrCreate();
            if (server.IsRunning)
            {
                server.Stop();
                _examModeButton.Content = "启动考试模式";
                var stopNote = _settings.UseSeriousToneInExamMode
                    ? "考试模式已停止，AI 语气已恢复正常。"
                    : "考试模式已停止。";
                ShowToast(stopNote, "success");
            }
            else
            {
                await server.StartAsync();
                var url = $"{server.Url}/?t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });

                var toneNote = _settings.UseSeriousToneInExamMode
                    ? "\nAI 语气已自动切换为严肃模式。"
                    : "\n注意：\"考试时严肃语气\"开关已关闭，AI 语气保持当前设置。";
                ShowToast($"考试模式已启动！\n浏览器已打开：{server.Url}{toneNote}", "success");
                _examModeButton.Content = "停止考试模式";
            }

            // 同步语气下拉框状态
            RefreshExamModeState();
        }
        catch (Exception ex) { ShowToast($"操作失败: {ex.Message}", "error"); }
        finally { _examModeButton.IsEnabled = true; }
    }

    private void OnWelcomeWizardClicked(object? sender, RoutedEventArgs e)
    {
        AutoSaveSettings();
        var wizard = new WelcomeWizard(_settings);
        wizard.WizardCompleted += settings =>
        {
            _settings = settings;
            ApplySettingsToControls();
            AutoSaveSettings();
        };
        if (this.VisualRoot is Avalonia.Controls.Window owner)
            _ = wizard.ShowDialog(owner);
        else
            wizard.Show();
    }

    // ---- 关于卡片按钮 ----

    private void OnGitHubClicked(object? sender, RoutedEventArgs e)
    {
        OpenUrl("https://github.com/qbw101/AIIsland");
    }

    private void OnIssuesClicked(object? sender, RoutedEventArgs e)
    {
        OpenUrl("https://github.com/qbw101/AIIsland/issues");
    }

    private void OnOpenSourceLicensesClicked(object? sender, RoutedEventArgs e)
    {
        if (_openSourceWindow != null)
        {
            _openSourceWindow.Activate();
            return;
        }
        var window = new OpenSourceLicensesWindow();
        _openSourceWindow = window;
        window.Closed += (_, _) => _openSourceWindow = null;
        if (TopLevel.GetTopLevel(this) is Window owner) window.Show(owner);
        else window.Show();
    }

    private static void OpenUrl(string url)
    {
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
            Logger.Info($"打开链接失败: {ex.Message}");
        }
    }

    // ---- 本地数据按钮 ----

    private void OnOpenAiLogsFolderClicked(object? sender, RoutedEventArgs e)
    {
        var logDirectory = System.IO.Path.Combine(_configFolder, "Logs", "AIRequests");
        OpenFolder(logDirectory);
    }

    private void OnOpenPromptsFolderClicked(object? sender, RoutedEventArgs e)
    {
        var dataDir = GetPluginDataFolder();
        OpenFolder(dataDir);
    }

    /// <summary>获取插件的 Data 目录（插件安装位置，非配置目录）</summary>
    private static string GetPluginDataFolder()
    {
        var dllPath = Assembly.GetExecutingAssembly().Location;
        var pluginDir = System.IO.Path.GetDirectoryName(dllPath) ?? ".";
        return System.IO.Path.Combine(pluginDir, "Data");
    }

    /// <summary>从 manifest.yml 读取 version 字段</summary>
    private static string ReadManifestVersion()
    {
        try
        {
            var dllPath = Assembly.GetExecutingAssembly().Location;
            var pluginDir = System.IO.Path.GetDirectoryName(dllPath) ?? ".";
            var manifestPath = System.IO.Path.Combine(pluginDir, "manifest.yml");
            if (!System.IO.File.Exists(manifestPath)) return "?.?.?";
            var text = System.IO.File.ReadAllText(manifestPath);
            var match = System.Text.RegularExpressions.Regex.Match(text, @"^version:\s*(.+)$", System.Text.RegularExpressions.RegexOptions.Multiline);
            return match.Success ? match.Groups[1].Value.Trim() : "?.?.?";
        }
        catch { return "?.?.?"; }
    }

    private static void OpenFolder(string path)
    {
        try
        {
            if (!System.IO.Directory.Exists(path))
                System.IO.Directory.CreateDirectory(path);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Logger.Info($"打开文件夹失败: {ex.Message}");
        }
    }

    #endregion

    #region Toast 提示

    private void ShowToast(string message, string state = "", int? durationMs = null)
    {
        _toast?.Show(message, state switch
        {
            "success" => ToastKind.Success,
            "error" => ToastKind.Error,
            _ => ToastKind.Info
        }, durationMs);
    }

    #endregion

    #region 生命周期

    /// <summary>外部设置发生变更时刷新 UI（例如欢迎向导完成）</summary>
    private void OnExternalSettingsChanged(AISettings settings)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => OnExternalSettingsChanged(settings));
            return;
        }

        try
        {
            _settings = settings;
            ApplySettingsToControls();
        }
        catch (Exception ex)
        {
            Logger.Error($"设置页同步外部变更失败: {ex.Message}");
        }
    }

    private void OnPluginAuthorizationsChanged()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(OnPluginAuthorizationsChanged);
            return;
        }

        var sharedSettings = Plugin.GetAISettings();
        if (sharedSettings != null)
            _settings = sharedSettings;
        RefreshPluginAuthList();
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        Plugin.AISettingsChanged -= OnExternalSettingsChanged;
        Plugin.PluginAuthorizationsChanged -= OnPluginAuthorizationsChanged;

        // 页面关闭时兜底保存，确保即使事件未触发也不会丢设置
        AutoSaveSettings();

        foreach (var t in _buttonTimers.Values)
            t.Stop();
        _buttonTimers.Clear();
    }

    #endregion
}
