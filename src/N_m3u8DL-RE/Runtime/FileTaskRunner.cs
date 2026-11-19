using N_m3u8DL_RE.Util;

namespace N_m3u8DL_RE.Runtime;

internal static class FileTaskRunner
{
    internal static Task<ExecutionResult> RunAsync(FileTaskOptions options, TaskExecutionContext context) =>
        TaskExecution.RunAsync(context, async () =>
        {
            context.State("preparing");
            context.Observer.Tracks([new TrackInfo("0", Path.GetFileName(options.Output), "file")]);
            var result = await FileToolUtil.ExecuteAsync(options, context.Control.CancellationToken);
            if (!result.Success)
                throw new InvalidOperationException(result.ErrorMessage ?? "File processing failed.");
            // 预览只校验和规划，不把已有文件误报成本次产物。
            if (!options.DryRun)
                context.AddOutput(result.Output);
        });
}
