using System.Text.Json.Serialization;
using N_m3u8DL_RE.Protocol;

namespace N_m3u8DL_RE.Worker;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(WorkerCommand))]
[JsonSerializable(typeof(WorkerEvent))]
internal partial class WorkerJsonContext : JsonSerializerContext;
