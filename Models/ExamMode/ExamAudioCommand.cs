using System.Text.Json.Serialization;

namespace ClassIsland.AISmartClass.Models.ExamMode;

/// <summary>
/// 由调度器下发、网页端执行的音频播放命令。
/// </summary>
public sealed class ExamAudioCommand
{
    [JsonPropertyName("sequence")]
    public long Sequence { get; set; }

    [JsonPropertyName("action")]
    public string Action { get; set; } = "play";

    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("volume")]
    public double Volume { get; set; } = 0.8;

    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = "";

    [JsonPropertyName("examName")]
    public string ExamName { get; set; } = "";

    [JsonPropertyName("expiresAtUnixSeconds")]
    public long ExpiresAtUnixSeconds { get; set; }
}
