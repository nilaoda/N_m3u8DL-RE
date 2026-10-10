using N_m3u8DL_RE.CommandLine;
using N_m3u8DL_RE.Parser;
using N_m3u8DL_RE.Parser.Config;
using N_m3u8DL_RE.Util;
using static N_m3u8DL_RE.Tests.TestSupport.DownloadTestHelper;

namespace N_m3u8DL_RE.Tests.Util;

[Collection("Download console")]
public class LiveRecordingCleanupTests
{
    [Fact]
    public async Task MetadataWriterOnlyRegistersNewFiles()
    {
        var root = Directory.CreateTempSubdirectory("live-metadata-cleanup-").FullName;
        try
        {
            var existing = Path.Combine(root, "meta.json");
            File.WriteAllText(existing, "keep");
            using var extractor = new StreamExtractor(new ParserConfig());
            extractor.RawFiles["meta.json"] = "new metadata";
            extractor.RawFiles["raw.m3u8"] = "new manifest";
            var options = new MyOption();
            Assert.Empty(await Program.WriteRawFilesAsync(options, extractor, root));
            Assert.False(File.Exists(Path.Combine(root, "raw.m3u8")));
            options.WriteMetaJson = true;
            var created = await Program.WriteRawFilesAsync(options, extractor, root);
            Assert.Equal(Path.Combine(root, "raw.m3u8"), Assert.Single(created));
            Assert.Equal("new manifest", File.ReadAllText(created[0]));
            Assert.Empty(await Program.WriteRawFilesAsync(options, extractor, root));

            var cleanup = new LiveRecordingCleanup(root);
            foreach (var file in created) cleanup.DeleteFile(file);
            cleanup.Cleanup();

            Assert.Equal("keep", File.ReadAllText(existing));
            Assert.False(File.Exists(created[0]));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void CleanupDeletesNewInitFilesAndStopsAtTaskRoot()
    {
        var root = Directory.CreateTempSubdirectory("live-cleanup-").FullName;
        try
        {
            var task = Path.Combine(root, "task");
            var track = Path.Combine(task, "video");
            var cleanup = new LiveRecordingCleanup(task);
            cleanup.TrackDirectory(track);
            Directory.CreateDirectory(track);
            foreach (var name in new[] { "_init.mp4", "_init_dec.mp4", "_init_1.mp4" })
            {
                var file = Path.Combine(track, name);
                cleanup.TrackFile(file);
                File.WriteAllText(file, "created");
                cleanup.MarkMerged([file]);
            }

            cleanup.Cleanup();

            Assert.False(Directory.Exists(task));
            Assert.True(Directory.Exists(root));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void CleanupPreservesExistingInitAndUntrackedFilesAndDirectories()
    {
        var root = Directory.CreateTempSubdirectory("live-cleanup-").FullName;
        try
        {
            var task = Path.Combine(root, "task");
            var track = Path.Combine(task, "subtitles");
            var empty = Path.Combine(task, "user-directory");
            Directory.CreateDirectory(track);
            Directory.CreateDirectory(empty);
            var cleanup = new LiveRecordingCleanup(task);
            cleanup.TrackDirectory(track);
            foreach (var name in new[] { "_init.mp4", "_init_dec.mp4", "_init_99.mp4", "0001.png", "notes.txt", ".DS_Store" })
            {
                var file = Path.Combine(track, name);
                File.WriteAllText(file, "keep");
                cleanup.TrackFile(file);
                cleanup.MarkMerged([file]);
            }
            var unmerged = Path.Combine(track, "_init_1.mp4");
            cleanup.TrackFile(unmerged);
            File.WriteAllText(unmerged, "unmerged");
            var outside = Path.Combine(root, "outside.txt");
            cleanup.TrackFile(outside);
            File.WriteAllText(outside, "keep");

            cleanup.Cleanup();
            cleanup.DeleteFile(Path.Combine(task, "..", "outside.txt"));

            Assert.Equal(7, Directory.GetFiles(track).Length);
            Assert.All(Directory.GetFiles(track).Where(file => file != unmerged), file => Assert.Equal("keep", File.ReadAllText(file)));
            Assert.Equal("unmerged", File.ReadAllText(unmerged));
            Assert.True(Directory.Exists(empty));
            Assert.Equal("keep", File.ReadAllText(outside));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("task")]
    [InlineData("track")]
    [InlineData("file")]
    public async Task CleanupPreservesPathsReplacedByLinks(string replaced)
    {
        var root = Directory.CreateTempSubdirectory("live-cleanup-").FullName;
        try
        {
            var task = Path.Combine(root, "task");
            var track = Path.Combine(task, "video");
            Directory.CreateDirectory(track);
            var outside = Path.Combine(root, "outside", "video");
            Directory.CreateDirectory(outside);
            var protectedFile = Path.Combine(outside, "_init.mp4");
            File.WriteAllText(protectedFile, "keep");
            var cleanup = new LiveRecordingCleanup(task);
            cleanup.TrackDirectory(track);
            var init = Path.Combine(track, "_init.mp4");
            cleanup.TrackFile(init);
            File.WriteAllText(init, "created");
            cleanup.MarkMerged([init]);
            var link = replaced == "task" ? task : replaced == "track" ? track : init;
            if (replaced == "file")
            {
                File.Delete(link);
                await CreateLink(link, protectedFile, directory: false);
            }
            else
            {
                Directory.Delete(link, true);
                await CreateLink(link, replaced == "task" ? Path.GetDirectoryName(outside)! : outside, directory: true);
            }

            cleanup.Cleanup();
            cleanup.DeleteFile(init);

            Assert.Equal("keep", File.ReadAllText(protectedFile));
            // 删除 Windows 硬链接只移除本地目录项，外部源文件仍须保留。
            if (OperatingSystem.IsWindows() && replaced == "file")
                Assert.False(Path.Exists(link));
            else
                Assert.True(Path.Exists(link));
        }
        finally { Directory.Delete(root, true); }
    }
}
