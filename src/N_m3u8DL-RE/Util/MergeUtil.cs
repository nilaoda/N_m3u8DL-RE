using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Entity;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Diagnostics;
using System.Globalization;
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

        private static int InvokeFFmpeg(string binary, string command, string workingDirectory)
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
            return p.ExitCode;
        }

        public static string[] PartialCombineMultipleFiles(string[] files)
        {
            var newFiles = new List<string>();
            int div = 0;
            if (files.Length <= 90000)
                div = 100;
            else
                div = 200;

            string outputName = Path.Combine(Path.GetDirectoryName(files[0])!, "T");
            int index = 0; //序号

            //按照div的容量分割为小数组
            string[][] li = Enumerable.Range(0, files.Count() / div + 1).Select(x => files.Skip(x * div).Take(div).ToArray()).ToArray();
            foreach (var items in li)
            {
                if (items.Count() == 0)
                    continue;
                var output = outputName + index.ToString("0000") + ".ts";
                CombineMultipleFilesIntoSingleFile(items, output);
                newFiles.Add(output);
                //合并后删除这些文件
                foreach (var item in items)
                {
                    File.Delete(item);
                }
                index++;
            }

            return newFiles.ToArray();
        }

        public static bool MergeByFFmpeg(string binary, string[] files, string outputPath, string muxFormat, bool useAACFilter,
            bool fastStart = false,
            bool writeDate = true, bool useConcatDemuxer = false, string poster = "", string audioName = "", string title = "",
            string copyright = "", string comment = "", string encodingTool = "", string recTime = "")
        {
            //改为绝对路径
            outputPath = Path.GetFullPath(outputPath);

            string dateString = string.IsNullOrEmpty(recTime) ? DateTime.Now.ToString("o") : recTime;

            StringBuilder command = new StringBuilder("-loglevel warning -nostdin ");
            string ddpAudio = string.Empty;
            string addPoster = "-map 1 -c:v:1 copy -disposition:v:1 attached_pic";
            ddpAudio = (File.Exists($"{Path.GetFileNameWithoutExtension(outputPath + ".mp4")}.txt") ? File.ReadAllText($"{Path.GetFileNameWithoutExtension(outputPath + ".mp4")}.txt") : "");
            if (!string.IsNullOrEmpty(ddpAudio)) useAACFilter = false;

            if (useConcatDemuxer)
            {
                // 使用 concat demuxer合并
                var text = string.Join(Environment.NewLine, files.Select(f => $"file '{f}'"));
                var tempFile = Path.GetTempFileName();
                File.WriteAllText(tempFile, text);
                command.Append($" -f concat -safe 0 -i \"{tempFile}");
            }
            else
            {
                command.Append(" -i concat:\"");
                foreach (string t in files)
                {
                    command.Append(Path.GetFileName(t) + "|");
                }
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
                        $"\" -metadata:s:a:{(string.IsNullOrEmpty(ddpAudio) ? "0" : "1")} title=\"" + audioName + $"\" -metadata:s:a:{(string.IsNullOrEmpty(ddpAudio) ? "0" : "1")} handler=\"" + audioName + "\" ");
                    command.Append(string.IsNullOrEmpty(ddpAudio) ? "" : " -metadata:s:a:0 title=\"DD+\" -metadata:s:a:0 handler=\"DD+\" ");
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
                    command.Append("\" -map 0  -c copy -f mp4 -y " + (useAACFilter ? "-bsf:a aac_adtstoasc" : "") + " \"" + outputPath + ".m4a\"");
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

            var code = InvokeFFmpeg(binary, command.ToString(), Path.GetDirectoryName(files[0])!);

            return code == 0;
        }

        public static bool MuxInputsByFFmpeg(string binary, OutputFile[] files, string outputPath, bool mp4, bool dateinfo)
        {
            var ext = mp4 ? "mp4" : "mkv";
            string dateString = DateTime.Now.ToString("o");
            StringBuilder command = new StringBuilder("-loglevel warning -nostdin -y -dn ");

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

            var srt = files.Any(x => x.FilePath.EndsWith(".srt"));

            if (mp4)
                command.Append($" -strict unofficial -c:a copy -c:v copy -c:s mov_text "); //mp4不支持vtt/srt字幕，必须转换格式
            else
                command.Append($" -strict unofficial -c:a copy -c:v copy -c:s {(srt ? "srt" : "webvtt")} ");

            //CLEAN
            command.Append(" -map_metadata -1 ");

            //LANG and NAME
            var streamIndex = 0;
            for (int i = 0; i < files.Length; i++)
            {
                //转换语言代码
                LanguageCodeUtil.ConvertLangCodeAndDisplayName(files[i]);
                command.Append($" -metadata:s:{streamIndex} language=\"{files[i].LangCode ?? "und"}\" ");
                if (!string.IsNullOrEmpty(files[i].Description))
                {
                    command.Append($" -metadata:s:{streamIndex} title=\"{files[i].Description}\" ");
                }
                /**
                 * -metadata:s:xx标记的是 输出的第xx个流的metadata，
                 * 若输入文件存在不止一个流时，这里单纯使用files的index
                 * 就有可能出现metadata错位的情况，所以加了如下逻辑
                 */
                if (files[i].Mediainfos.Count > 0)
                    streamIndex += files[i].Mediainfos.Count;
                else
                    streamIndex++;
            }

            var videoTracks = files.Where(x => x.MediaType != Common.Enum.MediaType.AUDIO && x.MediaType != Common.Enum.MediaType.SUBTITLES);
            var audioTracks = files.Where(x => x.MediaType == Common.Enum.MediaType.AUDIO);
            var subTracks = files.Where(x => x.MediaType == Common.Enum.MediaType.AUDIO);
            if (videoTracks.Any()) command.Append(" -disposition:v:0 default ");
            //字幕都不设置默认
            if (subTracks.Any()) command.Append(" -disposition:s 0 ");
            if (audioTracks.Any())
            {
                //音频除了第一个音轨 都不设置默认
                command.Append(" -disposition:a:0 default ");
                for (int i = 1; i < audioTracks.Count(); i++)
                {
                    command.Append($" -disposition:a:{i} 0 ");
                }
            }

            if (dateinfo) command.Append($" -metadata date=\"{dateString}\" ");
            command.Append($" -ignore_unknown -copy_unknown ");
            command.Append($" \"{outputPath}.{ext}\"");

            var code = InvokeFFmpeg(binary, command.ToString(), Environment.CurrentDirectory);

            return code == 0;
        }

        public static bool MuxInputsByMkvmerge(string binary, OutputFile[] files, string outputPath)
        {
            StringBuilder command = new StringBuilder($"-q --output \"{outputPath}.mkv\" ");

            command.Append(" --no-chapters ");

            var dFlag = false;

            //LANG and NAME
            for (int i = 0; i < files.Length; i++)
            {
                //转换语言代码
                LanguageCodeUtil.ConvertLangCodeAndDisplayName(files[i]);
                command.Append($" --language 0:\"{files[i].LangCode ?? "und"}\" ");
                //字幕都不设置默认
                if (files[i].MediaType == Common.Enum.MediaType.SUBTITLES)
                    command.Append($" --default-track 0:no ");
                //音频除了第一个音轨 都不设置默认
                if (files[i].MediaType == Common.Enum.MediaType.AUDIO)
                {
                    if (dFlag)
                        command.Append($" --default-track 0:no ");
                    dFlag = true;
                }
                if (!string.IsNullOrEmpty(files[i].Description))
                    command.Append($" --track-name 0:\"{files[i].Description}\" ");
                command.Append($" \"{files[i].FilePath}\" ");
            }

            var code = InvokeFFmpeg(binary, command.ToString(), Environment.CurrentDirectory);

            return code == 0;
        }
    }
}
