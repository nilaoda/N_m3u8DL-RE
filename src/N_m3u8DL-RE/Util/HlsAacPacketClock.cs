using System.Globalization;

namespace N_m3u8DL_RE.Util;

// One selected audio output in FFmpeg's framecrc stream. Packet times come from
// the very same concat input used by the final mux, including its discontinuity handling.
internal sealed class HlsAacPacketClock(int audioIndex, bool concatList = false)
{
    // Keep the scan-only packet duration positive even for a negative preroll timestamp.
    internal const long TimestampBias = 1L << 32;
    internal int AudioIndex { get; } = audioIndex;
    internal string TimeBase => $"{numerator}/{denominator}";
    private readonly List<HlsAacGapDetector.DurationRun> runs = [];
    private readonly List<(int Size, uint Checksum)> configurations = [];
    private int numerator, denominator, rate, channels;
    private long? firstPts;
    private long previousSample;
    private uint? previousSegment;
    private bool invalid;

    internal static void ReadLine(HlsAacPacketClock[] clocks, string line)
    {
        if (line.StartsWith('#'))
        {
            int space = line.IndexOf(' '), colon = line.IndexOf(':');
            if (space < 0 || colon <= space || !int.TryParse(line.AsSpan(space + 1, colon - space - 1), out var index) ||
                (uint)index >= clocks.Length) return;
            var clock = clocks[index];
            var value = line[(colon + 1)..].Trim();
            switch (line[..space])
            {
                case "#tb":
                    var parts = value.Split('/');
                    if (parts.Length != 2 || !int.TryParse(parts[0], out clock.numerator) ||
                        !int.TryParse(parts[1], out clock.denominator)) clock.invalid = true;
                    break;
                case "#sample_rate":
                    if (!int.TryParse(value, out clock.rate)) clock.invalid = true;
                    break;
                case "#channel_layout_name":
                    clock.channels = Array.IndexOf(HlsAacGapRepair.Layouts, value);
                    break;
                case "#extradata":
                    clock.AddConfiguration(value);
                    break;
            }
        }
        else
        {
            var data = line.AsSpan();
            int comma = data.IndexOf(',');
            if (comma > 0 && int.TryParse(data[..comma], out var index) && (uint)index < clocks.Length)
                clocks[index].ReadPacket(data[(comma + 1)..]);
        }
    }

    private void ReadPacket(ReadOnlySpan<char> packet)
    {
        if (invalid) return;
        Span<Range> fields = stackalloc Range[4];
        if (packet.Split(fields, ',') < 4 || numerator <= 0 || denominator <= 0 || rate <= 0 ||
            !long.TryParse(packet[fields[0]], CultureInfo.InvariantCulture, out long dts) ||
            !long.TryParse(packet[fields[1]], CultureInfo.InvariantCulture, out long pts) || pts != dts || pts < 0)
        {
            invalid = true;
            return;
        }
        bool boundary = false;
        if (concatList)
        {
            if (!long.TryParse(packet[fields[2]], CultureInfo.InvariantCulture, out long encoded) || encoded < TimestampBias)
            { invalid = true; return; }
            pts = encoded - TimestampBias;
            const string marker = "Strings Metadata,";
            int offset = packet.IndexOf(marker, StringComparison.Ordinal);
            uint? segment = null;
            if (offset >= 0 && ReadSideData(packet[(offset + marker.Length)..], out _, out var checksum)) segment = checksum;
            boundary = previousSegment.HasValue && segment.HasValue && previousSegment != segment;
            previousSegment = segment;
        }
        // Round absolute timestamps before subtracting. Rounding each packet's
        // duration individually would drift on a 90 kHz clock at 44.1 kHz.
        Int128 scaled = ((Int128)pts * numerator * rate + denominator / 2) / denominator;
        if (scaled > long.MaxValue) { invalid = true; return; }
        long sample = (long)scaled;
        if (firstPts.HasValue)
        {
            long delta = sample - previousSample;
            // A retained part may start slightly inside the preceding AAC frame.
            // The MP4 muxer advances that DTS by one sample; mirror only a verified
            // join and only for a sub-frame overlap. A reset has no safe fill plan.
            if (delta <= 0 && delta > -1024 && boundary)
            {
                sample = previousSample + 1;
                delta = 1;
            }
            if (delta <= 0 || delta > 10L * rate) { invalid = true; return; }
            AddDuration((uint)delta);
        }
        else firstPts = pts;
        previousSample = sample;
        const string extra = "New Extradata,";
        int start = packet.IndexOf(extra, StringComparison.Ordinal);
        if (start >= 0) AddConfiguration(packet[(start + extra.Length)..]);
    }

    private void AddConfiguration(ReadOnlySpan<char> value)
    {
        if (!ReadSideData(value, out int size, out uint checksum)) { invalid = true; return; }
        if (!configurations.Contains((size, checksum))) configurations.Add((size, checksum));
        if (configurations.Count > 2) invalid = true;
    }

    private static bool ReadSideData(ReadOnlySpan<char> value, out int size, out uint checksum)
    {
        size = 0;
        checksum = 0;
        Span<Range> fields = stackalloc Range[3];
        return value.Split(fields, ',', StringSplitOptions.TrimEntries) >= 2 && int.TryParse(value[fields[0]], out size) &&
            value[fields[1]].StartsWith("0x") && uint.TryParse(value[fields[1]][2..], NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out checksum);
    }

    private void AddDuration(uint delta)
    {
        if (runs.Count != 0 && runs[^1].Duration == delta && runs[^1].Count < uint.MaxValue)
            runs[^1] = runs[^1] with { Count = runs[^1].Count + 1 };
        else runs.Add(new(1, delta));
    }

    internal HlsAacGapDetector.Track? Finish()
    {
        int freq = Array.IndexOf(HlsAacGapRepair.Rates, rate);
        if (invalid || !firstPts.HasValue || runs.Count == 0 || freq < 0 ||
            channels is < 1 or > 6 || configurations.Count == 0) return null;
        // framecrc identifies extradata by length and Adler-32 (seed 0). Require
        // LC, 1024 samples and no SBR/PCE both initially and on configuration changes.
        byte[] asc = [(byte)(0x10 | (freq >> 1)), (byte)(((freq & 1) << 7) | (channels << 3)), 0x56, 0xe5, 0];
        foreach (var (size, checksum) in configurations)
        {
            if (size is not (2 or 5)) return null;
            uint a = 0, b = 0;
            for (int i = 0; i < size; i++) { a += asc[i]; b += a; }
            if ((b << 16 | a) != checksum) return null;
        }
        AddDuration(1024); // No following timestamp; the final access unit is never padded.
        return new(AudioIndex, rate, channels, (double)firstPts.Value * numerator / denominator, runs);
    }
}
