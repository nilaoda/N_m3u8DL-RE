using System.CommandLine;
using N_m3u8DL_RE.Common.Resource;
using N_m3u8DL_RE.Worker;

namespace N_m3u8DL_RE.CommandLine;

internal static class WorkerCommandDefinition
{
    internal const string Name = "worker";

    internal static Command Create()
    {
        var stdio = new Option<bool>("--stdio")
        {
            Description = ResString.cmd_workerStdio,
            Required = true,
            Arity = ArgumentArity.Zero
        };
        stdio.Validators.Add(result =>
        {
            if (!result.GetValueOrDefault<bool>())
                result.AddError(ResString.workerStdioRequired);
        });
        var command = new Command(Name, ResString.cmd_worker) { stdio };
        command.SetAction(async (_, token) => await WorkerHost.RunConsoleAsync(token));
        return command;
    }
}
