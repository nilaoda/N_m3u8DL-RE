using System.Globalization;
using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Protocol;
using N_m3u8DL_RE.Runtime;
using N_m3u8DL_RE.Util;
using Spectre.Console;

namespace N_m3u8DL_RE.Worker;

internal sealed class WorkerUserInteraction(StdioTransport transport, ITaskObserver observer) : IUserInteraction
{
    private readonly object gate = new();
    private string? pendingId;
    private HashSet<string>? available;
    private TaskCompletionSource<string[]>? response;

    public async Task<List<StreamSpec>> SelectStreamsAsync(List<StreamSpec> streams, CancellationToken token)
    {
        if (streams.Count <= 1)
            return streams;
        var choices = streams.Select((s, i) => new SelectionChoice
        {
            Id = i.ToString(CultureInfo.InvariantCulture), Label = s.ToString().RemoveMarkup(),
            MediaType = (s.MediaType ?? MediaType.VIDEO).ToString().ToLowerInvariant(), Language = s.Language,
            Codecs = s.Codecs, Resolution = s.Resolution, Bandwidth = s.Bandwidth,
            DurationSeconds = s.Playlist?.TotalDuration
        }).ToArray();
        var defaults = new HashSet<StreamSpec> { streams[0] };
        if (streams[0].AudioId is { } audioId && FilterUtil.DefaultTrack(streams.Where(s => s.MediaType == MediaType.AUDIO).ToList(), audioId) is { } audio)
            defaults.Add(audio);
        if (streams[0].SubtitleId is { } subtitleId && FilterUtil.DefaultTrack(streams.Where(s => s.MediaType == MediaType.SUBTITLES).ToList(), subtitleId) is { } subtitle)
            defaults.Add(subtitle);
        var defaultIds = streams.Select((s, i) => (Stream: s, Id: choices[i].Id)).Where(s => defaults.Contains(s.Stream)).Select(s => s.Id).ToArray();
        var ids = await SelectAsync("streams", choices, defaultIds, token);
        return ids.Select(id => streams[int.Parse(id, CultureInfo.InvariantCulture)]).ToList();
    }

    public async Task<List<VodPartSelector.PartGroup>> SelectPartsAsync(List<VodPartSelector.PartGroup> groups, CancellationToken token)
    {
        var choices = groups.Select((g, i) => new SelectionChoice
        {
            Id = i.ToString(CultureInfo.InvariantCulture), Label = g.Display,
            DurationSeconds = g.Duration, PartIds = g.Ids.ToArray()
        }).ToArray();
        var ids = await SelectAsync("parts", choices, choices.Select(c => c.Id).ToArray(), token);
        return ids.Select(id => groups[int.Parse(id, CultureInfo.InvariantCulture)]).ToList();
    }

    private async Task<string[]> SelectAsync(string kind, SelectionChoice[] choices, string[] defaults, CancellationToken token)
    {
        var id = Guid.NewGuid().ToString("N");
        TaskCompletionSource<string[]> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (gate)
        {
            pendingId = id;
            available = choices.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
            response = completion;
        }
        observer.State("waitingForInput", "analyzing");
        transport.Publish(new WorkerEvent { Type = "interaction", Interaction = new(id, kind, choices, defaults) });
        try
        {
            return await completion.Task.WaitAsync(TimeSpan.FromMinutes(30), token);
        }
        finally
        {
            lock (gate)
            {
                pendingId = null;
                response = null;
                available = null;
            }
            observer.State("running", "analyzing");
        }
    }

    internal void Respond(WorkerCommand command, Action accepted)
    {
        lock (gate)
        {
            if (response == null || response.Task.IsCompleted || command.InteractionId != pendingId)
                throw new ArgumentException("Unknown or expired interaction.");
            if (command.SelectedIds is not { Length: > 0 } ids || ids.Any(id => !available!.Contains(id)) || ids.Distinct().Count() != ids.Length)
                throw new ArgumentException("Select one or more distinct IDs from this interaction.");
            accepted();
            response.TrySetResult(ids);
        }
    }
}
