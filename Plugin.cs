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
using Microsoft.Extensions.Logging;

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

    /// <summary>设置页面保存后调用，实时同步 API 配置到 AI 服务。</summary>
    public static void SyncAISettings(AISettings settings)
    {
        _sharedAiService?.SyncFrom(settings);
        Logger.Info("设置已实时同步到 AIChatService");
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
        private readonly ILogger<PluginInitializer> _logger;

        public PluginInitializer(IProfileService profileService, ILessonsService lessonsService, ILogger<PluginInitializer> logger)
        {
            _profileService = profileService;
            _lessonsService = lessonsService;
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            ProfileService = _profileService;
            LessonsService = _lessonsService;
            _logger.LogInformation("[AIIsland] ProfileService 和 LessonsService 已获取");
            Logger.Info("ProfileService / LessonsService 已写入静态属性");
            return Task.CompletedTask;
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
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
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
                    var settings = JsonSerializer.Deserialize<AISettings>(json) ?? new AISettings();
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
                                        var json = System.Text.Json.JsonSerializer.Serialize(settings);
                                        System.IO.File.WriteAllText(configPath, json);
                                        _sharedAiService?.SyncFrom(settings);
                                        Logger.Info("欢迎向导完成，设置已自动保存");
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

            // 10. 提前获取核心服务（BuildServiceProvider 在这里是安全的，因为主程序已完成注册）
            try
            {
                var sp = services.BuildServiceProvider();
                ProfileService = sp.GetService<IProfileService>();
                LessonsService = sp.GetService<ILessonsService>();
                Logger.Info($"核心服务获取: ProfileService={ProfileService != null}, LessonsService={LessonsService != null}");

                // 同步 ExamModeServer 开关
                var examServer = sp.GetService<ExamModeServer>();
                if (examServer != null && _sharedSettings != null)
                {
                    examServer.Enabled = _sharedSettings.EnableExamModeLocalServer;
                    Logger.Info($"ExamModeServer Enabled={examServer.Enabled}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"核心服务获取失败（将由 PluginInitializer 兜底）: {ex.Message}");
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
