using System.Text;
using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Util;

namespace N_m3u8DL_RE.Tests.Util;

public class OutputFileNameTests
{
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
