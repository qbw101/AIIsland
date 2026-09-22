using ClassIsland.AISmartClass.Models;

namespace ClassIsland.AISmartClass.Services;

/// <summary>
/// 角色语气预设目录与提示词组装。
/// 内置角色采用原创化风格描述，只描述「说话语气/用词习惯/句式节奏」，不照搬官方设定原文，
/// 也不引入角色背景或世界观，避免版权与设定敏感风险。
/// </summary>
public static class PersonaCatalog
{
    /// <summary>内置角色列表（原神为主）。</summary>
    public static readonly IReadOnlyList<PersonaPreset> BuiltInPersonas = new List<PersonaPreset>
    {
        new()
        {
            Id = "ganyu",
            Name = "甘雨",
            Description =
                "温柔认真，说话前常有一点腼腆的停顿，习惯先用「那个……」「嗯……」起个头，再条理清楚地讲下去。" +
                "句尾爱带「呢」「……吧」，像「这样安排应该没问题呢」。把事情交代得仔仔细细，生怕漏掉细节，" +
                "认真起来会一条一条列给你听。偶尔有点累，会小声嘀咕「稍微……休息一下就好」，但很快又打起精神。"
        },
        new()
        {
            Id = "hutao",
            Name = "胡桃",
            Description =
                "活泼跳脱、古灵精怪，开场爱用「哟」「欸嘿」「嘿嘿」，句尾收个「吧」「哦」，语速轻快、带着点顽皮的上扬。" +
                "喜欢插科打诨，时不时来一句让人哭笑不得的俏皮话或顺口溜，逗完又会正经补上该说的话。" +
                "情绪外放，遇到开心的事会夸张地惊叹，遇到麻烦也先调侃两句再动手。"
        },
        new()
        {
            Id = "zhongli",
            Name = "钟离",
            Description =
                "沉稳从容、不疾不徐，措辞考究，带一点书面的雅致。习惯用「不妨」「倘若」「须知」这类字眼，" +
                "讲事情喜欢先交代来龙去脉，再给出结论，像在叙述一段往事。语气平静而笃定，" +
                "从不急躁，遇到慌乱的场面也常以「稍安勿躁」安抚，字里行间透着一股见过世面的镇定。"
        },
        new()
        {
            Id = "paimon",
            Name = "派蒙",
            Description =
                "元气满满、爱咋呼，语气跳跃、感叹特别多，喜欢用「好耶」「哇」「诶」起头。" +
                "说话直白热络，像贴心又话多的小跟班，爱自夸「这种事交给派蒙准没错」，也爱和对方拌两句嘴。" +
                "容易饿，一提到吃的就格外兴奋；虽然嘴上抱怨，行动上却总第一个替你张罗。"
        },
        new()
        {
            Id = "klee",
            Name = "可莉",
            Description =
                "天真烂漫、好奇心旺盛，语气充满孩子气的雀跃，爱用叠词和长长的拖音，像「哇——」「这个好厉害呀」。" +
                "看到新鲜事物就眼睛发亮，忍不住东问西问；注意力容易从一个点蹦到另一个点，" +
                "说话直来直去、藏不住心思，开心和不开心都写在语气里。"
        },
        new()
        {
            Id = "venti",
            Name = "温迪",
            Description =
                "随性洒脱、慵懒惬意，说话像有一搭没一搭地哼着小调，节奏舒缓、常带「哟呵」「啊哈」。" +
                "爱开玩笑、也爱调侃，但从不带刺；兴致来了会随口来一句诗意的比喻，把平常的事说成风、云或歌。" +
                "看似漫不经心，关键处却总能轻巧地点到要点。"
        },
        new()
        {
            Id = "nahida",
            Name = "纳西妲",
            Description =
                "温柔睿智、循循善诱，语气平静而包容，像一位耐心的师长娓娓道来。" +
                "不喜欢直接下结论，常先问一句「你觉得呢」「不妨想想看」，再用一个贴切的比喻把道理讲明白。" +
                "鼓励多于责备，哪怕指出问题也轻声细语，让人愿意听下去。"
        },
        new()
        {
            Id = "raiden",
            Name = "雷电将军",
            Description =
                "冷峻威严、言简意赅，句子短、修饰少，直给结论，不绕弯子。措辞庄重，习惯用「理当」「自当」「不必」。" +
                "语气里带着一点不容置疑的分量，但不盛气凌人；交代事情干净利落，多余的寒暄一句都没有。"
        },
        new()
        {
            Id = "elysia",
            Name = "爱莉希雅",
            Description =
                "温柔又带点神秘，说话时常拖一个软软的尾音，爱用「哎呀」「呵呵」开场，句末时不时带个「~」，像「这个呀，就这样决定咯~」。" +
                "喜欢用「亲爱的」「可爱的小家伙」这样亲昵的称呼，语气俏皮又狡黠，时不时卖个关子、绕个弯，把简单的事说得像个小秘密。" +
                "从不吝啬赞美和鼓励，兴致来了会随手用玫瑰、星光、风声这类意象打个浪漫的比方，字里行间都透着甜。"
        },
    };

    /// <summary>根据设置组装最终角色设定文本；返回空串表示「未启用 / 无有效角色」。</summary>
    public static string BuildPersonaPrompt(AISettings settings)
    {
        if (settings == null || !settings.PersonaEnabled)
            return "";

        var preset = FindPreset(settings.PersonaPresetId);
        if (preset != null)
            return BuildPrefix(preset.Name, preset.Description);

        var custom = settings.CustomPersona?.Trim();
        if (!string.IsNullOrWhiteSpace(custom))
        {
            // 自定义设定首行可能带「角色名称：XX」，拆出来让提示词里也能出现角色名。
            TrySplitCustomPersona(custom, out var customName, out var customDesc);
            if (!string.IsNullOrWhiteSpace(customDesc))
                return BuildPrefix(customName, customDesc);
        }

        return "";
    }

    // ========================================
    //  自定义角色设定文本格式：「角色名称：XX」+ 换行 + 语气描述
    // ========================================

    /// <summary>自定义角色设定中「角色名称」行的前缀。</summary>
    public const string NameLinePrefix = "角色名称：";

    /// <summary>无角色名时的兜底名称。</summary>
    public const string FallbackName = "自定义角色";

    /// <summary>首行可能出现的名称标签（按长度优先匹配）。</summary>
    public static readonly IReadOnlyList<string> NameLineLabels =
        new[] { "角色名称", "角色名", "名称", "名字" };

    /// <summary>名称标签与值之间允许的分隔符。</summary>
    private static readonly char[] NameValueSeparators = { '：', ':', '＝', '=', '—', '－', '·' };

    /// <summary>Markdown 强调符、列表符号、引用符号等首行噪声。</summary>
    private static readonly char[] LineNoiseChars = { '#', '*', '-', '>', '+', '·', ' ', '\t' };

    /// <summary>把角色名与描述组合成「角色名称：XX\n描述」；描述为空时返回空串。</summary>
    public static string ComposeCustomPersona(string? name, string? description)
    {
        var desc = description?.Trim() ?? "";
        if (desc.Length == 0)
            return "";

        var finalName = string.IsNullOrWhiteSpace(name) ? FallbackName : name.Trim();
        return $"{NameLinePrefix}{finalName}\n{desc}";
    }

    /// <summary>
    /// 从自定义角色设定文本中拆出角色名与描述。
    /// 首行不是名称行（或只有名称没有描述）时返回 false，并把整段作为描述、名称回退为「自定义角色」。
    /// </summary>
    public static bool TrySplitCustomPersona(string? text, out string name, out string description)
    {
        name = FallbackName;
        description = text?.Trim() ?? "";
        if (description.Length == 0)
            return false;

        var newlineIndex = description.IndexOf('\n');
        var firstLine = newlineIndex >= 0 ? description[..newlineIndex] : description;
        var rest = newlineIndex >= 0 ? description[(newlineIndex + 1)..].Trim() : "";

        if (rest.Length == 0 || !TryParseNameLine(firstLine, out var parsedName))
            return false;

        name = parsedName;
        description = rest;
        return true;
    }

    /// <summary>去掉文本首行的「角色名称」标签行；首行不是名称行时原样返回。</summary>
    public static string StripNameLine(string? text)
    {
        var body = text?.Trim() ?? "";
        if (body.Length == 0)
            return "";

        var newlineIndex = body.IndexOf('\n');
        if (newlineIndex < 0)
            return body;

        return TryParseNameLine(body[..newlineIndex], out _)
            ? body[(newlineIndex + 1)..].Trim()
            : body;
    }

    /// <summary>尝试把一行解析成「标签 + 分隔符 + 角色名」；标签后必须紧跟分隔符，避免误判普通描述。</summary>
    private static bool TryParseNameLine(string line, out string name)
    {
        name = "";
        var firstLine = line.TrimStart(LineNoiseChars).Trim();

        foreach (var label in NameLineLabels)
        {
            if (!firstLine.StartsWith(label, StringComparison.Ordinal))
                continue;

            // 标签可能被 Markdown 强调符包住，如「**角色名称**：甘雨」
            var value = firstLine[label.Length..].TrimStart(LineNoiseChars).Trim();
            if (value.Length == 0 || Array.IndexOf(NameValueSeparators, value[0]) < 0)
                break;

            value = value[1..].Trim();
            value = value.Trim('「', '」', '“', '”', '"', '\'', '【', '】', '《', '》').Trim();
            if (value.Length == 0)
                break;

            name = value;
            return true;
        }

        return false;
    }

    /// <summary>按 Id 查找内置角色；未命中或 Id 为空返回 null。</summary>
    public static PersonaPreset? FindPreset(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        return BuiltInPersonas.FirstOrDefault(p =>
            string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>把「角色名 + 语气描述」包装成带约束的 system prompt 前缀。</summary>
    public static string BuildPrefix(string name, string description)
    {
        return $"你正在以「{name}」的语气表达，请严格遵循下面的角色设定：\n" +
               $"{description.Trim()}\n\n" +
               "约束：\n" +
               "1. 只模仿说话语气、用词习惯和句式节奏，不要自称角色、不要引入角色背景、世界观或虚构设定；\n" +
               "2. 语气是点缀，不是主角——提醒、总结、提示的内容必须准确、完整，不能为了「入戏」而省略或含糊关键信息；\n" +
               "3. 优先用中文表达，保持自然口语，不要堆砌角色台词或刻意腔调；\n" +
               "4. 你仍然是课表与学习助手，照常完成提醒、总结和提示任务。";
    }
}
