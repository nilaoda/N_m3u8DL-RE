﻿using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;

using N_m3u8DL_RE.Enumerations;

namespace N_m3u8DL_RE.Util
{
    internal static partial class OtherUtil
    {
        public static Dictionary<string, string> SplitHeaderArrayToDic(string[]? headers)
        {
            Dictionary<string, string> dic = [];
            if (headers == null)
            {
                return dic;
            }

            foreach (string header in headers)
            {
                int index = header.IndexOf(':');
                if (index != -1)
                {
                    dic[header[..index].Trim().ToLowerInvariant()] = header[(index + 1)..].Trim();
                }
            }

            return dic;
        }

        private static readonly char[] InvalidChars = [.. "34,60,62,124,0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,25,26,27,28,29,30,31,58,42,63,92,47"
            .Split(',').Select(s => (char)int.Parse(s, CultureInfo.InvariantCulture))];
        public static string GetValidFileName(string input, string re = "_", bool filterSlash = false)
        {
            string title = InvalidChars.Aggregate(input, (current, invalidChar) => current.Replace(invalidChar.ToString(), re));
            if (filterSlash)
            {
                title = title.Replace("/", re);
                title = title.Replace("\\", re);
            }
            return title.Trim('.');
        }

        /// <summary>
        /// 从输入自动获取文件名
        /// </summary>
        /// <param name="input"></param>
        /// <param name="addSuffix"></param>
        /// <returns></returns>
        public static string GetFileNameFromInput(string input, bool addSuffix = true)
        {
            string saveName = addSuffix ? DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture) : string.Empty;
            if (File.Exists(input))
            {
                saveName = Path.GetFileNameWithoutExtension(input) + "_" + saveName;
            }
            else
            {
                Uri uri = new(input.Split('?').First());
                string name = Path.GetFileNameWithoutExtension(uri.LocalPath);
                saveName = GetValidFileName(name) + "_" + saveName;
            }
            return saveName;
        }

        /// <summary>
        /// 从 hh:mm:ss 解析TimeSpan
        /// </summary>
        /// <param name="timeStr"></param>
        /// <returns></returns>
        public static TimeSpan ParseDur(string timeStr)
        {
            string[] arr = timeStr.Replace("：", ":").Split(':');
            int days = -1;
            int hours = -1;
            int mins = -1;
            int secs = -1;
            arr.Reverse().Select(i => Convert.ToInt32(i, CultureInfo.InvariantCulture)).ToList().ForEach(item =>
            {
                if (secs == -1)
                {
                    secs = item;
                }
                else if (mins == -1)
                {
                    mins = item;
                }
                else if (hours == -1)
                {
                    hours = item;
                }
                else if (days == -1)
                {
                    days = item;
                }
            });

            if (days == -1)
            {
                days = 0;
            }

            if (hours == -1)
            {
                hours = 0;
            }

            if (mins == -1)
            {
                mins = 0;
            }

            if (secs == -1)
            {
                secs = 0;
            }

            return new TimeSpan(days, hours, mins, secs);
        }

        /// <summary>
        /// 从1h3m20s解析出总秒数
        /// </summary>
        /// <param name="timeStr"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        public static double ParseSeconds(string timeStr)
        {
            Regex pattern = TimeStrRegex();

            Match match = pattern.Match(timeStr);

            if (!match.Success)
            {
                throw new ArgumentException("时间格式无效");
            }

            int hours = match.Groups[1].Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
            int minutes = match.Groups[2].Success ? int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) : 0;
            int seconds = match.Groups[3].Success ? int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture) : 0;

            return (hours * 3600) + (minutes * 60) + seconds;
        }

        // 若该文件夹为空，删除，同时判断其父文件夹，直到遇到根目录或不为空的目录
        public static void SafeDeleteDir(string dirPath)
        {
            if (string.IsNullOrEmpty(dirPath) || !Directory.Exists(dirPath))
            {
                return;
            }

            string parent = Path.GetDirectoryName(dirPath)!;
            if (!Directory.EnumerateFileSystemEntries(dirPath).Any())
            {
                Directory.Delete(dirPath);
            }
            else
            {
                return;
            }
            SafeDeleteDir(parent);
        }

        /// <summary>
        /// 解压并替换原文件
        /// </summary>
        /// <param name="filePath"></param>
        public static async Task DeGzipFileAsync(string filePath)
        {
            string deGzipFile = Path.ChangeExtension(filePath, ".dezip_tmp");
            try
            {
                await using (FileStream fileToDecompressAsStream = File.OpenRead(filePath))
                {
                    await using FileStream decompressedStream = File.Create(deGzipFile);
                    await using GZipStream decompressionStream = new(fileToDecompressAsStream, CompressionMode.Decompress);
                    await decompressionStream.CopyToAsync(decompressedStream);
                }
                ;
                File.Delete(filePath);
                File.Move(deGzipFile, filePath);
            }
            catch
            {
                if (File.Exists(deGzipFile))
                {
                    File.Delete(deGzipFile);
                }
            }
        }

        public static string GetEnvironmentVariable(string key, string defaultValue = "")
        {
            return Environment.GetEnvironmentVariable(key) ?? defaultValue;
        }

        public static string GetMuxExtension(MuxFormat muxFormat)
        {
            return muxFormat switch
            {
                MuxFormat.MP4 => ".mp4",
                MuxFormat.MKV => ".mkv",
                MuxFormat.TS => ".ts",
                _ => throw new ArgumentException($"unknown format: {muxFormat}")
            };
        }

        [GeneratedRegex(@"^(?:(\d+)h)?(?:(\d+)m)?(?:(\d+)s)?$")]
        private static partial Regex TimeStrRegex();
    }
}