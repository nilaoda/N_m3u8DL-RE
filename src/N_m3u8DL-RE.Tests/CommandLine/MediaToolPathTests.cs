using N_m3u8DL_RE.CommandLine;
using N_m3u8DL_RE.Enum;

namespace N_m3u8DL_RE.Tests.CommandLine;

public class MediaToolPathTests
{
    [Theory]
    [InlineData("MP4DECRYPT", false)]
    [InlineData("MP4DECRYPT", true)]
    [InlineData("SHAKA_PACKAGER", false)]
    [InlineData("SHAKA_PACKAGER", true)]
    [InlineData("FFMPEG", false)]
    [InlineData("FFMPEG", true)]
    public void ManualDecryptionPathTakesPriority(string engine, bool relative)
    {
        var root = Directory.CreateTempSubdirectory("media-tool-path-").FullName;
        try
        {
            var ffmpeg = Path.Combine(root, "ffmpeg.exe");
            var decrypt = Path.Combine(root, "custom decrypt.exe");
            File.WriteAllText(ffmpeg, "");
            File.WriteAllText(decrypt, "");
            var options = new MyOption
            {
                FFmpegBinaryPath = ffmpeg, DecryptionEngine = System.Enum.Parse<DecryptEngine>(engine),
                DecryptionBinaryPath = relative ? Path.GetRelativePath(Environment.CurrentDirectory, decrypt) : decrypt,
                Keys = ["00000000000000000000000000000000"]
            };

            Program.CheckMediaTools(options);

            Assert.Equal(decrypt, options.DecryptionBinaryPath);
            Assert.Equal(ffmpeg, options.FFmpegBinaryPath);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("MP4DECRYPT")]
    [InlineData("SHAKA_PACKAGER")]
    [InlineData("FFMPEG")]
    public void MissingManualDecryptionPathFailsWithoutFallback(string engine)
    {
        var root = Directory.CreateTempSubdirectory("media-tool-path-").FullName;
        try
        {
            var ffmpeg = Path.Combine(root, "ffmpeg.exe");
            File.WriteAllText(ffmpeg, "");
            var missing = Path.Combine(root, "missing.exe");
            var options = new MyOption
            {
                FFmpegBinaryPath = ffmpeg, DecryptionEngine = System.Enum.Parse<DecryptEngine>(engine),
                DecryptionBinaryPath = missing, KeyTextFile = Path.Combine(root, "keys.txt")
            };

            Assert.Throws<FileNotFoundException>(() => Program.CheckMediaTools(options));
            Assert.Equal(missing, options.DecryptionBinaryPath);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void FfmpegDecryptionDefaultsToMediaFfmpegPath()
    {
        var root = Directory.CreateTempSubdirectory("media-tool-path-").FullName;
        try
        {
            var ffmpeg = Path.Combine(root, "ffmpeg.exe");
            File.WriteAllText(ffmpeg, "");
            var options = new MyOption
            {
                FFmpegBinaryPath = ffmpeg, DecryptionEngine = DecryptEngine.FFMPEG,
                Keys = ["00000000000000000000000000000000"]
            };

            Program.CheckMediaTools(options);

            Assert.Equal(ffmpeg, options.DecryptionBinaryPath);
        }
        finally { Directory.Delete(root, true); }
    }
}
