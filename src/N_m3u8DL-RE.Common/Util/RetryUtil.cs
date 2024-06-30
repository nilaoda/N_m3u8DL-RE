using System.Net;
using N_m3u8DL_RE.Common.Log;
using Spectre.Console;

namespace N_m3u8DL_RE.Common.Util;

public class RetryUtil
{
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