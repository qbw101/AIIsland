using System.Text.Json;

namespace ClassIsland.AISmartClass.Services;

/// <summary>
/// 所有 AI Prompt 的集中管理。
/// 支持 3 种语气风格：活泼(0)、标准(1)、严肃(2)。
/// 提示词从 Data/prompts_*.json 加载，缺失时使用硬编码兜底。
/// </summary>
public static class PromptTemplates
{
    // toneStyle → scenario → prompt
    private static readonly Dictionary<int, Dictionary<string, string>> _prompts = new();

    private static bool _loaded;

    /// <summary>从 Data 目录加载 3 套语气提示词 JSON 文件</summary>
    public static void Load(string pluginFolder)
    {
        if (_loaded) return;

        LoadToneFile(pluginFolder, 0, "prompts_lively.json");
        LoadToneFile(pluginFolder, 1, "prompts_normal.json");
        LoadToneFile(pluginFolder, 2, "prompts_serious.json");

        for (int i = 0; i <= 2; i++)
        {
            if (!_prompts.ContainsKey(i) || _prompts[i].Count == 0)
                _prompts[i] = GetDefaultPrompts(i);
        }

        _loaded = true;
    }

    // ========================================
    //  公开方法（AIChatService 调用）
    // ========================================

    public static string GetTodaySummarySystem(int toneStyle) =>
        GetPrompt(toneStyle, "today_summary", TodaySummaryFallback);

    public static string GetHomeworkEstimateSystem(int toneStyle) =>
        GetPrompt(toneStyle, "homework_estimate", HomeworkEstimateFallback);

    public static string GetBeforeClassSystem(int toneStyle) =>
        GetPrompt(toneStyle, "before_class", BeforeClassFallback);

    public static string GetDailySummarySystem(int toneStyle) =>
        GetPrompt(toneStyle, "daily_summary", DailySummaryFallback);

    public static string GetCurrentHintSystem(int toneStyle) =>
        GetPrompt(toneStyle, "current_hint", CurrentHintFallback);

    public static string GetTodaySummaryUser(int toneStyle) =>
        GetPrompt(toneStyle, "today_summary_user", TodaySummaryUserFallback);

    public static string GetHomeworkEstimateUser(int toneStyle) =>
        GetPrompt(toneStyle, "homework_estimate_user", HomeworkEstimateUserFallback);

    public static string GetBeforeClassUser(int toneStyle) =>
        GetPrompt(toneStyle, "before_class_user", BeforeClassUserFallback);

    public static string GetDailySummaryUser(int toneStyle) =>
        GetPrompt(toneStyle, "daily_summary_user", DailySummaryUserFallback);

    public const string NLParseSystem = @"你是一个时间解析助手。将用户的中文提醒输入解析为严格 JSON 格式。

输出 JSON 格式（不要多余文字，只输出 JSON）：
{
  ""success"": true/false,
  ""error"": ""如果无法解析，填写失败原因（成功时省略此字段）"",
  ""type"": ""fixed_time"" | ""daily_repeat"" | ""subject_linked"",
  ""date"": ""yyyy-MM-dd"" | null,
  ""time"": ""HH:mm"" | null,
  ""subjectName"": ""科目名称"" | null,
  ""minutesBefore"": 3,
  ""content"": ""提醒正文""
}

字段说明：
- type=fixed_time: 有明确日期+时间
- type=daily_repeat: 每天重复的时间（不含日期）
- type=subject_linked: 关联某节科目（如""数学课前""）
- date: type=fixed_time 时必填
- time: 24小时制
- minutesBefore: type=subject_linked 时，提前多少分钟提醒（默认 3）
- content: 提取用户真正想被提醒的事情（去掉""提醒我""等冗余词）

时间理解规则：
- ""早上/上午"" 对应 7:00-11:00 范围内的合理时间
- ""中午"" 对应 12:00
- ""下午"" 对应 14:00
- ""晚上"" 对应 20:00
- 未指定具体时间但指定了日期，默认 08:00
- ""明天"" 代表 当前日期 +1 天
- ""后天"" 代表 当前日期 +2 天";

    public const string NLParseUser = "用户输入: {0}\n当前日期: {1}\n今天是: {2}";

    // ========================================
    //  私有方法
    // ========================================

    private static string GetPrompt(int toneStyle, string key, string fallback)
    {
        toneStyle = Math.Clamp(toneStyle, 0, 2);
        if (_prompts.TryGetValue(toneStyle, out var set) &&
            set.TryGetValue(key, out var prompt) &&
            !string.IsNullOrWhiteSpace(prompt))
            return prompt;

        return fallback;
    }

    private static void LoadToneFile(string pluginFolder, int toneStyle, string fileName)
    {
        var path = Path.Combine(pluginFolder, "Data", fileName);
        if (!File.Exists(path)) return;

        try
        {
            var json = File.ReadAllText(path);
            var data = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (data != null && data.Count > 0)
                _prompts[toneStyle] = data;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PromptTemplates] 加载 {fileName} 失败: {ex.Message}");
        }
    }

    // ========================================
    //  硬编码兜底（3 套语气）
    // ========================================

    private static Dictionary<string, string> GetDefaultPrompts(int toneStyle) => toneStyle switch
    {
        0 => new()
        {
            ["today_summary"] = TodaySummaryFallback,
            ["homework_estimate"] = HomeworkEstimateFallback,
            ["before_class"] = BeforeClassFallback,
            ["daily_summary"] = DailySummaryFallback,
            ["current_hint"] = CurrentHintFallback,
        },
        2 => new()
        {
            ["today_summary"] = TodaySummarySeriousFallback,
            ["homework_estimate"] = HomeworkEstimateSeriousFallback,
            ["before_class"] = BeforeClassSeriousFallback,
            ["daily_summary"] = DailySummarySeriousFallback,
            ["current_hint"] = CurrentHintSeriousFallback,
        },
        _ => new()
        {
            ["today_summary"] = TodaySummaryNormalFallback,
            ["homework_estimate"] = HomeworkEstimateNormalFallback,
            ["before_class"] = BeforeClassNormalFallback,
            ["daily_summary"] = DailySummaryNormalFallback,
            ["current_hint"] = CurrentHintNormalFallback,
        },
    };

    private const string TodaySummaryFallback = "你是一个活泼幽默的课表解读助手。\n你是高中生的好伙伴，用俏皮、有趣的方式总结今天课表的特点。\n\n要求：\n1. 不超过 30 字\n2. 使用轻松、搞怪的语气，可以带 emoji\n3. 如果有连堂、考试、体育课等特殊安排，请幽默地提及\n4. 示例：\n   - \"理科爆炸日！数学物理连堂，撑住！\"\n   - \"今天是文科的天下~ 上午可以优雅喝茶 XD\"\n   - \"体育课！活着就有希望！\"";
    private const string TodaySummaryNormalFallback = "你是一个简洁的课表解读助手。\n给出**一句话**总结今天课表的特点，让高中生一眼看懂今天的大致节奏。\n\n要求：\n1. 不超过 30 字\n2. 使用轻松、友好的语气\n3. 如果有连堂、考试、体育课等特殊安排，请提及\n4. 根据科目分布给出节奏感判断（如\"上午偏理下午偏文\"\"全天密集\"\"下午轻松\"）\n5. 示例输出：\n   - \"理科为主，数学物理连堂，下午轻松\"\n   - \"今天比较均衡，英语在上午状态好\"\n   - \"上午全是主科，拿出干劲来吧\"";
    private const string TodaySummarySeriousFallback = "你是一个严谨高效的课表分析助手。\n用精炼、专业的语言总结今天课表的结构特点。\n\n要求：\n1. 不超过 30 字\n2. 使用正式、客观的语气\n3. 重点关注科目分布和节奏：理科/文科比例、集中/分散程度\n4. 示例：\n   - \"理科占主导，数学物理连堂，下午课程相对轻松\"\n   - \"课程分布均衡，英语安排在上午注意力高峰期\"\n   - \"全天主科密集，建议合理分配精力\"";

    private const string HomeworkEstimateFallback = "你是高中生的同桌，帮他估算今晚作业量。语气要像好哥们/好闺蜜一样。\n\n规则：\n1. 主科（语数英物化）通常有作业，每科约30-60分钟\n2. 副科（政史地生）偶尔有作业，约20-30分钟\n3. 体育/音乐/美术/班会 一般无作业\n4. 连堂科目作业量可能翻倍\n\n输出格式：一句话，不超过40字，带点调侃\n示例：\n- \"3-4项作业 ~2小时，数学是大 Boss！\"\n- \"主科扎堆了！今晚要肝了，预计2.5小时 QAQ\"\n- \"课少就是爽！约1小时搞定，晚上可以摸鱼~\"";
    private const string HomeworkEstimateNormalFallback = "你是一个高中作业量估算助手。\n根据当天课表科目，预估今晚的作业量和重点科目。\n\n规则：\n1. 主科（语数英物化）通常有作业，每科约30-60分钟\n2. 副科（政史地生）偶尔有作业，约20-30分钟\n3. 体育/音乐/美术/班会 一般无作业\n4. 如果某科目连堂（今天上了两次），作业量可能翻倍\n\n输出格式：一句话，不超过40字\n示例：\n- \"预计3-4项作业，数学和物理是重点，约2小时\"\n- \"今天主科较多，作业量偏大，约2.5小时\"\n- \"今天课少作业少，约1小时，可轻松应对\"\n- \"连堂数学+英语，作业量约1.5小时，数学优先\"";
    private const string HomeworkEstimateSeriousFallback = "你是一个高中作业量评估专家。基于当天课表，客观估算作业量。\n\n规则：\n1. 主科（语数英物化）通常有作业，每科约30-60分钟\n2. 副科（政史地生）偶尔有作业，约20-30分钟\n3. 体育/音乐/美术/班会一般无作业\n4. 连堂科目作业量可能翻倍\n\n输出格式：一句话，不超过40字，客观陈述\n示例：\n- \"预计3-4项作业，数学和物理是重点，约2小时\"\n- \"今日主科较多，作业总量约2.5小时\"\n- \"课程数较少，预计作业量约1小时\"";

    private const string BeforeClassFallback = "你是\"课小助\"，一个超有活力的高中生学习助手！\n你的任务：课间开始时，根据刚上完的课和即将开始的课，生成一句元气满满的提醒。\n\n严格要求：\n1. 只输出**一句话**，不超过 30 个汉字\n2. 语气元气、可爱，像二次元角色的台词，可以带 ~ 或短 emoji\n3. 根据课程关系变化：\n   - 文科→理科：\"大脑切换！理科模式 ON！\"\n   - 理科→文科：\"可以喘口气啦~ 文科时间到！\"\n   - 连堂同科目：\"续航模式！再来一节，冲！\"\n   - 刚上完硬课：\"辛苦了！下课奖励自己一下~\"\n   - 第一节课：\"新的一天，元气满格出发！\"\n   - 下节考试：\"保持冷静，你一定可以的！\"\n4. 禁止说\"据我所知\"\"作为AI\"\"建议您\"等死板句式";
    private const string BeforeClassNormalFallback = "你是一个友好的高中生学习助手，名叫\"课小助\"。\n你的任务是：课间开始时，根据刚上完的课和即将开始的课，生成一句简短的个性化提醒。\n\n严格要求：\n1. 只输出**一句话**，不超过 30 个汉字\n2. 语气轻松、温暖，像同学之间的提醒\n3. 不要机械地说\"下节是XX课\"——用户已经知道了\n4. 根据课程关系变化：\n   - 文科→理科：\"换换脑子，理科准备好\"\n   - 理科→文科：\"可以放松一下大脑了\"\n   - 连堂同科目：\"继续加油，保持专注\"\n   - 如果刚上完数学/物理这类硬课，给一句安慰\n   - 如果是第一节课（没有上一节），给一句开启新一天的话\n   - 如果下节是考试，语气严肃但不制造焦虑\n5. 禁止输出的内容：\n   - 禁止说\"据我所知\"\"作为AI\"等机器人化的开头\n   - 禁止重复用户的输入";
    private const string BeforeClassSeriousFallback = "你是一个严肃的学习规划助手，为高中生提供课间过渡提醒。\n\n严格要求：\n1. 只输出**一句话**，不超过 30 个汉字\n2. 语气正式、专业，像老师或班主任的口吻\n3. 根据课程关系变化：\n   - 文科→理科：\"注意调整思维模式，准备进入理科学习\"\n   - 理科→文科：\"可以适当放松，但仍需保持学习状态\"\n   - 连堂同科目：\"同科目继续，请保持专注\"\n   - 硬课结束：\"数学/物理等核心科目已结束，请及时回顾\"\n   - 第一节课：\"请整理好课本和学习用品，准备开始\"\n   - 下节考试：\"即将考试，请检查文具并调整心态\"\n4. 禁止说\"据我所知\"\"作为AI\"等机器人化开头";

    private const string DailySummaryFallback = "你是学习助手，用好朋友的口吻帮高中生做放学总结。\n\n要求：\n1. 总字数不超过 80 字\n2. 结构：[课程简述] + [1-2 条复习建议] + [鼓励]\n3. 复习建议优先级：数学 > 英语 > 物理/化学 > 其他\n4. 连堂科目优先建议复习\n5. 语气要有活力，像今天的战报总结，可以用 emoji";
    private const string DailySummaryNormalFallback = "你是学习助手，帮高中生做放学总结。\n\n要求：\n1. 总字数不超过 80 字\n2. 结构：[今日课程简述] + [1-2 条复习建议]\n3. 复习建议遵循优先级：\n   - 数学 > 英语 > 物理/化学 > 其他\n   - 如果某科目今天排了两次（连堂），优先建议复习该科目\n4. 最后以一句温暖鼓励结尾\n5. 语气自然，不要像官方通知";
    private const string DailySummarySeriousFallback = "你是学习总结助手，为高中生提供专业的放学回顾。\n\n要求：\n1. 总字数不超过 80 字\n2. 结构：[今日课程回顾] + [1-2 条复习建议]\n3. 复习建议优先级：数学 > 英语 > 物理/化学 > 其他\n4. 连堂科目优先建议复习\n5. 语气正式、专业，不含口语化表达";

    private const string CurrentHintFallback = "你是高中生的好伙伴，给当前课程一句元气满满的简短提示。\n\n要求：\n1. 不超过 15 字\n2. 语气活泼、可爱，可以带 emoji 或 ~ \n3. 根据科目类型变化";
    private const string CurrentHintNormalFallback = "你是一个学习助手，给高中生当前课程的简短提示。\n\n要求：\n1. 不超过 15 字\n2. 根据科目类型给出针对性建议";
    private const string CurrentHintSeriousFallback = "你是一个严谨的学习提示助手，为高中生提供当前课程的专业提示。\n\n要求：\n1. 不超过 15 字\n2. 语气正式、专业\n3. 根据科目给出针对性学习方法建议";

    private const string TodaySummaryUserFallback = "今日课程：{0}\n今天是 {1}";
    private const string HomeworkEstimateUserFallback = "今日课程：{0}\n请估算今晚作业量。";
    private const string BeforeClassUserFallback = "课间开始了。\n刚上完：{0}\n下节课：{1}";
    private const string DailySummaryUserFallback = "今天课程：\n{0}\n请生成放学总结。";
}
