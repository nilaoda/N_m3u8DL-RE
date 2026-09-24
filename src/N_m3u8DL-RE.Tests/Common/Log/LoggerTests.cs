using N_m3u8DL_RE.Common.Log;

namespace N_m3u8DL_RE.Tests.Common.Log;

public class LoggerTests
{
    [Fact]
    public void ClaimAutoLogFile_SameTimestamp_ReturnsDistinctExistingFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), "nm3u8dlre_log_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            // 模拟两个进程在同一毫秒内启动
            var now = DateTime.Now;
            var first = Logger.ClaimAutoLogFile(dir, now);
            var second = Logger.ClaimAutoLogFile(dir, now);

            Assert.NotEqual(first, second);
            Assert.True(File.Exists(first));
            Assert.True(File.Exists(second));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
