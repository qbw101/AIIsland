using System;
using System.Threading;
using System.Threading.Tasks;
using ClassIsland.AISmartClass.Models;
using ClassIsland.AISmartClass.Services.Location;

namespace ClassIsland.AISmartClass.Services;

/// <summary>
/// 统一地理位置服务。
/// 当前仅支持 ClassIsland 本地天气定位数据，自动分辨城市选择或坐标定位。
/// </summary>
public sealed class LocationService
{
    private readonly Func<string> _getClassIslandInstallDirectory;

    public LocationService(Func<string>? getClassIslandInstallDirectory = null)
    {
        _getClassIslandInstallDirectory = getClassIslandInstallDirectory ?? (static () => "");
    }

    /// <summary>
    /// 从 ClassIsland 本地设置获取地理位置。
    /// </summary>
    public Task<GeoLocation?> GetLocationAsync(CancellationToken ct = default)
    {
        var provider = new ClassIslandSettingsProvider(_getClassIslandInstallDirectory);
        return provider.GetLocationAsync(ct);
    }
}
