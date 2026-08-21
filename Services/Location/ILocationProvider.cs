using System.Threading;
using System.Threading.Tasks;
using ClassIsland.AISmartClass.Models;

namespace ClassIsland.AISmartClass.Services.Location;

/// <summary>
/// 地理位置提供方接口。
/// </summary>
public interface ILocationProvider
{
    /// <summary>提供方类型。</summary>
    LocationProviderType ProviderType { get; }

    /// <summary>提供方名称，用于日志和展示。</summary>
    string DisplayName { get; }

    /// <summary>
    /// 异步获取当前地理位置。
    /// 返回 null 表示当前不可用或失败。
    /// </summary>
    Task<GeoLocation?> GetLocationAsync(CancellationToken ct = default);
}
