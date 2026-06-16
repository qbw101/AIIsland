using System.Text.Json;

namespace ClassIsland.AISmartClass.Services;

/// <summary>
/// 管理离线降级句子库。
/// 当 AI 不可用时，从本地 JSON 中选择合适的预设句子。
/// </summary>
public class FallbackPhraseService
{
    private readonly Dictionary<string, List<string>> _phrases = new();
    private readonly Random _random = new();

    /// <summary>加载降级句子库</summary>
    public void Load(string pluginFolder)
    {
        var path = Path.Combine(pluginFolder, "Data", "fallback_phrases.json");
        if (!File.Exists(path))
        {
            LoadDefaults();
            return;
        }

        try
        {
            var json = File.ReadAllText(path);
            var data = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json);
            if (data != null)
            {
                foreach (var kv in data)
                    _phrases[kv.Key] = kv.Value;
            }
        }
        catch (Exception)
        {
            LoadDefaults();
        }
    }

    /// <summary>根据类别获取随机句子</summary>
    public string GetRandomPhrase(string category, string? parameter = null)
    {
        if (!_phrases.TryGetValue(category, out var list) || list.Count == 0)
            return "准备好迎接下一节课吧~";

        var phrase = list[_random.Next(list.Count)];
        if (parameter != null)
            phrase = phrase.Replace("{subject}", parameter);
        return phrase;
    }

    private void LoadDefaults()
    {
        _phrases["before_class"] = new List<string>
        {
            "下节是 {subject}，调整好状态~",
            "{subject} 准备好了吗？",
            "打起精神，{subject} 要开始啦！",
            "{subject}课加油！",
            "专注时间到，{subject} 来了！",
            "喝口水，整理下桌面，{subject}马上开始~",
            "上节课辛苦了，{subject}继续努力！",
        };

        _phrases["after_school"] = new List<string>
        {
            "今天辛苦了！回顾一下今天的重点，明天继续加油~",
            "放学啦！记得整理好今天的笔记，预习明天的内容。",
            "充实的一天结束了，给自己点个赞！",
            "今天学的内容都掌握了吗？建议睡前过一遍重点。",
            "辛苦了！别忘了劳逸结合，晚上好好休息~",
        };

        _phrases["api_key_missing"] = new List<string>
        {
            "请在设置中配置 AI API Key",
        };

        _phrases["api_error"] = new List<string>
        {
            "AI 暂时不可用，先用预设提醒代替吧~",
        };
    }
}
