using ClassIsland.AISmartClass.Models.Automation;
using ClassIsland.AISmartClass.Services.NotificationProviders;
using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;

namespace ClassIsland.AISmartClass.Services.Automation.Actions;

[ActionInfo(
    "aiisland.generate-ai-notification",
    "生成 AIIsland 贴心提醒",
    "\ue8bd",
    addDefaultToMenu: true,
    defaultGroupToMenu: "AIIsland")]
public class GenerateAiNotificationAction : ActionBase<GenerateAiNotificationSettings>
{
    protected override async Task OnInvoke()
    {
        await base.OnInvoke();

        var ai = Plugin.GetAIService()
            ?? throw new InvalidOperationException("AIIsland AI 服务尚未就绪。");
        if (Settings.BypassCache)
        {
            ai.ClearCache();
        }

        var profileService = Plugin.ProfileService
            ?? throw new InvalidOperationException("ClassIsland 课表服务尚未就绪。");
        var lessonsService = Plugin.LessonsService
            ?? throw new InvalidOperationException("ClassIsland 课程服务尚未就绪。");
        var notifier = Plugin.SmartClassNotifierInstance
            ?? throw new InvalidOperationException("AIIsland 智能提醒提供方尚未就绪。");

        var todaySubjects = ScheduleQueryHelper.GetTodaySubjectNames(profileService, distinct: false);
        var plan = ScheduleQueryHelper.GetActivePlan(profileService);
        var now = DateTime.Now.TimeOfDay;
        var currentClass = plan == null ? null : ScheduleQueryHelper.GetCurrentClass(plan, now);
        var nextClass = plan == null ? null : ScheduleQueryHelper.GetNextClass(plan, now);
        var currentSubject = ScheduleQueryHelper.NormalizeSubjectName(
            currentClass == null
                ? lessonsService.CurrentSubject?.Name
                : ScheduleQueryHelper.GetSubjectName(profileService, currentClass.SubjectId),
            "无");
        var nextSubject = ScheduleQueryHelper.NormalizeSubjectName(
            nextClass == null
                ? lessonsService.NextClassSubject?.Name
                : ScheduleQueryHelper.GetSubjectName(profileService, nextClass.SubjectId),
            "无");
        var firstClass = plan == null ? null : ThoughtfulReminderTiming.GetFirstClass(plan);
        var firstSubject = ScheduleQueryHelper.NormalizeSubjectName(
            firstClass == null ? null : ScheduleQueryHelper.GetSubjectName(profileService, firstClass.SubjectId),
            "第一节课");
        var hintContext = ScheduleQueryHelper.GetLearningHintContext(profileService, todaySubjects);

        try
        {
            var thoughtfulContext = Settings.Scenario switch
            {
                AiNotificationScenario.BeforeSchoolReminder => await notifier.BuildThoughtfulContextAsync(
                    ThoughtfulScene.DailyBriefing, InterruptCancellationToken),
                AiNotificationScenario.BeforeClassReminder => await notifier.BuildThoughtfulContextAsync(
                    ThoughtfulScene.BreakStart, InterruptCancellationToken),
                AiNotificationScenario.AfterSchoolSummary => await notifier.BuildThoughtfulContextAsync(
                    ThoughtfulScene.AfterSchool, InterruptCancellationToken),
                _ => null
            };

            var result = Settings.Scenario switch
            {
                AiNotificationScenario.CurrentHint => await ai.GenerateLearningHintStream(
                    hintContext.Scene, hintContext.Focus, _ => { }, InterruptCancellationToken, throwOnError: true),
                AiNotificationScenario.BeforeSchoolReminder => await ai.GenerateDailyBriefing(
                    notifier.GetTodayBriefingClasses(), InterruptCancellationToken,
                    throwOnError: true, context: thoughtfulContext),
                AiNotificationScenario.BeforeClassReminder => await ai.GenerateBeforeClassReminder(
                    string.IsNullOrWhiteSpace(currentSubject) ? "今日课程" : currentSubject,
                    string.IsNullOrWhiteSpace(nextSubject) ? "下一节课" : nextSubject,
                    InterruptCancellationToken,
                    throwOnError: true,
                    context: thoughtfulContext),
                AiNotificationScenario.TodaySummary => await ai.SummarizeToday(
                    todaySubjects, InterruptCancellationToken, throwOnError: true),
                AiNotificationScenario.AfterSchoolSummary => await ai.GenerateDailySummary(
                    todaySubjects, InterruptCancellationToken, throwOnError: true, context: thoughtfulContext),
                AiNotificationScenario.HomeworkEstimate => await ai.EstimateHomeworkLoad(
                    todaySubjects, InterruptCancellationToken, throwOnError: true),
                AiNotificationScenario.CustomPrompt => await GenerateCustomPromptAsync(
                    ai,
                    currentSubject,
                    nextSubject,
                    todaySubjects,
                    hintContext.Scene),
                _ => throw new InvalidOperationException("不支持的 AI 生成场景。")
            };

            InterruptCancellationToken.ThrowIfCancellationRequested();
            await notifier.ShowAutomationNotificationAsync(
                string.IsNullOrWhiteSpace(Settings.NotificationTitle)
                    ? "AIIsland 智能提醒"
                    : Settings.NotificationTitle.Trim(),
                result,
                false,
                InterruptCancellationToken);
        }
        catch (OperationCanceledException) when (InterruptCancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "AI 智能生成并提醒失败");
            await notifier.ShowAutomationNotificationAsync(
                string.IsNullOrWhiteSpace(Settings.NotificationTitle)
                    ? "AIIsland 智能提醒"
                    : Settings.NotificationTitle.Trim(),
                GenerateAiNotificationAction.FormatCustomPromptError(ex),
                false,
                InterruptCancellationToken);
        }
    }

    private async Task<string> GenerateCustomPromptAsync(
        AIChatService ai,
        string currentSubject,
        string nextSubject,
        IReadOnlyList<string> todaySubjects,
        string timeState)
    {
        try
        {
            return await ai.ChatAsync(
                "你是 AIIsland 智能学习助手，请按用户指令直接给出简洁、适合通知展示的中文结果。",
                BuildCustomPrompt(currentSubject, nextSubject, todaySubjects, timeState),
                ct: InterruptCancellationToken,
                throwOnError: true);
        }
        catch (OperationCanceledException) when (InterruptCancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "自动化自定义指令 AI 调用失败");
            return FormatCustomPromptError(ex);
        }
    }

    public static string FormatCustomPromptError(Exception exception)
    {
        var message = exception.Message.Trim();
        return string.IsNullOrWhiteSpace(message)
            ? "AI 调用失败：未知错误"
            : $"AI 调用失败：{message}";
    }

    private string BuildCustomPrompt(
        string currentSubject,
        string nextSubject,
        IReadOnlyList<string> todaySubjects,
        string timeState)
    {
        var subjectsText = todaySubjects.Count == 0 ? "无" : string.Join("、", todaySubjects);
        var prompt = (Settings.CustomPrompt ?? "")
            .Replace("{currentSubject}", currentSubject, StringComparison.Ordinal)
            .Replace("{nextSubject}", nextSubject, StringComparison.Ordinal)
            .Replace("{todaySubjects}", subjectsText, StringComparison.Ordinal)
            .Replace("{timeState}", timeState, StringComparison.Ordinal)
            .Replace("{currentTime}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), StringComparison.Ordinal);

        if (!Settings.IncludeScheduleContext)
        {
            return prompt;
        }

        return $"{prompt}\n\n课表上下文：\n当前科目：{currentSubject}\n下一科目：{nextSubject}\n今日科目：{subjectsText}\n当前状态：{timeState}\n当前时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}";
    }
}
