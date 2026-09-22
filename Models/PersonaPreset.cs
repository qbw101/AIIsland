namespace ClassIsland.AISmartClass.Models;

/// <summary>
/// 角色语气预设（角色卡）。角色 = 说话语气/风格，不是完整角色扮演；
/// AI 仍是课表/学习助手，只是用该角色的口吻表达。
/// </summary>
public class PersonaPreset
{
    /// <summary>稳定标识（如 ganyu / hutao），用于设置持久化。</summary>
    public string Id { get; set; } = "";

    /// <summary>角色名，如「甘雨」。</summary>
    public string Name { get; set; } = "";

    /// <summary>说话风格描述（原创化，供拼入 system prompt）。</summary>
    public string Description { get; set; } = "";
}
