using System;

namespace ClassIsland.AISmartClass.Services;

/// <summary>
/// 贴心提示上下文的共享缓存。因 CurrentHint 与 SmartClassPanel 可能并存，
/// 缓存必须是进程级共享的，故用静态类。跨天自动视为无缓存（重置）。
/// </summary>
public static class ThoughtfulContextCache
{
    private static readonly object Lock = new();
    private static ThoughtfulContextSnapshot? _last;
    private static DateOnly _lastDate;

    /// <summary>
    /// 获取上次快照。跨天返回 null（视为首次，触发全量播报）。
    /// </summary>
    public static ThoughtfulContextSnapshot? GetLast()
    {
        lock (Lock)
        {
            if (_lastDate != DateOnly.FromDateTime(DateTime.Now))
                return null;
            return _last;
        }
    }

    /// <summary>更新缓存为最新快照，并刷新日期。</summary>
    public static void Update(ThoughtfulContextSnapshot snapshot)
    {
        lock (Lock)
        {
            _lastDate = DateOnly.FromDateTime(DateTime.Now);
            _last = snapshot;
        }
    }

    /// <summary>
    /// 清空增量缓存。用户点击「重新生成」时调用，使下一次 GetThoughtfulHintChangesAsync
    /// 将当前快照视为首次（previous=null），从而全量生成第二段贴心提示。
    /// </summary>
    public static void Reset()
    {
        lock (Lock)
        {
            _last = null;
            _lastDate = default;
        }
    }
}
