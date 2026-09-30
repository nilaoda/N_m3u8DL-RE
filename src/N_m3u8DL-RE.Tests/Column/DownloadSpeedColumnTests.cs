using System.Collections.Concurrent;
using N_m3u8DL_RE.Column;
using N_m3u8DL_RE.Entity;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace N_m3u8DL_RE.Tests.Column;

public class DownloadSpeedColumnTests
{
    [Fact]
    public async Task FullResponseCanStillRollBackUntilDownloadIsValidated()
    {
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(TextWriter.Null) });
        await console.Progress().AutoRefresh(false).StartAsync(ctx =>
        {
            var task = ctx.AddTask("file", maxValue: 100_000);
            var speed = new SpeedContainer { SingleSegment = true, ResponseLength = 100_000 };
            speed.Add(100_000);
            var column = new DownloadSpeedColumn(new ConcurrentDictionary<int, SpeedContainer>(
                [new KeyValuePair<int, SpeedContainer>(task.Id, speed)]));
            var render = new RenderOptions(console.Profile.Capabilities, new Size(80, 25));
            column.Render(render, task, TimeSpan.Zero);
            Assert.False(task.IsFinished);
            Assert.True(task.Percentage <= 99.99);
            // 尾部校验失败后，进度必须仍能回退，速度统计则保留已经接收的网络流量。
            speed.AddDownloaded(-60_000);
            column.Render(render, task, TimeSpan.Zero);
            Assert.Equal(40_000, task.Value);
            Assert.Equal(100_000, speed.Downloaded);
            Assert.False(task.IsFinished);
            task.Value = task.MaxValue;
            Assert.True(task.IsFinished);
            return Task.CompletedTask;
        });
    }
}
