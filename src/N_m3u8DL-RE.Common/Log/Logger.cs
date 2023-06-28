using Spectre.Console;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static System.Net.Mime.MediaTypeNames;

namespace N_m3u8DL_RE.Common.Log
{
    public partial class Logger
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
        private static string? LogFilePath { get; set; }

        //读写锁
        static ReaderWriterLockSlim LogWriteLock = new ReaderWriterLockSlim();

        public static void InitLogFile()
        {
            if (!IsWriteFile) return;

            try
            {
                var logDir = Path.GetDirectoryName(Environment.ProcessPath) + "/Logs";
                if (!Directory.Exists(logDir)) { Directory.CreateDirectory(logDir); }
                LogFilePath = Path.Combine(logDir, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff") + ".log");
                //若文件存在则加序号
                int index = 1;
                var fileName = Path.GetFileNameWithoutExtension(LogFilePath);
                while (File.Exists(LogFilePath))
                {
                    LogFilePath = Path.Combine(Path.GetDirectoryName(LogFilePath)!, $"{fileName}-{index++}.log");
                }
                string now = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                string init = "LOG " + DateTime.Now.ToString("yyyy/MM/dd") + Environment.NewLine
                    + "Save Path: " + Path.GetDirectoryName(LogFilePath) + Environment.NewLine
                    + "Task Start: " + DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss") + Environment.NewLine
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
            if (subWrite == "")
            {
                AnsiConsole.MarkupLine(write);
            }
            else
            {
                AnsiConsole.Markup(write);
                Console.WriteLine(subWrite);
            }
            if (IsWriteFile && File.Exists(LogFilePath))
            {
                var plain = write.RemoveMarkup() + subWrite.RemoveMarkup();
                try
                {
                    //进入写入
                    LogWriteLock.EnterWriteLock();
                    using (StreamWriter sw = File.AppendText(LogFilePath))
                    {
                        sw.WriteLine(plain);
                    }
                }
                finally
                {
                    //释放占用
                    LogWriteLock.ExitWriteLock();
                }
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
            if (LogLevel >= LogLevel.INFO)
            {
                data = ReplaceVars(data, ps);
                var write = GetCurrTime() + " " + "[underline #548c26]INFO[/] : ";
                HandleLog(write, data);
            }
        }

        public static void InfoMarkUp(string data, params object[] ps)
        {
            if (LogLevel >= LogLevel.INFO)
            {
                data = ReplaceVars(data, ps);
                var write = GetCurrTime() + " " + "[underline #548c26]INFO[/] : " + data;
                HandleLog(write);
            }
        }

        public static void Debug(string data, params object[] ps)
        {
            if (LogLevel >= LogLevel.DEBUG)
            {
                data = ReplaceVars(data, ps);
                var write = GetCurrTime() + " " + "[underline grey]DEBUG[/]: ";
                HandleLog(write, data);
            }
        }

        public static void DebugMarkUp(string data, params object[] ps)
        {
            if (LogLevel >= LogLevel.DEBUG)
            {
                data = ReplaceVars(data, ps);
                var write = GetCurrTime() + " " + "[underline grey]DEBUG[/]: " + data;
                HandleLog(write);
            }
        }

        public static void Warn(string data, params object[] ps)
        {
            if (LogLevel >= LogLevel.WARN)
            {
                data = ReplaceVars(data, ps);
                var write = GetCurrTime() + " " + "[underline #a89022]WARN[/] : ";
                HandleLog(write, data);
            }
        }

        public static void WarnMarkUp(string data, params object[] ps)
        {
            if (LogLevel >= LogLevel.WARN)
            {
                data = ReplaceVars(data, ps);
                var write = GetCurrTime() + " " + "[underline #a89022]WARN[/] : " + data;
                HandleLog(write);
            }
        }

        public static void Error(string data, params object[] ps)
        {
            if (LogLevel >= LogLevel.ERROR)
            {
                data = ReplaceVars(data, ps);
                var write = GetCurrTime() + " " + "[underline red1]ERROR[/]: ";
                HandleLog(write, data);
            }
        }

        public static void ErrorMarkUp(string data, params object[] ps)
        {
            if (LogLevel >= LogLevel.ERROR)
            {
                data = ReplaceVars(data, ps);
                var write = GetCurrTime() + " " + "[underline red1]ERROR[/]: " + data;
                HandleLog(write);
            }
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
            if (IsWriteFile && File.Exists(LogFilePath))
            {
                data = ReplaceVars(data, ps);
                var plain = GetCurrTime() + " " + "EXTRA: " + data.RemoveMarkup();
                try
                {
                    //进入写入
                    LogWriteLock.EnterWriteLock();
                    using (StreamWriter sw = File.AppendText(LogFilePath))
                    {
                        sw.WriteLine(plain, Encoding.UTF8);
                    }
                }
                finally
                {
                    //释放占用
                    LogWriteLock.ExitWriteLock();
                }
            }
        }
    }
}
