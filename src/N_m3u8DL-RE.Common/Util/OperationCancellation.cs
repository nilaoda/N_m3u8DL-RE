namespace N_m3u8DL_RE.Common.Util;

/// <summary>同一次执行中的嵌套请求共用取消信号。</summary>
public static class OperationCancellation
{
    private static readonly AsyncLocal<CancellationToken> current = new();
    public static CancellationToken Token { get => current.Value; set => current.Value = value; }
    public static CancellationTokenSource Link(CancellationToken token) =>
        CancellationTokenSource.CreateLinkedTokenSource(token, Token);
}
