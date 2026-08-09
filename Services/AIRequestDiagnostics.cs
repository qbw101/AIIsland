using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text.Json;

namespace ClassIsland.AISmartClass.Services;

/// <summary>一次 AI 请求失败尝试的结构化诊断信息。</summary>
public sealed record AIRequestFailureInfo(
    int Attempt,
    string Source,
    string Category,
    string ExceptionType,
    string Message,
    string ExceptionDetails,
    int? HttpStatusCode,
    string? HttpReasonPhrase,
    string? ApiRequestId,
    string? ApiResponseBody,
    long DurationMs,
    bool IsRetryable);

/// <summary>AI 调用最终诊断信息，不包含 API Key。</summary>
public sealed record AIRequestDiagnostics(
    string Endpoint,
    int AttemptCount,
    long TotalDurationMs,
    IReadOnlyList<AIRequestFailureInfo> Attempts);

/// <summary>携带 API HTTP 错误响应详情的异常。</summary>
internal sealed class AIResponseFormatException : Exception
{
    public AIResponseFormatException(string message, string? responseBody, Exception? innerException = null)
        : base(message, innerException)
    {
        ResponseBody = responseBody;
    }

    public string? ResponseBody { get; }
}

internal sealed class AIHttpResponseException : HttpRequestException
{
    public AIHttpResponseException(
        HttpStatusCode statusCode,
        string? reasonPhrase,
        string? requestId,
        string? responseBody)
        : base(
            $"API 返回 HTTP {(int)statusCode} {reasonPhrase}".TrimEnd(),
            null,
            statusCode)
    {
        ReasonPhrase = reasonPhrase;
        RequestId = requestId;
        ResponseBody = responseBody;
    }

    public string? ReasonPhrase { get; }
    public string? RequestId { get; }
    public string? ResponseBody { get; }
}

internal static class AIRequestFailureClassifier
{
    public static AIRequestFailureInfo Classify(
        Exception exception,
        int attempt,
        long durationMs,
        bool callerCanceled = false)
    {
        var root = FindDiagnosticException(exception);
        var source = "本地客户端";
        var category = "客户端内部错误";
        int? statusCode = null;
        string? reasonPhrase = null;
        string? requestId = null;
        string? responseBody = null;
        var retryable = false;

        switch (root)
        {
            case AIHttpResponseException httpResponse:
                source = "API 源头";
                category = "API HTTP 错误";
                statusCode = httpResponse.StatusCode is null
                    ? null
                    : (int)httpResponse.StatusCode.Value;
                reasonPhrase = httpResponse.ReasonPhrase;
                requestId = httpResponse.RequestId;
                responseBody = httpResponse.ResponseBody;
                retryable = statusCode is 408 or 409 or 425 or 429 || statusCode >= 500;
                break;
            case TimeoutException:
                category = "本地超时";
                retryable = true;
                break;
            case OperationCanceledException when callerCanceled:
                category = "调用方取消";
                break;
            case OperationCanceledException:
                category = "本地超时";
                retryable = true;
                break;
            case HttpRequestException httpRequest:
                category = ClassifyNetworkCategory(httpRequest);
                retryable = true;
                break;
            case AIResponseFormatException responseFormat:
                source = "API 源头";
                category = "API 响应格式错误";
                responseBody = responseFormat.ResponseBody;
                retryable = true;
                break;
            case JsonException:
            case KeyNotFoundException:
            case InvalidDataException:
                source = "API 源头";
                category = "API 响应格式错误";
                retryable = true;
                break;
            case IOException when root.Message.Contains("流式", StringComparison.Ordinal):
                source = "API 源头";
                category = "API 流式响应中断";
                retryable = true;
                break;
            case IOException:
                category = "本地网络/响应读取失败";
                retryable = true;
                break;
            case UriFormatException:
            case InvalidOperationException when root.Message.Contains("URI", StringComparison.OrdinalIgnoreCase):
                category = "本地配置错误";
                break;
        }

        return new AIRequestFailureInfo(
            attempt,
            source,
            category,
            root.GetType().FullName ?? root.GetType().Name,
            BuildExceptionMessage(root),
            exception.ToString(),
            statusCode,
            reasonPhrase,
            requestId,
            responseBody,
            durationMs,
            retryable);
    }

    public static string SanitizeEndpoint(string endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
        {
            return endpoint;
        }

        return new UriBuilder(uri)
        {
            UserName = "",
            Password = "",
            Query = "",
            Fragment = ""
        }.Uri.ToString();
    }

    private static string ClassifyNetworkCategory(HttpRequestException exception)
    {
        Exception root = exception;
        while (root.InnerException != null)
        {
            root = root.InnerException;
        }

        return root switch
        {
            SocketException socket when socket.SocketErrorCode == SocketError.HostNotFound => "本地网络/DNS 解析失败",
            SocketException socket when socket.SocketErrorCode == SocketError.ConnectionRefused => "本地网络/连接被拒绝",
            SocketException => "本地网络/套接字错误",
            AuthenticationException => "本地网络/TLS 认证失败",
            _ => "本地网络/HTTP 连接失败"
        };
    }

    private static Exception FindDiagnosticException(Exception exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            if (current is AIHttpResponseException or AIResponseFormatException or TimeoutException or OperationCanceledException or
                HttpRequestException or JsonException or KeyNotFoundException or InvalidDataException or
                IOException or UriFormatException)
            {
                return current;
            }
        }

        return exception;
    }

    private static string BuildExceptionMessage(Exception exception)
    {
        var messages = new List<string>();
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            if (!string.IsNullOrWhiteSpace(current.Message) && !messages.Contains(current.Message))
            {
                messages.Add(current.Message);
            }
        }

        return string.Join(" --> ", messages);
    }
}
