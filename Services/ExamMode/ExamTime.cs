using ClassIsland.AISmartClass.Models;

namespace ClassIsland.AISmartClass.Services.ExamMode;

/// <summary>
/// 考试模式专用时钟。考试可以独立选择偏移时间或精确时间，
/// 不跟随也不修改日常课表使用的全局时间来源。
/// </summary>
public static class ExamTime
{
    /// <summary>当前考试时间来源；配置缺失时按偏移时间处理。</summary>
    public static BusinessTimeSource Source =>
        (Plugin.GetAISettings()?.ExamMode?.TimeSource ?? BusinessTimeSource.ClassIslandTime).Normalize();

    /// <summary>当前考试时间。</summary>
    public static DateTime Now => BusinessTime.NowFor(Source);

    /// <summary>当前考试日期，用于跨天课表与日志。</summary>
    public static DateOnly Today => DateOnly.FromDateTime(Now);

    /// <summary>读取完整快照，包含回退状态。</summary>
    public static BusinessTimeSnapshot Read() => BusinessTime.Read(Source);

    /// <summary>用于界面与日志的来源文案。</summary>
    public static string SourceText => Source == BusinessTimeSource.SystemTime ? "精确时间" : "偏移时间";
}
