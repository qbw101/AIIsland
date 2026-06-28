using System;

namespace ClassIsland.AISmartClass.Attributes;

/// <summary>
/// 标记组件使用自定义字体图标（AIIsland Icons 字体）。
/// 在插件初始化后，IconPatcher 会反射替换 ComponentInfo.IconSource
/// 为使用此 glyph 的 FontIconSource，实现主题感知自动变色。
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class AIIslandIconAttribute : Attribute
{
    /// <summary>字体图标的 Unicode 码点字符（如 '\ue001'）。</summary>
    public string Glyph { get; }

    public AIIslandIconAttribute(string glyph)
    {
        Glyph = glyph;
    }
}
