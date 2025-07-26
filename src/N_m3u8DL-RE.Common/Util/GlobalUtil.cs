using System.Text.Json;
using System.Text.Json.Serialization;

using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.JsonConverter;
using N_m3u8DL_RE.Common.CommonEnumerations;

namespace N_m3u8DL_RE.Common.Util
{
    public static class GlobalUtil
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = {
                new JsonStringEnumConverter<MediaType>(),
                new JsonStringEnumConverter<EncryptMethod>(),
                new JsonStringEnumConverter<ExtractorType>(),
                new BytesBase64Converter()
            }
        };
        private static readonly JsonContext Context = new(Options);

        public static string ConvertToJson(object o)
        {
            return o is StreamSpec s
                ? JsonSerializer.Serialize(s, Context.StreamSpec)
                : o is IOrderedEnumerable<StreamSpec> ss
                ? JsonSerializer.Serialize(ss, Context.IOrderedEnumerableStreamSpec)
                : o is List<StreamSpec> sList
                ? JsonSerializer.Serialize(sList, Context.ListStreamSpec)
                : o is IEnumerable<MediaSegment> mList ? JsonSerializer.Serialize(mList, Context.IEnumerableMediaSegment) : "{NOT SUPPORTED}";
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

        // 此函数用于格式化输出时长  
        public static string FormatTime(int time)
        {
            TimeSpan ts = new(0, 0, time);
            string str = (ts.Hours.ToString("00") == "00" ? "" : ts.Hours.ToString("00") + "h") + ts.Minutes.ToString("00") + "m" + ts.Seconds.ToString("00") + "s";
            return str;
        }

        /// <summary>
        /// 寻找可执行程序
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public static string? FindExecutable(string name)
        {
            string fileExt = OperatingSystem.IsWindows() ? ".exe" : "";
            string?[] searchPath = [Environment.CurrentDirectory, Path.GetDirectoryName(Environment.ProcessPath)];
            string[] envPath = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? [];
            return searchPath.Concat(envPath).Select(p => Path.Combine(p!, name + fileExt)).FirstOrDefault(File.Exists);
        }
    }
}