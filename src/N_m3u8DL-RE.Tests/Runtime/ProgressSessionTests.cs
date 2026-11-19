using System.Collections.Concurrent;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.ConsoleUI;
using N_m3u8DL_RE.Entity;
using N_m3u8DL_RE.Runtime;
using Spectre.Console;

namespace N_m3u8DL_RE.Tests.Runtime;

[Collection("Download console")]
public class ProgressSessionTests
{
    [Fact]
    public async Task FinalSamplingDoesNotRewindAValidatedSingleFile()
    {
        using var control = new TaskControl();
        var previous = TaskExecutionContext.Current;
        TaskExecutionContext.Current = new(new NullTaskObserver(), new ConsoleUserInteraction(), control, isWorker: true);
        ProgressTask? task = null;
        try
        {
            var speeds = new ConcurrentDictionary<int, SpeedContainer>();
            await ProgressSession.RunAsync(CustomAnsiConsole.Console.Progress(), speeds, context =>
            {
                task = context.AddTask("file", maxValue: 100);
                var speed = new SpeedContainer { SingleSegment = true, ResponseLength = 100 };
                speed.Add(100);
                speeds[task.Id] = speed;
                task.Value = task.MaxValue;
                return Task.CompletedTask;
            });
            Assert.NotNull(task);
            Assert.Equal(100, task.Value);
            Assert.True(task.IsFinished);
        }
        finally
        {
            TaskExecutionContext.Current = previous;
        }
    }
}
