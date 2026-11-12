namespace ClassIsland.AISmartClass.Services;

/// <summary>
/// 模型请求执行的一次工具调用。<see cref="Arguments"/> 是原始 JSON 字符串（由模型生成），
/// 解析失败时按空对象处理，不影响整轮对话。
/// </summary>
internal sealed record ChatToolCall(string Id, string Name, string Arguments);

/// <summary>
/// 内部聊天消息，仅用于组装 Chat Completions 请求体的 messages 数组。
/// <para>
/// AIIsland 不做多轮对话历史管理，这里的消息列表只服务两类场景：
/// 单轮的 system + user，以及「模型调用工具 → 回传结果 → 再回答」的两轮工具回传。
/// 不进入公开 API，避免把内部协议暴露给插件调用方。
/// </para>
/// </summary>
internal sealed class ChatMessage
{
    /// <summary>system / user / assistant / tool。</summary>
    public required string Role { get; init; }

    /// <summary>string（纯文本）或 object[]（多模态 content）。</summary>
    public object? Content { get; init; }

    /// <summary>assistant 消息携带的工具调用列表。</summary>
    public IReadOnlyList<ChatToolCall>? ToolCalls { get; init; }

    /// <summary>tool 消息对应的调用 id。</summary>
    public string? ToolCallId { get; init; }

    public static ChatMessage System(string text) => new()
    {
        Role = "system",
        Content = text
    };

    public static ChatMessage User(string text) => new()
    {
        Role = "user",
        Content = text
    };

    /// <summary>带图片的用户消息：content 改为「文本 + image_url」数组。</summary>
    public static ChatMessage UserWithImage(string text, VisionImage image) => new()
    {
        Role = "user",
        Content = new object[]
        {
            new { type = "text", text },
            new { type = "image_url", image_url = new { url = image.ToDataUri() } }
        }
    };

    public static ChatMessage Assistant(string text, IReadOnlyList<ChatToolCall>? toolCalls = null) => new()
    {
        Role = "assistant",
        Content = text,
        ToolCalls = toolCalls
    };

    public static ChatMessage ToolResult(string toolCallId, string text) => new()
    {
        Role = "tool",
        Content = text,
        ToolCallId = toolCallId
    };

    /// <summary>单轮 system + user 的便捷构造，供现有不涉及工具的调用点使用。</summary>
    public static IReadOnlyList<ChatMessage> Simple(string system, string user) => new[]
    {
        System(system),
        User(user)
    };

    /// <summary>
    /// 序列化成请求体里的 messages 元素。键名必须保持小写字面量——
    /// 请求体用的是默认 JsonSerializerOptions，没有命名策略。
    /// </summary>
    public object ToPayload()
    {
        if (Role == "tool")
        {
            return new
            {
                role = "tool",
                tool_call_id = ToolCallId ?? "",
                content = Content
            };
        }

        if (ToolCalls is { Count: > 0 })
        {
            // 模型请求工具时正文常为空串；部分厂商要求显式 null，回传时统一成 null
            var content = Content is string text && text.Length == 0 ? null : Content;
            return new
            {
                role = Role,
                content,
                tool_calls = ToolCalls.Select(call => new
                {
                    id = call.Id,
                    type = "function",
                    function = new
                    {
                        name = call.Name,
                        arguments = call.Arguments
                    }
                }).ToArray()
            };
        }

        return new
        {
            role = Role,
            content = Content
        };
    }
}
