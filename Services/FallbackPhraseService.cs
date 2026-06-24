using System.Text.Json;

namespace ClassIsland.AISmartClass.Services;

/// <summary>
/// 管理离线降级句子库。
/// 支持 3 种语气风格（活泼/标准/严肃），当 AI 不可用时根据当前语气风格选择合适的预设句子。
/// </summary>
public class FallbackPhraseService
{
    // toneStyle → category → phrases
    private readonly Dictionary<int, Dictionary<string, List<string>>> _tonePhrases = new();
    private readonly Random _random = new();

    /// <summary>当前语气风格（0=活泼，1=标准，2=严肃）</summary>
    public int ToneStyle { get; set; } = 1;

    /// <summary>加载降级句子库（所有语气风格）</summary>
    public void Load(string pluginFolder)
    {
        // 尝试加载 3 套语气 JSON 文件
        LoadToneFile(pluginFolder, 0, "fallback_phrases_lively.json");
        LoadToneFile(pluginFolder, 1, "fallback_phrases_normal.json");
        LoadToneFile(pluginFolder, 2, "fallback_phrases_serious.json");

        // 对任何未加载的语气风格使用兜底
        for (int i = 0; i <= 2; i++)
        {
            if (!_tonePhrases.ContainsKey(i) || _tonePhrases[i].Count == 0)
                _tonePhrases[i] = GetDefaultPhrases(i);
        }
    }

    /// <summary>根据类别和当前语气风格获取随机句子</summary>
    public string GetRandomPhrase(string category, string? parameter = null)
    {
        var phrases = _tonePhrases.GetValueOrDefault(ToneStyle);
        if (phrases == null)
            phrases = GetDefaultPhrases(ToneStyle);

        if (!phrases.TryGetValue(category, out var list) || list.Count == 0)
            return ToneStyle switch
            {
                0 => "准备好迎接下一节课吧~ OvO",
                2 => "请准备下一节课程。",
                _ => "准备好迎接下一节课吧~"
            };

        var phrase = list[_random.Next(list.Count)];
        if (parameter != null)
            phrase = phrase.Replace("{subject}", parameter);
        return phrase;
    }

    private void LoadToneFile(string pluginFolder, int toneStyle, string fileName)
    {
        var path = Path.Combine(pluginFolder, "Data", fileName);
        if (!File.Exists(path)) return;

        try
        {
            var json = File.ReadAllText(path);
            var data = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json);
            if (data != null && data.Count > 0)
                _tonePhrases[toneStyle] = data;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Fallback] 加载 {fileName} 失败: {ex.Message}");
        }
    }

    private static Dictionary<string, List<string>> GetDefaultPhrases(int toneStyle)
    {
        return toneStyle switch
        {
            0 => new Dictionary<string, List<string>>
            {
                ["before_class"] = new()
                {
                    "下节是 {subject}，元气满满冲鸭！OvO",
                    "{subject} 来啦！准备好接招了吗~",
                    "大脑切换！{subject} 模式 ON！",
                    "{subject}课冲呀！今天也要当卷王！",
                    "专注 BOSS 战：{subject}，开打！",
                    "喝口水续个命，{subject}马上开始~",
                    "上节课辛苦了！{subject}继续肝！",
                },
                ["after_school"] = new()
                {
                    "今天太强啦！奖励自己一杯奶茶吧~ OvO",
                    "放学！今天的你也超棒的！摸摸头~",
                    "充实的一天结束了，给自己点个大大的赞！",
                    "今天的内容都拿下了吗？睡前可以快速过一遍~",
                    "辛苦了！记得劳逸结合，今晚睡个好觉！",
                },
                ["api_key_missing"] = new()
                {
                    "还没有配置 AI 小伙伴哦~ 在设置里填一下 API Key 吧！",
                },
                ["api_error"] = new()
                {
                    "AI 小助手暂时摸鱼中...先用预设提醒凑合下吧~",
                },
            },
            2 => new Dictionary<string, List<string>>
            {
                ["before_class"] = new()
                {
                    "下一节为 {subject}，请做好准备。",
                    "{subject}课程即将开始，请回到座位。",
                    "{subject}时间到，请保持专注。",
                    "请整理学习用品，{subject}马上开始。",
                    "{subject}课程现在开始，请注意听讲。",
                    "课间结束，请进入{subject}学习状态。",
                    "上一节已结束，请切换到{subject}。",
                },
                ["after_school"] = new()
                {
                    "今日课程结束，请回顾今日所学重点内容。",
                    "放学后建议整理课堂笔记，预习明日课程。",
                    "今日学习任务完成，请做好总结和反思。",
                    "今日内容是否已全部掌握？建议睡前复习重点。",
                    "请注意劳逸结合，合理安排晚间学习时间。",
                },
                ["api_key_missing"] = new()
                {
                    "未配置 AI API Key，请在设置中填写。",
                },
                ["api_error"] = new()
                {
                    "AI 服务不可用，将使用预设提醒。",
                },
            },
            _ => new Dictionary<string, List<string>>
            {
                ["before_class"] = new()
                {
                    "下节是 {subject}，调整好状态~",
                    "{subject} 准备好了吗？",
                    "打起精神，{subject} 要开始啦！",
                    "{subject}课加油！",
                    "专注时间到，{subject} 来了！",
                    "喝口水，整理下桌面，{subject}马上开始~",
                    "上节课辛苦了，{subject}继续努力！",
                },
                ["after_school"] = new()
                {
                    "今天辛苦了！回顾一下今天的重点，明天继续加油~",
                    "放学啦！记得整理好今天的笔记，预习明天的内容。",
                    "充实的一天结束了，给自己点个赞！",
                    "今天学的内容都掌握了吗？建议睡前过一遍重点。",
                    "辛苦了！别忘了劳逸结合，晚上好好休息~",
                },
                ["api_key_missing"] = new()
                {
                    "请在设置中配置 AI API Key",
                },
                ["api_error"] = new()
                {
                    "AI 暂时不可用，先用预设提醒代替吧~",
                },
            },
        };
    }
}
