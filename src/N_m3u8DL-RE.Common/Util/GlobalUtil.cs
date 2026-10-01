using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.JsonConverter;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace N_m3u8DL_RE.Common.Util;

public static class GlobalUtil
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(), new BytesBase64Converter() }
    };
    private static readonly JsonContext Context = new JsonContext(Options);

    public static string ConvertToJson(object o)
    {
        if (o is StreamSpec s)
        {
            return JsonSerializer.Serialize(s, Context.StreamSpec);
        }
        if (o is IOrderedEnumerable<StreamSpec> ss)
        {
            return JsonSerializer.Serialize(ss, Context.IOrderedEnumerableStreamSpec);
        }
        if (o is List<StreamSpec> sList)
        {
            return JsonSerializer.Serialize(sList, Context.ListStreamSpec);
        }
        if (o is IEnumerable<MediaSegment> mList)
        {
            return JsonSerializer.Serialize(mList, Context.IEnumerableMediaSegment);
        }
        return "{NOT SUPPORTED}";
    }

    public static string FormatFileSize(double fileSize)
    {
        return fileSize switch
        {
            < 0 => throw new ArgumentOutOfRangeException(nameof(fileSize)),
            >= 1024 * 1024 * 1024 => $"{fileSize / (1024 * 1024 * 1024):########0.00}GB",
            >= 1024 * 1024 => $"{fileSize / (1024 * 1024):####0.00}MB",
            >= 1024 => $"{fileSize / 1024:####0.00}KB",
            _ => $"{fileSize:####0.00}B"
        };
    }

    public static string FormatTime(int time) => FormatTime(TimeSpan.FromSeconds(time));

    public static string FormatTime(TimeSpan time)
    {
        // Hours 只表示一天内的小时数，录制超过 24 小时后仍需显示累计小时。
        var hours = time.Ticks / TimeSpan.TicksPerHour;
        return (hours == 0 ? "" : hours.ToString("00") + "h") +
               time.Minutes.ToString("00") + "m" + time.Seconds.ToString("00") + "s";
    }

    /// <summary>
    /// 寻找可执行程序
    /// </summary>
    /// <param name="name"></param>
    /// <returns></returns>
    public static string? FindExecutable(string name)
    {
        var fileExt = OperatingSystem.IsWindows() ? ".exe" : "";
        var searchPath = new[] { Environment.CurrentDirectory, Path.GetDirectoryName(Environment.ProcessPath) };
        var envPath = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? [];
        return searchPath.Concat(envPath).Select(p => Path.Combine(p!, name + fileExt)).FirstOrDefault(File.Exists);
    }
}