using Avalonia.Controls;

namespace ClassIsland.AISmartClass.Controls;

/// <summary>
/// ClassIsland 在组件构造完成之后才注入 <c>Settings</c>，而组件 XAML 里的
/// <c>Settings.*</c> 绑定在构造函数加载 XAML 时已经按 null 求值并使用了 Fallback。
/// OnLoaded 时把 DataContext 重新挂一次，让绑定按已注入的设置重新解析。
/// </summary>
internal static class ComponentBindingRefresh
{
    public static void Refresh(Control control)
    {
        var dataContext = control.DataContext;
        control.DataContext = null;
        control.DataContext = dataContext;
    }
}
