using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

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
            if (IsWriteFile)
            {
                var plain = write.RemoveMarkup();
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
    }
}
