﻿using Spectre.Console;
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
                LogFilePath = Path.Combine(logDir, now.ToString("yyyy-MM-dd_HH-mm-ss-fff") + ".log");
                int index = 1;
                var fileName = Path.GetFileNameWithoutExtension(LogFilePath);
                // 若文件存在则加序号
                while (File.Exists(LogFilePath))
                {
                    LogFilePath = Path.Combine(Path.GetDirectoryName(LogFilePath)!, $"{fileName}-{index++}.log");
                }
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

    private static string GetCurrTime()
    {
        return DateTime.Now.ToString("HH:mm:ss.fff");
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
                CustomAnsiConsole.Markup(write);
                Console.WriteLine(subWrite);
            }

            if (!IsWriteFile || !File.Exists(LogFilePath)) return;
            
            var plain = write.RemoveMarkup() + subWrite.RemoveMarkup();
            try
            {
                // 进入写入
                LogWriteLock.EnterWriteLock();
                using (StreamWriter sw = File.AppendText(LogFilePath))
                {
                    sw.WriteLine(plain);
                }
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
            using (StreamWriter sw = File.AppendText(LogFilePath))
            {
                sw.WriteLine(plain, Encoding.UTF8);
            }
        }
        finally
        {
            // 释放占用
            LogWriteLock.ExitWriteLock();
        }
    }
}
