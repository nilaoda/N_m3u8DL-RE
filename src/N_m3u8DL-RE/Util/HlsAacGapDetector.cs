namespace N_m3u8DL_RE.Util;

internal static class HlsAacGapDetector
{
    internal readonly record struct DurationRun(uint Count, uint Duration);
    internal sealed record Track(int AudioIndex, int SampleRate, int Channels, double StartSeconds,
        IReadOnlyList<DurationRun> Durations);

    internal static bool HasSparseClock(IReadOnlyList<DurationRun> durations, int rate, CancellationToken token = default)
    {
        bool nominal = false, gap = false;
        long gapDebt = 0, clockDrift = 0;
        for (int i = 0; i < durations.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            var (count, duration) = durations[i];
            if (count == 0 || duration == 0 || duration > 10L * rate)
                return false;
            // The final access unit has no following timestamp to compare with.
            long followed = count - (i == durations.Count - 1 ? 1L : 0L);
            if (followed == 0) continue;
            long excess = (long)duration - 1024;
            nominal |= Math.Abs(excess) <= 1;
            if (Math.Abs(excess) < 512)
            {
                // Ordinary transport clock rounding alternates around 1024. A
                // persistent sample rate mismatch cannot be repaired with silence.
                clockDrift += excess * followed;
                if (Math.Abs(clockDrift) >= 1024)
                    return false;
            }
            else if (!gap)
            {
                gapDebt = Math.Max(0, gapDebt + excess * followed);
                gap = gapDebt >= 1024;
            }
        }
        return gap && nominal;
    }
}
