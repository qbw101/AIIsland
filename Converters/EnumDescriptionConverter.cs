using System;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using Avalonia.Data.Converters;

namespace ClassIsland.AISmartClass.Converters;

/// <summary>
/// 将枚举值转换为其 [Description] 特性文本，用于 ComboBox 等控件显示。
/// </summary>
public class EnumDescriptionConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null) return null;
        var type = value.GetType();
        var name = value.ToString();
        var field = type.GetField(name ?? "");
        if (field == null) return name;
        var attr = field.GetCustomAttribute<DescriptionAttribute>();
        return attr?.Description ?? name;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
