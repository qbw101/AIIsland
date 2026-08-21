using System.Text.Json.Serialization;

namespace ClassIsland.AISmartClass.Models;

/// <summary>
/// 地理位置信息。
/// </summary>
public sealed record GeoLocation
{
    /// <summary>纬度。</summary>
    [JsonPropertyName("latitude")]
    public double Latitude { get; init; }

    /// <summary>经度。</summary>
    [JsonPropertyName("longitude")]
    public double Longitude { get; init; }

    /// <summary>可读的地址描述，可能为空。</summary>
    [JsonPropertyName("address")]
    public string Address { get; init; } = "";

    /// <summary>数据来源提供方名称。</summary>
    [JsonPropertyName("provider")]
    public string Provider { get; init; } = "";

    /// <summary>获取简要文本表示。</summary>
    public override string ToString()
    {
        var coordinate = $"{Latitude:F4}, {Longitude:F4}";
        if (!string.IsNullOrWhiteSpace(Address))
            return $"{Address} ({coordinate})";
        return coordinate;
    }
}
