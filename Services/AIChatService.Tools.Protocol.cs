namespace ClassIsland.AISmartClass.Services;

/// <summary>
/// 工具调用协议维护：把 assistant 的工具调用与结果写回消息列表，
/// 并在发送前校验每个 <c>tool_call_id</c> 都有对应的 tool 消息。
/// 从 <c>AIChatService.Tools.Execution.cs</c> 拆出，避免单文件过长。
/// </summary>
public partial class AIChatService
{
    /// <summary>
    /// 把 assistant 的工具调用与执行结果写入消息列表。
    /// <para>
    /// 结构化 <c>tool_calls</c> 必须为**每一个** id 各回一条 <c>role:"tool"</c> 消息：
    /// 少回一条，严格实现会报 400
    /// <c>insufficient tool messages following tool_calls message</c>，整轮对话直接失败。
    /// </para>
    /// <para>
    /// 文本协议（<c>✿FUNCTION✿</c>）没有 tool 角色，结果合并成一条 user 消息。
    /// </para>
    /// </summary>
    private static void AppendToolTurn(
        List<ChatMessage> messages,
        AIChatResponse response,
        IReadOnlyList<ToolExecution> executions)
    {
        if (response.TextualToolCalls)
        {
            messages.Add(ChatMessage.Assistant(response.Content));
            if (executions.Count > 0)
            {
                var combined = string.Join("\n\n", executions.Select(execution => execution.Text));
                messages.Add(ChatMessage.User(TextToolCallParser.BuildResultMessage(combined)));
            }

            return;
        }

        messages.Add(ChatMessage.Assistant(response.Content, response.ToolCalls));
        for (var i = 0; i < response.ToolCalls.Count; i++)
        {
            var text = i < executions.Count ? executions[i].Text : "（该调用未执行，请直接回答用户问题。）";
            messages.Add(ChatMessage.ToolResult(response.ToolCalls[i].Id, text));
        }
    }

    /// <summary>
    /// 协议自检：每个 assistant <c>tool_calls</c> 里的 id 都必须有对应的 tool 消息。
    /// 缺失时补一条说明，避免严格实现因「tool 消息数量不足」直接 400。
    /// 正常情况下不会触发，纯粹是防止后续改动漏掉分支。
    /// </summary>
    internal static void EnsureToolCallsAnswered(List<ChatMessage> messages)
    {
        for (var i = 0; i < messages.Count; i++)
        {
            if (messages[i].ToolCalls is not { Count: > 0 } calls) continue;

            var insertAt = i + 1;
            var answered = new HashSet<string>(StringComparer.Ordinal);
            while (insertAt < messages.Count && messages[insertAt].Role == "tool")
            {
                var id = messages[insertAt].ToolCallId;
                if (!string.IsNullOrEmpty(id)) answered.Add(id);
                insertAt++;
            }

            foreach (var call in calls)
            {
                if (answered.Contains(call.Id)) continue;

                messages.Insert(insertAt++, ChatMessage.ToolResult(
                    call.Id, "（该调用未执行，请直接回答用户问题。）"));
                Logger.Info($"工具历史缺少 tool 消息，已补齐：{call.Id}");
            }
        }
    }

    private static bool HasStructuredToolHistory(IReadOnlyList<ChatMessage> messages)
        => messages.Any(message => message.ToolCalls is { Count: > 0 });

}
