using System.Collections;
using System.Reflection;
using Avalonia.Threading;
using ClassIsland.Core.Abstractions;
using ClassIsland.Core.Abstractions.Services;
using ClassIsland.Core.Models.Notification;
using Microsoft.Extensions.Hosting;

namespace ClassIsland.AISmartClass.Services.ExamMode;

/// <summary>
/// 考试模式提醒过滤器。启用后只放行 AIIsland 考试提醒与地震预警提醒。
/// 不修改 ClassIsland 的持久化提醒开关，退出考试模式后立即停止过滤。
/// </summary>
public sealed class ExamNotificationFilterService : IHostedService, IDisposable
{
    public static readonly Guid ExamModeProviderGuid = Guid.Parse("A1B2C3D4-E5F6-7890-1234-567890ABCDEF");
    public static readonly Guid EarthquakeWarningProviderGuid = Guid.Parse("B27C0AF3-C917-44DE-A61D-8010C3F3FB92");

    private readonly INotificationHostService _notificationHostService;
    private readonly object _forwardQueueLock = new();
    private readonly Queue<ForwardBatch> _forwardQueue = new();
    private readonly HashSet<NotificationRequest> _pendingForwardRequests = new();
    private readonly SemaphoreSlim _forwardSignal = new(0);
    private readonly CancellationTokenSource _forwardCancellation = new();
    private INotificationConsumer? _consumerProxy;
    private Task? _forwardTask;
    private int _isRegistered;
    private int _isDisposed;

    public ExamNotificationFilterService(INotificationHostService notificationHostService)
    {
        _notificationHostService = notificationHostService;
    }

    private int QueuedNotificationCount => 0;

    private bool AcceptsNotificationRequests =>
        Volatile.Read(ref _isDisposed) == 0 &&
        ExamModeServer.GetOrCreate().IsRunning &&
        Plugin.GetAISettings()?.ExamMode.SuppressOtherNotifications == true;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _isRegistered, 1) == 0)
        {
            _consumerProxy = CreateConsumerProxy();
            _notificationHostService.RegisterNotificationConsumer(_consumerProxy, int.MinValue);
            _forwardTask = Task.Run(() => ForwardAllowedRequestsAsync(_forwardCancellation.Token));
            Logger.Info("[ExamMode] 考试提醒过滤器已注册");
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Unregister();
        _forwardCancellation.Cancel();
        _forwardSignal.Release();
        if (_forwardTask != null)
        {
            try
            {
                await _forwardTask;
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private INotificationConsumer CreateConsumerProxy()
    {
        // ClassIsland 2.0.0.2 与 2.1 的 INotificationConsumer.ReceiveNotifications
        // 参数类型不同。运行时代理会按当前主程序实际加载的接口签名实现方法，
        // 避免插件因静态实现旧签名而在新版主程序启动时出现 TypeLoadException。
        var proxy = DispatchProxy.Create(typeof(INotificationConsumer), typeof(NotificationConsumerProxy));
        ((NotificationConsumerProxy)proxy).Owner = this;
        return (INotificationConsumer)proxy;
    }

    private void ReceiveNotifications(object? requestsObject)
    {
        if (requestsObject is not IEnumerable incomingRequests)
        {
            return;
        }

        var allowedItems = new List<object>();
        var allowedRequests = new List<NotificationRequest>();
        foreach (var item in incomingRequests)
        {
            if (item == null)
            {
                continue;
            }

            var request = GetNotificationRequest(item);
            if (request == null)
            {
                Logger.Info("[ExamMode] 无法识别提醒请求，已按普通提醒屏蔽");
                CancelNotificationItem(item, null);
                continue;
            }

            var sourceGuid = GetNotificationSourceGuid(request);
            if (sourceGuid is not null && IsAllowedProvider(sourceGuid.Value))
            {
                lock (_forwardQueueLock)
                {
                    if (_pendingForwardRequests.Add(request))
                    {
                        allowedItems.Add(item);
                        allowedRequests.Add(request);
                    }
                }
                continue;
            }

            CancelNotificationItem(item, request);
            Logger.Info($"[ExamMode] 已屏蔽提醒来源: {sourceGuid?.ToString() ?? "未知"}");
        }

        if (allowedItems.Count == 0)
        {
            return;
        }

        var payload = CreateCompatibleRequestList(requestsObject.GetType(), allowedItems);
        lock (_forwardQueueLock)
        {
            _forwardQueue.Enqueue(new ForwardBatch(payload, allowedRequests));
        }
        _forwardSignal.Release();
    }

    private async Task ForwardAllowedRequestsAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await _forwardSignal.WaitAsync(cancellationToken);
                while (TryPeekForwardBatch(out var batch))
                {
                    var forwarded = await Dispatcher.UIThread.InvokeAsync(() => TryForwardToNextConsumer(batch.Payload));
                    if (!forwarded)
                    {
                        await Task.Delay(100, cancellationToken);
                        continue;
                    }

                    RemoveForwardBatch(batch);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Logger.Error($"[ExamMode] 移交允许提醒失败: {ex}");
        }
    }

    private bool TryForwardToNextConsumer(object requests)
    {
        var consumersProperty = _notificationHostService.GetType().GetProperty(
            "RegisteredConsumers",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (consumersProperty?.GetValue(_notificationHostService) is not IEnumerable registrations)
        {
            Logger.Error("[ExamMode] 无法读取 ClassIsland 提醒消费者列表，允许提醒暂缓移交");
            return false;
        }

        var consumerInterface = typeof(INotificationConsumer);
        var acceptsProperty = consumerInterface.GetProperty(nameof(INotificationConsumer.AcceptsNotificationRequests));
        var queuedProperty = consumerInterface.GetProperty(nameof(INotificationConsumer.QueuedNotificationCount));
        var receiveMethod = consumerInterface.GetMethod("ReceiveNotifications");
        if (acceptsProperty == null || queuedProperty == null || receiveMethod == null)
        {
            Logger.Error("[ExamMode] ClassIsland 提醒消费者接口结构异常，允许提醒暂缓移交");
            return false;
        }

        foreach (var registration in registrations)
        {
            if (registration == null)
            {
                continue;
            }

            var consumer = registration.GetType().GetProperty("Consumer")?.GetValue(registration);
            if (consumer == null || ReferenceEquals(consumer, _consumerProxy) ||
                acceptsProperty.GetValue(consumer) is not true ||
                queuedProperty.GetValue(consumer) is not int queuedCount || queuedCount > 0)
            {
                continue;
            }

            receiveMethod.Invoke(consumer, new[] { requests });
            return true;
        }

        return false;
    }

    private bool TryPeekForwardBatch(out ForwardBatch batch)
    {
        lock (_forwardQueueLock)
        {
            if (_forwardQueue.Count > 0)
            {
                batch = _forwardQueue.Peek();
                return true;
            }
        }

        batch = null!;
        return false;
    }

    private void RemoveForwardBatch(ForwardBatch batch)
    {
        lock (_forwardQueueLock)
        {
            if (_forwardQueue.Count > 0 && ReferenceEquals(_forwardQueue.Peek(), batch))
            {
                _forwardQueue.Dequeue();
                foreach (var request in batch.SourceRequests)
                {
                    _pendingForwardRequests.Remove(request);
                }
            }
        }
    }

    private static object CreateCompatibleRequestList(Type sourceCollectionType, IReadOnlyList<object> items)
    {
        var elementType = GetCollectionElementType(sourceCollectionType)
            ?? items.FirstOrDefault()?.GetType()
            ?? typeof(object);
        var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType))!;
        foreach (var item in items)
        {
            list.Add(item);
        }
        return list;
    }

    private static Type? GetCollectionElementType(Type collectionType)
    {
        if (collectionType.IsGenericType && collectionType.GetGenericArguments().Length == 1)
        {
            return collectionType.GetGenericArguments()[0];
        }

        return collectionType.GetInterfaces()
            .FirstOrDefault(type => type.IsGenericType &&
                type.GetGenericTypeDefinition() == typeof(IEnumerable<>))?
            .GetGenericArguments()[0];
    }

    private static NotificationRequest? GetNotificationRequest(object item)
    {
        if (item is NotificationRequest request)
        {
            return request;
        }

        return item.GetType().GetProperty("Request", BindingFlags.Instance | BindingFlags.Public)?
            .GetValue(item) as NotificationRequest;
    }

    private static void CancelNotificationItem(object item, NotificationRequest? request)
    {
        if (request != null)
        {
            request.Cancel();
            return;
        }

        item.GetType().GetMethod("Cancel", BindingFlags.Instance | BindingFlags.Public)?.Invoke(item, null);
    }

    private static bool IsAllowedProvider(Guid providerGuid)
    {
        return providerGuid == ExamModeProviderGuid || providerGuid == EarthquakeWarningProviderGuid;
    }

    private static Guid? GetNotificationSourceGuid(object request)
    {
        try
        {
            var property = request.GetType().GetProperty(
                "NotificationSourceGuid",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            return property?.GetValue(request) is Guid guid ? guid : null;
        }
        catch (Exception ex)
        {
            Logger.Info($"[ExamMode] 读取提醒来源失败: {ex.Message}");
            return null;
        }
    }

    private void Unregister()
    {
        if (Interlocked.Exchange(ref _isRegistered, 0) != 0 && _consumerProxy != null)
        {
            _notificationHostService.UnregisterNotificationConsumer(_consumerProxy);
            _consumerProxy = null;
            Logger.Info("[ExamMode] 考试提醒过滤器已注销");
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        {
            return;
        }

        Unregister();
        _forwardCancellation.Cancel();
        _forwardSignal.Release();
    }

    private sealed record ForwardBatch(object Payload, IReadOnlyList<NotificationRequest> SourceRequests);

    private class NotificationConsumerProxy : DispatchProxy
    {
        public ExamNotificationFilterService? Owner { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod == null || Owner == null)
            {
                return null;
            }

            return targetMethod.Name switch
            {
                "get_QueuedNotificationCount" => Owner.QueuedNotificationCount,
                "get_AcceptsNotificationRequests" => Owner.AcceptsNotificationRequests,
                "ReceiveNotifications" => Receive(args),
                "ToString" => nameof(ExamNotificationFilterService),
                "GetHashCode" => Owner.GetHashCode(),
                "Equals" => ReferenceEquals(this, args?.FirstOrDefault()),
                _ => throw new NotSupportedException($"不支持的提醒消费者调用: {targetMethod.Name}")
            };
        }

        private object? Receive(object?[]? args)
        {
            Owner!.ReceiveNotifications(args?.FirstOrDefault());
            return null;
        }
    }
}
