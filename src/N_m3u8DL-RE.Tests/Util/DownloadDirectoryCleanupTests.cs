using N_m3u8DL_RE.Util;
using static N_m3u8DL_RE.Tests.TestSupport.DownloadTestHelper;

namespace N_m3u8DL_RE.Tests.Util;

public class DownloadDirectoryCleanupTests
{
    [Fact]
    public void CleanupPreservesRealFilesAndParentMetadata()
    {
        var root = Directory.CreateTempSubdirectory("download-cleanup-").FullName;
        try
        {
            var task = Path.Combine(root, "task");
            var nested = Path.Combine(task, "subtitles");
            Directory.CreateDirectory(nested);
            var parentMetadata = Path.Combine(root, ".DS_Store");
            File.WriteAllText(parentMetadata, "parent");
            File.WriteAllText(Path.Combine(task, ".DS_Store"), "task");
            File.WriteAllText(Path.Combine(nested, ".DS_Store"), "nested");
            string[] preserved = [Path.Combine(nested, "0001.png"), Path.Combine(task, "notes.txt"), Path.Combine(task, "0001.m4s")];
            foreach (var file in preserved) File.WriteAllText(file, "keep");

            OtherUtil.SafeDeleteDir(task, cleanMetadata: true);

            foreach (var file in preserved) Assert.Equal("keep", File.ReadAllText(file));
            Assert.Equal("parent", File.ReadAllText(parentMetadata));
            Assert.Empty(Directory.GetFiles(task, ".DS_Store", SearchOption.AllDirectories));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void CleanupStopsAtTaskRootWhenOnlyMetadataRemains()
    {
        var root = Directory.CreateTempSubdirectory("download-cleanup-").FullName;
        try
        {
            var task = Path.Combine(root, "task");
            var nested = Path.Combine(task, "part", "empty");
            Directory.CreateDirectory(nested);
            File.WriteAllText(Path.Combine(task, ".DS_Store"), "task");
            File.WriteAllText(Path.Combine(nested, ".DS_Store"), "nested");

            OtherUtil.SafeDeleteDir(task, cleanMetadata: true);

            Assert.False(Directory.Exists(task));
            Assert.True(Directory.Exists(root));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task CleanupDoesNotFollowDirectoryLinks()
    {
        var root = Directory.CreateTempSubdirectory("download-cleanup-").FullName;
        try
        {
            var outside = Path.Combine(root, "outside");
            Directory.CreateDirectory(outside);
            var metadata = Path.Combine(outside, ".DS_Store");
            File.WriteAllText(metadata, "outside");
            var task = Path.Combine(root, "task");
            Directory.CreateDirectory(task);
            var link = Path.Combine(task, "link");
            await CreateLink(link, outside, directory: true);

            OtherUtil.SafeDeleteDir(task, cleanMetadata: true);
            OtherUtil.SafeDeleteDir(link, cleanMetadata: true);

            Assert.Equal("outside", File.ReadAllText(metadata));
            Assert.True(Directory.Exists(link));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void DefaultCleanupDeletesEmptyParentsAndStopsAtMetadata()
    {
        var root = Directory.CreateTempSubdirectory("download-cleanup-").FullName;
        try
        {
            var nested = Path.Combine(root, "task", "part");
            Directory.CreateDirectory(nested);
            var metadata = Path.Combine(root, ".DS_Store");
            File.WriteAllText(metadata, "parent");

            OtherUtil.SafeDeleteDir(nested);

            Assert.False(Directory.Exists(Path.Combine(root, "task")));
            Assert.Equal("parent", File.ReadAllText(metadata));
        }
        finally { Directory.Delete(root, true); }
    }
}
