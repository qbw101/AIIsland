using System.Text;

namespace ClassIsland.AISmartClass.Services;

/// <summary>
/// 解析「文本形式」的工具调用。
/// <para>
/// 标准实现走 OpenAI 的 <c>tool_calls</c> 字段，但部分模型（尤其是自托管、OCR/VL 变体，
/// 以及沿用 Qwen1/2 老模板的接口）会把工具调用直接写进正文，格式来自 Qwen-Agent 的
/// <c>qwen_fncall_prompt</c>：
/// </para>
/// <code>
/// ✿FUNCTION✿: web_search
/// ✿ARGS✿: {"query": "…"}
/// ✿RESULT✿: （工具结果，由我们回传）
/// ✿RETURN✿: （模型的最终回答）
/// </code>
/// <para>
/// 这类模型在兼容层里不会把上面的文本转成 <c>tool_calls</c>，如果不做兜底，
/// 正文会原样返回给用户，联网检索也不会执行。
/// </para>
/// </summary>
internal static class TextToolCallParser
{
    internal const string FunctionMarker = "✿FUNCTION✿";
    internal const string ArgsMarker = "✿ARGS✿";
    internal const string ResultMarker = "✿RESULT✿";
    internal const string ReturnMarker = "✿RETURN✿";

    /// <summary>正文里是否包含文本工具调用（模型可能先说一句再调用）。</summary>
    internal static bool ContainsToolCall(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return text.Contains(FunctionMarker, StringComparison.Ordinal);
    }

    /// <summary>
    /// 解析正文里的一个或多个 <c>✿FUNCTION✿ / ✿ARGS✿</c> 块。
    /// 解析不出工具名时返回空集合，调用方按普通文本处理。
    /// </summary>
    internal static IReadOnlyList<ChatToolCall> Parse(string? content)
    {
        if (string.IsNullOrEmpty(content)) return Array.Empty<ChatToolCall>();
        if (content.IndexOf(FunctionMarker, StringComparison.Ordinal) < 0)
            return Array.Empty<ChatToolCall>();

        var result = new List<ChatToolCall>();
        var cursor = 0;

        while (cursor < content.Length)
        {
            var functionIndex = content.IndexOf(FunctionMarker, cursor, StringComparison.Ordinal);
            if (functionIndex < 0) break;

            var nameStart = SkipSeparators(content, functionIndex + FunctionMarker.Length);
            var argsIndex = content.IndexOf(ArgsMarker, nameStart, StringComparison.Ordinal);

            if (argsIndex < 0)
            {
                // 只有函数名没有参数：取到行尾，参数按空对象处理
                var nameOnly = ReadName(content, nameStart, content.Length);
                if (nameOnly.Length > 0)
                    result.Add(BuildCall(result.Count, nameOnly, ""));
                break;
            }

            var name = ReadName(content, nameStart, argsIndex);
            var argsStart = SkipSeparators(content, argsIndex + ArgsMarker.Length);
            var (arguments, next) = ReadArguments(content, argsStart);

            if (name.Length > 0) result.Add(BuildCall(result.Count, name, arguments));

            cursor = next > argsStart ? next : argsStart;
            if (cursor >= content.Length) break;
        }

        return result;
    }

    /// <summary>
    /// 清理最终答案里的残留标记：去掉开头的 <c>✿RETURN✿:</c>，
    /// 以及模型在禁止工具后仍然重复输出的 <c>✿FUNCTION✿ … ✿ARGS✿ …</c> 片段。
    /// </summary>
    internal static string CleanFinalAnswer(string? content)
    {
        if (string.IsNullOrEmpty(content)) return "";

        var text = content.TrimStart();
        text = RemoveLeadingToolCalls(text);

        // 可能连着出现多个 ✿RETURN✿
        while (text.StartsWith(ReturnMarker, StringComparison.Ordinal))
            text = text[ReturnMarker.Length..].TrimStart(':', '：', ' ', '\t', '\r', '\n');

        return text.Trim();
    }

    /// <summary>
    /// 去掉正文开头残留的工具调用块（已禁用工具的一轮里模型仍可能重复输出）。
    /// 解析不出参数边界时原样返回，避免误删正常内容。
    /// </summary>
    private static string RemoveLeadingToolCalls(string text)
    {
        while (text.StartsWith(FunctionMarker, StringComparison.Ordinal))
        {
            var nameStart = SkipSeparators(text, FunctionMarker.Length);
            var argsIndex = text.IndexOf(ArgsMarker, nameStart, StringComparison.Ordinal);
            if (argsIndex < 0) return text;

            var argsStart = SkipSeparators(text, argsIndex + ArgsMarker.Length);
            var (_, next) = ReadArguments(text, argsStart);
            if (next <= argsStart) return text;

            text = text[next..].TrimStart();
        }

        return text;
    }

    /// <summary>
    /// 构造回传给模型的检索结果消息。
    /// <para>
    /// Qwen-Agent 的原始做法是把 <c>✿RESULT✿</c> 追加在 assistant 续写里，
    /// 但 OpenAI 兼容接口不支持 assistant prefill，只能把结果作为下一轮输入。
    /// 这里不再依赖标记语义，直接把资料以自然语言交给模型，任何模型都能正确消费。
    /// </para>
    /// </summary>
    internal static string BuildResultMessage(string toolText)
        => $"{toolText}\n\n以上是工具返回的检索结果，请直接基于这些资料回答用户的问题，不要再次调用工具。";

    private static ChatToolCall BuildCall(int index, string name, string arguments)
        => new($"call_text_{index}", name, string.IsNullOrWhiteSpace(arguments) ? "{}" : arguments);

    /// <summary>跳过标记后面的分隔符：冒号（半角/全角）、空白。</summary>
    private static int SkipSeparators(string content, int index)
    {
        while (index < content.Length)
        {
            var c = content[index];
            if (c is ':' or '：' or ' ' or '\t' or '\r' or '\n') index++;
            else break;
        }
        return index;
    }

    /// <summary>读取工具名：截到下一个标记、换行或行内多余空白之前。</summary>
    private static string ReadName(string content, int start, int end)
    {
        if (start >= end) return "";

        var limit = end;
        for (var i = start; i < end; i++)
        {
            var c = content[i];
            if (c is '\r' or '\n' or ' ' or '\t')
            {
                limit = i;
                break;
            }
        }

        return content[start..limit].Trim().TrimEnd(':', '：');
    }

    /// <summary>
    /// 读取参数 JSON。优先按括号配对截取完整对象；JSON 被截断时取到下一个标记之前，
    /// 保证 <see cref="AIChatService.ExtractSearchQuery"/> 仍能尽力解析出 query。
    /// </summary>
    private static (string Json, int Next) ReadArguments(string content, int start)
    {
        if (start >= content.Length) return ("", start);

        var offset = start;
        while (offset < content.Length && char.IsWhiteSpace(content[offset])) offset++;

        if (offset >= content.Length) return ("", offset);

        var first = content[offset];
        if (first is not ('{' or '['))
        {
            // 不是 JSON：取到行尾或下一个标记之前
            var end = FindMarkerOrLineEnd(content, offset);
            return (content[offset..end].Trim(), end);
        }

        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var i = offset; i < content.Length; i++)
        {
            var c = content[i];
            if (inString)
            {
                if (escaped) escaped = false;
                else if (c == '\\') escaped = true;
                else if (c == '"') inString = false;
                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    break;
                case '{' or '[':
                    depth++;
                    break;
                case '}' or ']':
                    depth--;
                    if (depth == 0) return (content[offset..(i + 1)], i + 1);
                    break;
            }
        }

        // 括号没闭合（响应被截断）：取到下一个标记之前
        var stop = FindMarkerOrLineEnd(content, offset);
        return (content[offset..stop].Trim(), stop);
    }

    private static int FindMarkerOrLineEnd(string content, int start)
    {
        var end = content.Length;
        foreach (var marker in new[] { FunctionMarker, ArgsMarker, ResultMarker, ReturnMarker })
        {
            var index = content.IndexOf(marker, start, StringComparison.Ordinal);
            if (index >= 0 && index < end) end = index;
        }

        var newline = content.IndexOf('\n', start);
        if (newline >= 0 && newline < end) end = newline;

        return end;
    }
}

/// <summary>
/// 流式输出阶段的过滤器：模型逐字吐 <c>✿FUNCTION✿</c> 时不能把标记闪给用户，
/// 需要先把开头这段「可能是标记」的内容扣住，等判定完成后再决定丢弃还是放行。
/// <para>
/// 另外也负责剥掉最终回答开头的 <c>✿RETURN✿:</c>——这个标记同样是逐字到达的。
/// </para>
/// </summary>
internal sealed class TextToolCallStreamFilter
{
    private readonly StringBuilder _raw = new();
    private string _visible = "";

    /// <summary>模型原始输出（含标记与工具参数）。</summary>
    public string Raw => _raw.ToString();

    /// <summary>可以展示给用户的文本（已剔除标记）。</summary>
    public string Visible => _visible;

    /// <summary>本轮是否包含一段文本工具调用（整轮正文都不展示）。</summary>
    public bool IsToolCall => TextToolCallParser.ContainsToolCall(_raw.ToString());

    /// <summary>追加一段增量，返回本次新增的可展示文本（可能为空）。</summary>
    public string Push(string? delta)
    {
        if (!string.IsNullOrEmpty(delta)) _raw.Append(delta);

        var text = _raw.ToString().TrimStart();
        var next = Resolve(text);

        if (next.Length <= _visible.Length) return "";

        var added = next[_visible.Length..];
        _visible = next;
        return added;
    }

    private static string Resolve(string text)
    {
        if (text.Length == 0) return "";

        // 最终回答：剥掉 ✿RETURN✿ 前缀后正常展示
        if (text.StartsWith(TextToolCallParser.ReturnMarker, StringComparison.Ordinal))
            return text[TextToolCallParser.ReturnMarker.Length..].TrimStart(':', '：', ' ', '\t', '\r', '\n');

        // 工具调用（含「先说一句再调用」的情况）：标记之后的内容一律不展示
        var cut = FindToolCallStart(text);
        if (cut >= 0) return text[..cut].TrimEnd();

        return text;
    }

    /// <summary>
    /// 找到工具调用的起点：完整标记，或末尾正在逐字输入的标记前缀。
    /// 返回 -1 表示这段文本与工具调用无关。
    /// </summary>
    private static int FindToolCallStart(string text)
    {
        var index = text.IndexOf(TextToolCallParser.FunctionMarker, StringComparison.Ordinal);
        if (index >= 0) return index;

        // 末尾的 ✿ 可能正是标记的开头，先扣住不显示
        var last = text.LastIndexOf('✿');
        if (last < 0) return -1;

        var tail = text[last..];
        return TextToolCallParser.FunctionMarker.StartsWith(tail, StringComparison.Ordinal) ||
               TextToolCallParser.ReturnMarker.StartsWith(tail, StringComparison.Ordinal)
            ? last
            : -1;
    }
}
