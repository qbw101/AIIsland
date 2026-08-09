using Windows.Media.Control;
using Windows.Devices.Geolocation;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClassIsland.AISmartClass.Models;

namespace ClassIsland.AISmartClass.Services;

/// <summary>Windows system context adapter for media, location, and Xiaomi weather.</summary>
public sealed class WindowsSystemContextService
{
    private const string XiaomiWeatherBaseUrl = "https://weatherapi.market.xiaomi.com/wtr-v3";
    private const string XiaomiWeatherAppKey = "weather20151024";
    private const string XiaomiWeatherSign = "zUFJoAR2ZVrDy1vF3D07";

    public static Version MinimumWindowsVersion { get; } = new(10, 0, 17763);
    public static bool IsSupportedWindowsVersion(Version version)
        => version >= MinimumWindowsVersion;

    public static bool IsWindowsSystemContextSupported
        => OperatingSystem.IsWindows() && IsSupportedWindowsVersion(Environment.OSVersion.Version);

    public static string GetSupportStatus()
    {
        if (!OperatingSystem.IsWindows()) return "仅支持 Windows。";
        var version = Environment.OSVersion.Version;
        return version < MinimumWindowsVersion
            ? $"当前 Windows 版本 {version} 不满足最低要求 {MinimumWindowsVersion}。"
            : $"Windows {version} 支持系统媒体、定位和小米天气上下文。";
    }

    private GlobalSystemMediaTransportControlsSessionManager? _mediaManager;

    public sealed record MusicTrack(string Title, string Artist, string Album);
    public sealed record DailyWeatherForecast(
        double MinimumTemperatureC,
        double MaximumTemperatureC,
        double? MinimumApparentTemperatureC,
        double? MaximumApparentTemperatureC,
        int WeatherCode,
        int? NightWeatherCode = null);

    public sealed record WeatherAlert(
        string Title,
        string? Level,
        string? Description,
        DateTime? PublishTime);

    public sealed record WeatherSnapshot(
        double TemperatureC,
        double? ApparentTemperatureC,
        int WeatherCode,
        GeoLocation? Location,
        IReadOnlyList<WeatherAlert> Alerts,
        DailyWeatherForecast? Tomorrow);

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };
    private static readonly JsonSerializerOptions XiaomiJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<MusicTrack?> GetCurrentMusicAsync(CancellationToken ct = default)
    {
        if (!IsWindowsSystemContextSupported) return null;
        try
        {
            _mediaManager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            ct.ThrowIfCancellationRequested();
            var session = _mediaManager.GetCurrentSession();
            if (session == null) return null;
            var playback = session.GetPlaybackInfo();
            if (playback?.PlaybackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                return null;
            var properties = await session.TryGetMediaPropertiesAsync();
            ct.ThrowIfCancellationRequested();
            if (properties == null || string.IsNullOrWhiteSpace(properties.Title)) return null;
            return new MusicTrack(properties.Title.Trim(), properties.Artist?.Trim() ?? "", properties.AlbumTitle?.Trim() ?? "");
        }
        catch (Exception ex)
        {
            Logger.Info($"读取 Windows 媒体会话失败: {ex.Message}");
            return null;
        }
    }

    public async Task<WeatherSnapshot?> GetCurrentWeatherAsync(GeoLocation? location = null, CancellationToken ct = default)
    {
        if (!IsWindowsSystemContextSupported) return null;
        try
        {
            location ??= await GetCurrentLocationAsync(ct);
            if (location == null)
            {
                Logger.Info("未取得定位信息，无法获取小米天气");
                return null;
            }

            var latitude = location.Latitude;
            var longitude = location.Longitude;

            var locationJson = await Http.GetStringAsync(BuildXiaomiLocationUri(latitude, longitude), ct);
            var locationKey = ParseXiaomiLocationKey(locationJson);
            if (string.IsNullOrWhiteSpace(locationKey))
            {
                Logger.Info("小米天气未能匹配当前位置的城市编码");
                return null;
            }

            var weatherJson = await Http.GetStringAsync(
                BuildXiaomiWeatherUri(latitude, longitude, locationKey), ct);
            return ParseXiaomiWeather(weatherJson, location);
        }
        catch (Exception ex)
        {
            Logger.Info($"读取小米天气上下文失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 通过 Windows 定位服务获取当前地理位置。
    /// </summary>
    public async Task<GeoLocation?> GetCurrentLocationAsync(CancellationToken ct = default)
    {
        if (!IsWindowsSystemContextSupported) return null;
        try
        {
            var access = await Geolocator.RequestAccessAsync();
            if (access is not GeolocationAccessStatus.Allowed)
            {
                Logger.Info($"Windows 定位权限未授予: {access}");
                return null;
            }

            var position = await new Geolocator { DesiredAccuracyInMeters = 1000 }
                .GetGeopositionAsync(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(5));
            ct.ThrowIfCancellationRequested();
            var point = position.Coordinate.Point.Position;
            return new GeoLocation
            {
                Latitude = point.Latitude,
                Longitude = point.Longitude,
                Provider = "Windows 定位服务"
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.Info($"读取 Windows 定位失败: {ex.Message}");
            return null;
        }
    }

    internal static string BuildXiaomiLocationUri(double latitude, double longitude)
    {
        return FormattableString.Invariant($"{XiaomiWeatherBaseUrl}/location/city/geo?latitude={latitude:F5}&longitude={longitude:F5}&locale=zh_cn&appKey={XiaomiWeatherAppKey}&sign={XiaomiWeatherSign}");
    }

    internal static string BuildXiaomiWeatherUri(double latitude, double longitude, string locationKey)
    {
        var encodedLocationKey = Uri.EscapeDataString(locationKey);
        return FormattableString.Invariant($"{XiaomiWeatherBaseUrl}/weather/all?latitude={latitude:F5}&longitude={longitude:F5}&isLocated=true&isGlobal=false&locationKey={encodedLocationKey}&days=2&appKey={XiaomiWeatherAppKey}&sign={XiaomiWeatherSign}&locale=zh_cn");
    }

    internal static string? ParseXiaomiLocationKey(string json)
    {
        var locations = JsonSerializer.Deserialize<XiaomiLocation[]>(json, XiaomiJsonOptions);
        return locations?.FirstOrDefault(x => x.Status == 0)?.LocationKey;
    }

    internal static WeatherSnapshot? ParseXiaomiWeather(string json, GeoLocation? location = null)
    {
        var response = JsonSerializer.Deserialize<XiaomiWeatherResponse>(json, XiaomiJsonOptions);
        var current = response?.Current;
        if (!TryParseDouble(current?.Temperature?.Value, out var temperature) ||
            !TryParseInt(current?.Weather, out var weatherCode))
        {
            return null;
        }

        double? apparentTemperature = TryParseDouble(current?.FeelsLike?.Value, out var apparent)
            ? apparent
            : null;

        var alerts = ParseXiaomiAlerts(response?.Alerts ?? current?.Alerts);

        DailyWeatherForecast? tomorrow = null;
        var temperatures = response?.ForecastDaily?.Temperature?.Value;
        var weather = response?.ForecastDaily?.Weather?.Value;
        if (temperatures?.Length > 1 && weather?.Length > 1 &&
            TryParseDouble(temperatures[1].From, out var firstTemperature) &&
            TryParseDouble(temperatures[1].To, out var secondTemperature) &&
            TryParseInt(weather[1].From, out var dayWeatherCode))
        {
            var nightWeatherCode = TryParseInt(weather[1].To, out var nightCode)
                ? nightCode
                : (int?)null;
            tomorrow = new DailyWeatherForecast(
                Math.Min(firstTemperature, secondTemperature),
                Math.Max(firstTemperature, secondTemperature),
                null,
                null,
                dayWeatherCode,
                nightWeatherCode);
        }

        return new WeatherSnapshot(temperature, apparentTemperature, weatherCode, location, alerts, tomorrow);
    }

    private static IReadOnlyList<WeatherAlert> ParseXiaomiAlerts(XiaomiAlert[]? alerts)
    {
        if (alerts == null || alerts.Length == 0)
            return Array.Empty<WeatherAlert>();

        var result = new List<WeatherAlert>(alerts.Length);
        foreach (var alert in alerts)
        {
            if (string.IsNullOrWhiteSpace(alert.Title)) continue;

            DateTime? publishTime = null;
            if (!string.IsNullOrWhiteSpace(alert.PubTime) &&
                DateTime.TryParse(alert.PubTime, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed))
            {
                publishTime = parsed;
            }

            result.Add(new WeatherAlert(alert.Title.Trim(), alert.Level?.Trim(), alert.Desc?.Trim(), publishTime));
        }

        return result;
    }

    public static string DescribeWeatherCode(int code) => code switch
    {
        0 => "晴",
        1 => "多云",
        2 => "阴",
        3 => "阵雨",
        4 => "雷阵雨",
        5 => "雷阵雨伴冰雹",
        6 => "雨夹雪",
        7 => "小雨",
        8 => "中雨",
        9 => "大雨",
        10 => "暴雨",
        11 => "大暴雨",
        12 => "特大暴雨",
        13 => "阵雪",
        14 => "小雪",
        15 => "中雪",
        16 => "大雪",
        17 => "暴雪",
        18 => "雾",
        19 => "冻雨",
        20 => "沙尘暴",
        21 => "小到中雨",
        22 => "中到大雨",
        23 => "大到暴雨",
        24 => "暴雨到大暴雨",
        25 => "大暴雨到特大暴雨",
        26 => "小到中雪",
        27 => "中到大雪",
        28 => "大到暴雪",
        29 => "浮尘",
        30 => "扬沙",
        31 => "强沙尘暴",
        32 => "飑",
        33 => "龙卷风",
        34 => "弱高吹雪",
        35 => "轻雾",
        53 => "霾",
        _ => "天气变化"
    };

    public static string DescribeDailyWeather(DailyWeatherForecast forecast)
    {
        var daytime = DescribeWeatherCode(forecast.WeatherCode);
        if (forecast.NightWeatherCode is not int nightCode || nightCode == forecast.WeatherCode)
            return daytime;
        return $"{daytime}转{DescribeWeatherCode(nightCode)}";
    }

    private static bool TryParseDouble(string? value, out double result)
        => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);

    private static bool TryParseInt(string? value, out int result)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);

    private sealed class XiaomiLocation
    {
        [JsonPropertyName("locationKey")]
        public string? LocationKey { get; set; }
        [JsonPropertyName("status")]
        public int Status { get; set; }
    }

    private sealed class XiaomiWeatherResponse
    {
        [JsonPropertyName("current")]
        public XiaomiCurrentWeather? Current { get; set; }
        [JsonPropertyName("forecastDaily")]
        public XiaomiDailyForecast? ForecastDaily { get; set; }
        [JsonPropertyName("alerts")]
        public XiaomiAlert[]? Alerts { get; set; }
    }

    private sealed class XiaomiCurrentWeather
    {
        [JsonPropertyName("temperature")]
        public XiaomiValue? Temperature { get; set; }
        [JsonPropertyName("feelsLike")]
        public XiaomiValue? FeelsLike { get; set; }
        [JsonPropertyName("weather")]
        public string? Weather { get; set; }
        [JsonPropertyName("alerts")]
        public XiaomiAlert[]? Alerts { get; set; }
    }

    private sealed class XiaomiAlert
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }
        [JsonPropertyName("level")]
        public string? Level { get; set; }
        [JsonPropertyName("desc")]
        public string? Desc { get; set; }
        [JsonPropertyName("pubTime")]
        public string? PubTime { get; set; }
    }

    private sealed class XiaomiDailyForecast
    {
        [JsonPropertyName("temperature")]
        public XiaomiRangeSeries? Temperature { get; set; }
        [JsonPropertyName("weather")]
        public XiaomiRangeSeries? Weather { get; set; }
    }

    private sealed class XiaomiValue
    {
        [JsonPropertyName("value")]
        public string? Value { get; set; }
    }

    private sealed class XiaomiRangeSeries
    {
        [JsonPropertyName("value")]
        public XiaomiRange[]? Value { get; set; }
    }

    private sealed class XiaomiRange
    {
        [JsonPropertyName("from")]
        public string? From { get; set; }
        [JsonPropertyName("to")]
        public string? To { get; set; }
    }
}
