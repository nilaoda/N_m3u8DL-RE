using System.Globalization;
using System.Text.RegularExpressions;
using N_m3u8DL_RE.Common.Resource;

namespace N_m3u8DL_RE.Entity;

internal sealed partial class LiveCatchup
{
    public TimeSpan? Offset { get; private init; }
    public DateTimeOffset? Time { get; private init; }

    [GeneratedRegex(@"^\d+:\d{2}:\d{2}$")]
    private static partial Regex DurationRegex();

    public static LiveCatchup Parse(string input)
    {
        input = input.Trim();
        if (DurationRegex().IsMatch(input))
        {
            var parts = input.Split(':').Select(value => long.Parse(value, CultureInfo.InvariantCulture)).ToArray();
            if (parts[1] < 60 && parts[2] < 60)
            {
                var offset = TimeSpan.FromSeconds(checked(parts[0] * 3600 + parts[1] * 60 + parts[2]));
                if (offset > TimeSpan.Zero)
                    return new LiveCatchup { Offset = offset };
            }
        }
        else if (DateTimeOffset.TryParseExact(input,
            ["yyyy-MM-dd'T'HH:mm:sszzz", "yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd HH:mm:ss"],
            CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var time))
        {
            // 未指定时区时使用本地时区；Z 明确表示 UTC。
            if (input.EndsWith('Z'))
                time = new DateTimeOffset(time.DateTime, TimeSpan.Zero);
            return new LiveCatchup { Time = time };
        }
        throw new ArgumentException(ResString.liveCatchupInvalid);
    }

    public DateTimeOffset Resolve(DateTimeOffset now) => Time ?? now - Offset!.Value;
}
