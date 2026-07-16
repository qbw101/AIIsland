namespace ClassIsland.AISmartClass.Services;

/// <summary>
/// 手动重新生成请求的中央事件总线。
/// 托盘菜单按钮通过此类广播"重新生成"请求，当前已加载的组件订阅事件并触发刷新。
/// </summary>
public static class AIRegenerationService
{
    private static int _examModeToggleInProgress;
    /// <summary>请求重新生成课表总结（ScheduleInsight / SmartClassPanel 订阅）</summary>
    public static event Action? RegenerateSummaryRequested;

    /// <summary>请求重新生成学习提示（CurrentHint / SmartClassPanel 订阅）</summary>
    public static event Action? RegenerateHintRequested;

    /// <summary>请求触发课前提醒（SmartClassNotifier 订阅）</summary>
    public static event Action? TriggerBeforeClassReminderRequested;

    /// <summary>请求触发放学总结（SmartClassNotifier 订阅）</summary>
    public static event Action? TriggerAfterSchoolSummaryRequested;

    /// <summary>请求重新生成作业量估算（HomeworkEstimate 订阅）</summary>
    public static event Action? RegenerateHomeworkEstimateRequested;

    /// <summary>广播"重新生成课表总结"请求。调用前会清除 AI 缓存以确保获取新内容。</summary>
    public static void RequestRegenerateSummary()
    {
        try
        {
            Plugin.GetAIService()?.ClearCache();
            RegenerateSummaryRequested?.Invoke();
            Logger.Info("[TrayMenu] 已请求重新生成课表总结");
        }
        catch (Exception ex)
        {
            Logger.Info($"[TrayMenu] 请求重新生成课表总结异常: {ex.Message}");
        }
    }

    /// <summary>广播"重新生成学习提示"请求。调用前会清除 AI 缓存以确保获取新内容。</summary>
    public static void RequestRegenerateHint()
    {
        try
        {
            Plugin.GetAIService()?.ClearCache();
            RegenerateHintRequested?.Invoke();
            Logger.Info("[TrayMenu] 已请求重新生成学习提示");
        }
        catch (Exception ex)
        {
            Logger.Info($"[TrayMenu] 请求重新生成学习提示异常: {ex.Message}");
        }
    }

    /// <summary>广播"触发课前提醒"请求。手动操作始终绕过 AI 缓存。</summary>
    public static void RequestTriggerBeforeClassReminder()
    {
        try
        {
            Plugin.GetAIService()?.ClearCache();
            TriggerBeforeClassReminderRequested?.Invoke();
            Logger.Info("[TrayMenu] 已请求触发课前提醒");
        }
        catch (Exception ex)
        {
            Logger.Info($"[TrayMenu] 请求触发课前提醒异常: {ex.Message}");
        }
    }

    /// <summary>广播"触发放学总结"请求。手动操作始终绕过 AI 缓存。</summary>
    public static void RequestTriggerAfterSchoolSummary()
    {
        try
        {
            Plugin.GetAIService()?.ClearCache();
            TriggerAfterSchoolSummaryRequested?.Invoke();
            Logger.Info("[TrayMenu] 已请求触发放学总结");
        }
        catch (Exception ex)
        {
            Logger.Info($"[TrayMenu] 请求触发放学总结异常: {ex.Message}");
        }
    }

    /// <summary>广播"重新生成作业量估算"请求。调用前会清除 AI 缓存。</summary>
    public static void RequestRegenerateHomeworkEstimate()
    {
        try
        {
            Plugin.GetAIService()?.ClearCache();
            RegenerateHomeworkEstimateRequested?.Invoke();
            Logger.Info("[TrayMenu] 已请求重新生成作业量估算");
        }
        catch (Exception ex)
        {
            Logger.Info($"[TrayMenu] 请求重新生成作业量估算异常: {ex.Message}");
        }
    }

    /// <summary>切换考试模式启动状态。启动成功后打开本地仪表盘。</summary>
    public static async Task<bool> ToggleExamModeAsync()
    {
        if (Interlocked.Exchange(ref _examModeToggleInProgress, 1) != 0)
        {
            Logger.Info("[TrayMenu] 考试模式正在切换，忽略重复点击");
            return false;
        }

        try
        {
            var server = ExamModeServer.GetOrCreate();
            if (server.IsRunning)
            {
                server.Stop();
                Logger.Info("[TrayMenu] 已停止考试模式");
                return true;
            }

            server.Enabled = true;
            await server.StartAsync();
            if (!server.IsRunning)
            {
                Logger.Error("[TrayMenu] 考试模式服务器未能启动");
                return false;
            }

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = server.Url,
                UseShellExecute = true
            });
            Logger.Info($"[TrayMenu] 已启动考试模式并打开仪表盘: {server.Url}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"[TrayMenu] 切换考试模式异常: {ex}");
            return false;
        }
        finally
        {
            Interlocked.Exchange(ref _examModeToggleInProgress, 0);
        }
    }
}
