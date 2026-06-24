using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.AISmartClass.Services;
using ClassIsland.Core.Attributes;
using ClassIsland.AISmartClass.Models;

namespace ClassIsland.AISmartClass.Controls.CurrentHint;

[ComponentInfo(
    "11000000-0000-0000-0000-000000000004",
    "AIIsland 课程提示",
    "bitmap(avares://ClassIsland.AISmartClass/icon/4.png)",
    "上课时自动生成当前课程学习提示"
)]
public partial class CurrentHint : ComponentBase<CurrentHintSettings>
{
    public static readonly DirectProperty<CurrentHint, string> HintProperty =
        AvaloniaProperty.RegisterDirect<CurrentHint, string>(nameof(Hint),
            o => o.Hint, (o, v) => o.Hint = v);
    private string _hint = "等待课程开始...";
    public string Hint { get => _hint; set => SetAndRaise(HintProperty, ref _hint, value); }

    private bool _loaded = false;

    public CurrentHint()
    {
        DataContext = this;
        InitializeComponent();
    }
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    protected override void OnLoaded(RoutedEventArgs e)
    {
        DataContext = this;
        base.OnLoaded(e);
        var ls = Plugin.LessonsService;
        if (ls != null) ls.OnClass += OnClassHandler;
        _ = RefreshAsync();
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        var ls = Plugin.LessonsService;
        if (ls != null) ls.OnClass -= OnClassHandler;
    }

    private void OnClassHandler(object? sender, EventArgs e) { _loaded = false; _ = RefreshAsync(); }

    private async Task RefreshAsync()
    {
        if (_loaded) return;
        try
        {
            var ps = Plugin.ProfileService;
            var ai = Plugin.GetAIService();
            if (ps == null || ai == null) return;

            var current = GetCurrentSubjectName(ps);
            var userMsg = string.IsNullOrEmpty(current) ? "请给一句15字以内的学习提示。" : $"当前课程：{current}\n请给一句15字以内的学习提示。";
            var systemPrompt = PromptTemplates.GetCurrentHintSystem(ai.ToneStyle);
            var result = await ai.ChatAsync(systemPrompt, userMsg);
            Hint = string.IsNullOrEmpty(result) || result.Contains("AI 暂时不可用") ? "" : result;
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"课程提示失败: {ex.Message}"); }
    }

    private static string GetCurrentSubjectName(ClassIsland.Core.Abstractions.Services.IProfileService ps)
    {
        try
        {
            if (ps?.Profile == null) return "";
            var plan = ps.Profile.ClassPlans.Values.FirstOrDefault(p => p.IsActivated && p.IsEnabled);
            if (plan == null) return "";
            var now = TimeSpan.FromTicks(DateTime.Now.TimeOfDay.Ticks);
            var cls = ScheduleQueryHelper.GetClassAtTime(plan, now);
            if (cls != null) return ScheduleQueryHelper.GetSubjectName(ps, cls.SubjectId);
            var next = ScheduleQueryHelper.GetNextClass(plan, now);
            if (next != null) return ScheduleQueryHelper.GetSubjectName(ps, next.SubjectId);
        }
        catch { }
        return "";
    }
}
