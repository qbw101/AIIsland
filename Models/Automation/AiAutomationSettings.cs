using System.ComponentModel;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClassIsland.AISmartClass.Models.Automation;

public enum AiNotificationScenario
{
    [Description("当前学习提示")]
    CurrentHint,
    [Description("智能每日简报")]
    BeforeSchoolReminder,
    [Description("课间贴心提醒")]
    BeforeClassReminder,
    [Description("今日课表总结")]
    TodaySummary,
    [Description("放学贴心总结")]
    AfterSchoolSummary,
    [Description("作业量估算")]
    HomeworkEstimate,
    [Description("自定义指令")]
    CustomPrompt
}

public enum AiIslandRefreshTarget
{
    [Description("课表总结")]
    ScheduleSummary,
    [Description("学习提示")]
    LearningHint,
    [Description("作业量估算")]
    HomeworkEstimate,
    [Description("全部组件")]
    All
}

public enum AiIslandReminder
{
    [Description("智能每日简报")]
    BeforeSchool,
    [Description("课间贴心提醒")]
    BeforeClass,
    [Description("放学贴心总结")]
    AfterSchool
}

public enum AiIslandExamMode
{
    [Description("启动考试模式")]
    Start,
    [Description("停止考试模式")]
    Stop
}

public partial class GenerateAiNotificationSettings : ObservableObject
{
    [ObservableProperty]
    [property: JsonPropertyName("scenario")]
    private AiNotificationScenario _scenario = AiNotificationScenario.CurrentHint;

    [JsonIgnore]
    public bool IsCustomPromptScenario => Scenario == AiNotificationScenario.CustomPrompt;

    partial void OnScenarioChanged(AiNotificationScenario value)
    {
        OnPropertyChanged(nameof(IsCustomPromptScenario));
    }

    [ObservableProperty]
    [property: JsonPropertyName("customPrompt")]
    private string _customPrompt = "";

    [ObservableProperty]
    [property: JsonPropertyName("includeScheduleContext")]
    private bool _includeScheduleContext = true;

    [ObservableProperty]
    [property: JsonPropertyName("bypassCache")]
    private bool _bypassCache = true;

    [ObservableProperty]
    [property: JsonPropertyName("notificationTitle")]
    private string _notificationTitle = "AIIsland 智能提醒";
}

public partial class RefreshAiIslandComponentsSettings : ObservableObject
{
    [ObservableProperty]
    [property: JsonPropertyName("target")]
    private AiIslandRefreshTarget _target = AiIslandRefreshTarget.All;
}

public partial class TriggerAiIslandReminderSettings : ObservableObject
{
    [ObservableProperty]
    [property: JsonPropertyName("reminder")]
    private AiIslandReminder _reminder = AiIslandReminder.BeforeClass;

    [ObservableProperty]
    [property: JsonPropertyName("bypassCache")]
    private bool _bypassCache = true;
}

public partial class SetAiIslandExamModeSettings : ObservableObject
{
    [ObservableProperty]
    [property: JsonPropertyName("mode")]
    private AiIslandExamMode _mode = AiIslandExamMode.Start;

    [ObservableProperty]
    [property: JsonPropertyName("openDashboard")]
    private bool _openDashboard = true;
}
