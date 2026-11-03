using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClassIsland.AISmartClass.Models;

/// <summary>
/// 匿名遥测配置。只上报运行状态、错误与崩溃信息，不上报课表内容、提示词或 API Key。
/// 关闭开关后插件不会建立任何遥测连接。
/// </summary>
public partial class TelemetrySettings : ObservableObject
{
    /// <summary>内置遥测接入点域名（不含协议与端口）。</summary>
    public const string DefaultServerHost = "cn-sh-5.qwq.fan";

    /// <summary>内置遥测接入端口（TCP），与服务器 Telemetry:IngestPort 一致。</summary>
    public const int DefaultServerPort = 49058;

    /// <summary>内置接入令牌，与服务器 Telemetry:IngestToken 一致。</summary>
    public const string DefaultToken = "tlm_14qXkdcbCB8dr5CPSx6ztiZQcsqEE3s1";

    /// <summary>早期版本的默认接入点，仅用于把旧配置迁移到内置接入点。</summary>
    public const string LegacyServerHost = "telemetry.qbwnas.top";

    /// <summary>早期版本的默认端口，仅用于把旧配置迁移到内置接入点。</summary>
    public const int LegacyServerPort = 9700;

    /// <summary>是否启用匿名遥测。</summary>
    [ObservableProperty]
    [property: JsonPropertyName("enabled")]
    private bool _enabled = true;

    /// <summary>
    /// 遥测服务器地址（域名或 IP，不带协议与端口）。
    /// 设置页不再暴露该项，改用内置接入点；用户历史配置仍会被读取。
    /// </summary>
    [ObservableProperty]
    [property: JsonPropertyName("serverHost")]
    private string _serverHost = DefaultServerHost;

    /// <summary>遥测服务器 TCP 端口，与服务器 appsettings.json 的 Telemetry:IngestPort 一致。</summary>
    [ObservableProperty]
    [property: JsonPropertyName("serverPort")]
    private int _serverPort = DefaultServerPort;

    /// <summary>接入令牌，与服务器 Telemetry:IngestToken 一致；留空表示服务器未开启校验。</summary>
    [ObservableProperty]
    [property: JsonPropertyName("token")]
    private string _token = DefaultToken;

    /// <summary>崩溃时是否附带 ClassIsland 日志尾部（可能包含课表名称等本地文本）。</summary>
    [ObservableProperty]
    [property: JsonPropertyName("includeLogTail")]
    private bool _includeLogTail = true;

    /// <summary>匿名设备 ID，首次启动自动生成，用于统计去重。不包含任何硬件或账号信息。</summary>
    [ObservableProperty]
    [property: JsonPropertyName("installId")]
    private string _installId = "";

    /// <summary>是否上报崩溃（未处理异常与上次异常退出）。</summary>
    [ObservableProperty]
    [property: JsonPropertyName("reportCrashes")]
    private bool _reportCrashes = true;

    /// <summary>最近一次成功上报的时间，仅用于设置页状态展示。</summary>
    [ObservableProperty]
    [property: JsonPropertyName("lastReportedAt")]
    private string _lastReportedAt = "";

    /// <summary>端点是否已配置完整。</summary>
    [JsonIgnore]
    public bool HasEndpoint => !string.IsNullOrWhiteSpace(ServerHost) && ServerPort > 0;
}
