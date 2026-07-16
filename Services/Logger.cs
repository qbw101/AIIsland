using System.Runtime.CompilerServices;

namespace ClassIsland.AISmartClass.Services;

/// <summary>
/// 统一日志输出，替代散落的 System.Diagnostics.Debug.WriteLine。
/// 使用 Trace 以便在 Release 构建中仍保留日志输出。
/// </summary>
public static class Logger
{
    private const string Prefix = "[AIIsland]";

    public static void Info(string message,
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "")
    {
        System.Diagnostics.Trace.WriteLine($"{Prefix} {ShortName(file)}::{member} — {message}");
    }

    public static void Warn(string message,
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "")
    {
        System.Diagnostics.Trace.WriteLine($"{Prefix} ⚠ {ShortName(file)}::{member} — {message}");
    }

    public static void Error(string message,
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "")
    {
        System.Diagnostics.Trace.WriteLine($"{Prefix} ❌ {ShortName(file)}::{member} — {message}");
    }

    public static void Error(Exception ex, string? context = null,
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "")
    {
        var ctx = context != null ? $" ({context})" : "";
        System.Diagnostics.Trace.WriteLine($"{Prefix} ❌ {ShortName(file)}::{member}{ctx} — {ex.GetType().Name}: {ex.Message}");
    }

    private static string ShortName(string filePath)
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(filePath);
        return name.Length > 25 ? name[..25] : name;
    }
}
