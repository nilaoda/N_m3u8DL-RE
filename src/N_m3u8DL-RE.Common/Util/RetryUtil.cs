using System.Net;
using System.Net.Sockets;
using N_m3u8DL_RE.Common.Log;
using Spectre.Console;

namespace N_m3u8DL_RE.Common.Util;

public static class RetryUtil
{
    // 只重试临时网络故障；鉴权失败、资源不存在及本地文件错误应交给调用者处理。
    public static bool IsTransientNetworkError(Exception exception) => exception switch
    {
        HttpRequestException { HttpRequestError: HttpRequestError.ConfigurationLimitExceeded } => false,
        HttpRequestException request => request.StatusCode == null ||
            request.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests ||
            (int)request.StatusCode >= 500,
        HttpIOException => true,
        IOException { InnerException: SocketException } => true,
        WebException => true,
        TimeoutException => true,
        OperationCanceledException => true, // 调用者须先排除用户主动取消。
        _ => false
    };

    public static async Task<T?> WebRequestRetryAsync<T>(Func<Task<T>> funcAsync, int maxRetries = 10, int retryDelayMilliseconds = 1500, int retryDelayIncrementMilliseconds = 0)
    {
        var retryCount = 0;
        var result = default(T);
        Exception currentException = new();

        while (retryCount < maxRetries)
        {
            try
            {
                result = await funcAsync();
                break;
            }
            catch (Exception ex) when (ex is WebException or IOException or HttpRequestException)
            {
                currentException = ex;
                retryCount++;
                Logger.WarnMarkUp($"[grey]{ex.Message.EscapeMarkup()} ({retryCount}/{maxRetries})[/]");
                await Task.Delay(retryDelayMilliseconds + (retryDelayIncrementMilliseconds * (retryCount - 1)));
            }
        }

        if (retryCount == maxRetries)
        {
            throw new Exception($"Failed to execute action after {maxRetries} retries.", currentException);
        }

        return result;
    }
}