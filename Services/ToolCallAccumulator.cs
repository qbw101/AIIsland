using System.Text;

namespace ClassIsland.AISmartClass.Services;

/// <summary>
/// 流式响应里的一个 <c>tool_calls</c> 增量分片。
/// </summary>
/// <param name="Index">同一个工具调用在分片间的序号，必须按它聚合。</param>
/// <param name="Id">调用 id，通常只在首个分片出现。</param>
/// <param name="Name">函数名，通常只在首个分片出现。</param>
/// <param name="Arguments">JSON 参数片段，会被拆成多段依次下发。</param>
internal readonly record struct ChatToolCallDelta(int Index, string? Id, string? Name, string? Arguments);

/// <summary>
/// 按 <c>index</c> 聚合流式 tool_calls 分片。OpenAI 兼容接口把同一个调用的
/// id/name 放在首个分片、把 arguments 拆成多段依次下发，因此不能逐片独立解析。
/// </summary>
internal sealed class ToolCallAccumulator
{
    private readonly Dictionary<int, Entry> _entries = new();

    public bool IsEmpty => _entries.Count == 0;

    public void Append(ChatToolCallDelta delta)
    {
        if (!_entries.TryGetValue(delta.Index, out var entry))
        {
            entry = new Entry();
            _entries[delta.Index] = entry;
        }

        if (!string.IsNullOrEmpty(delta.Id) && entry.Id.Length == 0)
            entry.Id = delta.Id;

        if (!string.IsNullOrEmpty(delta.Name) && entry.Name.Length == 0)
            entry.Name = delta.Name;

        if (!string.IsNullOrEmpty(delta.Arguments))
            entry.Arguments.Append(delta.Arguments);
    }

    public void Clear() => _entries.Clear();

    /// <summary>
    /// 输出聚合后的调用列表，按 index 升序。缺失 id 时补一个可回传的占位值，
    /// 缺失 arguments 时补 <c>{}</c>，避免后续 JSON 解析拿到空串。
    /// </summary>
    public IReadOnlyList<ChatToolCall> Build()
    {
        if (_entries.Count == 0) return Array.Empty<ChatToolCall>();

        var result = new List<ChatToolCall>(_entries.Count);
        foreach (var (index, entry) in _entries.OrderBy(pair => pair.Key))
        {
            result.Add(new ChatToolCall(
                entry.Id.Length > 0 ? entry.Id : $"call_{index}",
                entry.Name,
                entry.Arguments.Length > 0 ? entry.Arguments.ToString() : "{}"));
        }

        return result;
    }

    private sealed class Entry
    {
        public string Id = "";
        public string Name = "";
        public readonly StringBuilder Arguments = new();
    }
}
