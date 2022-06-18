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
        public static string ConvertToJson(object o)
        {
            var options = new JsonSerializerOptions
            {
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                Converters = { new JsonStringEnumConverter(), new BytesBase64Converter() }
            };
            return JsonSerializer.Serialize(o, options);
        }
    }
}
