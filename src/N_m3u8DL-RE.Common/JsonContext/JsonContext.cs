using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Enum;
using System.Text.Json.Serialization;

namespace N_m3u8DL_RE.Common
{
    [JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
    [JsonSerializable(typeof(MediaType))]
    [JsonSerializable(typeof(EncryptMethod))]
    [JsonSerializable(typeof(ExtractorType))]
    [JsonSerializable(typeof(Choise))]
    [JsonSerializable(typeof(StreamSpec))]
    [JsonSerializable(typeof(IOrderedEnumerable<StreamSpec>))]
    [JsonSerializable(typeof(IEnumerable<MediaSegment>))]
    [JsonSerializable(typeof(List<StreamSpec>))]
    [JsonSerializable(typeof(List<MediaSegment>))]
    [JsonSerializable(typeof(Dictionary<string, string>))]
    internal partial class JsonContext : JsonSerializerContext { }
}
