namespace ClassIsland.AISmartClass.Services;

/// <summary>
/// 业务时钟基线。供按 tick 比较“上次检查 → 本次”的消费者使用：
/// 首次取样只建立基线；检测到回拨或大幅前跳（切换时间来源、调整偏移、休眠恢复）时重建基线，
/// 让调用方跳过该轮，避免把跳变当成自然状态迁移而补发陈旧提醒。
/// </summary>
public sealed class BusinessClockBaseline
{
    private DateTime _last;
    private bool _initialized;

    /// <summary>上一次观察到的业务时间。</summary>
    public DateTime Last => _last;

    /// <summary>
    /// 观察当前业务时间。
    /// </summary>
    /// <returns>true 表示检测到时间跳变并已重建基线，调用方应跳过本轮业务判断。</returns>
    public bool Observe(DateTime now)
    {
        if (!_initialized)
        {
            _initialized = true;
            _last = now;
            return false;
        }

        if (BusinessClockContinuity.ShouldResetBaseline(_last, now))
        {
            _last = now;
            return true;
        }

        _last = now;
        return false;
    }
}
