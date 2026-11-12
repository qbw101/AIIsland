namespace ClassIsland.AISmartClass.Services.WebSearch;

/// <summary>
/// 联网搜索返回的单条结果。各字段均可为空字符串，但 <see cref="Url"/> 为空时调用方应丢弃该条。
/// </summary>
internal sealed record WebSearchItem(string Title, string Url, string Snippet, string? PublishedDate);

/// <summary>单个搜索提供者的返回结果。失败时 <see cref="Items"/> 为空集合而非 null。</summary>
internal sealed record WebSearchResponse(bool Success, IReadOnlyList<WebSearchItem> Items, string? Error)
{
    public static WebSearchResponse Ok(IReadOnlyList<WebSearchItem> items) => new(true, items, null);

    public static WebSearchResponse Fail(string error) => new(false, Array.Empty<WebSearchItem>(), error);
}

/// <summary>
/// 链式调度后的最终结果。<see cref="ProviderName"/> 记录实际命中的提供者，便于日志与诊断。
/// </summary>
internal sealed record WebSearchOutcome(
    bool Success, IReadOnlyList<WebSearchItem> Items, string ProviderName, string? Error)
{
    public static WebSearchOutcome Fail(string error) =>
        new(false, Array.Empty<WebSearchItem>(), "none", error);
}

/// <summary>
/// 搜索配置快照。由 <see cref="WebSearchService"/> 从设置同步后传给提供者，
/// 使提供者保持无状态、可直接单元测试。
/// </summary>
/// <param name="Endpoint">OpenSERP 实例地址，可在末尾附带引擎名（如 <c>http://host/baidu</c>）。</param>
/// <param name="Engine">默认搜索引擎；地址里写了引擎时以地址为准。</param>
/// <param name="MaxResults">单次返回条数，限制在 1-10。</param>
/// <param name="TimeoutSeconds">单次检索超时秒数，限制在 3-30。</param>
/// <param name="AiEndpoint">AI 对话接口地址，用于推导 DeepSeek 官方搜索端点。</param>
/// <param name="AiApiKey">AI 对话的 API Key，复用给 DeepSeek 官方搜索，不额外配置。</param>
/// <param name="EnableDeepSeekFallback">是否允许 OpenSERP 无有效结果时改用 DeepSeek 官方搜索。</param>
internal sealed record WebSearchConfig(
    string Endpoint,
    string Engine,
    int MaxResults,
    int TimeoutSeconds,
    string AiEndpoint = "",
    string AiApiKey = "",
    bool EnableDeepSeekFallback = false)
{
    /// <summary>内置检索服务地址。设置页不再暴露该字段，配置为空时回落到这里。</summary>
    public const string DefaultEndpoint = "http://search.qbwnas.top";

    /// <summary>规范化后的结果条数，限制在 1-10。</summary>
    public int EffectiveMaxResults => Math.Clamp(MaxResults, 1, 10);

    /// <summary>规范化后的超时秒数，限制在 3-30。</summary>
    public int EffectiveTimeoutSeconds => Math.Clamp(TimeoutSeconds, 3, 30);

    /// <summary>规范化后的引擎名，空值回落到 bing。</summary>
    public string EffectiveEngine =>
        string.IsNullOrWhiteSpace(Engine) ? "bing" : Engine.Trim().ToLowerInvariant();
}

/// <summary>
/// 搜索提供者。实现必须自行吞掉预期内的失败（鉴权、限流、超时、格式异常）
/// 并以 <see cref="WebSearchResponse.Fail"/> 返回，不得向上抛出——
/// 搜索失败只能降级，不能拖垮对话主流程。
/// </summary>
internal interface IWebSearchProvider
{
    /// <summary>提供者名称，用于日志与结果诊断。</summary>
    string Name { get; }

    /// <summary>当前配置下是否可用（例如服务地址未填时判定为不可用）。</summary>
    bool IsConfigured(WebSearchConfig config);

    Task<WebSearchResponse> SearchAsync(string query, WebSearchConfig config, CancellationToken ct);
}
