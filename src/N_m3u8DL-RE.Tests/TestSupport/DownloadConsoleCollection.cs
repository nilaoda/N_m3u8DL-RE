namespace N_m3u8DL_RE.Tests.TestSupport;

// 下载进度及日志共享全局 Console，集成测试不能与替换 Console.Out 的测试并行。
[CollectionDefinition("Download console", DisableParallelization = true)]
public class DownloadConsoleCollection;
