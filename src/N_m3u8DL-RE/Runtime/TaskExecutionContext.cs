using System.Globalization;
using System.Collections.Concurrent;
using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Enum;
using Spectre.Console;

namespace N_m3u8DL_RE.Runtime;

internal sealed class TaskExecutionContext(ITaskObserver observer, IUserInteraction interaction, TaskControl control, bool isWorker = false)
{
    private static readonly AsyncLocal<TaskExecutionContext?> current = new();
    public static TaskExecutionContext? Current { get => current.Value; set => current.Value = value; }
    public ITaskObserver Observer { get; } = observer;
    public IUserInteraction Interaction { get; } = interaction;
    public TaskControl Control { get; } = control;
    public bool IsWorker { get; } = isWorker;
    public bool IsLive { get; set; }
    public string Phase { get; private set; } = "analyzing";
    private readonly ConcurrentDictionary<string, byte> outputs = new(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    public void State(string phase)
    {
        Phase = phase;
        Observer.State(Control.CancellationToken.IsCancellationRequested || Control.StopToken.IsCancellationRequested ? "stopping" : "running", phase);
    }
    public void SetTracks(List<StreamSpec> streams) => Observer.Tracks(streams.Select((stream, index) => new TrackInfo(
        index.ToString(CultureInfo.InvariantCulture), stream.ToString().RemoveMarkup(),
        (stream.MediaType ?? MediaType.VIDEO).ToString().ToLowerInvariant(), stream.Language,
        stream.Codecs, stream.Resolution, stream.Bandwidth, stream.Playlist?.TotalDuration)).ToArray());
    public void AddOutput(string path) => outputs.TryAdd(Path.GetFullPath(path), 0);
    public string[] Outputs => outputs.Keys.Where(File.Exists).Order(StringComparer.Ordinal).ToArray();
    public void ThrowIfCancelled() => Control.CancellationToken.ThrowIfCancellationRequested();
}
