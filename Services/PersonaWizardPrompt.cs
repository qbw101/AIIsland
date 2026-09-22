namespace ClassIsland.AISmartClass.Services;

/// <summary>
/// AI 角色设定生成向导所用的提示词。
/// 向导默认预填一份「用户提示词」，其中用占位符留出需要用户填写的空缺（角色名、补充要求）；
/// 用户可以在向导里完整修改这份提示词，甚至换成自己的写法。
/// </summary>
public static class PersonaWizardPrompt
{
    /// <summary>角色名占位符。</summary>
    public const string NamePlaceholder = "{角色名}";

    /// <summary>补充要求占位符。</summary>
    public const string ExtraPlaceholder = "{补充要求}";

    /// <summary>生成角色设定时使用的系统提示词。</summary>
    public const string SystemPrompt =
        "你是一名「语气角色」设定师，负责为 AI 课表与学习助手设计一种说话语气风格。\n" +
        "你需要根据用户给出的角色名和补充要求，提炼出该角色「说话的语气、常用词、句式节奏、情绪表达」。\n" +
        "硬性要求：\n" +
        "1. 只描述语气风格，绝对不要引入角色背景故事、世界观设定、剧情或官方台词原文；\n" +
        "2. 语气自然、具体、可直接使用，不要自称角色，不要输出标题或解释；\n" +
        "3. 严格按下面的格式输出，共两行以上，不要用 Markdown 加粗：\n" +
        "第一行：角色名称：<角色名>（用户没给名字时，你替他取一个贴合气质的中文名字）\n" +
        "第二行起：2~4 句语气描述。";

    /// <summary>向导里默认预填的用户提示词模板（用户可完全修改）。</summary>
    public const string DefaultUserPromptTemplate =
        "请帮我设计一个用于课表与学习助手的语气角色，只描述说话语气，不要涉及背景故事。\n\n" +
        "角色名称：" + NamePlaceholder + "\n" +
        "补充要求：" + ExtraPlaceholder + "\n\n" +
        "请输出一段 2~4 句、可以直接填入角色设定的语气描述。";

    /// <summary>补充要求为空时占位符的默认替换文案。</summary>
    private const string EmptyExtraHint = "（无特别要求，请按角色本身的气质自由发挥）";

    /// <summary>角色名为空时占位符的默认替换文案。</summary>
    private const string EmptyNameHint = "（未指定，请你替我推荐一个合适的名字并在描述里点到）";

    /// <summary>
    /// 用角色名与补充要求填充用户提示词模板。
    /// 若模板中缺少对应占位符，则把该信息以补充行的形式追加到末尾，保证用户输入不丢失。
    /// </summary>
    public static string BuildUserPrompt(string? template, string? characterName, string? extraRequirements)
    {
        var body = string.IsNullOrWhiteSpace(template)
            ? DefaultUserPromptTemplate
            : template;

        var name = string.IsNullOrWhiteSpace(characterName) ? EmptyNameHint : characterName.Trim();
        var extra = string.IsNullOrWhiteSpace(extraRequirements) ? EmptyExtraHint : extraRequirements.Trim();

        var hasNameSlot = body.Contains(NamePlaceholder, StringComparison.Ordinal);
        var hasExtraSlot = body.Contains(ExtraPlaceholder, StringComparison.Ordinal);

        var result = body
            .Replace(NamePlaceholder, name, StringComparison.Ordinal)
            .Replace(ExtraPlaceholder, extra, StringComparison.Ordinal);

        if (hasNameSlot && hasExtraSlot)
            return result.Trim();

        // 模板被用户改得没有占位符了：把信息追加到末尾，避免填的内容被丢掉。
        var appended = result.TrimEnd();
        var extraLines = new List<string>();
        if (!hasNameSlot)
            extraLines.Add($"角色名称：{name}");
        if (!hasExtraSlot)
            extraLines.Add($"补充要求：{extra}");

        if (extraLines.Count == 0)
            return appended;

        return appended + "\n\n" + string.Join("\n", extraLines);
    }

    /// <summary>
    /// 清洗 AI 返回的角色设定：去掉常见的包裹引号、Markdown 代码块和三引号，收尾空白。
    /// </summary>
    public static string SanitizeResult(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "";

        var text = raw.Trim();

        // 去掉 ```  代码块围栏
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = text.IndexOf('\n');
            if (firstNewline >= 0)
                text = text[(firstNewline + 1)..];

            var lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
            if (lastFence >= 0)
                text = text[..lastFence];
        }

        text = text.Trim();

        // 去掉整段被一对引号包裹的情况
        if (text.Length >= 2)
        {
            var first = text[0];
            var last = text[^1];
            var paired = (first == '"' && last == '"')
                         || (first == '\'' && last == '\'')
                         || (first == '「' && last == '」')
                         || (first == '“' && last == '”');
            if (paired)
                text = text[1..^1].Trim();
        }

        return text;
    }

    /// <summary>
    /// 把 AI 返回内容整理成最终的自定义角色设定文本，保证首行带「角色名称：XX」。
    /// AI 已给出名称行时沿用其名称；否则用用户填写（或用户留空时兜底）的名称补一行。
    /// </summary>
    public static string ComposePersonaText(string? aiResult, string? fallbackName)
    {
        var text = SanitizeResult(aiResult);
        if (string.IsNullOrWhiteSpace(text))
            return "";

        if (PersonaCatalog.TrySplitCustomPersona(text, out var aiName, out var aiDesc)
            && !string.IsNullOrWhiteSpace(aiDesc))
        {
            return PersonaCatalog.ComposeCustomPersona(aiName, aiDesc);
        }

        // AI 没按格式给名称行 → 去掉可能的名称行残留，用用户填写的名字补上
        var body = PersonaCatalog.StripNameLine(text);
        return PersonaCatalog.ComposeCustomPersona(fallbackName, body);
    }
}
