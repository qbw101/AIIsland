using System.ComponentModel;
using System.Text.Json.Serialization;

namespace ClassIsland.AISmartClass.Models;

/// <summary>
/// 地理位置提供方类型。
/// 当前仅保留 ClassIsland 本地设置，因为天气位置统一由 ClassIsland 管理。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LocationProviderType
{
    /// <summary>读取 ClassIsland 本地天气定位数据，自动分辨城市选择或坐标定位。</summary>
    [Description("ClassIsland 本地设置")]
    ClassIslandSettings
}
