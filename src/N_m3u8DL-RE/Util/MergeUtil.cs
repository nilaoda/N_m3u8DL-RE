using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Entity;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace N_m3u8DL_RE.Util
{
    internal class MergeUtil
    {
        /// <summary>
        /// 输入一堆已存在的文件，合并到新文件
        /// </summary>
        /// <param name="files"></param>
        /// <param name="outputFilePath"></param>
        public static void CombineMultipleFilesIntoSingleFile(string[] files, string outputFilePath)
        {
            if (files.Length == 0) return;
            if (files.Length == 1)
            {
                FileInfo fi = new FileInfo(files[0]);
                fi.CopyTo(outputFilePath, true);
                return;
            }

            if (!Directory.Exists(Path.GetDirectoryName(outputFilePath)))
                Directory.CreateDirectory(Path.GetDirectoryName(outputFilePath)!);

            string[] inputFilePaths = files;
            using (var outputStream = File.Create(outputFilePath))
            {
                foreach (var inputFilePath in inputFilePaths)
                {
                    if (inputFilePath == "")
                        continue;
                    using (var inputStream = File.OpenRead(inputFilePath))
                    {
                        inputStream.CopyTo(outputStream);
                    }
                }
            }
        }

        private static void InvokeFFmpeg(string binary, string command, string workingDirectory)
        {
            Logger.DebugMarkUp($"{binary}: {command}");

            using var p = new Process();
            p.StartInfo = new ProcessStartInfo()
            {
                WorkingDirectory = workingDirectory,
                FileName = binary,
                Arguments = command,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            p.ErrorDataReceived += (sendProcess, output) =>
            {
                if (!string.IsNullOrEmpty(output.Data))
                {
                    Logger.WarnMarkUp($"[grey]{output.Data.EscapeMarkup()}[/]");
                }
            };
            p.Start();
            p.BeginErrorReadLine();
            p.WaitForExit();
        }

        public static bool MergeByFFmpeg(string binary, string[] files, string outputPath, string muxFormat, bool useAACFilter,
            bool fastStart = false,
            bool writeDate = true, string poster = "", string audioName = "", string title = "",
            string copyright = "", string comment = "", string encodingTool = "", string recTime = "")
        {
            //改为绝对路径
            outputPath = Path.GetFullPath(outputPath);

            string dateString = string.IsNullOrEmpty(recTime) ? DateTime.Now.ToString("o") : recTime;

            StringBuilder command = new StringBuilder("-loglevel warning -i concat:\"");
            string ddpAudio = string.Empty;
            string addPoster = "-map 1 -c:v:1 copy -disposition:v:1 attached_pic";
            ddpAudio = (File.Exists($"{Path.GetFileNameWithoutExtension(outputPath + ".mp4")}.txt") ? File.ReadAllText($"{Path.GetFileNameWithoutExtension(outputPath + ".mp4")}.txt") : "");
            if (!string.IsNullOrEmpty(ddpAudio)) useAACFilter = false;

            foreach (string t in files)
            {
                command.Append(Path.GetFileName(t) + "|");
            }

            switch (muxFormat.ToUpper())
            {
                case ("MP4"):
                    command.Append("\" " + (string.IsNullOrEmpty(poster) ? "" : "-i \"" + poster + "\""));
                    command.Append(" " + (string.IsNullOrEmpty(ddpAudio) ? "" : "-i \"" + ddpAudio + "\""));
                    command.Append(
                        $" -map 0:v? {(string.IsNullOrEmpty(ddpAudio) ? "-map 0:a?" : $"-map {(string.IsNullOrEmpty(poster) ? "1" : "2")}:a -map 0:a?")} -map 0:s? " + (string.IsNullOrEmpty(poster) ? "" : addPoster)
                        + (writeDate ? " -metadata date=\"" + dateString + "\"" : "") +
                        " -metadata encoding_tool=\"" + encodingTool + "\" -metadata title=\"" + title +
                        "\" -metadata copyright=\"" + copyright + "\" -metadata comment=\"" + comment +
                        $"\" -metadata:s:a:{(string.IsNullOrEmpty(ddpAudio) ? "0" : "1")} handler_name=\"" + audioName + $"\" -metadata:s:a:{(string.IsNullOrEmpty(ddpAudio) ? "0" : "1")} handler=\"" + audioName + "\" ");
                    command.Append(string.IsNullOrEmpty(ddpAudio) ? "" : " -metadata:s:a:0 handler_name=\"DD+\" -metadata:s:a:0 handler=\"DD+\" ");
                    if (fastStart)
                        command.Append("-movflags +faststart");
                    command.Append("  -c copy -y " + (useAACFilter ? "-bsf:a aac_adtstoasc" : "") + " \"" + outputPath + ".mp4\"");
                    break;
                case ("MKV"):
                    command.Append("\" -map 0  -c copy -y " + (useAACFilter ? "-bsf:a aac_adtstoasc" : "") + " \"" + outputPath + ".mkv\"");
                    break;
                case ("FLV"):
                    command.Append("\" -map 0  -c copy -y " + (useAACFilter ? "-bsf:a aac_adtstoasc" : "") + " \"" + outputPath + ".flv\"");
                    break;
                case ("M4A"):
                    command.Append("\" -map 0  -c copy -y " + (useAACFilter ? "-bsf:a aac_adtstoasc" : "") + " \"" + outputPath + ".m4a\"");
                    break;
                case ("TS"):
                    command.Append("\" -map 0  -c copy -y -f mpegts -bsf:v h264_mp4toannexb \"" + outputPath + ".ts\"");
                    break;
                case ("EAC3"):
                    command.Append("\" -map 0:a -c copy -y \"" + outputPath + ".eac3\"");
                    break;
                case ("AAC"):
                    command.Append("\" -map 0:a -c copy -y \"" + outputPath + ".m4a\"");
                    break;
                case ("AC3"):
                    command.Append("\" -map 0:a -c copy -y \"" + outputPath + ".ac3\"");
                    break;
            }

            InvokeFFmpeg(binary, command.ToString(), Path.GetDirectoryName(files[0])!);

            if (File.Exists($"{outputPath}.{muxFormat}") && new FileInfo($"{outputPath}.{muxFormat}").Length > 0)
                return true;

            return false;
        }

        public static bool MuxInputsByFFmpeg(string binary, OutputFile[] files, string outputPath)
        {
            string dateString = DateTime.Now.ToString("o");
            StringBuilder command = new StringBuilder("-loglevel warning -y ");

            //INPUT
            foreach (var item in files)
            {
                command.Append($" -i \"{item.FilePath}\" ");
            }

            //MAP
            for (int i = 0; i < files.Length; i++)
            {
                command.Append($" -map {i} ");
            }

            //CLEAN
            command.Append(" -map_metadata -1 ");

            //LANG and NAME
            for (int i = 0; i < files.Length; i++)
            {
                command.Append($" -metadata:s:{i} language={files[i].LangCode ?? "und"} ");
                if (!string.IsNullOrEmpty(files[i].Description))
                    command.Append($" -metadata:s:{i} title={files[i].Description} ");
            }

            command.Append($" -metadata date=\"{dateString}\" -ignore_unknown -copy_unknown -c copy \"{outputPath}.mkv\"");

            InvokeFFmpeg(binary, command.ToString(), Environment.CurrentDirectory);

            if (File.Exists($"{outputPath}.mkv") && new FileInfo($"{outputPath}.mkv").Length > 1024)
                return true;

            return false;
        }

        public static bool MuxInputsByMkvmerge(string binary, OutputFile[] files, string outputPath)
        {
            StringBuilder command = new StringBuilder($"-q --output \"{outputPath}.mkv\" ");

            command.Append(" --no-chapters ");

            //LANG and NAME
            for (int i = 0; i < files.Length; i++)
            {
                command.Append($" --language 0:{files[i].LangCode ?? "und"} ");
                if (!string.IsNullOrEmpty(files[i].Description))
                    command.Append($" --track-name 0:\"{files[i].Description}\" ");
                command.Append($" \"{files[i].FilePath}\" ");
            }

            InvokeFFmpeg(binary, command.ToString(), Environment.CurrentDirectory);

            if (File.Exists($"{outputPath}.mkv") && new FileInfo($"{outputPath}.mkv").Length > 1024)
                return true;

            return false;
        }
    }
}
