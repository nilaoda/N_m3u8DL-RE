using Spectre.Console;
using Spectre.Console.Rendering;
using System.Text;
using System.Text.RegularExpressions;

namespace N_m3u8DL_RE.Common.Log;

public static partial class Logger
{
    [GeneratedRegex("{}")]
    private static partial Regex VarsRepRegex();

    /// <summary>
    /// 日志级别，默认为INFO
    /// </summary>
    public static LogLevel LogLevel { get; set; } = LogLevel.INFO;

    /// <summary>
    /// 是否写出日志文件
    /// </summary>
    public static bool IsWriteFile { get; set; } = true;

    /// <summary>
    /// 本次运行日志文件所在位置
    /// </summary>
    public static string? LogFilePath { get; set; }

    // 读写锁
    static ReaderWriterLockSlim LogWriteLock = new ReaderWriterLockSlim();

    public static void InitLogFile()
    {
        if (!IsWriteFile) return;

        try
        {
            var logDir = Path.GetDirectoryName(LogFilePath) ?? (Path.GetDirectoryName(Environment.ProcessPath) + "/Logs");
            if (!Directory.Exists(logDir))
            {
                Directory.CreateDirectory(logDir);
            }

            var now = DateTime.Now;
            if (string.IsNullOrEmpty(LogFilePath))
            {
                // 原子地占用一个唯一的日志文件名，避免多个进程在同一毫秒内选中相同文件名而冲突
                LogFilePath = ClaimAutoLogFile(logDir, now);
            }

            string init = "LOG " + now.ToString("yyyy/MM/dd") + Environment.NewLine
                          + "Save Path: " + Path.GetDirectoryName(LogFilePath) + Environment.NewLine
                          + "Task Start: " + now.ToString("yyyy/MM/dd HH:mm:ss") + Environment.NewLine
                          + "Task CommandLine: " + Environment.CommandLine;
            init += $"{Environment.NewLine}{Environment.NewLine}";
            File.WriteAllText(LogFilePath, init, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Error($"Init log failed! {ex.Message.RemoveMarkup()}");
        }
    }

    /// <summary>
    /// 以独占方式创建并占用一个唯一的日志文件，返回其完整路径。
    /// 若文件名已被占用（例如另一个进程在同一毫秒内启动）则递增序号后重试，
    /// 从而消除“先判断存在再创建”所带来的竞态。
    /// </summary>
    internal static string ClaimAutoLogFile(string logDir, DateTime now)
    {
        var baseName = now.ToString("yyyy-MM-dd_HH-mm-ss-fff");
        var fileName = baseName;
        var index = 1;
        while (true)
        {
            var path = Path.Combine(logDir, fileName + ".log");
            try
            {
                // CreateNew 在文件已存在时会抛出 IOException，借此原子地占用文件名
                using (new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite)) { }
                return path;
            }
            catch (IOException) when (File.Exists(path))
            {
                // 该文件名已被占用，换一个序号继续尝试
                fileName = $"{baseName}-{index++}";
            }
        }
    }

    private static string GetCurrTime()
    {
        return DateTime.Now.ToString("HH:mm:ss.fff");
    }

    private sealed class PlainLogLine(string prefix, string message, bool noAnsiOutput) : Renderable
    {
        private readonly IRenderable prefixMarkup = new Markup(prefix);
        // 普通日志正文按原始文本处理，保留方括号和 :emoji: 等内容，不解释为 markup。
        private readonly IRenderable body = new Text(message);

        protected override IEnumerable<Segment> Render(RenderOptions options, int maxWidth)
        {
            if (noAnsiOutput)
            {
                var line = prefix.RemoveMarkup() + message + Environment.NewLine;
                // 控制段跳过 ANSI 后端的文本分行，让 NonAnsiWriter 一次收到整条日志，
                // 保留正文空行和缩进；非 ANSI 后端直接写出普通文本段。
                yield return options.Ansi ? Segment.Control(line) : new Segment(line);
                yield break;
            }

            // 只取消日志的硬折行，不修改全局终端宽度，进度条仍按实际宽度布局。
            // 终端自行做视觉折行，鼠标复制长 URL 时不会带入额外的换行符。
            foreach (var segment in prefixMarkup.Render(options, int.MaxValue))
                yield return segment;
            foreach (var segment in body.Render(options, int.MaxValue))
                yield return segment;
            yield return Segment.LineBreak;
        }
    }

    private static void HandleLog(string write, string subWrite = "")
    {
        try
        {
            if (subWrite == "")
            {
                CustomAnsiConsole.MarkupLine(write);
            }
            else
            {
                // 前缀和正文必须在同一次渲染中输出，避免正文绕过进度条的光标管理。
                var console = CustomAnsiConsole.Console;
                console.Write(new PlainLogLine(write, subWrite, console.Profile.Out.Writer is NonAnsiWriter));
            }

            if (!IsWriteFile || !File.Exists(LogFilePath)) return;
            
            var plain = write.RemoveMarkup() + subWrite.RemoveMarkup();
            try
            {
                // 进入写入
                LogWriteLock.EnterWriteLock();
                using StreamWriter sw = new StreamWriter(LogFilePath, append: true, Encoding.UTF8);
                sw.WriteLine(plain);
            }
            finally
            {
                // 释放占用
                LogWriteLock.ExitWriteLock();
            }
        }
        catch (Exception)
        {
            Console.WriteLine("Failed to write: " + write);
        }
    }

    private static string ReplaceVars(string data, params object[] ps)
    {
        for (int i = 0; i < ps.Length; i++)
        {
            data = VarsRepRegex().Replace(data, $"{ps[i]}", 1);
        }

        return data;
    }

    public static void Info(string data, params object[] ps)
    {
        if (LogLevel < LogLevel.INFO) return;
        
        data = ReplaceVars(data, ps);
        var write = GetCurrTime() + " " + "[underline #548c26]INFO[/] : ";
        HandleLog(write, data);
    }

    public static void InfoMarkUp(string data, params object[] ps)
    {
        if (LogLevel < LogLevel.INFO) return;
        
        data = ReplaceVars(data, ps);
        var write = GetCurrTime() + " " + "[underline #548c26]INFO[/] : " + data;
        HandleLog(write);
    }

    public static void Debug(string data, params object[] ps)
    {
        if (LogLevel < LogLevel.DEBUG) return;
        
        data = ReplaceVars(data, ps);
        var write = GetCurrTime() + " " + "[underline grey]DEBUG[/]: ";
        HandleLog(write, data);
    }

    public static void DebugMarkUp(string data, params object[] ps)
    {
        if (LogLevel < LogLevel.DEBUG) return;
        
        data = ReplaceVars(data, ps);
        var write = GetCurrTime() + " " + "[underline grey]DEBUG[/]: " + data;
        HandleLog(write);
    }

    public static void Warn(string data, params object[] ps)
    {
        if (LogLevel < LogLevel.WARN) return;
        
        data = ReplaceVars(data, ps);
        var write = GetCurrTime() + " " + "[underline #a89022]WARN[/] : ";
        HandleLog(write, data);
    }

    public static void WarnMarkUp(string data, params object[] ps)
    {
        if (LogLevel < LogLevel.WARN) return;
        
        data = ReplaceVars(data, ps);
        var write = GetCurrTime() + " " + "[underline #a89022]WARN[/] : " + data;
        HandleLog(write);
    }

    public static void Error(string data, params object[] ps)
    {
        if (LogLevel < LogLevel.ERROR) return;
        
        data = ReplaceVars(data, ps);
        var write = GetCurrTime() + " " + "[underline red1]ERROR[/]: ";
        HandleLog(write, data);
    }

    public static void ErrorMarkUp(string data, params object[] ps)
    {
        if (LogLevel < LogLevel.ERROR) return;
        
        data = ReplaceVars(data, ps);
        var write = GetCurrTime() + " " + "[underline red1]ERROR[/]: " + data;
        HandleLog(write);
    }

    public static void ErrorMarkUp(Exception exception)
    {
        string data = exception.Message.EscapeMarkup();
        if (LogLevel >= LogLevel.ERROR)
        {
            data = exception.ToString().EscapeMarkup();
        }

        ErrorMarkUp(data);
    }

    /// <summary>
    /// This thing will only write to the log file.
    /// </summary>
    /// <param name="data"></param>
    /// <param name="ps"></param>
    public static void Extra(string data, params object[] ps)
    {
        if (!IsWriteFile || !File.Exists(LogFilePath)) return;
        
        data = ReplaceVars(data, ps);
        var plain = GetCurrTime() + " " + "EXTRA: " + data.RemoveMarkup();
        try
        {
            // 进入写入
            LogWriteLock.EnterWriteLock();
            using StreamWriter sw = new StreamWriter(LogFilePath, append: true, Encoding.UTF8);
            sw.WriteLine(plain);
        }
        finally
        {
            // 释放占用
            LogWriteLock.ExitWriteLock();
        }
    }
}
