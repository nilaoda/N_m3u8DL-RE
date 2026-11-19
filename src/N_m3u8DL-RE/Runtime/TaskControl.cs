namespace N_m3u8DL_RE.Runtime;

internal sealed class TaskControl : IDisposable
{
    private readonly CancellationTokenSource cancellation = new();
    private readonly CancellationTokenSource stop = new();
    public CancellationToken CancellationToken => cancellation.Token;
    public CancellationToken StopToken => stop.Token;
    public void Cancel() => cancellation.Cancel();
    public void Stop() => stop.Cancel();
    public void Dispose()
    {
        cancellation.Dispose();
        stop.Dispose();
    }
}
