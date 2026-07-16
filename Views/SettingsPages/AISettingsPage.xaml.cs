using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Platform.Storage;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using ClassIsland.AISmartClass.Models;
using ClassIsland.AISmartClass.Services;
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
    private ComboBox? _toneStyleComboBox;
    private NumericUpDown? _maxTokensBox;
    private NumericUpDown? _timeoutBox;
    private NumericUpDown? _cacheBox;
    private NumericUpDown? _maxRetriesBox;
    private Button? _testButton;
    private Button? _testReminderButton;
    private Button? _testSummaryButton;
    private TextBlock? _testResultText;
    private Border? _testResultBorder;
    private Button? _examModeButton;
    private TextBlock? _versionLabel;

    // 功能开关
    private ToggleSwitch? _enableCacheCb;
    private ToggleSwitch? _enableFallbackCb;
    private ToggleSwitch? _examSeriousToneCb;

    // 托盘菜单快捷操作复选框
    private CheckBox? _trayBeforeClassReminderCb;
    private CheckBox? _trayAfterSchoolSummaryCb;
    private CheckBox? _trayRegenerateHomeworkCb;
    private CheckBox? _trayExamModeCb;
    private CheckBox? _trayRegenerateSummaryCb;
    private CheckBox? _trayRegenerateHintCb;

    // 按钮缩放动画（复用 WelcomeWizard 风格）
    private readonly List<Button> _animatedButtons = new();
    private readonly Dictionary<Button, ScaleTransform> _buttonTransforms = new();
    private readonly Dictionary<Button, DispatcherTimer> _buttonTimers = new();
    private const double PressScale = 0.98;

    private bool _controlsInitialized;
    private bool _applyingSettings;

    public AISettingsPage(string configFolder)
    {
        _configFolder = configFolder;
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

        // ---- 按钮 ----
        _testButton = WireButton("TestButton", OnTestClicked);
        _testReminderButton = WireButton("TestReminderButton", OnTestReminderClicked);
        _testSummaryButton = WireButton("TestSummaryButton", OnTestSummaryClicked);
        _examModeButton = WireButton("ExamModeButton", OnExamModeClicked);
        WireButton("SaveButton", OnSaveClicked);
        WireButton("GitHubBtn", OnGitHubClicked);
        WireButton("IssuesBtn", OnIssuesClicked);
        WireButton("WelcomeWizardButton", OnWelcomeWizardClicked);
        WireButton("OpenFallbackBtn", OnOpenFallbackFolderClicked);
        WireButton("OpenPromptsBtn", OnOpenPromptsFolderClicked);
        WireButton("ImportConfigBtn", OnImportConfigClicked);
        WireButton("ExportConfigBtn", OnExportConfigClicked);
        WireButton("RestoreDefaultsBtn", OnRestoreDefaultsClicked);

        // ---- 功能开关 ----
        _enableCacheCb = this.FindControl<ToggleSwitch>("EnableCacheCb");
        if (_enableCacheCb != null) _enableCacheCb.IsCheckedChanged += (_, _) => AutoSaveSettings();

        _enableFallbackCb = this.FindControl<ToggleSwitch>("EnableFallbackCb");
        if (_enableFallbackCb != null) _enableFallbackCb.IsCheckedChanged += (_, _) => AutoSaveSettings();

        _examSeriousToneCb = this.FindControl<ToggleSwitch>("ExamSeriousToneCb");
        if (_examSeriousToneCb != null) _examSeriousToneCb.IsCheckedChanged += (_, _) => AutoSaveSettings();

        // 托盘菜单快捷操作复选框
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

        if (_toneStyleComboBox != null)
            _toneStyleComboBox.SelectionChanged += (_, _) => AutoSaveSettings();

        // ---- 测试结果区域 ----
        _testResultText = this.FindControl<TextBlock>("TestResultText");
        _testResultBorder = this.FindControl<Border>("TestResultBorder");
        if (_testResultBorder != null) _testResultBorder.IsVisible = false;

        // ---- 版本号 ----
        _versionLabel = this.FindControl<TextBlock>("VersionLabel");
        if (_versionLabel != null)
            _versionLabel.Text = $"v{ReadManifestVersion()}";
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

        // 功能开关
        if (_enableCacheCb != null) _enableCacheCb.IsChecked = _settings.EnableApiCache;
        if (_enableFallbackCb != null) _enableFallbackCb.IsChecked = _settings.EnableFallbackWhenAiUnavailable;
        if (_examSeriousToneCb != null) _examSeriousToneCb.IsChecked = _settings.UseSeriousToneInExamMode;

        // 托盘菜单快捷操作开关
        if (_trayBeforeClassReminderCb != null) _trayBeforeClassReminderCb.IsChecked = _settings.TrayShowBeforeClassReminder;
        if (_trayAfterSchoolSummaryCb != null) _trayAfterSchoolSummaryCb.IsChecked = _settings.TrayShowAfterSchoolSummary;
        if (_trayRegenerateHomeworkCb != null) _trayRegenerateHomeworkCb.IsChecked = _settings.TrayShowRegenerateHomework;
        if (_trayExamModeCb != null) _trayExamModeCb.IsChecked = _settings.TrayShowExamMode;
        if (_trayRegenerateSummaryCb != null) _trayRegenerateSummaryCb.IsChecked = _settings.TrayShowRegenerateSummary;
        if (_trayRegenerateHintCb != null) _trayRegenerateHintCb.IsChecked = _settings.TrayShowRegenerateHint;

        // 考试模式状态恢复：如果服务器正在运行，刷新按钮文案并锁定语气下拉
        RefreshExamModeState();
    }

    /// <summary>根据 ExamModeServer 运行状态刷新 UI</summary>
    private void RefreshExamModeState()
    {
        try
        {
            var server = ExamModeServer.GetOrCreate();
            var isRunning = server.IsRunning;

            if (_examModeButton != null)
                _examModeButton.Content = isRunning ? "🛑 停止考试模式" : "启动考试模式";

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
    private void AutoSaveSettings()
    {
        // 正在把设置值写回控件时，禁止反向读取保存，避免递归和事件风暴
        if (_applyingSettings) return;

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

            // 功能开关
            _settings.EnableApiCache = _enableCacheCb?.IsChecked ?? true;
            _settings.EnableFallbackWhenAiUnavailable = _enableFallbackCb?.IsChecked ?? true;
            _settings.UseSeriousToneInExamMode = _examSeriousToneCb?.IsChecked ?? true;

            // 托盘菜单快捷操作开关
            _settings.TrayShowBeforeClassReminder = _trayBeforeClassReminderCb?.IsChecked ?? false;
            _settings.TrayShowAfterSchoolSummary = _trayAfterSchoolSummaryCb?.IsChecked ?? false;
            _settings.TrayShowRegenerateHomework = _trayRegenerateHomeworkCb?.IsChecked ?? false;
            _settings.TrayShowExamMode = _trayExamModeCb?.IsChecked ?? true;
            _settings.TrayShowRegenerateSummary = _trayRegenerateSummaryCb?.IsChecked ?? true;
            _settings.TrayShowRegenerateHint = _trayRegenerateHintCb?.IsChecked ?? true;

            SaveSettingsFile();
            Plugin.SyncAISettings(_settings);
        }
        catch (Exception ex)
        {
            Logger.Error($"保存设置失败: {ex.Message}");
        }
    }

    private void SaveSettingsFile()
    {
        System.IO.Directory.CreateDirectory(_configFolder);
        var configPath = System.IO.Path.Combine(_configFolder, "aisettings.json");
        var json = System.Text.Json.JsonSerializer.Serialize(_settings, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        });
        System.IO.File.WriteAllText(configPath, json);
        Logger.Info($"设置已保存到 {configPath}");
    }

    #endregion

    #region 按钮事件

    private void OnSaveClicked(object? sender, RoutedEventArgs e) => AutoSaveSettings();

    private void OnRestoreDefaultsClicked(object? sender, RoutedEventArgs e)
    {
        _settings = new AISettings();
        ApplySettingsToControls();
        AutoSaveSettings();
        ShowTestResult(true, "✅ 已恢复默认设置并保存。");
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

            ShowTestResult(true, $"✅ 配置已导出到：{file.Path.LocalPath}");
        }
        catch (Exception ex)
        {
            Logger.Error($"导出配置失败: {ex.Message}");
            ShowTestResult(false, $"❌ 导出失败: {ex.Message}");
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
                ShowTestResult(false, "❌ 配置文件为空或格式不正确。");
                return;
            }

            _settings = imported;
            ApplySettingsToControls();
            AutoSaveSettings();
            ShowTestResult(true, "✅ 配置已导入并保存。");
        }
        catch (Exception ex)
        {
            Logger.Error($"导入配置失败: {ex.Message}");
            ShowTestResult(false, $"❌ 导入失败: {ex.Message}");
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
        if (_testResultBorder == null || _testResultText == null || _testButton == null) return;
        _testButton.IsEnabled = false;
        ShowTestProgress("⏳ 正在测试 API 连接...");

        try
        {
            var endpoint = _endpointBox?.Text ?? "";
            var apiKey = _apiKeyBox?.Text ?? "";
            var model = _modelBox?.Text ?? "";
            var result = await Services.ApiConnectionTester.FullTestAsync(endpoint, apiKey, model);
            ShowTestResult(result.Success, result.Success ? $"✅ {result.Message}" : $"❌ {result.Message}");
        }
        finally { _testButton.IsEnabled = true; }
    }

    private async void OnTestReminderClicked(object? sender, RoutedEventArgs e)
    {
        if (_testResultBorder == null || _testResultText == null || _testReminderButton == null) return;
        AutoSaveSettings();
        _testReminderButton.IsEnabled = false;
        ShowTestProgress("⏳ 正在调用 AI 生成课前提醒...");

        try
        {
            var aiService = Plugin.GetAIService();
            if (aiService == null) { ShowTestResult(false, "❌ AI 服务未初始化，请先保存配置"); return; }
            var result = await aiService.GenerateBeforeClassReminder("数学", "英语", throwOnError: true);
            ShowTestResult(true, $"✅ AI 课前提醒测试成功！\n\n模拟：数学 → 英语\n\nAI 回复：{result}");
        }
        catch (Exception ex) { ShowTestResult(false, $"❌ 测试失败: {ex.Message}"); }
        finally { _testReminderButton.IsEnabled = true; }
    }

    private async void OnTestSummaryClicked(object? sender, RoutedEventArgs e)
    {
        if (_testResultBorder == null || _testResultText == null || _testSummaryButton == null) return;
        AutoSaveSettings();
        _testSummaryButton.IsEnabled = false;
        ShowTestProgress("⏳ 正在调用 AI 生成每日总结...");

        try
        {
            var aiService = Plugin.GetAIService();
            if (aiService == null) { ShowTestResult(false, "❌ AI 服务未初始化，请先保存配置"); return; }
            var subjects = new List<string> { "语文", "数学", "英语", "物理", "体育", "化学" };
            var result = await aiService.GenerateDailySummary(subjects, throwOnError: true);
            ShowTestResult(true, $"✅ AI 每日总结测试成功！\n\n模拟：语数英理化体\n\nAI 回复：{result}");
        }
        catch (Exception ex) { ShowTestResult(false, $"❌ 测试失败: {ex.Message}"); }
        finally { _testSummaryButton.IsEnabled = true; }
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
                ShowTestResult(true, stopNote);
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
                ShowTestResult(true, $"✅ 考试模式已启动！\n浏览器已打开：{server.Url}{toneNote}");
                _examModeButton.Content = "🛑 停止考试模式";
            }

            // 同步语气下拉框状态
            RefreshExamModeState();
        }
        catch (Exception ex) { ShowTestResult(false, $"❌ 操作失败: {ex.Message}"); }
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

    // ---- 离线数据按钮 ----

    private void OnOpenFallbackFolderClicked(object? sender, RoutedEventArgs e)
    {
        var dataDir = GetPluginDataFolder();
        OpenFolder(dataDir);
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

    #region 测试结果 UI

    private void ShowTestProgress(string message)
    {
        if (_testResultBorder == null || _testResultText == null) return;
        SetTestResultState();
        _testResultBorder.IsVisible = true;
        _testResultText.Text = message;
    }

    private void ShowTestResult(bool success, string message)
    {
        if (_testResultBorder == null || _testResultText == null) return;
        SetTestResultState(success ? "success" : "error");
        _testResultBorder.IsVisible = true;
        _testResultText.Text = message;
    }

    private void SetTestResultState(string? state = null)
    {
        if (_testResultBorder == null || _testResultText == null) return;
        _testResultBorder.Classes.Remove("success");
        _testResultBorder.Classes.Remove("error");
        _testResultText.Classes.Remove("success");
        _testResultText.Classes.Remove("error");
        if (string.IsNullOrWhiteSpace(state)) return;
        _testResultBorder.Classes.Add(state);
        _testResultText.Classes.Add(state);
    }

    #endregion

    #region 生命周期

    /// <summary>外部设置发生变更时刷新 UI（例如欢迎向导完成）</summary>
    private void OnExternalSettingsChanged(AISettings settings)
    {
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

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        Plugin.AISettingsChanged -= OnExternalSettingsChanged;

        // 页面关闭时兜底保存，确保即使事件未触发也不会丢设置
        AutoSaveSettings();

        foreach (var t in _buttonTimers.Values)
            t.Stop();
        _buttonTimers.Clear();
    }

    #endregion
}
