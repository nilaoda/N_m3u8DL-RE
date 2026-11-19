using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using N_m3u8DL_RE.Protocol;
using N_m3u8DL_RE.Worker;

namespace N_m3u8DL_RE.Tests.Worker;

[Collection("Download console")]
public sealed class WorkerApiDocumentationTests
{
    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "src/N_m3u8DL-RE.sln")))
                return directory.FullName;
        throw new DirectoryNotFoundException("找不到仓库根目录。");
    }

    [Fact]
    public void GuideExamplesUseValidProtocolMessagesAndRequestOptions()
    {
        var guide = File.ReadAllText(Path.Combine(RepositoryRoot(), "docs/usage/stdio-worker.md"));
        var examples = GuideMessages(guide).ToArray();
        Assert.NotEmpty(examples);
        foreach (var example in examples)
        {
            var json = example.GetRawText();
            var type = example.GetProperty("type").GetString();
            if (type is "start" or "respond" or "stop" or "cancel")
            {
                var command = JsonSerializer.Deserialize(json, WorkerJsonContext.Default.WorkerCommand)!;
                Assert.False(string.IsNullOrWhiteSpace(command.Id));
                if (type == "start")
                {
                    Assert.Equal(WorkerProtocol.Version, command.ProtocolVersion);
                    if (command.Operation is null or "download")
                    {
                        Assert.Null(command.FileRequest);
                        Assert.NotNull(command.Request);
                        ProtocolMapper.ToOptions(command.Request);
                    }
                    else
                    {
                        Assert.Null(command.Request);
                        Assert.NotNull(command.FileRequest);
                        ProtocolMapper.ToFileOptions(command.Operation, command.FileRequest);
                    }
                }
            }
            else
            {
                var message = JsonSerializer.Deserialize(json, WorkerJsonContext.Default.WorkerEvent)!;
                Assert.True(message.Sequence > 0);
                Assert.Contains(message.Type, new[] { "ready", "accepted", "error", "state", "tracks", "progress", "interaction", "log", "result" });
                if (message.ProtocolVersion != null)
                    Assert.Equal(WorkerProtocol.Version, message.ProtocolVersion);
                if (message.Progress is { } progress)
                {
                    Assert.False(string.IsNullOrWhiteSpace(progress.TrackId));
                    if (progress.Fraction is { } fraction)
                        Assert.InRange(fraction, 0, 1);
                }
            }
        }
        Assert.Contains(WorkerProtocol.MaxMessageCharacters.ToString("N0", CultureInfo.InvariantCulture), guide);
    }

    [Fact]
    public async Task ReadyExampleMatchesTheWorkerCapabilities()
    {
        var guide = File.ReadAllText(Path.Combine(RepositoryRoot(), "docs/usage/stdio-worker.md"));
        var example = Assert.Single(GuideMessages(guide), message => message.GetProperty("type").GetString() == "ready");
        using var input = new StringReader("");
        using var output = new StringWriter();
        Assert.Equal(1, await WorkerHost.RunAsync(input, output));
        var ready = Assert.Single(ReadJson(output.ToString()), message => message.GetProperty("type").GetString() == "ready");
        Assert.Equal(ready.GetProperty("protocolVersion").GetInt32(), example.GetProperty("protocolVersion").GetInt32());
        Assert.Equal(ready.GetProperty("capabilities").EnumerateArray().Select(value => value.GetString()),
            example.GetProperty("capabilities").EnumerateArray().Select(value => value.GetString()));
    }

    private static IEnumerable<JsonElement> GuideMessages(string guide) =>
        Regex.Matches(guide, @"```json\s*\r?\n([\s\S]*?)\r?\n```")
            .SelectMany(block => ReadJson(block.Groups[1].Value));

    private static List<JsonElement> ReadJson(string json)
    {
        // 同时接受单个格式化 JSON 值和 NDJSON；克隆后不依赖 JsonDocument 的生命周期。
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json), new JsonReaderOptions { AllowMultipleValues = true });
        var messages = new List<JsonElement>();
        while (reader.Read())
        {
            using var document = JsonDocument.ParseValue(ref reader);
            messages.Add(document.RootElement.Clone());
        }
        return messages;
    }

}
