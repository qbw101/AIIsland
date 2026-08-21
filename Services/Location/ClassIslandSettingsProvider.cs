using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ClassIsland.AISmartClass.Models;

namespace ClassIsland.AISmartClass.Services.Location;

/// <summary>
/// 从 ClassIsland 本地 Settings.json 读取天气定位数据，并自动分辨城市选择/坐标定位。
/// 路径为 &lt;ClassIsland 安装目录&gt;/data/Settings.json。
/// </summary>
public sealed class ClassIslandSettingsProvider : ILocationProvider
{
    public LocationProviderType ProviderType => LocationProviderType.ClassIslandSettings;

    public string DisplayName => "ClassIsland 本地设置";

    private readonly Func<string> _getInstallDirectory;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public ClassIslandSettingsProvider(Func<string> getInstallDirectory)
    {
        _getInstallDirectory = getInstallDirectory;
    }

    public async Task<GeoLocation?> GetLocationAsync(CancellationToken ct = default)
    {
        var path = ResolveSettingsPath();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            Logger.Info($"ClassIsland 本地设置文件不存在: {path}");
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            var document = await JsonSerializer.DeserializeAsync<JsonDocument>(stream, cancellationToken: ct);
            if (document?.RootElement.ValueKind != JsonValueKind.Object)
            {
                Logger.Info("ClassIsland 设置文件格式无效，无法解析为 JSON 对象");
                return null;
            }

            var root = document.RootElement;
            var source = GetIntOrDefault(root, "WeatherLocationSource", 0);
            var cityName = GetStringOrDefault(root, "CityName", "").Trim();

            // WeatherLocationSource == 1 表示坐标定位模式
            if (source == 1)
            {
                if (!TryGetDouble(root, "WeatherLatitude", out var lat) ||
                    !TryGetDouble(root, "WeatherLongitude", out var lon))
                {
                    Logger.Info("ClassIsland 坐标定位模式未找到 WeatherLatitude/WeatherLongitude");
                    return null;
                }

                if (Math.Abs(lat) < double.Epsilon && Math.Abs(lon) < double.Epsilon)
                {
                    Logger.Info("ClassIsland 坐标定位模式坐标为 (0, 0)，视为未设置");
                    return null;
                }

                return new GeoLocation
                {
                    Latitude = lat,
                    Longitude = lon,
                    Address = string.IsNullOrWhiteSpace(cityName) ? $"坐标定位（{lat:F4}, {lon:F4}）" : cityName,
                    Provider = $"{DisplayName} · 坐标定位"
                };
            }

            // 城市选择模式：通过 CityId 查询城市坐标
            var cityId = GetStringOrDefault(root, "CityId", "").Trim();
            if (string.IsNullOrWhiteSpace(cityId))
            {
                Logger.Info("ClassIsland 城市选择模式未找到 CityId");
                return null;
            }

            var cityLocation = await ResolveCityLocationAsync(cityId, ct);
            if (cityLocation == null)
            {
                Logger.Info($"无法通过 CityId 解析城市坐标: {cityId}");
                return null;
            }

            return new GeoLocation
            {
                Latitude = cityLocation.Value.Latitude,
                Longitude = cityLocation.Value.Longitude,
                Address = string.IsNullOrWhiteSpace(cityName) ? cityLocation.Value.Name : cityName,
                Provider = $"{DisplayName} · 城市选择"
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Info($"读取 ClassIsland 本地设置失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 通过小米天气 location/city/info 接口把 CityId 解析为经纬度。
    /// </summary>
    private static async Task<(double Latitude, double Longitude, string Name)?> ResolveCityLocationAsync(
        string cityId, CancellationToken ct)
    {
        try
        {
            var encodedKey = Uri.EscapeDataString(cityId);
            var uri = $"https://weatherapi.market.xiaomi.com/wtr-v3/location/city/info?locationKey={encodedKey}&locale=zh_cn";
            var json = await Http.GetStringAsync(uri, ct);
            var cities = JsonSerializer.Deserialize<CityInfo[]>(json, JsonOptions);
            var city = cities?.FirstOrDefault(c =>
                string.Equals(c.LocationKey, cityId, StringComparison.OrdinalIgnoreCase) &&
                c.Status == 0);

            if (city == null ||
                !TryParseDouble(city.Latitude, out var lat) ||
                !TryParseDouble(city.Longitude, out var lon))
            {
                return null;
            }

            var name = string.IsNullOrWhiteSpace(city.Name)
                ? cityId
                : $"{city.Name} ({city.Affiliation})".TrimEnd(' ', '(');

            return (lat, lon, name);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Info($"解析 ClassIsland 城市坐标失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 解析 ClassIsland Settings.json 的完整路径。
    /// 优先使用用户指定的安装目录；未指定时尝试自动探测常见位置。
    /// </summary>
    private string? ResolveSettingsPath()
    {
        var configured = _getInstallDirectory()?.Trim();
        if (!string.IsNullOrWhiteSpace(configured))
            return Path.Combine(configured!, "data", "Settings.json");

        var detected = DetectInstallDirectory();
        if (!string.IsNullOrWhiteSpace(detected))
            return Path.Combine(detected, "data", "Settings.json");

        return null;
    }

    /// <summary>
    /// 自动探测 ClassIsland 安装目录。
    /// 依次检查：当前进程启动目录、常见 Program Files 路径、用户下载目录。
    /// </summary>
    public static string? DetectInstallDirectory()
    {
        try
        {
            var candidates = new[]
            {
                GetExecutableDirectory(),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ClassIsland"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "ClassIsland"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClassIsland"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "ClassIsland"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Desktop", "ClassIsland"),
            };

            return candidates
                .Where(static x => !string.IsNullOrWhiteSpace(x))
                .FirstOrDefault(static x => Directory.Exists(x) && File.Exists(Path.Combine(x, "ClassIsland.exe")));
        }
        catch (Exception ex)
        {
            Logger.Info($"自动探测 ClassIsland 安装目录失败: {ex.Message}");
            return null;
        }
    }

    private static string? GetExecutableDirectory()
    {
        try
        {
            var path = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(path)) return null;
            var directory = Path.GetDirectoryName(path);
            if (directory == null) return null;

            // 如果当前进程是插件 DLL 宿主（如 testhost），向上回退到可能的 ClassIsland 根目录
            var current = new DirectoryInfo(directory);
            while (current != null)
            {
                if (File.Exists(Path.Combine(current.FullName, "ClassIsland.exe")))
                    return current.FullName;
                current = current.Parent;
            }

            return directory;
        }
        catch
        {
            return null;
        }
    }

    private static bool TryGetDouble(JsonElement root, string propertyName, out double value)
    {
        value = 0;
        if (!root.TryGetProperty(propertyName, out var element))
            return false;

        if (element.ValueKind == JsonValueKind.Number)
            return element.TryGetDouble(out value);

        if (element.ValueKind == JsonValueKind.String &&
            double.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            value = parsed;
            return true;
        }

        return false;
    }

    private static bool TryParseDouble(string? value, out double result)
        => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);

    private static string GetStringOrDefault(JsonElement root, string propertyName, string defaultValue)
    {
        if (!root.TryGetProperty(propertyName, out var element))
            return defaultValue;

        if (element.ValueKind == JsonValueKind.String)
            return element.GetString() ?? defaultValue;

        return defaultValue;
    }

    private static int GetIntOrDefault(JsonElement root, string propertyName, int defaultValue)
    {
        if (!root.TryGetProperty(propertyName, out var element))
            return defaultValue;

        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var value))
            return value;

        if (element.ValueKind == JsonValueKind.String && int.TryParse(element.GetString(), out var parsed))
            return parsed;

        return defaultValue;
    }

    private sealed class CityInfo
    {
        [JsonPropertyName("locationKey")]
        public string? LocationKey { get; set; }
        [JsonPropertyName("status")]
        public int Status { get; set; }
        [JsonPropertyName("latitude")]
        public string? Latitude { get; set; }
        [JsonPropertyName("longitude")]
        public string? Longitude { get; set; }
        [JsonPropertyName("name")]
        public string? Name { get; set; }
        [JsonPropertyName("affiliation")]
        public string? Affiliation { get; set; }
    }
}
