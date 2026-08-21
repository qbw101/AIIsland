namespace ClassIsland.AISmartClass.Services;

/// <summary>
/// AIChatService 规则兜底方法：难度估算、规则化课表总结、规则化作业量估算。
/// </summary>
public partial class AIChatService
{
    /// <summary>
    /// 根据科目列表估算今日课程难度（1-5星）。
    /// </summary>
    public int EstimateDifficulty(List<string> subjectNames)
    {
        var hardSubjects = new HashSet<string> { "数学", "物理", "化学", "英语" };
        var mediumSubjects = new HashSet<string> { "生物", "地理", "历史", "政治" };

        int score = 0;
        foreach (var s in subjectNames)
        {
            if (hardSubjects.Contains(s)) score += 2;
            else if (mediumSubjects.Contains(s)) score += 1;
        }
        return Math.Clamp((int)Math.Ceiling(score / 2.0), 1, 5);
    }

    /// <summary>
    /// 规则兜底：根据科目类型估算作业量。
    /// </summary>
    private string RuleBasedHomeworkEstimate(List<string> subjects)
    {
        var heavySubjects = new HashSet<string> { "数学", "物理", "化学" };
        var normalSubjects = new HashSet<string> { "语文", "英语", "生物" };
        var lightSubjects = new HashSet<string> { "历史", "地理", "政治" };

        int minutes = 0;
        var heavyList = new List<string>();
        foreach (var s in subjects)
        {
            if (heavySubjects.Contains(s))
            {
                minutes += 45;
                heavyList.Add(s);
            }
            else if (normalSubjects.Contains(s)) minutes += 30;
            else if (lightSubjects.Contains(s)) minutes += 15;
        }

        // 连堂检测：同科目出现两次→翻倍
        foreach (var g in subjects.GroupBy(s => s).Where(g => g.Count() >= 2))
        {
            if (heavySubjects.Contains(g.Key) || normalSubjects.Contains(g.Key))
                minutes += 30;
        }

        minutes = Math.Clamp(minutes, 30, 180);
        var hours = (double)minutes / 60;
        var count = subjects.Count(s =>
            heavySubjects.Contains(s) || normalSubjects.Contains(s) || lightSubjects.Contains(s));

        if (count == 0) return "今天没有主科课程，作业不多~";
        var focus = heavyList.Count > 0 ? $"，{string.Join("和", heavyList)}是重点" : "";

        return $"预计{count}项作业，约{hours:F1}小时{focus}";
    }

    /// <summary>
    /// 规则兜底：根据科目列表生成今日课表总结。
    /// </summary>
    private string GenerateRuleBasedSummary(List<string> subjects)
    {
        int count = subjects.Count;
        if (count == 0) return "今天没有课程安排~";

        var hard = subjects.Count(s => s is "数学" or "物理" or "化学");
        var easy = subjects.Count(s => s is "体育" or "音乐" or "美术" or "班会");

        if (hard >= 3) return $"今天偏理科，{count}节课中有{hard}节硬课，做好心理准备~";
        if (easy >= 2) return $"今天有{easy}节轻松课，相对舒适~";
        return $"{count}节课的一天，加油！";
    }
}
