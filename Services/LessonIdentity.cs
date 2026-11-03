namespace ClassIsland.AISmartClass.Services;

/// <summary>
/// 课时内容身份。阶段相同但课程不同（切换时间来源、临时换课、单双周切换）时，
/// 提示类消费者仍需刷新，不能只比较阶段。
/// </summary>
public static class LessonIdentity
{
    /// <summary>生成用于提示刷新的内容身份键。</summary>
    public static string ForHint(LessonSnapshot snapshot)
    {
        var subject = snapshot.Phase switch
        {
            LessonPhase.OnClass => snapshot.CurrentClass?.SubjectId,
            LessonPhase.Breaking or LessonPhase.BeforeFirstClass => snapshot.NextClass?.SubjectId,
            _ => null
        };

        return snapshot.Phase switch
        {
            LessonPhase.OnClass => $"class:{subject}",
            LessonPhase.Breaking => $"next:{subject}",
            LessonPhase.BeforeFirstClass => $"next:{subject}",
            LessonPhase.AfterSchool => "after-school",
            LessonPhase.NoCourses => "no-courses",
            _ => $"phase:{snapshot.Phase}"
        };
    }
}
