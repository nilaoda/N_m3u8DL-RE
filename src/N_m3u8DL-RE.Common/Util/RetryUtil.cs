using System.Net;

using N_m3u8DL_RE.Common.Log;

using Spectre.Console;

namespace N_m3u8DL_RE.Common.Util
{
    public static class RetryUtil
    {
        public static async Task<T?> WebRequestRetryAsync<T>(Func<Task<T>> funcAsync, int maxRetries = 10, int retryDelayMilliseconds = 1500, int retryDelayIncrementMilliseconds = 0)
        {
            int retryCount = 0;
            T? result = default;
            Exception? currentException = null;

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

            return retryCount == maxRetries
                ? throw new InvalidOperationException($"Failed to execute action after {maxRetries} retries.", currentException)
                : result;
        }
    }
}