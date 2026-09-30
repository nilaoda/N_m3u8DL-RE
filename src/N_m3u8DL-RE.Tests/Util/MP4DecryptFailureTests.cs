using System.ComponentModel;
using N_m3u8DL_RE.Enum;
using N_m3u8DL_RE.Util;

namespace N_m3u8DL_RE.Tests.Util;

public class MP4DecryptFailureTests
{
    [Fact]
    public async Task MissingDecryptBinaryRestoresSourceAndDoesNotPublishPartialResult()
    {
        var root = Directory.CreateTempSubdirectory("decrypt-failure-").FullName;
        try
        {
            var source = Path.Combine(root, "source.mp4");
            var dest = Path.Combine(root, "source_dec.mp4");
            await File.WriteAllTextAsync(source, "original");
            var kid = new string('1', 32);
            await Assert.ThrowsAsync<Win32Exception>(() => MP4DecryptUtil.DecryptAsync(
                DecryptEngine.MP4DECRYPT, Path.Combine(root, "missing-binary"), [$"{kid}:{new string('a', 32)}"], source, dest, kid));
            Assert.Equal("original", await File.ReadAllTextAsync(source));
            Assert.False(File.Exists(dest));
            Assert.Single(Directory.GetFiles(root));
        }
        finally { Directory.Delete(root, true); }
    }
}
