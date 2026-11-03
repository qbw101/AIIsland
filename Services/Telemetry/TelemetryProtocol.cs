using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClassIsland.AISmartClass.Services.Telemetry;

/// <summary>遥测协议帧定义。与服务器端 Protocol/TelemetryFrame.cs 保持一致。</summary>
internal sealed class TelemetryFrame
{
    [JsonPropertyName("v")] public int Version { get; set; } = 1;

    [JsonPropertyName("type")] public string Type { get; set; } = "";

    [JsonPropertyName("id")] public string? Id { get; set; }

    [JsonPropertyName("ts")] public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;

    [JsonPropertyName("token")] public string? Token { get; set; }

    [JsonPropertyName("data")] public object? Data { get; set; }
}

internal sealed class HelloPayload
{
    public string InstallId { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string? PluginVersion { get; set; }
    public string? CiVersion { get; set; }
    public string? Os { get; set; }
    public string? Locale { get; set; }
    public bool FirstRun { get; set; }
    public string? AiEndpointHost { get; set; }
    public string? Client { get; set; }
}

internal sealed class HeartbeatPayload
{
    public int UptimeSeconds { get; set; }
    public string? State { get; set; }
    public int Pending { get; set; }
}

internal sealed class EventPayload
{
    public string? EventId { get; set; }
    public string? Severity { get; set; }
    public string? Scenario { get; set; }
    public string? Message { get; set; }
    public string? ExceptionType { get; set; }
    public string? StackTrace { get; set; }
    public string? Source { get; set; }
    public Dictionary<string, string>? Context { get; set; }
    public string? LogTail { get; set; }
    public string? EventLog { get; set; }
    public string? PluginVersion { get; set; }
    public string? CiVersion { get; set; }
    public string? Os { get; set; }
    public DateTimeOffset? OccurredAt { get; set; }
}

internal sealed class ByePayload
{
    public string? Reason { get; set; }
    public int UptimeSeconds { get; set; }
}

internal static class TelemetryJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}
