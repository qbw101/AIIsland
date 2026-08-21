using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace ClassIsland.AISmartClass.Services;

/// <summary>
/// 统一日志输出，使用 Microsoft.Extensions.Logging 标准框架。
/// 日志会自动写入 ClassIsland 日志系统（日志文件 + 日志窗口 + Trace 输出）。
/// </summary>
public static class Logger
{
    private const string Prefix = "[AIIsland]";
    private static ILogger? _logger;

    /// <summary>
    /// 初始化日志服务（由 Plugin 类在启动时调用）
    /// </summary>
    public static void Initialize(ILoggerFactory? loggerFactory)
    {
        _logger = loggerFactory?.CreateLogger("ClassIsland.AISmartClass");
    }

    public static void Info(string message,
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "")
    {
        var formattedMsg = $"{ShortName(file)}::{member} — {message}";
        System.Diagnostics.Trace.WriteLine($"{Prefix} {formattedMsg}");
        _logger?.LogInformation("{Message}", formattedMsg);
    }

    public static void Warn(string message,
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "")
    {
        var formattedMsg = $"{ShortName(file)}::{member} — {message}";
        System.Diagnostics.Trace.WriteLine($"{Prefix} ⚠ {formattedMsg}");
        _logger?.LogWarning("{Message}", formattedMsg);
    }

    public static void Error(string message,
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "")
    {
        var formattedMsg = $"{ShortName(file)}::{member} — {message}";
        System.Diagnostics.Trace.WriteLine($"{Prefix} ❌ {formattedMsg}");
        _logger?.LogError("{Message}", formattedMsg);
    }

    public static void Error(Exception ex, string? context = null,
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "")
    {
        var ctx = context != null ? $" ({context})" : "";
        var formattedMsg = $"{ShortName(file)}::{member}{ctx}";
        System.Diagnostics.Trace.WriteLine($"{Prefix} ❌ {formattedMsg} — {ex.GetType().Name}: {ex.Message}");
        _logger?.LogError(ex, "{Message}", formattedMsg);
    }

    private static string ShortName(string filePath)
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(filePath);
        return name.Length > 25 ? name[..25] : name;
    }
}
