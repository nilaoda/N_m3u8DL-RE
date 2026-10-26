using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Threading.Channels;
using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Common.Resource;
using N_m3u8DL_RE.Common.Util;
using N_m3u8DL_RE.Entity;

namespace N_m3u8DL_RE.DownloadManager;

// 新片和补片分开调度；同一分片始终只有一个请求，按序消费者只等待下载结果。
internal sealed class LiveSegmentDownloadQueue : IAsyncDisposable
{
    private sealed class Work(MediaSegment segment, string path)
    {
        internal readonly MediaSegment Segment = segment;
        internal readonly string Path = path;
        internal readonly TaskCompletionSource<DownloadResult?> Result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Failures;
        internal long FailedAt;
        internal long RetryAt;
        internal TimeSpan Budget;
        internal TimeSpan PublicationWait;
        internal CancellationTokenSource? RetryStop;
        internal Task? Expiration;
        internal int Finishing;
    }

    private readonly ConcurrentDictionary<MediaSegment, Work> pending = new(ReferenceEqualityComparer.Instance);
    private readonly Channel<Work> fresh = Channel.CreateUnbounded<Work>();
    private readonly Channel<Work> retries = Channel.CreateUnbounded<Work>();
    private readonly CancellationTokenSource stop;
    private readonly Task[] workers;
    private readonly Func<MediaSegment, string, CancellationToken, Task<DownloadResult?>> download;
    private readonly LiveSegmentNotFoundPolicy policy;
    private readonly int retryCount;
    private readonly Action fatalFailure;

    internal LiveSegmentDownloadQueue(int concurrency, int retryCount, LiveSegmentNotFoundPolicy policy,
        Func<MediaSegment, string, CancellationToken, Task<DownloadResult?>> download, Action fatalFailure,
        CancellationToken cancellationToken)
    {
        this.download = download;
        this.policy = policy;
        this.retryCount = retryCount;
        this.fatalFailure = fatalFailure;
        stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        // 补片独占一个并发槽，即使只配置一个新片线程，也不会被旧片占满。
        workers = Enumerable.Range(0, Math.Max(1, concurrency)).Select(_ => RunAsync(fresh.Reader, false))
            .Append(RunAsync(retries.Reader, true)).ToArray();
    }

    internal void Enqueue(MediaSegment segment, string path)
    {
        var work = new Work(segment, path);
        if (pending.TryAdd(segment, work))
            fresh.Writer.TryWrite(work);
    }

    internal async Task<DownloadResult?> TakeAsync(MediaSegment segment)
    {
        stop.Token.ThrowIfCancellationRequested();
        var work = pending[segment];
        try { return await work.Result.Task.WaitAsync(stop.Token); }
        catch (Exception) when (!stop.IsCancellationRequested)
        {
            // 先保留已完成的正常前缀；消费者到达致命错误所在批次时再停止其它请求。
            fatalFailure();
            throw;
        }
        finally { pending.TryRemove(segment, out _); }
    }

    private async Task RunAsync(ChannelReader<Work> reader, bool retrying)
    {
        try
        {
            await foreach (var work in reader.ReadAllAsync(stop.Token))
            {
                if (retrying)
                {
                    var delay = TimeSpan.FromSeconds(1) - Stopwatch.GetElapsedTime(work.RetryAt);
                    if (delay > TimeSpan.Zero)
                        await Task.Delay(delay, stop.Token);
                    if (Stopwatch.GetElapsedTime(work.FailedAt) >= work.Budget)
                    {
                        Unavailable(work);
                    }
                }
                if (work.Result.Task.IsCompleted)
                {
                    await FinishRetryAsync(work);
                    continue;
                }
                await AttemptAsync(work);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
    }

    private async Task AttemptAsync(Work work)
    {
        try
        {
            var result = await download(work.Segment, work.Path, work.RetryStop?.Token ?? stop.Token);
            if (work.Result.TrySetResult(result) && work.Failures > 0)
                Logger.Info(ResString.liveNetworkRecovered);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            work.Result.TrySetCanceled(stop.Token);
        }
        catch (OperationCanceledException) when (work.RetryStop is { IsCancellationRequested: true })
        {
            Unavailable(work);
        }
        catch (Exception error)
        {
            if (++work.Failures == 1)
            {
                work.FailedAt = Stopwatch.GetTimestamp();
                work.Budget = policy.GetRetryWindow(work.Segment);
                work.PublicationWait = policy.GetPublicationWait(work.Segment);
            }
            var elapsed = Stopwatch.GetElapsedTime(work.FailedAt);
            var notFound = error is HttpRequestException { StatusCode: HttpStatusCode.NotFound };
            if (error is HttpRequestException { StatusCode: HttpStatusCode.Gone } ||
                notFound && !policy.ShouldRetry(work.Segment, work.Failures, retryCount, elapsed, work.PublicationWait) ||
                (notFound || RetryUtil.IsTransientNetworkError(error)) && elapsed >= work.Budget)
            {
                Unavailable(work);
                return;
            }
            if (notFound || RetryUtil.IsTransientNetworkError(error) ||
                error is HttpRequestException { StatusCode: HttpStatusCode.Forbidden } && work.Failures <= retryCount)
            {
                if (work.Failures == 1)
                    Logger.Warn(notFound ? ResString.liveSegmentNotReady : ResString.liveNetworkRetry);
                if (work.RetryStop == null)
                {
                    work.RetryStop = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                    work.Expiration = ExpireAsync(work);
                }
                work.RetryAt = Stopwatch.GetTimestamp();
                retries.Writer.TryWrite(work);
                return;
            }
            work.Result.TrySetException(error);
            // 主动停止可能不再消费后续批次，仍须观察已失败的任务。
            _ = work.Result.Task.Exception;
        }
        finally
        {
            if (stop.IsCancellationRequested)
                work.Result.TrySetCanceled(stop.Token);
            if (work.Result.Task.IsCompleted)
                await FinishRetryAsync(work);
        }
    }

    private static void Unavailable(Work work)
    {
        if (work.Result.TrySetResult(null))
            Logger.Warn(ResString.liveSegmentUnavailable);
    }

    private async Task ExpireAsync(Work work)
    {
        try
        {
            var remaining = work.Budget - Stopwatch.GetElapsedTime(work.FailedAt);
            if (remaining > TimeSpan.Zero)
                await Task.Delay(remaining, work.RetryStop!.Token);
            if (!stop.IsCancellationRequested)
                Unavailable(work);
            // 到期同时解除按序等待并取消在途请求，排队和慢传输都不能延长补片窗口。
            work.RetryStop!.Cancel();
        }
        catch (OperationCanceledException) when (work.RetryStop is { IsCancellationRequested: true }) { }
    }

    private static async Task FinishRetryAsync(Work work)
    {
        if (work.RetryStop == null || Interlocked.Exchange(ref work.Finishing, 1) != 0)
            return;
        work.RetryStop.Cancel();
        await work.Expiration!;
        work.RetryStop.Dispose();
        work.RetryStop = null;
    }

    public async ValueTask DisposeAsync()
    {
        stop.Cancel();
        fresh.Writer.TryComplete();
        retries.Writer.TryComplete();
        await Task.WhenAll(workers);
        while (fresh.Reader.TryRead(out var freshWork))
            await FinishRetryAsync(freshWork);
        while (retries.Reader.TryRead(out var retryWork))
            await FinishRetryAsync(retryWork);
        foreach (var work in pending.Values)
        {
            work.Result.TrySetCanceled(stop.Token);
            await FinishRetryAsync(work);
        }
        stop.Dispose();
    }
}
