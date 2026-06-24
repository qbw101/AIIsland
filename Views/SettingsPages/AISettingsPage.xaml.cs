using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using ClassIsland.AISmartClass.Models;
using ClassIsland.AISmartClass.Services;

namespace ClassIsland.AISmartClass.Views.SettingsPages;

[SettingsPageInfo(
    "aisettings.aisettingspage",
    "AIIsland"
)]
public partial class AISettingsPage : SettingsPageBase
{
    private readonly string _configFolder;
    private AISettings _settings = new();

    // ===== 手动声明 XAML 控件引用（Avalonia 源码生成器可能未生效） =====

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
    private bool _disposed;

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
        InitializeControls();
        LoadSettings();
    }

    private void InitializeControls()
    {
        _endpointBox = this.FindControl<TextBox>("EndpointBox");
        _apiKeyBox = this.FindControl<TextBox>("ApiKeyBox");
        _modelBox = this.FindControl<TextBox>("ModelBox");
        _toneStyleComboBox = this.FindControl<ComboBox>("ToneStyleComboBox");
        _maxTokensBox = this.FindControl<NumericUpDown>("MaxTokensBox");
        _timeoutBox = this.FindControl<NumericUpDown>("TimeoutBox");
        _cacheBox = this.FindControl<NumericUpDown>("CacheBox");
        _maxRetriesBox = this.FindControl<NumericUpDown>("MaxRetriesBox");

        if (_toneStyleComboBox != null)
            _toneStyleComboBox.SelectionChanged += OnToneStyleChanged;

        var saveButton = this.FindControl<Button>("SaveButton");
        if (saveButton != null)
            saveButton.Click += OnSaveClicked;

        _testButton = this.FindControl<Button>("TestButton");
        if (_testButton != null)
            _testButton.Click += OnTestClicked;

        _testReminderButton = this.FindControl<Button>("TestReminderButton");
        if (_testReminderButton != null)
            _testReminderButton.Click += OnTestReminderClicked;

        _testSummaryButton = this.FindControl<Button>("TestSummaryButton");
        if (_testSummaryButton != null)
            _testSummaryButton.Click += OnTestSummaryClicked;

        _testResultText = this.FindControl<TextBlock>("TestResultText");
        _testResultBorder = this.FindControl<Border>("TestResultBorder");
        if (_testResultBorder != null)
            _testResultBorder.IsVisible = false;

        _examModeButton = this.FindControl<Button>("ExamModeButton");
        if (_examModeButton != null)
            _examModeButton.Click += OnExamModeClicked;

        var welcomeWizardButton = this.FindControl<Button>("WelcomeWizardButton");
        if (welcomeWizardButton != null)
            welcomeWizardButton.Click += OnWelcomeWizardClicked;
    }

    private void LoadSettings()
    {
        // 从配置文件加载设置
        try
        {
            var configPath = System.IO.Path.Combine(_configFolder, "aisettings.json");
            if (System.IO.File.Exists(configPath))
            {
                var json = System.IO.File.ReadAllText(configPath);
                _settings = System.Text.Json.JsonSerializer.Deserialize<AISettings>(json) ?? new AISettings();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"加载设置失败: {ex.Message}");
            _settings = new AISettings();
        }

        ApplySettingsToControls();
    }

    private void ApplySettingsToControls()
    {
        if (_endpointBox != null) _endpointBox.Text = _settings.Endpoint;
        if (_apiKeyBox != null) _apiKeyBox.Text = _settings.ApiKey;
        if (_modelBox != null) _modelBox.Text = _settings.Model;
        if (_toneStyleComboBox != null) _toneStyleComboBox.SelectedIndex = _settings.ToneStyle;
        if (_maxTokensBox != null) _maxTokensBox.Value = _settings.MaxTokens;
        if (_timeoutBox != null) _timeoutBox.Value = _settings.TimeoutSeconds;
        if (_cacheBox != null) _cacheBox.Value = _settings.CacheMinutes;
        if (_maxRetriesBox != null) _maxRetriesBox.Value = _settings.MaxRetries;
    }

    private void OnToneStyleChanged(object? sender, SelectionChangedEventArgs e)
    {
        // 语气风格切换后自动保存
        AutoSaveSettings();
    }

    private async void OnTestReminderClicked(object? sender, RoutedEventArgs e)
    {
        if (_testResultBorder == null || _testResultText == null || _testReminderButton == null) return;

        // 先自动保存，确保 AI 服务使用最新配置
        AutoSaveSettings();

        _testReminderButton.IsEnabled = false;
        _testResultBorder.IsVisible = true;
        _testResultText.Text = "⏳ 正在调用 AI 生成课前提醒...";
        if (_testResultBorder.Background is Avalonia.Media.Immutable.ImmutableSolidColorBrush)
            _testResultBorder.Background = Avalonia.Media.Brushes.Transparent;

        try
        {
            var aiService = Plugin.GetAIService();
            if (aiService == null)
            {
                ShowTestResult(false, "❌ AI 服务未初始化，请先保存配置");
                return;
            }

            // 模拟：刚上完数学，下节是英语
            var result = await aiService.GenerateBeforeClassReminder("数学", "英语");
            ShowTestResult(true, $"✅ AI 课前提醒测试成功！\n\n模拟场景：刚上完数学 → 下节英语\n\nAI 回复：{result}");
        }
        catch (Exception ex)
        {
            ShowTestResult(false, $"❌ 测试失败: {ex.Message}");
        }
        finally
        {
            _testReminderButton.IsEnabled = true;
        }
    }

    private async void OnTestSummaryClicked(object? sender, RoutedEventArgs e)
    {
        if (_testResultBorder == null || _testResultText == null || _testSummaryButton == null) return;

        // 先自动保存，确保 AI 服务使用最新配置
        AutoSaveSettings();

        _testSummaryButton.IsEnabled = false;
        _testResultBorder.IsVisible = true;
        _testResultText.Text = "⏳ 正在调用 AI 生成每日总结...";
        if (_testResultBorder.Background is Avalonia.Media.Immutable.ImmutableSolidColorBrush)
            _testResultBorder.Background = Avalonia.Media.Brushes.Transparent;

        try
        {
            var aiService = Plugin.GetAIService();
            if (aiService == null)
            {
                ShowTestResult(false, "❌ AI 服务未初始化，请先保存配置");
                return;
            }

            // 模拟：今天的课程列表
            var subjects = new List<string> { "语文", "数学", "英语", "物理", "体育", "化学" };
            var result = await aiService.GenerateDailySummary(subjects);
            ShowTestResult(true, $"✅ AI 每日总结测试成功！\n\n模拟场景：语文、数学、英语、物理、体育、化学\n\nAI 回复：{result}");
        }
        catch (Exception ex)
        {
            ShowTestResult(false, $"❌ 测试失败: {ex.Message}");
        }
        finally
        {
            _testSummaryButton.IsEnabled = true;
        }
    }

    private async void OnTestClicked(object? sender, RoutedEventArgs e)
    {
        if (_testResultBorder == null || _testResultText == null || _testButton == null) return;

        _testButton.IsEnabled = false;
        _testResultBorder.IsVisible = true;
        _testResultText.Text = "⏳ 正在测试 API 连接...";
        if (_testResultBorder.Background is Avalonia.Media.Immutable.ImmutableSolidColorBrush)
            _testResultBorder.Background = Avalonia.Media.Brushes.Transparent;

        try
        {
            var endpoint = _endpointBox?.Text ?? "";
            var apiKey = _apiKeyBox?.Text ?? "";
            var model = _modelBox?.Text ?? "";

            var result = await Services.ApiConnectionTester.FullTestAsync(endpoint, apiKey, model);
            ShowTestResult(result.Success, result.Success ? $"✅ {result.Message}" : $"❌ {result.Message}");
        }
        finally
        {
            _testButton.IsEnabled = true;
        }
    }

    private void ShowTestResult(bool success, string message)
    {
        if (_testResultBorder == null || _testResultText == null) return;
        _testResultBorder.IsVisible = true;
        _testResultText.Text = message;
        _testResultBorder.Background = success
            ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(40, 80, 40))
            : new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(80, 40, 40));
    }

    private void OnSaveClicked(object? sender, RoutedEventArgs e)
    {
        AutoSaveSettings();
    }

    private void OnExamModeClicked(object? sender, RoutedEventArgs e)
    {
        if (_examModeButton == null) return;
        _examModeButton.IsEnabled = false;

        try
        {
            var server = ExamModeServer.GetOrCreate();
            if (!server.IsRunning)
            {
                server.Start();
            }

            var url = $"{server.Url}/?t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });

            ShowTestResult(true, $"✅ 考试模式已启动！\n浏览器已打开：{server.Url}");
            _examModeButton.Content = "🔄 重新打开考试模式";
        }
        catch (Exception ex)
        {
            ShowTestResult(false, $"❌ 启动失败: {ex.Message}");
        }
        finally
        {
            _examModeButton.IsEnabled = true;
        }
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

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        if (_disposed) return;
        _disposed = true;
        // 设置页关闭时不释放单例，避免重复开关页导致端口变更；应用退出时由 DI 容器统一释放
    }

    /// <summary>收集 UI 控件当前值 → 持久化到 JSON → 实时同步到 AIChatService</summary>
    private void AutoSaveSettings()
    {
        _settings.Endpoint = _endpointBox?.Text ?? "";
        _settings.ApiKey = _apiKeyBox?.Text ?? "";
        _settings.Model = _modelBox?.Text ?? "";
        _settings.ToneStyle = _toneStyleComboBox?.SelectedIndex ?? 1;
        _settings.MaxTokens = (int)(_maxTokensBox?.Value ?? 200);
        _settings.TimeoutSeconds = (int)(_timeoutBox?.Value ?? 10);
        _settings.CacheMinutes = (int)(_cacheBox?.Value ?? 5);
        _settings.MaxRetries = (int)(_maxRetriesBox?.Value ?? 1);

        // 持久化设置到配置文件夹
        try
        {
            // 确保配置目录存在
            System.IO.Directory.CreateDirectory(_configFolder);

            var configPath = System.IO.Path.Combine(_configFolder, "aisettings.json");
            var json = System.Text.Json.JsonSerializer.Serialize(_settings, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });
            System.IO.File.WriteAllText(configPath, json);
            System.Diagnostics.Debug.WriteLine($"[AIIsland] 设置已保存到 {configPath}");

            // 实时同步到 AIChatService
            Plugin.SyncAISettings(_settings);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"保存设置失败: {ex.Message}");
        }
    }
}
