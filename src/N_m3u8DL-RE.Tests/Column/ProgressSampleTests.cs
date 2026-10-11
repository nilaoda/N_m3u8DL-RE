using System.Collections.Concurrent;
using N_m3u8DL_RE.Column;
using N_m3u8DL_RE.Common.Util;
using N_m3u8DL_RE.Entity;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace N_m3u8DL_RE.Tests.Column;

public class ProgressSampleTests
{
    [Fact]
    public void SamplesOncePerSecondAndKeepsTasksIndependent()
    {
        var clock = new ManualTimeProvider();
        var sample = new ProgressSample<long>(clock);
        var first = new ProgressTask(1, "first", 100);
        var second = new ProgressTask(2, "second", 100);
        var elapsed = TimeSpan.Zero;
        long value = 1;
        long ReadValue(TimeSpan interval)
        {
            elapsed = interval;
            return value;
        }

        Assert.Equal(1, sample.GetValue(first, ReadValue));
        value = 2;
        clock.Elapsed = TimeSpan.FromMilliseconds(500);
        Assert.Equal(1, sample.GetValue(first, ReadValue));
        Assert.Equal(2, sample.GetValue(second, ReadValue));
        clock.Elapsed = TimeSpan.FromSeconds(1);
        Assert.Equal(2, sample.GetValue(first, ReadValue));
        Assert.Equal(TimeSpan.FromSeconds(1), elapsed);
        value = 3;
        Assert.Equal(2, sample.GetValue(second, ReadValue));
        clock.Elapsed = TimeSpan.FromSeconds(1.5);
        Assert.Equal(3, sample.GetValue(second, ReadValue));
        // 渲染调度延迟时，速度应使用实际采样间隔。
        clock.Elapsed = TimeSpan.FromSeconds(3);
        sample.GetValue(first, ReadValue);
        Assert.Equal(TimeSpan.FromSeconds(2), elapsed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EndingBeforeOneSecondRefreshesFinalValue(bool complete)
    {
        var sample = new ProgressSample<long>(new ManualTimeProvider());
        var task = new ProgressTask(1, "file", 100);
        task.StartTask();
        Assert.Equal(1, sample.GetValue(task, _ => 1));
        if (complete) task.Value = task.MaxValue;
        else task.StopTask();
        Assert.Equal(2, sample.GetValue(task, _ => 2));
    }

    [Theory]
    [InlineData("binary")]
    [InlineData("recording")]
    [InlineData("download")]
    public void SizeColumnsCacheLiveValuesAndShowFinalSize(string kind)
    {
        var task = new ProgressTask(1, "file", 100);
        task.StartTask();
        task.Value = 1;
        var sizes = new ConcurrentDictionary<int, long>();
        var speed = new SpeedContainer { SingleSegment = true, ResponseLength = 8192 };
        ProgressColumn column = kind switch
        {
            "binary" => new BinaryDownloadSizeColumn(sizes, null),
            "recording" => new RecordingSizeColumn(sizes),
            _ => new DownloadStatusColumn(new ConcurrentDictionary<int, SpeedContainer>(
                [new KeyValuePair<int, SpeedContainer>(task.Id, speed)]))
        };
        var options = new RenderOptions(AnsiConsole.Profile.Capabilities, new Size(80, 25));
        string Render() => string.Concat(column.Render(options, task, TimeSpan.Zero).Render(options, 80).Select(segment => segment.Text)).Trim();

        sizes[task.Id] = 1024;
        speed.Add(1024);
        var first = Render();
        sizes[task.Id] = 8192;
        speed.Add(7168);
        Assert.Equal(first, Render());
        task.StopTask();
        Assert.Equal(GlobalUtil.FormatFileSize(8192), Render());
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        internal TimeSpan Elapsed { get; set; }
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Elapsed.Ticks;
    }
}
