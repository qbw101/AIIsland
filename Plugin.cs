using System.IO;
using System.Net.Http;
using System.Text.Json;
using Avalonia;
using ClassIsland.Core.Abstractions;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Extensions.Registry;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.AISmartClass.Models;
using ClassIsland.AISmartClass.Services;
using ClassIsland.AISmartClass.Services.NotificationProviders;
using ClassIsland.AISmartClass.Controls.NotificationProviders;
using ClassIsland.AISmartClass.Views.SettingsPages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ClassIsland.AISmartClass;

/// <summary>
/// AIIsland 插件入口。
/// 整合课表助手 + 学习仪表盘 + 智能提醒管家。
/// </summary>
[PluginEntrance]
public class Plugin : PluginBase
{
    /// <summary>全局 AI 服务引用，供设置页面保存后实时同步。</summary>
    private static AIChatService? _sharedAiService;

    /// <summary>已加载的 AI 设置快照，供启动后状态检查。</summary>
    private static AISettings? _sharedSettings;

    /// <summary>自然语言解析服务引用。</summary>
    private static ReminderParserService? _sharedReminderParser;

    /// <summary>插件配置文件夹路径，供其他组件使用。</summary>
    public static string? ConfigFolderPath { get; private set; }

    /// <summary>ClassIsland 核心服务引用，经 SmartClassNotifier 构造函数注入后可用。</summary>
    public static IProfileService? ProfileService { get; internal set; }
    public static ILessonsService? LessonsService { get; internal set; }

    /// <summary>AI 设置发生变更时触发，供设置页面等 UI 自动刷新。</summary>
    public static event Action<AISettings>? AISettingsChanged;

    /// <summary>检查配置 JSON 是否来自尚未记录托盘默认版本的旧版。</summary>
    public static bool IsLegacyTrayMenuSettings(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return !document.RootElement.TryGetProperty("trayMenuDefaultsVersion", out _);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>将旧版托盘菜单默认值迁移为当前默认组合。</summary>
    public static bool MigrateAISettings(AISettings settings, bool isLegacyConfig)
    {
        if (!isLegacyConfig && settings.TrayMenuDefaultsVersion >= 1) return false;

        settings.TrayShowBeforeClassReminder = false;
        settings.TrayShowAfterSchoolSummary = false;
        settings.TrayShowRegenerateHomework = false;
        settings.TrayShowExamMode = true;
        settings.TrayShowRegenerateSummary = true;
        settings.TrayShowRegenerateHint = true;
        settings.TrayMenuDefaultsVersion = 1;
        Logger.Info("已迁移托盘菜单默认选项");
        return true;
    }

    /// <summary>设置页面保存后调用，实时同步 API 配置到 AI 服务并通知所有订阅者刷新 UI。</summary>
    public static void SyncAISettings(AISettings settings)
    {
        _sharedSettings = settings;
        _sharedAiService?.SyncFrom(settings);

        try
        {
            var examServer = ExamModeServer.GetOrCreate();
            examServer.Enabled = settings.EnableExamModeLocalServer;
            TrayMenuRegistrar.Refresh(settings);
            AISettingsChanged?.Invoke(settings);
        }
        catch (Exception ex)
        {
            Logger.Error($"实时应用设置失败: {ex}");
        }

        Logger.Info("设置已实时同步到 AI 服务与托盘菜单");
    }

    /// <summary>获取全局 AI 服务实例，供测试按钮使用。</summary>
    public static AIChatService? GetAIService() => _sharedAiService;

    /// <summary>获取自然语言解析服务实例。</summary>
    public static ReminderParserService? GetReminderParser() => _sharedReminderParser;

    /// <summary>IHostedService：在应用启动后获取核心服务，写入静态属性供各组件使用。</summary>
    internal class PluginInitializer : IHostedService
    {
        private readonly IProfileService _profileService;
        private readonly ILessonsService _lessonsService;
        private readonly ClassIsland.Core.Abstractions.Services.ITaskBarIconService? _taskBarIconService;

        public PluginInitializer(
            IProfileService profileService,
            ILessonsService lessonsService,
            ClassIsland.Core.Abstractions.Services.ITaskBarIconService? taskBarIconService)
        {
            _profileService = profileService;
            _lessonsService = lessonsService;
            _taskBarIconService = taskBarIconService;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            ProfileService = _profileService;
            LessonsService = _lessonsService;
            Logger.Info("ProfileService / LessonsService 已写入静态属性");

            // 注册托盘菜单快捷操作（IHostedService 启动阶段主窗口尚未初始化，
            // TaskBarIconService.MoreOptionsMenu 为 null，需等待 AppStarted 事件后再注册）
            if (_taskBarIconService != null)
                RegisterTrayMenuWhenReady(_taskBarIconService);
            else
                Logger.Info("ITaskBarIconService 未注入，跳过托盘菜单注册");

            return Task.CompletedTask;
        }

        private void RegisterTrayMenuWhenReady(ITaskBarIconService taskBarService)
        {
            try
            {
                var app = ClassIsland.Core.AppBase.Current;
                if (app?.MainWindow is not null)
                {
                    TrayMenuRegistrar.Register(taskBarService);
                    Logger.Info("[TrayMenu] 主窗口已存在，直接注册托盘菜单");
                    return;
                }

                if (app is null)
                {
                    Logger.Warn("[TrayMenu] AppBase.Current 为空，无法注册托盘菜单");
                    return;
                }

                Logger.Info("[TrayMenu] 主窗口尚未初始化，等待 AppStarted 事件后注册");
                EventHandler? handler = null;
                handler = (_, _) =>
                {
                    try
                    {
                        TrayMenuRegistrar.Register(taskBarService);
                        Logger.Info("[TrayMenu] AppStarted 事件触发，托盘菜单已注册");
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"[TrayMenu] AppStarted 中注册托盘菜单失败: {ex.Message}");
                    }
                    finally
                    {
                        app.AppStarted -= handler;
                    }
                };
                app.AppStarted += handler;
            }
            catch (Exception ex)
            {
                Logger.Error($"[TrayMenu] 准备托盘菜单注册失败: {ex.Message}");
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    public override void Initialize(HostBuilderContext context, IServiceCollection services)
    {
        try
        {
            Logger.Info("插件初始化开始...");

            // 0. 存储配置文件夹路径供其他组件使用
            ConfigFolderPath = PluginConfigFolder;

            // 1. 初始化离线降级句子库
            var fallback = new FallbackPhraseService();
            fallback.Load(PluginConfigFolder);
            services.AddSingleton(fallback);
            Logger.Info("FallbackPhraseService 初始化完成");

            // 1.5. 加载 3 套语气风格提示词 JSON
            PromptTemplates.Load(PluginConfigFolder);
            Logger.Info("PromptTemplates 初始化完成");

            // 2. 初始化 AI 聊天服务（统一后端：缓存 + 重试 + 降级）
            var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            var aiService = new AIChatService(http, fallback);
            _sharedAiService = aiService;
            services.AddSingleton(aiService);
            Logger.Info("AIChatService 初始化完成");

            // 3. 启动时从配置文件加载 AI 设置并同步到服务
            try
            {
                var configPath = Path.Combine(PluginConfigFolder, "aisettings.json");
                if (File.Exists(configPath))
                {
                    var json = File.ReadAllText(configPath);
                    var settings = JsonSerializer.Deserialize<AISettings>(json,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new AISettings();
                    if (MigrateAISettings(settings, IsLegacyTrayMenuSettings(json)))
                    {
                        File.WriteAllText(configPath, JsonSerializer.Serialize(settings,
                            new JsonSerializerOptions { WriteIndented = true }));
                    }
                    aiService.SyncFrom(settings);
                    _sharedSettings = settings;
                    Logger.Info("已加载 AI 设置");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"加载 AI 设置失败: {ex.Message}");
            }

            // 4.5 注册托管服务：应用启动后自动获取 IProfileService / ILessonsService
            services.AddHostedService<PluginInitializer>();
            Logger.Info("PluginInitializer 已注册");

            // 5. 注册自然语言解析服务
            var parser = new ReminderParserService(aiService);
            services.AddSingleton(parser);
            _sharedReminderParser = parser;
            Logger.Info("ReminderParserService 已注册");

            // 5. 注册提醒提供方（课前提醒 / 放学总结 / 换课提醒 / 定时提醒）
            services.AddNotificationProvider<SmartClassNotifier, SmartClassNotifierSettingsControl>();
            Logger.Info("SmartClassNotifier 已注册");

            // 6. 注册主界面组件（每个功能独立为一个组件，用户可自由排列，带设置控件）
            services.AddComponent<Controls.ScheduleInsight.ScheduleInsight, Controls.ScheduleInsight.ScheduleInsightSettingsControl>();
            services.AddComponent<Controls.HomeworkEstimate.HomeworkEstimate, Controls.HomeworkEstimate.HomeworkEstimateSettingsControl>();
            services.AddComponent<Controls.ClassCountdown.ClassCountdown, Controls.ClassCountdown.ClassCountdownSettingsControl>();
            services.AddComponent<Controls.CurrentHint.CurrentHint, Controls.CurrentHint.CurrentHintSettingsControl>();
            services.AddComponent<Controls.DifficultyInfo.DifficultyInfo, Controls.DifficultyInfo.DifficultyInfoSettingsControl>();
            Logger.Info("5 个主界面组件已注册");

            // 6.5. 反射替换组件图标为自定义字体图标（主题感知自动变色）
            IconPatcher.PatchAll();

            // 7. 注册 AI 设置页面（API 端点 / Key / 模型配置）
            services.AddSingleton(PluginConfigFolder);
            services.AddSettingsPage<AISettingsPage>();
            SettingsPageIconPatcher.Initialize();
            Logger.Info("AISettingsPage 已注册");

            // 8. 注册考试模式 HTTP 服务器
            services.AddSingleton<ExamModeServer>();
            Logger.Info("ExamModeServer 已注册");

            // 9. 首次运行检测：没有配置文件时自动弹出欢迎向导
            try
            {
                var settingsPath = System.IO.Path.Combine(PluginConfigFolder, "aisettings.json");
                if (!System.IO.File.Exists(settingsPath))
                {
                    Logger.Info("检测到首次运行，将在启动后显示欢迎向导");
                    Task.Run(async () =>
                    {
                        await Task.Delay(1200);
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            try
                            {
                                var wizard = new Views.WelcomeWizard();
                                wizard.WizardCompleted += settings =>
                                {
                                    try
                                    {
                                        var configPath = System.IO.Path.Combine(ConfigFolderPath!, "aisettings.json");
                                        var json = System.Text.Json.JsonSerializer.Serialize(settings,
                                            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                                        System.IO.File.WriteAllText(configPath, json);
                                        SyncAISettings(settings);
                                        Logger.Info("欢迎向导完成，设置已自动保存并立即应用");
                                    }
                                    catch (Exception ex)
                                    {
                                        Logger.Error($"向导设置保存失败: {ex.Message}");
                                    }
                                };
                                wizard.Show();
                            }
                            catch (Exception ex)
                            {
                                Logger.Error($"欢迎向导启动失败: {ex.Message}");
                            }
                        });
                    });
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"首次运行检测失败: {ex.Message}");
            }

            // 9.5. 启动时配置状态提醒（ShowConfigStatusOnStartup）
            try
            {
                if (_sharedSettings is { ShowConfigStatusOnStartup: true } && string.IsNullOrWhiteSpace(_sharedSettings.ApiKey))
                {
                    Logger.Info("未配置 API Key，将在启动后显示状态提醒");
                    // 稍后在 UI 线程弹出提醒（等待主窗口就绪）
                    Task.Run(async () =>
                    {
                        await Task.Delay(2500); // 等待 ClassIsland 主窗口就绪
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            Logger.Info("API Key 未配置：建议在设置中填入以启用 AI 功能");
                        });
                    });
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"启动状态提醒失败: {ex.Message}");
            }

        // 10. 同步 ExamModeServer 开关（无需构建独立 ServiceProvider）
        try
        {
            var examServer = ExamModeServer.GetOrCreate();
            if (_sharedSettings != null)
            {
                examServer.Enabled = _sharedSettings.EnableExamModeLocalServer;
                Logger.Info($"ExamModeServer Enabled={examServer.Enabled}");
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"同步 ExamModeServer 开关失败: {ex.Message}");
        }

        Logger.Info("插件初始化完成 ✓");
        }
        catch (Exception ex)
        {
            Logger.Error($"插件初始化失败: {ex.GetType().Name}: {ex.Message}");
            Logger.Info($"堆栈: {ex.StackTrace}");
        }
    }
}
