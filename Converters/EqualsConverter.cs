using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace ClassIsland.AISmartClass.Converters;

/// <summary>
/// 比较绑定值与参数是否相等，返回布尔值。常用于根据枚举值控制可见性。
/// </summary>
public sealed class EqualsConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null || parameter == null)
            return value == parameter;
        return value.Equals(parameter);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
