using System.ComponentModel;
using N_m3u8DL_RE.Enum;
using N_m3u8DL_RE.Util;

namespace N_m3u8DL_RE.Tests.Util;

public class MP4DecryptFailureTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task MissingDecryptBinaryRestoresSourceAndDoesNotPublishPartialResult(int engine)
    {
        var root = Directory.CreateTempSubdirectory("decrypt-failure-,中文 ").FullName;
        try
        {
            var source = Path.Combine(root, "source, 中文.mp4");
            var dest = Path.Combine(root, "source, 中文_dec.mp4");
            await File.WriteAllTextAsync(source, "original");
            await File.WriteAllTextAsync(dest, "existing output");
            var kid = new string('1', 32);
            await Assert.ThrowsAsync<Win32Exception>(() => MP4DecryptUtil.DecryptAsync(
                (DecryptEngine)engine, Path.Combine(root, "missing-binary"), [$"{kid}:{new string('a', 32)}"], source, dest, kid));
            Assert.Equal("original", await File.ReadAllTextAsync(source));
            Assert.Equal("existing output", await File.ReadAllTextAsync(dest));
            Assert.Equal(2, Directory.GetFiles(root).Length);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task MissingShakaBinaryDuringKidProbeRestoresSource()
    {
        var root = Directory.CreateTempSubdirectory("shaka-probe-failure-,中文 ").FullName;
        try
        {
            var source = Path.Combine(root, "source, 中文.webm");
            await File.WriteAllTextAsync(source, "original");
            Assert.Throws<Win32Exception>(() => MP4DecryptUtil.ReadInitShaka(source, Path.Combine(root, "missing-binary")));
            Assert.Equal("original", await File.ReadAllTextAsync(source));
            Assert.Single(Directory.GetFiles(root));
        }
        finally { Directory.Delete(root, true); }
    }
}
