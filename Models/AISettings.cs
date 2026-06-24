using CommunityToolkit.Mvvm.ComponentModel;

namespace ClassIsland.AISmartClass.Models;

/// <summary>
/// AI 服务的全局配置。修改后立即生效，无需重启。
/// 存储于插件的 PluginConfigFolder 下，由 ClassIsland 托管持久化。
/// </summary>
public partial class AISettings : ObservableRecipient
{
    // ===== API 连接配置 =====

    [ObservableProperty]
    private string _endpoint = "https://api.deepseek.com/v1/chat/completions";
    // 兼容 OpenAI Chat Completions 格式的 API 地址

    [ObservableProperty]
    private string _apiKey = "";
    // API 密钥（本地存储，不出网）

    [ObservableProperty]
    private string _model = "deepseek-chat";
    // 模型名称，如 deepseek-chat / gpt-4o-mini / qwen-turbo

    // ===== 语气风格 =====

    /// <summary>
    /// 语气风格索引：0=活泼，1=标准，2=严肃
    /// 映射到不同的 temperature 值
    /// </summary>
    [ObservableProperty]
    private int _toneStyle = 1;

    // ===== 行为参数 =====

    [ObservableProperty]
    private int _maxTokens = 200;
    // 单次回复最大 token 数（提醒类内容均短，200 足够）

    [ObservableProperty]
    private int _timeoutSeconds = 10;
    // HTTP 请求超时秒数

    [ObservableProperty]
    private int _cacheMinutes = 5;
    // 缓存有效期（分钟），相同上下文此时间内复用

    [ObservableProperty]
    private int _maxRetries = 1;
    // 失败重试次数

    // ===== 向导与偏好 =====

    [ObservableProperty]
    private bool _wizardCompleted;

    [ObservableProperty]
    private string _setupMode = "unknown";
    // unknown / manual / recommended / offline

    [ObservableProperty]
    private bool _useSeriousToneInExamMode = true;
    // 考试期间自动切换严肃语气

    [ObservableProperty]
    private bool _enableFallbackWhenAiUnavailable = true;
    // AI 不可用时使用本地降级提示

    [ObservableProperty]
    private bool _enableApiCache = true;
    // 启用 AI 结果缓存节省 API 调用

    [ObservableProperty]
    private bool _enableExamModeLocalServer = true;
    // 允许考试模式启动本地 HTTP 服务

    [ObservableProperty]
    private bool _showConfigStatusOnStartup = true;
    // 启动后显示配置状态提醒

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
