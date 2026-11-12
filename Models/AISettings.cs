using System.Collections.Generic;
using System.Text.Json.Serialization;
using ClassIsland.AISmartClass.PublicApi;
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
    private string _endpoint = "https://api.deepseek.com/chat/completions";
    // 兼容 OpenAI Chat Completions 格式的 API 地址

    [ObservableProperty]
    [property: JsonPropertyName("apiKey")]
    private string _apiKey = "";
    // API 密钥（本地存储，不出网）

    [ObservableProperty]
    [property: JsonPropertyName("model")]
    private string _model = "deepseek-chat";
    // 模型名称，如 deepseek-chat / gpt-4o-mini / qwen-turbo

    // ===== 图片识别能力 =====

    /// <summary>
    /// 当前模型能否识别图片（视觉输入）。取值 unknown / supported / unsupported，
    /// 由 VisionCapabilityService 写入；只有签名与当前接口地址、模型一致时才可信。
    /// </summary>
    [ObservableProperty]
    [property: JsonPropertyName("visionSupport")]
    private string _visionSupport = VisionSupportValues.Unknown;

    /// <summary>
    /// 上次检测时的「接口地址|模型名」签名。用户换了模型或接口后旧结论自动失效，
    /// 避免把旧模型的结论套到新模型上。
    /// </summary>
    [ObservableProperty]
    [property: JsonPropertyName("visionSupportSignature")]
    private string _visionSupportSignature = "";

    /// <summary>上次检测时间（ISO 8601），仅用于展示。</summary>
    [ObservableProperty]
    [property: JsonPropertyName("visionSupportCheckedAt")]
    private string _visionSupportCheckedAt = "";

    // ===== 语气风格 =====

    /// <summary>
    /// 语气风格索引：0=活泼，1=标准，2=严肃
    /// 映射到不同的 temperature 值
    /// </summary>
    [ObservableProperty]
    [property: JsonPropertyName("toneStyle")]
    private int _toneStyle = 1;

    /// <summary>
    /// 自定义温度值（0.0-2.0），为null时使用语气风格映射的默认温度
    /// 某些AI大模型可能要求特定的温度值，用户可手动调整
    /// </summary>
    [ObservableProperty]
    [property: JsonPropertyName("customTemperature")]
    private double? _customTemperature = null;

    // ===== 角色语气 =====

    /// <summary>
    /// 是否启用角色语气。角色 = 说话语气/风格，独立叠加在「语气风格」之上。
    /// </summary>
    [ObservableProperty]
    [property: JsonPropertyName("personaEnabled")]
    private bool _personaEnabled = false;

    /// <summary>
    /// 选中的内置角色 Id（如 ganyu / hutao）。空表示未选内置角色（可能是自定义或关闭）。
    /// </summary>
    [ObservableProperty]
    [property: JsonPropertyName("personaPresetId")]
    private string _personaPresetId = "";

    /// <summary>
    /// 自定义角色设定文本。未选内置角色时生效。
    /// </summary>
    [ObservableProperty]
    [property: JsonPropertyName("customPersona")]
    private string _customPersona = "";

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

    // ===== 深度思考 =====

    /// <summary>
    /// 是否开启深度思考（推理）。关闭时按厂商支持情况下发「关闭」参数；
    /// 开启后会先推理再回答，更准但更慢、更耗 token。
    /// </summary>
    [ObservableProperty]
    [property: JsonPropertyName("enableDeepThinking")]
    private bool _enableDeepThinking = false;

    /// <summary>思考强度，仅在开启深度思考时生效。</summary>
    [ObservableProperty]
    [property: JsonPropertyName("thinkingEffort")]
    private ThinkingEffort _thinkingEffort = ThinkingEffort.Auto;

    [ObservableProperty]
    [property: JsonPropertyName("aiLogRetentionDays")]
    private int _aiLogRetentionDays = 30;
    // AI 调用日志保留天数，超期日志自动清理

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
    [property: JsonPropertyName("enableApiCache")]
    private bool _enableApiCache = true;
    // 启用 AI 结果缓存节省 API 调用

    // ===== 联网搜索 =====

    /// <summary>
    /// 是否允许模型在对话中自主调用 web_search 工具联网检索。
    /// 默认关闭；关闭时请求体与未接入搜索前完全一致。
    /// 启用后本次请求的输出上限会自动抬到至少 800 token、超时至少 30 秒。
    /// </summary>
    [ObservableProperty]
    [property: JsonPropertyName("enableWebSearch")]
    private bool _enableWebSearch = false;

    /// <summary>
    /// 是否允许模型在检索摘要不够时调用 web_fetch 打开网页读取正文。
    /// 默认开启；关闭后请求体里只有 web_search 一个工具，抓取代码不会执行。
    /// </summary>
    [ObservableProperty]
    [property: JsonPropertyName("enableWebFetch")]
    private bool _enableWebFetch = true;

    /// <summary>OpenSERP 默认搜索引擎；地址固定为内置实例，设置页不再暴露，这里保留以便调整默认引擎。</summary>
    [ObservableProperty]
    [property: JsonPropertyName("searchEngine")]
    private string _searchEngine = "bing";

    [ObservableProperty]
    [property: JsonPropertyName("webSearchMaxResults")]
    private int _webSearchMaxResults = 5;
    // 单次检索返回条数（1-10）

    [ObservableProperty]
    [property: JsonPropertyName("webSearchTimeoutSeconds")]
    private int _webSearchTimeoutSeconds = 8;
    // 单次检索超时秒数（3-30），与 AI 请求超时相互独立

    /// <summary>
    /// 是否允许在 OpenSERP 没有有效结果、或网页抓取失败时改用 DeepSeek 官方接口兜底。
    /// 复用 AI 对话的 API Key，按成功请求计费（约 5 元/千次），默认关闭。
    /// </summary>
    [ObservableProperty]
    [property: JsonPropertyName("enableDeepSeekSearchFallback")]
    private bool _enableDeepSeekSearchFallback = false;

    [ObservableProperty]
    [property: JsonPropertyName("enableExamModeLocalServer")]
    private bool _enableExamModeLocalServer = true;
    // 允许考试模式启动本地 HTTP 服务

    [ObservableProperty]
    [property: JsonPropertyName("showConfigStatusOnStartup")]
    private bool _showConfigStatusOnStartup = true;
    // 启动后显示配置状态提醒

    // ===== 时间来源 =====

    /// <summary>
    /// 业务时间来源：所有课表、倒计时、提醒、考试判断统一使用此设置。
    /// 默认使用 ClassIsland 时间，保持课时倒计时的原有默认行为。
    /// 真实时间场景（AI 提示词、日志、网络校时、缓存有效期、超时）不受此设置影响。
    /// </summary>
    [ObservableProperty]
    [property: JsonPropertyName("timeSource")]
    private BusinessTimeSource _timeSource = BusinessTimeSource.ClassIslandTime;

    // ===== 托盘菜单快捷操作开关 =====

    [ObservableProperty]
    [property: JsonPropertyName("trayMenuDefaultsVersion")]
    private int _trayMenuDefaultsVersion = 1;

    [ObservableProperty]
    [property: JsonPropertyName("trayShowBeforeClassReminder")]
    private bool _trayShowBeforeClassReminder = false;
    // 托盘菜单显示"触发课前提醒"

    [ObservableProperty]
    [property: JsonPropertyName("trayShowBeforeSchoolReminder")]
    private bool _trayShowBeforeSchoolReminder = false;
    // 托盘菜单显示"生成智能每日简报"

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

    [ObservableProperty]
    [property: JsonPropertyName("trayShowCustomReminders")]
    private bool _trayShowCustomReminders;
    // 托盘菜单显示"触发自定义提醒"子菜单

    // ===== 外部插件 API 授权 =====

    /// <summary>
    /// 默认授权策略：true=所有未单独配置的插件每次确认，false=允许直接调用。
    /// 实际策略以每个 PluginAuthEntry 的 AuthMode 为准，此值为新插件初始模式。
    /// </summary>
    [ObservableProperty]
    [property: JsonPropertyName("defaultAuthMode")]
    private int _defaultAuthMode = 0;
    // 0=PerCallConfirm（默认），1=Trusted

    /// <summary>
    /// 已记录的外部插件授权列表。按 PluginId 索引。
    /// </summary>
    [property: JsonPropertyName("pluginAuthEntries")]
    public List<AIIslandPluginAuthEntry> PluginAuthEntries { get; set; } = new();

    // ===== 插件集成（AIIsland 调用其他插件） =====

    /// <summary>
    /// 贴心提醒是否启用外部插件信息（生日提醒、值日插件等）
    /// </summary>
    [ObservableProperty]
    [property: JsonPropertyName("enablePluginIntegration")]
    private bool _enablePluginIntegration = false;

    /// <summary>
    /// 已授权的外部插件 GUID 集合（用于贴心提醒读取）
    /// </summary>
    [property: JsonPropertyName("authorizedIntegrationPlugins")]
    public HashSet<string> AuthorizedIntegrationPlugins { get; set; } = new();

    /// <summary>
    /// 插件集成授权向导是否已完成（首次打开设置时触发）
    /// </summary>
    [ObservableProperty]
    [property: JsonPropertyName("pluginIntegrationAuthorizationCompleted")]
    private bool _pluginIntegrationAuthorizationCompleted = false;

    // ===== 匿名遥测 =====

    /// <summary>
    /// 匿名遥测配置：在线状态、错误与崩溃上报。关闭后插件不会建立任何遥测连接。
    /// </summary>
    [property: JsonPropertyName("telemetry")]
    public TelemetrySettings Telemetry { get; set; } = new();

    /// <summary>获取 temperature 值：自定义温度优先，未设置时按语气风格映射。</summary>
    public double GetTemperature()
    {
        // 如果用户设置了自定义温度，使用自定义值（限制在0.0-2.0范围内）
        if (CustomTemperature.HasValue)
        {
            return Math.Clamp(CustomTemperature.Value, 0.0, 2.0);
        }

        // 未自定义时按语气风格映射：活泼高创造性、标准平衡、严肃稳定准确
        return ToneStyle switch
        {
            0 => 1.0,
            1 => 0.7,
            2 => 0.3,
            _ => 0.7
        };
    }
}
