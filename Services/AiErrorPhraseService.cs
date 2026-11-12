namespace ClassIsland.AISmartClass.Services;

/// <summary>
/// 把 AI 请求失败翻译成用户可读的报错文案。
/// 插件不再内置预设句子库：AI 不可用时明确报错，不伪装成"正常提醒"。
/// </summary>
public class AiErrorPhraseService
{
    /// <summary>所有报错文案的固定前缀，供降级判定复用，避免两处硬编码不一致。</summary>
    public const string ErrorPrefix = "AI 请求失败";

    /// <summary>
    /// 生成 AI 请求失败的报错文案。传入结构化失败信息时，常见错误会翻译成
    /// 用户能直接处理的具体原因（如 Key 无效、超时、限流）。
    /// </summary>
    public string GetErrorPhrase(AIRequestFailureInfo? failure = null)
        => BuildErrorPhrase(failure == null ? null : DescribeFailure(failure));

    /// <summary>根据已有的原因文本（如"未配置 API Key"）生成报错文案。</summary>
    public string GetErrorPhraseFromReason(string? reason)
        => BuildErrorPhrase(reason);

    private static string BuildErrorPhrase(string? reason)
        => string.IsNullOrWhiteSpace(reason)
            ? $"{ErrorPrefix}，请稍后重试。"
            : $"{ErrorPrefix}：{reason.Trim()}。";

    /// <summary>把请求失败诊断翻译成一句具体的错误原因；无法归类时返回空串。</summary>
    public static string DescribeFailure(AIRequestFailureInfo failure)
    {
        // HTTP 状态码最准确，优先用它判定常见错误。
        if (failure.HttpStatusCode is { } status)
        {
            return status switch
            {
                401 or 403 => $"API Key 无效或无访问权限（HTTP {status}）",
                404 => $"接口地址或模型不存在（HTTP {status}）",
                408 => $"API 服务端超时（HTTP {status}）",
                429 => $"请求过于频繁或额度已用尽（HTTP {status}）",
                >= 500 => $"API 服务端异常（HTTP {status}）",
                _ => $"API 返回 HTTP {status}"
            };
        }

        return failure.Category switch
        {
            "本地超时" => "请求超时，AI 服务未响应",
            "本地网络/DNS 解析失败" => "无法解析服务器地址，请检查网络或接口地址",
            "本地网络/连接被拒绝" => "连接被拒绝，请确认接口地址和端口",
            "本地网络/TLS 认证失败" => "TLS 认证失败，请检查证书或代理设置",
            "本地网络/套接字错误" or "本地网络/HTTP 连接失败" => "网络连接失败，请检查网络或代理",
            "API 响应格式错误" => "API 响应格式异常，请确认接口兼容 OpenAI 规范",
            "API 流式响应中断" => "API 流式响应中断",
            "本地配置错误" => "接口地址配置有误",
            "调用方取消" => "请求已取消",
            // 未归类的技术异常对用户没有参考价值，回落到通用报错文案。
            _ => ""
        };
    }
}
