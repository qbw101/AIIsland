using System.IO;
using System.Net.Http;
using System.Text.Json;
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

    /// <summary>插件配置文件夹路径，供其他组件使用。</summary>
    public static string? ConfigFolderPath { get; private set; }

    /// <summary>ClassIsland 核心服务引用，经 SmartClassNotifier 构造函数注入后可用。</summary>
    public static IProfileService? ProfileService { get; internal set; }
    public static ILessonsService? LessonsService { get; internal set; }

    /// <summary>设置页面保存后调用，实时同步 API 配置到 AI 服务。</summary>
    public static void SyncAISettings(AISettings settings)
    {
        _sharedAiService?.SyncFrom(settings);
        System.Diagnostics.Debug.WriteLine("[AIIsland] 设置已实时同步到 AIChatService");
    }

    /// <summary>获取全局 AI 服务实例，供测试按钮使用。</summary>
    public static AIChatService? GetAIService() => _sharedAiService;

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
            System.Diagnostics.Debug.WriteLine("[AIIsland] ProfileService / LessonsService 已写入静态属性");
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    public override void Initialize(HostBuilderContext context, IServiceCollection services)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[AIIsland] 插件初始化开始...");

            // 0. 存储配置文件夹路径供其他组件使用
            ConfigFolderPath = PluginConfigFolder;

            // 1. 初始化离线降级句子库
            var fallback = new FallbackPhraseService();
            fallback.Load(PluginConfigFolder);
            services.AddSingleton(fallback);
            System.Diagnostics.Debug.WriteLine("[AIIsland] FallbackPhraseService 初始化完成");

            // 2. 初始化 AI 聊天服务（统一后端：缓存 + 重试 + 降级）
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var aiService = new AIChatService(http, fallback);
            _sharedAiService = aiService;
            services.AddSingleton(aiService);
            System.Diagnostics.Debug.WriteLine("[AIIsland] AIChatService 初始化完成");

            // 3. 启动时从配置文件加载 AI 设置并同步到服务
            try
            {
                var configPath = Path.Combine(PluginConfigFolder, "aisettings.json");
                if (File.Exists(configPath))
                {
                    var json = File.ReadAllText(configPath);
                    var settings = JsonSerializer.Deserialize<AISettings>(json) ?? new AISettings();
                    aiService.SyncFrom(settings);
                    System.Diagnostics.Debug.WriteLine("[AIIsland] 已加载 AI 设置");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AIIsland] 加载 AI 设置失败: {ex.Message}");
            }

            // 4.5 注册托管服务：应用启动后自动获取 IProfileService / ILessonsService
            services.AddHostedService<PluginInitializer>();
            System.Diagnostics.Debug.WriteLine("[AIIsland] PluginInitializer 已注册");

            // 5. 注册自然语言解析服务
            services.AddSingleton<ReminderParserService>();
            System.Diagnostics.Debug.WriteLine("[AIIsland] ReminderParserService 已注册");

            // 5. 注册提醒提供方（课前提醒 / 放学总结 / 换课提醒 / 定时提醒）
            services.AddNotificationProvider<SmartClassNotifier, SmartClassNotifierSettingsControl>();
            System.Diagnostics.Debug.WriteLine("[AIIsland] SmartClassNotifier 已注册");

            // 6. 注册主界面组件（每个功能独立为一个组件，用户可自由排列，带设置控件）
            services.AddComponent<Controls.ScheduleInsight.ScheduleInsight, Controls.ScheduleInsight.ScheduleInsightSettingsControl>();
            services.AddComponent<Controls.HomeworkEstimate.HomeworkEstimate, Controls.HomeworkEstimate.HomeworkEstimateSettingsControl>();
            services.AddComponent<Controls.ClassCountdown.ClassCountdown, Controls.ClassCountdown.ClassCountdownSettingsControl>();
            services.AddComponent<Controls.CurrentHint.CurrentHint, Controls.CurrentHint.CurrentHintSettingsControl>();
            services.AddComponent<Controls.DifficultyInfo.DifficultyInfo, Controls.DifficultyInfo.DifficultyInfoSettingsControl>();
            System.Diagnostics.Debug.WriteLine("[AIIsland] 5 个主界面组件已注册");

            // 7. 注册 AI 设置页面（API 端点 / Key / 模型配置）
            services.AddSingleton(PluginConfigFolder);
            services.AddSettingsPage<AISettingsPage>();
            System.Diagnostics.Debug.WriteLine("[AIIsland] AISettingsPage 已注册");

            // 8. 注册考试模式 HTTP 服务器
            services.AddSingleton<ExamModeServer>();
            System.Diagnostics.Debug.WriteLine("[AIIsland] ExamModeServer 已注册");

            // 8. 提前获取核心服务（BuildServiceProvider 在这里是安全的，因为主程序已完成注册）
            try
            {
                var sp = services.BuildServiceProvider();
                ProfileService = sp.GetService<IProfileService>();
                LessonsService = sp.GetService<ILessonsService>();
                System.Diagnostics.Debug.WriteLine($"[AIIsland] 核心服务获取: ProfileService={ProfileService != null}, LessonsService={LessonsService != null}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AIIsland] 核心服务获取失败（将由 PluginInitializer 兜底）: {ex.Message}");
            }

            System.Diagnostics.Debug.WriteLine("[AIIsland] 插件初始化完成 ✓");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AIIsland] 插件初始化失败: {ex.GetType().Name}: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[AIIsland] 堆栈: {ex.StackTrace}");
        }
    }
}
