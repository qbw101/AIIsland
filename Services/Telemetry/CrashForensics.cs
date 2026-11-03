using System.Diagnostics;
using System.Text;

namespace ClassIsland.AISmartClass.Services.Telemetry;

/// <summary>
/// 崩溃现场取证：ClassIsland 日志尾部、Windows 应用程序事件日志、系统崩溃转储文件。
/// 全部为只读操作，失败一律静默返回 null，绝不影响插件运行。
/// </summary>
internal static class CrashForensics
{
    private static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static string RoamingAppData => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    /// <summary>读取最近一份 ClassIsland 日志的尾部若干行。</summary>
    public static string? ReadLogTail(DateTimeOffset since, int maxLines = 120, int maxChars = 12000)
    {
        try
        {
            var candidates = new[]
            {
                Path.Combine(LocalAppData, "ClassIsland", "Logs"),
                Path.Combine(RoamingAppData, "ClassIsland", "Logs"),
                Path.Combine(AppContext.BaseDirectory, "Logs")
            };

            string? newest = null;
            var newestTime = DateTime.MinValue;

            foreach (var dir in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var file in EnumerateLogFiles(dir))
                {
                    var time = File.GetLastWriteTimeUtc(file);
                    if (time < since.UtcDateTime.AddMinutes(-15)) continue;
                    if (time <= newestTime) continue;
                    newestTime = time;
                    newest = file;
                }
            }

            if (newest == null) return null;

            var lines = ReadTailLines(newest, maxLines);
            if (lines.Count == 0) return null;

            var text = string.Join('\n', lines);
            if (text.Length > maxChars) text = text[^maxChars..];
            return $"// {Path.GetFileName(newest)}\n{text}";
        }
        catch (Exception ex)
        {
            Logger.Info($"[Telemetry] 读取 ClassIsland 日志失败：{ex.Message}");
            return null;
        }
    }

    private static IEnumerable<string> EnumerateLogFiles(string dir)
    {
        var files = new List<string>();

        try
        {
            files.AddRange(Directory.EnumerateFiles(dir, "*.log", SearchOption.TopDirectoryOnly));
        }
        catch
        {
            // 目录不可访问
        }

        // 部分版本会把日志放在按日期命名的子目录里
        try
        {
            foreach (var sub in Directory.EnumerateDirectories(dir).Take(5))
            {
                try
                {
                    files.AddRange(Directory.EnumerateFiles(sub, "*.log", SearchOption.TopDirectoryOnly));
                }
                catch
                {
                    // ignore
                }
            }
        }
        catch
        {
            // ignore
        }

        return files;
    }

    private static List<string> ReadTailLines(string path, int maxLines)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        const int tailBytes = 64 * 1024;
        var length = stream.Length;
        var take = (int)Math.Min(length, tailBytes);
        stream.Seek(length - take, SeekOrigin.Begin);

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var text = reader.ReadToEnd();
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();

        // 从文件中间开始读时第一行可能被截断，丢掉
        if (take < length && lines.Count > 0) lines.RemoveAt(0);

        if (lines.Count > maxLines) lines = lines.Skip(lines.Count - maxLines).ToList();
        return lines;
    }

    /// <summary>读取 Windows 应用程序日志中与 ClassIsland 崩溃相关的条目。</summary>
    public static string? ReadApplicationErrors(DateTimeOffset since, int maxEntries = 3)
    {
        if (!OperatingSystem.IsWindows()) return null;

        try
        {
            using var log = new EventLog("Application");
            var entries = log.Entries;
            var builder = new StringBuilder();
            var found = 0;

            for (var i = entries.Count - 1; i >= 0 && i >= entries.Count - 800 && found < maxEntries; i--)
            {
                EventLogEntry entry;
                try
                {
                    entry = entries[i];
                }
                catch
                {
                    break;
                }

                if (entry.TimeGenerated < since.LocalDateTime.AddMinutes(-10)) break;

                var source = entry.Source ?? "";
                if (!source.Contains("Application Error", StringComparison.OrdinalIgnoreCase) &&
                    !source.Contains(".NET Runtime", StringComparison.OrdinalIgnoreCase) &&
                    !source.Contains("Windows Error Reporting", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var message = entry.Message ?? "";
                if (!message.Contains("ClassIsland", StringComparison.OrdinalIgnoreCase)) continue;

                builder.AppendLine($"[{entry.TimeGenerated:yyyy-MM-dd HH:mm:ss}] {source} (ID {entry.InstanceId})");
                builder.AppendLine(Trim(message, 3000));
                builder.AppendLine();
                found++;
            }

            return found > 0 ? builder.ToString().TrimEnd() : null;
        }
        catch (Exception ex)
        {
            Logger.Info($"[Telemetry] 读取 Windows 事件日志失败：{ex.Message}");
            return null;
        }
    }

    /// <summary>列出本次会话之后产生的 ClassIsland 崩溃转储文件（只上报路径与时间，不上传文件）。</summary>
    public static string? DescribeCrashDumps(DateTimeOffset since)
    {
        try
        {
            var dir = Path.Combine(LocalAppData, "CrashDumps");
            if (!Directory.Exists(dir)) return null;

            var found = new List<string>();
            foreach (var file in Directory.EnumerateFiles(dir, "ClassIsland*.dmp", SearchOption.TopDirectoryOnly))
            {
                var info = new FileInfo(file);
                if (info.LastWriteTimeUtc < since.UtcDateTime.AddMinutes(-10)) continue;
                found.Add($"{info.Name}（{info.Length / 1024 / 1024} MB，{info.LastWriteTime:yyyy-MM-dd HH:mm:ss}）");
                if (found.Count >= 3) break;
            }

            return found.Count > 0 ? string.Join('\n', found) : null;
        }
        catch
        {
            return null;
        }
    }

    private static string Trim(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";
}
