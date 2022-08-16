using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.JsonConverter;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace N_m3u8DL_RE.Common.Util
{
    public class GlobalUtil
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
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
            else if (o is IOrderedEnumerable<StreamSpec> ss)
            {
                return JsonSerializer.Serialize(ss, Context.IOrderedEnumerableStreamSpec);
            }
            else if (o is List<StreamSpec> sList)
            {
                return JsonSerializer.Serialize(sList, Context.ListStreamSpec);
            }
            return "{NOT SUPPORTED}";
        }

        //此函数用于格式化输出时长  
        public static string FormatTime(int time)
        {
            TimeSpan ts = new TimeSpan(0, 0, time);
            string str = "";
            str = (ts.Hours.ToString("00") == "00" ? "" : ts.Hours.ToString("00") + "h") + ts.Minutes.ToString("00") + "m" + ts.Seconds.ToString("00") + "s";
            return str;
        }

        /// <summary>
        /// 寻找可执行程序
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public static string? FindExecutable(string name)
        {
            var fileExt = OperatingSystem.IsWindows() ? ".exe" : "";
            var searchPath = new[] { Environment.CurrentDirectory, Environment.ProcessPath };
            var envPath = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ??
                          Array.Empty<string>();
            return searchPath.Concat(envPath).Select(p => Path.Combine(p, name + fileExt)).FirstOrDefault(File.Exists);
        }
    }
}
