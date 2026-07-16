using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClassIsland.AISmartClass.Models;

/// <summary>
/// AI 服务的全局配置。修改后立即生效，无需重启。
/// 存储于插件的 PluginConfigFolder 下，由 ClassIsland 托管持久化。
/// 注意：必须使用 ObservableObject（非 ObservableRecipient），
/// 否则 IsActive/Messenger 属性会被序列化到 JSON，导致反序列化失败。
/// </summary>
public partial class AISettings : ObservableObject
{
    // ===== API 连接配置 =====

    [ObservableProperty]
    [property: JsonPropertyName("endpoint")]
    private string _endpoint = "https://api.deepseek.com/v1/chat/completions";
    // 兼容 OpenAI Chat Completions 格式的 API 地址

    [ObservableProperty]
    [property: JsonPropertyName("apiKey")]
    private string _apiKey = "";
    // API 密钥（本地存储，不出网）

    [ObservableProperty]
    [property: JsonPropertyName("model")]
    private string _model = "deepseek-chat";
    // 模型名称，如 deepseek-chat / gpt-4o-mini / qwen-turbo

    // ===== 语气风格 =====

    /// <summary>
    /// 语气风格索引：0=活泼，1=标准，2=严肃
    /// 映射到不同的 temperature 值
    /// </summary>
    [ObservableProperty]
    [property: JsonPropertyName("toneStyle")]
    private int _toneStyle = 1;

    // ===== 行为参数 =====

    [ObservableProperty]
    [property: JsonPropertyName("maxTokens")]
    private int _maxTokens = 200;
    // 单次回复最大 token 数（提醒类内容均短，200 足够）

    [ObservableProperty]
    [property: JsonPropertyName("timeoutSeconds")]
    private int _timeoutSeconds = 10;
    // HTTP 请求超时秒数

    [ObservableProperty]
    [property: JsonPropertyName("cacheMinutes")]
    private int _cacheMinutes = 5;
    // 缓存有效期（分钟），相同上下文此时间内复用

    [ObservableProperty]
    [property: JsonPropertyName("maxRetries")]
    private int _maxRetries = 1;
    // 失败重试次数

    // ===== 向导与偏好 =====

    [ObservableProperty]
    [property: JsonPropertyName("wizardCompleted")]
    private bool _wizardCompleted;

    [ObservableProperty]
    [property: JsonPropertyName("setupMode")]
    private string _setupMode = "unknown";
    // unknown / manual / recommended / offline

    [ObservableProperty]
    [property: JsonPropertyName("useSeriousToneInExamMode")]
    private bool _useSeriousToneInExamMode = true;
    // 考试期间自动切换严肃语气

    [ObservableProperty]
    [property: JsonPropertyName("enableFallbackWhenAiUnavailable")]
    private bool _enableFallbackWhenAiUnavailable = true;
    // AI 不可用时使用本地降级提示

    [ObservableProperty]
    [property: JsonPropertyName("enableApiCache")]
    private bool _enableApiCache = true;
    // 启用 AI 结果缓存节省 API 调用

    [ObservableProperty]
    [property: JsonPropertyName("enableExamModeLocalServer")]
    private bool _enableExamModeLocalServer = true;
    // 允许考试模式启动本地 HTTP 服务

    [ObservableProperty]
    [property: JsonPropertyName("showConfigStatusOnStartup")]
    private bool _showConfigStatusOnStartup = true;
    // 启动后显示配置状态提醒

    // ===== 托盘菜单快捷操作开关 =====

    [ObservableProperty]
    [property: JsonPropertyName("trayMenuDefaultsVersion")]
    private int _trayMenuDefaultsVersion = 1;

    [ObservableProperty]
    [property: JsonPropertyName("trayShowBeforeClassReminder")]
    private bool _trayShowBeforeClassReminder = false;
    // 托盘菜单显示"触发课前提醒"

    [ObservableProperty]
    [property: JsonPropertyName("trayShowAfterSchoolSummary")]
    private bool _trayShowAfterSchoolSummary = false;
    // 托盘菜单显示"触发放学总结"

    [ObservableProperty]
    [property: JsonPropertyName("trayShowRegenerateHomework")]
    private bool _trayShowRegenerateHomework = false;
    // 托盘菜单显示"重新生成作业量估算"

    [ObservableProperty]
    [property: JsonPropertyName("trayShowExamMode")]
    private bool _trayShowExamMode = true;
    // 托盘菜单显示"启动/停止考试模式"

    [ObservableProperty]
    [property: JsonPropertyName("trayShowRegenerateSummary")]
    private bool _trayShowRegenerateSummary = true;
    // 托盘菜单显示"重新生成课表总结"

    [ObservableProperty]
    [property: JsonPropertyName("trayShowRegenerateHint")]
    private bool _trayShowRegenerateHint = true;
    // 托盘菜单显示"重新生成学习提示"

    /// <summary>根据语气风格获取 temperature 值</summary>
    public double GetTemperature()
    {
        return ToneStyle switch
        {
            0 => 1.0,   // 活泼：高创造性
            1 => 0.7,   // 标准：平衡
            2 => 0.3,   // 严肃：稳定准确
            _ => 0.7
        };
    }
}
