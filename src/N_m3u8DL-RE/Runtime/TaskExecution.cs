using N_m3u8DL_RE.Common.Util;

namespace N_m3u8DL_RE.Runtime;

// 下载与文件处理共用上下文作用域、取消判定和结果封装。
internal static class TaskExecution
{
    internal static async Task<ExecutionResult> RunAsync(TaskExecutionContext context, Func<Task> action,
        bool includeErrorDetails = false)
    {
        var previousContext = TaskExecutionContext.Current;
        var previousCancellation = OperationCancellation.Token;
        TaskExecutionContext.Current = context;
        OperationCancellation.Token = context.Control.CancellationToken;
        try
        {
            context.ThrowIfCancelled();
            await action();
            context.ThrowIfCancelled();
            return new("succeeded", context.Outputs);
        }
        catch (OperationCanceledException) when (context.Control.CancellationToken.IsCancellationRequested)
        {
            return new("cancelled", context.Outputs);
        }
        catch (Exception ex)
        {
            if (context.Control.CancellationToken.IsCancellationRequested)
                return new("cancelled", context.Outputs);
            return new("failed", context.Outputs, "task_failed", includeErrorDetails ? ex.ToString() : ex.Message);
        }
        finally
        {
            TaskExecutionContext.Current = previousContext;
            OperationCancellation.Token = previousCancellation;
        }
    }
}
