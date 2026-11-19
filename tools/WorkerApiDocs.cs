#:project ../src/N_m3u8DL-RE/N_m3u8DL-RE.csproj
#:property AssemblyName=WorkerApiDocs
#:property TargetFramework=net10.0
#:property PublishAot=false

using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization.Metadata;
using N_m3u8DL_RE.Protocol;
using N_m3u8DL_RE.Worker;

if (args.Length > 1 || args.Length == 1 && args[0] != "--check")
{
    Console.Error.WriteLine("用法：dotnet run tools/WorkerApiDocs.cs -- [--check]");
    return 2;
}
var root = WorkerApiDocumentation.FindRepositoryRoot(Directory.GetCurrentDirectory());
var check = args.Length == 1;
var differences = new List<string>();
foreach (var (relativePath, content) in WorkerApiDocumentation.Generate())
{
    var path = Path.Combine(root, relativePath);
    if (File.Exists(path) && File.ReadAllText(path).ReplaceLineEndings("\n") == content)
        continue;
    if (check)
        differences.Add(relativePath);
    else
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        Console.WriteLine(relativePath);
    }
}
if (differences.Count > 0)
{
    Console.Error.WriteLine("Worker API 文档与代码不一致，请重新生成：");
    foreach (var path in differences)
        Console.Error.WriteLine(path);
    return 1;
}
Console.WriteLine(check ? "Worker API 文档检查通过。" : "Worker API 文档已生成。");
return 0;

// 使用 Worker 实际使用的源生成契约，字段名、必填标记与 JSON 选项无需维护第二份。
public static class WorkerApiDocumentation
{
    private const string OutputDirectory = "docs/api/worker/";
    private static readonly JsonSerializerOptions IndentedJson = new()
    {
        WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static IReadOnlyDictionary<string, string> Generate()
    {
        var context = WorkerJsonContext.Default;
        var models = new SortedDictionary<string, JsonTypeInfo>(StringComparer.Ordinal);
        Collect(context.WorkerCommand.Type);
        Collect(context.WorkerEvent.Type);
        var text = new StringBuilder();
        text.AppendLine("# Worker API 数据模型");
        text.AppendLine();
        text.AppendLine("<!-- 此文件由 tools/WorkerApiDocs.cs 自动生成，请修改协议代码后重新生成。 -->");
        text.AppendLine();
        text.AppendLine($"协议版本：`{WorkerProtocol.Version}`。单行输入上限：`{WorkerProtocol.MaxMessageCharacters}` 个 UTF-16 字符。");
        text.AppendLine();
        text.AppendLine("入口：[Worker API](README.md) · [使用指南](../../usage/stdio-worker.md) · [命令 Schema](schemas/command.schema.json) · [事件 Schema](schemas/event.schema.json)");
        text.AppendLine();
        text.AppendLine("字段名来自实际 JSON 契约。“声明可空”来自 C# 可空类型标记；“JSON 必填”表示反序列化时字段必须出现；按消息类型决定的条件必填项、默认值和业务校验见使用指南。可空字段在输出时省略 null；省略字段是否保留旧值取决于字段语义。");
        text.AppendLine();
        text.AppendLine("JSON Schema 描述数据结构，不包含文件存在性、操作适用范围、事件顺序等运行规则。");
        foreach (var (name, info) in models)
        {
            text.AppendLine();
            text.AppendLine($"## {name}");
            text.AppendLine();
            text.AppendLine("| 字段 | JSON 类型 / 模型 | 声明可空 | JSON 必填 |");
            text.AppendLine("| --- | --- | --- | --- |");
            foreach (var property in info.Properties)
            {
                var nullable = Nullable.GetUnderlyingType(property.PropertyType) != null ||
                    !property.PropertyType.IsValueType && property.IsSetNullable;
                text.AppendLine($"| `{property.Name}` | {TypeName(property.PropertyType)} | {(nullable ? "是" : "否")} | {(property.IsRequired ? "是" : "否")} |");
            }
        }
        return new Dictionary<string, string>
        {
            [OutputDirectory + "models.md"] = text.ToString().ReplaceLineEndings("\n"),
            [OutputDirectory + "schemas/command.schema.json"] = Schema(context.WorkerCommand),
            [OutputDirectory + "schemas/event.schema.json"] = Schema(context.WorkerEvent)
        };

        void Collect(Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            if (type.IsArray)
            {
                Collect(type.GetElementType()!);
                return;
            }
            if (type.IsGenericType)
            {
                foreach (var argument in type.GetGenericArguments())
                    Collect(argument);
                return;
            }
            if (type.Namespace != typeof(WorkerCommand).Namespace || models.ContainsKey(type.Name))
                return;
            var info = context.GetTypeInfo(type) ?? throw new InvalidOperationException($"缺少 JSON 契约：{type.Name}");
            models.Add(type.Name, info);
            foreach (var property in info.Properties)
                Collect(property.PropertyType);
        }
    }

    private static string TypeName(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type.IsArray)
            return TypeName(type.GetElementType()!) + "[]";
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
            return "object（字符串键 → " + TypeName(type.GetGenericArguments()[1]) + "）";
        if (type == typeof(string))
            return "string";
        if (type == typeof(bool))
            return "boolean";
        if (type == typeof(int))
            return "integer（int32）";
        if (type == typeof(long))
            return "integer（int64）";
        if (type == typeof(double))
            return "number（double）";
        if (type.Namespace == typeof(WorkerCommand).Namespace)
            return $"[{type.Name}](#{type.Name.ToLowerInvariant()})";
        return $"`{type.Name}`（见 Schema）";
    }

    private static string Schema(JsonTypeInfo info)
    {
        var body = info.GetJsonSchemaAsNode(new JsonSchemaExporterOptions
        {
            TreatNullObliviousAsNonNullable = true
        }).AsObject();
        var schema = new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["$comment"] = "由 tools/WorkerApiDocs.cs 根据 Worker JSON 契约生成；业务规则见使用指南。"
        };
        foreach (var (name, value) in body)
            schema.Add(name, value?.DeepClone());
        return schema.ToJsonString(IndentedJson).ReplaceLineEndings("\n") + "\n";
    }

    public static string FindRepositoryRoot(string start)
    {
        for (var directory = new DirectoryInfo(start); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "src/N_m3u8DL-RE.sln")))
                return directory.FullName;
        throw new DirectoryNotFoundException("找不到 N_m3u8DL-RE 仓库根目录。");
    }
}
