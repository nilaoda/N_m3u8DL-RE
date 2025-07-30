﻿using System.Diagnostics;
using System.Globalization;
using System.Text;

using N_m3u8DL_RE.Common.CommonEnumerations;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Entity;
using N_m3u8DL_RE.Enumerations;

using Spectre.Console;

namespace N_m3u8DL_RE.Util
{
    internal static class MergeUtil
    {
        /// <summary>
        /// 输入一堆已存在的文件，合并到新文件
        /// </summary>
        /// <param name="files"></param>
        /// <param name="outputFilePath"></param>
        public static void CombineMultipleFilesIntoSingleFile(string[] files, string outputFilePath)
        {
            if (files.Length == 0)
            {
                return;
            }

            if (files.Length == 1)
            {
                FileInfo fi = new(files[0]);
                _ = fi.CopyTo(outputFilePath, true);
                return;
            }

            if (!Directory.Exists(Path.GetDirectoryName(outputFilePath)))
            {
                _ = Directory.CreateDirectory(Path.GetDirectoryName(outputFilePath)!);
            }

            string[] inputFilePaths = files;
            using FileStream outputStream = File.Create(outputFilePath);
            foreach (string inputFilePath in inputFilePaths)
            {
                if (inputFilePath == "")
                {
                    continue;
                }

                using FileStream inputStream = File.OpenRead(inputFilePath);
                inputStream.CopyTo(outputStream);
            }
        }

        private static int InvokeFFmpeg(string binary, string command, string workingDirectory)
        {
            Logger.DebugMarkUp($"{binary}: {command}");

            using Process p = new();
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
            _ = p.Start();
            p.BeginErrorReadLine();
            p.WaitForExit();
            return p.ExitCode;
        }

        public static string[] PartialCombineMultipleFiles(string[] files)
        {
            List<string> newFiles = [];
            int div = files.Length <= 90000 ? 100 : 200;

            string outputName = Path.Combine(Path.GetDirectoryName(files[0])!, "T");
            int index = 0; // 序号

            // 按照div的容量分割为小数组
            string[][] li = [.. Enumerable.Range(0, (files.Length / div) + 1).Select(x => files.Skip(x * div).Take(div).ToArray())];
            foreach (string[]? items in li)
            {
                if (items.Length == 0)
                {
                    continue;
                }

                string output = outputName + index.ToString("0000", CultureInfo.InvariantCulture) + ".ts";
                CombineMultipleFilesIntoSingleFile(items, output);
                newFiles.Add(output);
                // 合并后删除这些文件
                foreach (string? item in items)
                {
                    File.Delete(item);
                }
                index++;
            }

            return [.. newFiles];
        }

        public static bool MergeByFFmpeg(string binary, string[] files, string outputPath, string muxFormat, bool useAACFilter,
            bool fastStart = false,
            bool writeDate = true, bool useConcatDemuxer = false, string poster = "", string audioName = "", string title = "",
            string copyright = "", string comment = "", string encodingTool = "", string recTime = "")
        {
            // 改为绝对路径
            outputPath = Path.GetFullPath(outputPath);

            string dateString = string.IsNullOrEmpty(recTime) ? DateTime.Now.ToString("o") : recTime;

            StringBuilder command = new("-loglevel warning -nostdin ");
            string ddpAudio = string.Empty;
            string addPoster = "-map 1 -c:v:1 copy -disposition:v:1 attached_pic";
            ddpAudio = File.Exists($"{Path.GetFileNameWithoutExtension(outputPath + ".mp4")}.txt") ? File.ReadAllText($"{Path.GetFileNameWithoutExtension(outputPath + ".mp4")}.txt") : "";
            if (!string.IsNullOrEmpty(ddpAudio))
            {
                useAACFilter = false;
            }

            if (useConcatDemuxer)
            {
                // 使用 concat demuxer合并
                string text = string.Join(Environment.NewLine, files.Select(f => $"file '{f}'"));
                string tempFile = Path.GetTempFileName();
                File.WriteAllText(tempFile, text);
                _ = command.Append(CultureInfo.InvariantCulture, $" -f concat -safe 0 -i \"{tempFile}");
            }
            else
            {
                _ = command.Append(" -i concat:\"");
                foreach (string t in files)
                {
                    _ = command.Append(Path.GetFileName(t) + "|");
                }
            }


            switch (muxFormat.ToUpperInvariant())
            {
                case "MP4":
                    _ = command.Append("\" " + (string.IsNullOrEmpty(poster) ? "" : "-i \"" + poster + "\""));
                    _ = command.Append(" " + (string.IsNullOrEmpty(ddpAudio) ? "" : "-i \"" + ddpAudio + "\""));
                    _ = command.Append(
                        $" -map 0:v? {(string.IsNullOrEmpty(ddpAudio) ? "-map 0:a?" : $"-map {(string.IsNullOrEmpty(poster) ? "1" : "2")}:a -map 0:a?")} -map 0:s? " + (string.IsNullOrEmpty(poster) ? "" : addPoster)
                        + (writeDate ? " -metadata date=\"" + dateString + "\"" : "") +
                        " -metadata encoding_tool=\"" + encodingTool + "\" -metadata title=\"" + title +
                        "\" -metadata copyright=\"" + copyright + "\" -metadata comment=\"" + comment +
                        $"\" -metadata:s:a:{(string.IsNullOrEmpty(ddpAudio) ? "0" : "1")} title=\"" + audioName + $"\" -metadata:s:a:{(string.IsNullOrEmpty(ddpAudio) ? "0" : "1")} handler=\"" + audioName + "\" ");
                    _ = command.Append(string.IsNullOrEmpty(ddpAudio) ? "" : " -metadata:s:a:0 title=\"DD+\" -metadata:s:a:0 handler=\"DD+\" ");
                    if (fastStart)
                    {
                        _ = command.Append("-movflags +faststart");
                    }

                    _ = command.Append("  -c copy -y " + (useAACFilter ? "-bsf:a aac_adtstoasc" : "") + " \"" + outputPath + ".mp4\"");
                    break;
                case "MKV":
                    _ = command.Append("\" -map 0  -c copy -y " + (useAACFilter ? "-bsf:a aac_adtstoasc" : "") + " \"" + outputPath + ".mkv\"");
                    break;
                case "FLV":
                    _ = command.Append("\" -map 0  -c copy -y " + (useAACFilter ? "-bsf:a aac_adtstoasc" : "") + " \"" + outputPath + ".flv\"");
                    break;
                case "M4A":
                    _ = command.Append("\" -map 0  -c copy -f mp4 -y " + (useAACFilter ? "-bsf:a aac_adtstoasc" : "") + " \"" + outputPath + ".m4a\"");
                    break;
                case "TS":
                    _ = command.Append("\" -map 0  -c copy -y -f mpegts -bsf:v h264_mp4toannexb \"" + outputPath + ".ts\"");
                    break;
                case "EAC3":
                    _ = command.Append("\" -map 0:a -c copy -y \"" + outputPath + ".eac3\"");
                    break;
                case "AAC":
                    _ = command.Append("\" -map 0:a -c copy -y \"" + outputPath + ".m4a\"");
                    break;
                case "AC3":
                    _ = command.Append("\" -map 0:a -c copy -y \"" + outputPath + ".ac3\"");
                    break;
                default:
                    break;
            }

            int code = InvokeFFmpeg(binary, command.ToString(), Path.GetDirectoryName(files[0])!);

            return code == 0;
        }

        public static bool MuxInputsByFFmpeg(string binary, OutputFile[] files, string outputPath, MuxFormat muxFormat, bool dateinfo)
        {
            string ext = OtherUtil.GetMuxExtension(muxFormat);
            string dateString = DateTime.Now.ToString("o");
            StringBuilder command = new("-loglevel warning -nostdin -y -dn ");

            // INPUT
            foreach (OutputFile item in files)
            {
                _ = command.Append(CultureInfo.InvariantCulture, $" -i \"{item.FilePath}\" ");
            }

            // MAP
            for (int i = 0; i < files.Length; i++)
            {
                _ = command.Append(CultureInfo.InvariantCulture, $" -map {i} ");
            }

            bool srt = files.Any(x => x.FilePath.EndsWith(".srt", StringComparison.OrdinalIgnoreCase));

            if (muxFormat == MuxFormat.MP4)
            {
                _ = command.Append($" -strict unofficial -c:a copy -c:v copy -c:s mov_text "); // mp4不支持vtt/srt字幕，必须转换格式
            }
            else
            {
                _ = muxFormat == MuxFormat.TS
                    ? command.Append($" -strict unofficial -c:a copy -c:v copy ")
                    : muxFormat == MuxFormat.MKV
                    ? command.Append(CultureInfo.InvariantCulture, $" -strict unofficial -c:a copy -c:v copy -c:s {(srt ? "srt" : "webvtt")} ")
                    : throw new ArgumentException($"unknown format: {muxFormat}");
            }

            // CLEAN
            _ = command.Append(" -map_metadata -1 ");

            // LANG and NAME
            int streamIndex = 0;
            for (int i = 0; i < files.Length; i++)
            {
                // 转换语言代码
                LanguageCodeUtil.ConvertLangCodeAndDisplayName(files[i]);
                _ = command.Append(CultureInfo.InvariantCulture, $" -metadata:s:{streamIndex} language=\"{files[i].LangCode ?? "und"}\" ");
                if (!string.IsNullOrEmpty(files[i].Description))
                {
                    _ = command.Append(CultureInfo.InvariantCulture, $" -metadata:s:{streamIndex} title=\"{files[i].Description}\" ");
                }
                /**
                 * -metadata:s:xx标记的是 输出的第xx个流的metadata，
                 * 若输入文件存在不止一个流时，这里单纯使用files的index
                 * 就有可能出现metadata错位的情况，所以加了如下逻辑
                 */
                if (files[i].Mediainfos.Count > 0)
                {
                    streamIndex += files[i].Mediainfos.Count;
                }
                else
                {
                    streamIndex++;
                }
            }

            IEnumerable<OutputFile> videoTracks = files.Where(x => x.MediaType is not MediaType.AUDIO and not MediaType.SUBTITLES);
            IEnumerable<OutputFile> audioTracks = files.Where(x => x.MediaType == MediaType.AUDIO);
            IEnumerable<OutputFile> subTracks = files.Where(x => x.MediaType == MediaType.AUDIO);
            if (videoTracks.Any())
            {
                _ = command.Append(" -disposition:v:0 default ");
            }
            // 字幕都不设置默认
            if (subTracks.Any())
            {
                _ = command.Append(" -disposition:s 0 ");
            }

            if (audioTracks.Any())
            {
                // 音频除了第一个音轨 都不设置默认
                _ = command.Append(" -disposition:a:0 default ");
                for (int i = 1; i < audioTracks.Count(); i++)
                {
                    _ = command.Append(CultureInfo.InvariantCulture, $" -disposition:a:{i} 0 ");
                }
            }

            if (dateinfo)
            {
                _ = command.Append(CultureInfo.InvariantCulture, $" -metadata date=\"{dateString}\" ");
            }

            _ = command.Append($" -ignore_unknown -copy_unknown ");
            _ = command.Append(CultureInfo.InvariantCulture, $" \"{outputPath}{ext}\"");

            int code = InvokeFFmpeg(binary, command.ToString(), Environment.CurrentDirectory);

            return code == 0;
        }

        public static bool MuxInputsByMkvmerge(string binary, OutputFile[] files, string outputPath)
        {
            StringBuilder command = new($"-q --output \"{outputPath}.mkv\" ");

            _ = command.Append(" --no-chapters ");

            bool dFlag = false;

            // LANG and NAME
            for (int i = 0; i < files.Length; i++)
            {
                // 转换语言代码
                LanguageCodeUtil.ConvertLangCodeAndDisplayName(files[i]);
                _ = command.Append(CultureInfo.InvariantCulture, $" --language 0:\"{files[i].LangCode ?? "und"}\" ");
                // 字幕都不设置默认
                if (files[i].MediaType == MediaType.SUBTITLES)
                {
                    _ = command.Append($" --default-track 0:no ");
                }
                // 音频除了第一个音轨 都不设置默认
                if (files[i].MediaType == MediaType.AUDIO)
                {
                    if (dFlag)
                    {
                        _ = command.Append($" --default-track 0:no ");
                    }

                    dFlag = true;
                }
                if (!string.IsNullOrEmpty(files[i].Description))
                {
                    _ = command.Append(CultureInfo.InvariantCulture, $" --track-name 0:\"{files[i].Description}\" ");
                }

                _ = command.Append(CultureInfo.InvariantCulture, $" \"{files[i].FilePath}\" ");
            }

            int code = InvokeFFmpeg(binary, command.ToString(), Environment.CurrentDirectory);

            return code == 0;
        }
    }
}