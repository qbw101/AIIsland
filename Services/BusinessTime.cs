using ClassIsland.AISmartClass.Models;
using ClassIsland.Core.Abstractions.Services;

namespace ClassIsland.AISmartClass.Services;

/// <summary>一次业务时间读取的结果，保留请求来源与实际生效来源，便于诊断回退。</summary>
public readonly record struct BusinessTimeSnapshot(
    DateTime Now,
    BusinessTimeSource RequestedSource,
    BusinessTimeSource EffectiveSource)
{
    /// <summary>请求 ClassIsland 时间但宿主服务不可用时为 true。</summary>
    public bool IsFallback => RequestedSource != EffectiveSource;
}

/// <summary>
/// 可测试的业务时钟。业务时间与真实系统时间分离：
/// 课表、倒计时、日期缓存按业务时间；日志、网络、缓存有效期仍用真实时间。
/// </summary>
public sealed class BusinessTimeProvider
{
    private readonly Func<BusinessTimeSource> _sourceAccessor;
    private readonly Func<DateTime?> _hostTimeAccessor;
    private readonly Func<DateTime> _systemTimeAccessor;

    /// <param name="sourceAccessor">读取当前时间来源设置。</param>
    /// <param name="hostTimeAccessor">读取宿主 ClassIsland 时间；返回 null 表示服务未就绪。</param>
    /// <param name="systemTimeAccessor">读取系统时间，默认 <see cref="DateTime.Now"/>。</param>
    public BusinessTimeProvider(
        Func<BusinessTimeSource> sourceAccessor,
        Func<DateTime?> hostTimeAccessor,
        Func<DateTime>? systemTimeAccessor = null)
    {
        _sourceAccessor = sourceAccessor ?? throw new ArgumentNullException(nameof(sourceAccessor));
        _hostTimeAccessor = hostTimeAccessor ?? throw new ArgumentNullException(nameof(hostTimeAccessor));
        _systemTimeAccessor = systemTimeAccessor ?? (() => DateTime.Now);
    }

    /// <summary>读取当前业务时间。任何异常都回退到系统时间，保证业务不中断。</summary>
    public BusinessTimeSnapshot Read()
    {
        var systemNow = _systemTimeAccessor();

        BusinessTimeSource requested;
        try
        {
            requested = _sourceAccessor().Normalize();
        }
        catch
        {
            requested = BusinessTimeSource.ClassIslandTime;
        }

        if (requested == BusinessTimeSource.SystemTime)
        {
            return new BusinessTimeSnapshot(systemNow, requested, BusinessTimeSource.SystemTime);
        }

        try
        {
            var hostNow = _hostTimeAccessor();
            if (hostNow.HasValue)
            {
                return new BusinessTimeSnapshot(hostNow.Value, requested, requested);
            }
        }
        catch
        {
            // 宿主时间服务异常时安全回退，不影响倒计时等业务继续运行。
        }

        return new BusinessTimeSnapshot(systemNow, requested, BusinessTimeSource.SystemTime);
    }

    public DateTime Now => Read().Now;

    public DateOnly Today => DateOnly.FromDateTime(Now);
}

/// <summary>
/// 全插件统一的业务时钟入口。
/// 由 <see cref="Plugin.PluginInitializer"/> 在启动时注入 ClassIsland 时间服务解析器，
/// 之后每次读取只做委托调用，不反射、不解析文件、不阻塞。
/// </summary>
public static class BusinessTime
{
    private static readonly object Gate = new();
    private static BusinessTimeProvider _provider = CreateSystemOnlyProvider();
    private static Func<IExactTimeService?>? _exactTimeResolver;
    private static IExactTimeService? _exactTimeService;
    private static long _nextResolveTicks;

    /// <summary>当前业务时间。</summary>
    public static DateTime Now => _provider.Now;

    /// <summary>当前业务日期，用于跨天刷新与日期级缓存键。</summary>
    public static DateOnly Today => _provider.Today;

    /// <summary>当前业务时间的时段部分。</summary>
    public static TimeSpan TimeOfDay => _provider.Now.TimeOfDay;

    /// <summary>读取完整快照，包含回退状态。</summary>
    public static BusinessTimeSnapshot Read() => _provider.Read();

    /// <summary>
    /// 配置业务时钟。
    /// </summary>
    /// <param name="sourceAccessor">时间来源设置读取委托。</param>
    /// <param name="exactTimeResolver">
    /// ClassIsland <see cref="IExactTimeService"/> 解析委托。首次解析失败后会延迟重试，
    /// 避免在每次计时器回调中反复查询服务容器。
    /// </param>
    public static void Configure(
        Func<BusinessTimeSource>? sourceAccessor,
        Func<IExactTimeService?>? exactTimeResolver)
    {
        lock (Gate)
        {
            _exactTimeResolver = exactTimeResolver;
            _exactTimeService = null;
            Interlocked.Exchange(ref _nextResolveTicks, 0);
            _provider = new BusinessTimeProvider(
                sourceAccessor ?? (() => BusinessTimeSource.SystemTime),
                ResolveHostTime);
        }
    }

    /// <summary>恢复为仅系统时间的默认状态（测试或插件停止时使用）。</summary>
    public static void Reset()
    {
        lock (Gate)
        {
            _exactTimeResolver = null;
            _exactTimeService = null;
            Interlocked.Exchange(ref _nextResolveTicks, 0);
            _provider = CreateSystemOnlyProvider();
        }
    }

    private static BusinessTimeProvider CreateSystemOnlyProvider()
    {
        return new BusinessTimeProvider(
            () => BusinessTimeSource.SystemTime,
            () => null);
    }

    private static DateTime? ResolveHostTime()
    {
        var service = _exactTimeService;
        var resolver = _exactTimeResolver;
        if (resolver == null)
        {
            return null;
        }

        if (service == null)
        {
            var nowTicks = Environment.TickCount64;
            if (nowTicks < Interlocked.Read(ref _nextResolveTicks))
            {
                return null;
            }

            service = resolver();
            if (service == null)
            {
                // 启动早期服务可能尚未注册，5 秒后再试，避免每秒查容器。
                Interlocked.Exchange(ref _nextResolveTicks, nowTicks + 5000);
                return null;
            }

            _exactTimeService = service;
        }

        return service.GetCurrentLocalDateTime();
    }
}
