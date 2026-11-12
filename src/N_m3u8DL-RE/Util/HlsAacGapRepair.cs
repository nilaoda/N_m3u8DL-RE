using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Common.Resource;
using N_m3u8DL_RE.Entity;

namespace N_m3u8DL_RE.Util;

internal static class HlsAacGapRepair
{
    private const int FrameSamples = 1024;
    internal static readonly int[] Rates = [96000, 88200, 64000, 48000, 44100, 32000, 24000,
        22050, 16000, 12000, 11025, 8000, 7350];
    internal static readonly string[] Layouts = ["", "mono", "stereo", "3.0", "4.0", "5.0", "5.1"];

    internal sealed record PreparedTrack(int AudioIndex, string Path, double StartSeconds, int Inserted, string TimeBase);

    internal static async Task<List<PreparedTrack>> PrepareAsync(string binary, IReadOnlyList<string> inputArguments,
        IReadOnlyList<Mediainfo> streams, string tempDir, bool loopbackInput, CancellationToken token)
    {
        var scan = new List<string>(inputArguments);
        int input = scan.IndexOf("-i"), format = scan.IndexOf("-f");
        bool concatList = format >= 0 && format + 1 < input && scan[format + 1] == "concat";
        if (concatList) scan.InsertRange(input, ["-segment_time_metadata", "1"]);
        var clocks = streams.Where(stream => stream.Type == "Audio").Select((stream, index) => (stream, index))
            .Where(item => item.stream.BaseInfo?.StartsWith("aac (LC)", StringComparison.OrdinalIgnoreCase) == true)
            .Select(item => new HlsAacPacketClock(item.index, concatList)).ToArray();
        if (clocks.Length == 0) return [];

        // Activate the same source A/V tracks as the mux so FFmpeg keeps a delayed audio start.
        // framecrc emits packet timestamps without decoding. The reader retains duration runs only.
        scan.AddRange(["-loglevel", "error", "-map", "0:v?", "-map", "0:a?", "-c", "copy", "-f", "null", "-"]);
        foreach (var clock in clocks)
            scan.AddRange(["-map", $"0:a:{clock.AudioIndex}"]);
        // framecrc itself makes backwards DTS nondecreasing. Its duration column is unused
        // by our scan; carry the actual demuxed PTS there to verify overlaps at concat joins.
        string filter = concatList ? $"aac_adtstoasc,setts=duration=PTS+{HlsAacPacketClock.TimestampBias}" : "aac_adtstoasc";
        scan.AddRange(["-c:a", "copy", "-bsf:a", filter, "-f", "framecrc", "pipe:1"]);
        var scanResult = await Run(scan, line => HlsAacPacketClock.ReadLine(clocks, line));
        if (scanResult.ExitCode != 0 && ScanUnavailable(scanResult.Error))
        {
            Logger.Warn(ResString.hlsAacScanUnavailable);
            return [];
        }
        CheckResult(scanResult);

        List<(HlsAacGapDetector.Track Track, List<Gap> Gaps, string TimeBase)> repairs = [];
        foreach (var clock in clocks)
        {
            var track = clock.Finish();
            if (track == null || !HlsAacGapDetector.HasSparseClock(track.Durations, track.SampleRate, token)) continue;
            var gaps = Plan(track, token);
            if (gaps.Count != 0) repairs.Add((track, gaps, clock.TimeBase));
        }
        if (repairs.Count == 0) return [];

        // Only affected tracks need temporary audio. Read the source once for all of them.
        Directory.CreateDirectory(tempDir);
        List<string> extract = [.. inputArguments, "-loglevel", "error"];
        foreach (var (track, _, _) in repairs)
            extract.AddRange(["-f", "lavfi", "-i", $"anullsrc=r={track.SampleRate}:cl={Layouts[track.Channels]}"]);
        for (int i = 0; i < repairs.Count; i++)
        {
            int index = repairs[i].Track.AudioIndex;
            extract.AddRange(["-map", $"0:a:{index}", "-c:a", "copy", "-f", "adts", Path.Combine(tempDir, $"{index}.aac"),
                "-map", $"{i + 1}:a:0", "-frames:a", "3", "-c:a", "aac", "-profile:a", "aac_low", "-f", "adts", Path.Combine(tempDir, $"{index}-silence.aac")]);
        }
        CheckResult(await Run(extract));
        List<PreparedTrack> result = [];
        foreach (var (track, gaps, timeBase) in repairs)
        {
            var filled = Path.Combine(tempDir, $"{track.AudioIndex}-filled.aac");
            WriteAudio(Path.Combine(tempDir, $"{track.AudioIndex}.aac"), Path.Combine(tempDir, $"{track.AudioIndex}-silence.aac"), filled, track, gaps, token);
            result.Add(new(track.AudioIndex, filled, track.StartSeconds, gaps.Sum(gap => gap.Count), timeBase));
        }
        return result;

        Task<ProcessUtil.Result> Run(IEnumerable<string> arguments, Action<string>? output = null) =>
            ProcessUtil.RunAsync(binary, arguments, token, loopbackInput: loopbackInput, outputLine: output, captureLimit: 2048);

        static void CheckResult(ProcessUtil.Result result)
        {
            if (result.ExitCode != 0)
                throw new IOException($"FFmpeg ({result.ExitCode}): {result.Error.Trim()}");
        }
    }

    internal static bool ScanUnavailable(string error) =>
        error.Contains("Requested output format 'framecrc' is not", StringComparison.OrdinalIgnoreCase) ||
        error.Contains("Requested output format 'null' is not", StringComparison.OrdinalIgnoreCase) ||
        error.Contains("Unrecognized option 'segment_time_metadata'", StringComparison.OrdinalIgnoreCase) ||
        error.Contains("Option segment_time_metadata not found", StringComparison.OrdinalIgnoreCase) ||
        error.Contains("Bitstream filter not found", StringComparison.OrdinalIgnoreCase) &&
            (error.Contains("setts", StringComparison.OrdinalIgnoreCase) || error.Contains("aac_adtstoasc", StringComparison.OrdinalIgnoreCase)) ||
        error.Contains("Unknown bitstream filter 'setts'", StringComparison.OrdinalIgnoreCase) ||
        error.Contains("Unknown bitstream filter 'aac_adtstoasc'", StringComparison.OrdinalIgnoreCase);

    internal readonly record struct Gap(long AfterPacket, int Count);

    internal static List<Gap> Plan(HlsAacGapDetector.Track track, CancellationToken token)
    {
        List<Gap> gaps = [];
        // After eight consecutive nominal AAC intervals, a later timestamp change
        // belongs to a separate event. Never move an established gap across a splice.
        const int stableIntervals = 8;
        long packet = 0, residual = 0, stable = 0;
        int firstPending = 0;
        for (int i = 0; i < track.Durations.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            var run = track.Durations[i];
            long count = run.Count - (i == track.Durations.Count - 1 ? 1L : 0L);
            if (count == 0) break;
            long excess = (long)run.Duration - FrameSamples;
            stable = Math.Abs(excess) <= 1 ? stable + count : 0;
            if (stable >= stableIntervals)
                firstPending = gaps.Count;
            // Carry clock quantisation and fractional residuals forward. Only a
            // positive local gap of at least half a frame is a safe splice point.
            if (excess < FrameSamples / 2)
            {
                residual += excess * count;
                packet += count;
                // A delayed timestamp can be paid back by several shorter intervals.
                // Reconsider the most recent insertions first, keeping the location
                // of an earlier, genuinely missing sample intact.
                while (excess < -1 && residual < -FrameSamples / 2 && gaps.Count > firstPending)
                {
                    var last = gaps[^1];
                    int remove = (int)Math.Min(last.Count, (-residual + FrameSamples / 2 - 1) / FrameSamples);
                    residual += (long)remove * FrameSamples;
                    if (remove == last.Count)
                        gaps.RemoveAt(gaps.Count - 1);
                    else
                        gaps[^1] = last with { Count = last.Count - remove };
                }
                continue;
            }
            for (long j = 0; j < count; j++)
            {
                token.ThrowIfCancellationRequested();
                residual += excess;
                // Avoid manufacturing audio across a clock reset. Carry the fraction
                // between real gaps, so half-frame rounding does not accumulate.
                if (Math.Abs(residual) >= 10L * track.SampleRate)
                    return [];
                int missing = (int)((residual + FrameSamples / 2) / FrameSamples);
                if (missing > 0)
                {
                    gaps.Add(new(packet, missing));
                    residual -= (long)missing * FrameSamples;
                }
                packet++;
            }
        }
        // If a distant clock correction leaves fewer missing samples than planned,
        // their location is ambiguous. Preserve the original track.
        return residual < -FrameSamples / 2 ? [] : gaps;
    }

    private static void WriteAudio(string raw, string silence, string output, HlsAacGapDetector.Track track,
        List<Gap> gaps, CancellationToken token)
    {
        byte[] silentFrame = new byte[8192], frame = new byte[8192];
        using var template = File.OpenRead(silence);
        ReadFrame(template, silentFrame, track); // Encoder priming.
        var silentSize = ReadFrame(template, silentFrame, track);
        using var source = File.OpenRead(raw);
        using var target = File.Create(output);
        long total = track.Durations.Sum(run => (long)run.Count);
        int next = 0;
        for (long i = 0; i < total; i++)
        {
            token.ThrowIfCancellationRequested();
            int size = ReadFrame(source, frame, track);
            target.Write(frame, 0, size);
            if (next >= gaps.Count || gaps[next].AfterPacket != i)
                continue;
            for (int j = 0; j < gaps[next].Count; j++)
                target.Write(silentFrame, 0, silentSize);
            next++;
        }
        if (source.Position != source.Length || next != gaps.Count)
            throw new InvalidDataException("AAC sample count changed between timestamp scan and extraction.");
    }

    private static int ReadFrame(Stream input, byte[] frame, HlsAacGapDetector.Track track)
    {
        input.ReadExactly(frame, 0, 7);
        int rate = (frame[2] >> 2) & 15, channels = ((frame[2] & 1) << 2) | (frame[3] >> 6);
        int size = ((frame[3] & 3) << 11) | (frame[4] << 3) | (frame[5] >> 5);
        if (frame[0] != 0xff || (frame[1] & 0xf7) != 0xf1 || frame[2] >> 6 != 1 ||
            rate >= Rates.Length || Rates[rate] != track.SampleRate || channels != track.Channels ||
            (frame[6] & 3) != 0 || size <= 7)
            throw new InvalidDataException("Unexpected AAC-LC ADTS frame configuration.");
        input.ReadExactly(frame, 7, size - 7);
        return size;
    }
}
