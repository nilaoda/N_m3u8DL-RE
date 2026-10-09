using System.Text;
using N_m3u8DL_RE.CommandLine;
using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Config;
using N_m3u8DL_RE.DownloadManager;
using N_m3u8DL_RE.Parser;
using N_m3u8DL_RE.Parser.Config;
using N_m3u8DL_RE.Util;

namespace N_m3u8DL_RE.Tests.Util;

public class OutputFileNameTests
{
    [Fact]
    public void PipeTracksReserveOnlyOneMuxOutput()
    {
        var root = Directory.CreateTempSubdirectory("pipe-output-").FullName;
        try
        {
            var first = new StreamSpec { MediaType = MediaType.AUDIO, Language = "en-US" };
            var second = new StreamSpec { MediaType = MediaType.AUDIO, Language = "en-US" };
            using var extractor = new StreamExtractor(new ParserConfig());
            var manager = new SimpleLiveRecordManager2(new DownloaderConfig
                { DirPrefix = root, MyOptions = new MyOption { LivePipeMux = true } }, [first, second], extractor);
            var path = Path.Combine(root, "result.en-US.ts");
            Assert.Null(manager.RegisterPipeStream(1, "pipe-second", path, second));
            var mux = manager.RegisterPipeStream(0, "pipe-first", path, first);
            Assert.NotNull(mux);
            Assert.Equal(path, mux.Value.Output);
            Assert.Equal(["pipe-first", "pipe-second"], mux.Value.Names);
            Assert.Null(manager.RegisterPipeStream(0, "pipe-first", path, first));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ConcurrentTracksReserveDifferentPathsBeforeCreatingFiles()
    {
        var root = Directory.CreateTempSubdirectory("output-reservations-").FullName;
        try
        {
            var path = Path.Combine(root, "result.en-US.m4a");
            var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var stream = new StreamSpec { MediaType = MediaType.AUDIO, Language = "en-US" };
            var outputs = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
                OtherUtil.HandleFileCollision(path, stream, reserved))));
            Assert.Equal(4, outputs.Distinct().Count());
            Assert.Contains(path, outputs);
            Assert.All(outputs, output => Assert.False(File.Exists(output)));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ReservedPathsRemainUnavailableDuringDecryptionRenames()
    {
        var root = Directory.CreateTempSubdirectory("output-reservations-").FullName;
        try
        {
            var comparer = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            var reserved = new HashSet<string>(comparer);
            var path = Path.Combine(root, "result.m4a");
            var stream = new StreamSpec();
            Assert.Equal(path, OtherUtil.HandleFileCollision(path, stream, reserved));
            File.WriteAllText(path, "encrypted input");
            var temporary = Path.Combine(root, "decrypting.m4a");
            File.Move(path, temporary);
            // 相对路径和大小写别名同样不能占用仍在解密的轨道路径。
            var relative = Path.GetRelativePath(Environment.CurrentDirectory, path);
            Assert.Equal(Path.Combine(root, "result.copy.m4a"), Path.GetFullPath(
                OtherUtil.HandleFileCollision(relative, stream, reserved)));
            var differentCase = Path.Combine(root, "RESULT.M4A");
            var output = OtherUtil.HandleFileCollision(differentCase, stream, reserved);
            Assert.Equal(comparer.Equals("a", "A") ? Path.Combine(root, "RESULT.copy.copy.M4A") : differentCase, output);
            File.Move(temporary, path);
            Assert.Equal("encrypted input", File.ReadAllText(path));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("a", 300)]
    [InlineData("中", 150)]
    [InlineData("😀", 100)]
    public void AutomaticNameFitsOnDiskAndKeepsTimestamp(string character, int count)
    {
        var name = string.Concat(Enumerable.Repeat(character, count));
        var input = $"https://example.com/{Uri.EscapeDataString(name)}.m3u8";
        var result = OtherUtil.GetFileNameFromInput(input);
        Assert.InRange(Encoding.UTF8.GetByteCount(result), 1, 200);
        Assert.Matches(@"_[0-9]{4}-[0-9]{2}-[0-9]{2}_[0-9]{2}-[0-9]{2}-[0-9]{2}$", result);
        Assert.DoesNotContain('�', result);
        var root = Directory.CreateTempSubdirectory("output-name-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, result));
            var output = Path.Combine(root, OtherUtil.GetSafeFileName(result, ".mp4"));
            File.WriteAllText(output, "data");
            Assert.Equal(result + ".mp4", Path.GetFileName(output));
        }
        finally
        {
            Directory.Delete(root, true);
        }
        // 不带时间戳的调用用于直播源分片匹配，不能在识别阶段丢失名称尾部。
        Assert.Equal(name + "_", OtherUtil.GetFileNameFromInput(input, false));
    }

    [Fact]
    public void LocalInputAndShortNamesKeepTheirNamingFormat()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{new string('a', 240)}.m3u8");
        try
        {
            File.WriteAllText(path, "manifest");
            Assert.InRange(Encoding.UTF8.GetByteCount(OtherUtil.GetFileNameFromInput(path)), 1, 200);
        }
        finally
        {
            File.Delete(path);
        }
        Assert.Matches(@"^video_[0-9]{4}-[0-9]{2}-[0-9]{2}_[0-9]{2}-[0-9]{2}-[0-9]{2}$",
            OtherUtil.GetFileNameFromInput("https://example.com/video.m3u8?token=test"));
        Assert.Equal("video.en.mp4", OtherUtil.GetSafeFileName("video.en", ".mp4"));
    }

    [Fact]
    public void ShortNamesKeepDotsBeforeExtensionsAndTimestamps()
    {
        Assert.Equal("video..mp4", OtherUtil.GetSafeFileName("video.", ".mp4"));
        var root = Directory.CreateTempSubdirectory("output-dots-").FullName;
        try
        {
            var output = Path.Combine(root, "video..mp4");
            Assert.Equal(output, OtherUtil.HandleFileCollision(output, new StreamSpec()));
            var input = Path.Combine(root, "video..m3u8");
            File.WriteAllText(input, "manifest");
            Assert.StartsWith("video._", OtherUtil.GetFileNameFromInput(input));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void TemplateAndLanguageSuffixesRemainDistinctAfterTruncation()
    {
        var prefix = string.Concat(Enumerable.Repeat("字幕😀", 100));
        string[] languages = ["en", "zh", "ja"];
        var names = languages.Select(language =>
        {
            var stream = new StreamSpec { Language = language };
            var name = OtherUtil.FormatSavePattern("<SaveName>.<Language>", stream, prefix, 0);
            return OtherUtil.GetSafeFileName(name, ".srt");
        }).ToArray();
        Assert.Equal(3, names.Distinct().Count());
        Assert.All(names, name =>
        {
            Assert.EndsWith(".srt", name);
            Assert.InRange(Encoding.UTF8.GetByteCount(name), 1, OtherUtil.MaxFileNameBytes);
            Assert.DoesNotContain('�', name);
        });
    }

    [Fact]
    public void LongCollisionCandidatesStayBoundedAndDoNotOverwriteFiles()
    {
        var root = Directory.CreateTempSubdirectory("output-collision-").FullName;
        try
        {
            var path = Path.Combine(root, new string('a', 500) + ".mp4");
            var stream = new StreamSpec { MediaType = MediaType.VIDEO, Resolution = "1920x1080", Bandwidth = 8000000 };
            var outputs = new HashSet<string>();
            // 覆盖元数据候选耗尽后，反复追加 .copy 的场景。
            for (var index = 0; index < 12; index++)
            {
                var output = OtherUtil.HandleFileCollision(path, stream);
                Assert.True(outputs.Add(output));
                Assert.EndsWith(".mp4", output);
                Assert.InRange(Encoding.UTF8.GetByteCount(Path.GetFileName(output)), 1, OtherUtil.MaxFileNameBytes);
                File.WriteAllText(output, index.ToString());
            }
            Assert.Equal(12, Directory.GetFiles(root).Length);
            Assert.Equal(12, Directory.GetFiles(root).Select(File.ReadAllText).Distinct().Count());
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ShortCollisionCandidatesKeepExistingNames()
    {
        var root = Directory.CreateTempSubdirectory("short-collision-").FullName;
        try
        {
            var path = Path.Combine(root, "video.mp4");
            var stream = new StreamSpec { MediaType = MediaType.VIDEO, Resolution = "1920x1080" };
            Assert.Equal(path, OtherUtil.HandleFileCollision(path, stream));
            File.WriteAllText(path, "original");
            Assert.Equal(Path.Combine(root, "video.1920x1080.mp4"), OtherUtil.HandleFileCollision(path, stream));
            stream = new StreamSpec();
            File.WriteAllText(Path.Combine(root, "video.copy.mp4"), "copy");
            Assert.Equal(Path.Combine(root, "video.copy.copy.mp4"), OtherUtil.HandleFileCollision(path, stream));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(9)]
    public void SmallByteBudgetsIncludeTheHash(int budget)
    {
        var result = OtherUtil.TruncateFileName(new string('a', 100), budget);
        Assert.InRange(Encoding.UTF8.GetByteCount(result), 1, budget);
    }
}
