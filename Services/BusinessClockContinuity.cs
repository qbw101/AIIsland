namespace ClassIsland.AISmartClass.Services;

/// <summary>
/// 业务时钟连续性判断。
/// 时间来源切换、用户调整 ClassIsland 偏移或系统休眠恢复会造成时间跳变，
/// 依赖“上次检查时间 → 本次时间”窗口的调度器必须重建基线，
/// 否则会把跳变区间内的所有阈值当成刚跨过而补发大量陈旧提醒。
/// </summary>
public static class BusinessClockContinuity
{
    /// <summary>允许的正常回退容差（小于该值视为抖动，不重建基线）。</summary>
    public static readonly TimeSpan DefaultBackwardTolerance = TimeSpan.FromSeconds(1);

    /// <summary>允许的正常前进间隔（大于该值视为跳变，重建基线）。</summary>
    public static readonly TimeSpan DefaultForwardJumpThreshold = TimeSpan.FromSeconds(10);

    /// <summary>判断是否需要重建调度基线。</summary>
    public static bool ShouldResetBaseline(DateTime lastObserved, DateTime current)
        => ShouldResetBaseline(lastObserved, current, DefaultBackwardTolerance, DefaultForwardJumpThreshold);

    /// <summary>判断是否需要重建调度基线（可指定容差，便于测试与不同轮询周期复用）。</summary>
    public static bool ShouldResetBaseline(
        DateTime lastObserved,
        DateTime current,
        TimeSpan backwardTolerance,
        TimeSpan forwardJumpThreshold)
    {
        if (current < lastObserved - backwardTolerance)
        {
            return true;
        }

        return current > lastObserved + forwardJumpThreshold;
    }
}
