using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Common.Util;
using NiL.JS.Expressions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace N_m3u8DL_RE.Util
{
    internal class LargeSingleFileSplitUtil
    {
        class Clip
        {
            public required int index;
            public required long from;
            public required long to;
        }

        /// <summary>
        /// URL大文件切片处理
        /// </summary>
        /// <param name="url"></param>
        /// <param name="headers"></param>
        /// <param name="splitSegments"></param>
        /// <returns></returns>
        public static async Task<List<MediaSegment>?> SplitUrlAsync(MediaSegment segment, Dictionary<string,string> headers)
        {
            var url = segment.Url;
            if (!await CanSplitAsync(url, headers)) return null;

            if (segment.StartRange != null) return null;

            long fileSize = await GetFileSizeAsync(url, headers);
            if (fileSize == 0) return null;

            List<Clip> allClips = GetAllClips(url, fileSize);
            var splitSegments = new List<MediaSegment>();
            foreach (Clip clip in allClips)
            {
                splitSegments.Add(new MediaSegment()
                {
                    Index = clip.index,
                    Url = url,
                    StartRange = clip.from,
                    ExpectLength = clip.to == -1 ? null : clip.to - clip.from + 1,
                    EncryptInfo = segment.EncryptInfo,
                });
            }

            return splitSegments;
        }

        public static async Task<bool> CanSplitAsync(string url, Dictionary<string, string> headers)
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Head, url);
                var response = (await HTTPUtil.AppHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead)).EnsureSuccessStatusCode();
                bool supportsRangeRequests = response.Headers.Contains("Accept-Ranges");

                return supportsRangeRequests;
            }
            catch (Exception ex)
            {
                Logger.DebugMarkUp(ex.Message);
                return false;
            }
        }

        private static async Task<long> GetFileSizeAsync(string url, Dictionary<string, string> headers)
        {
            using var httpRequestMessage = new HttpRequestMessage();
            httpRequestMessage.RequestUri = new(url);
            foreach (var header in headers)
            {
                httpRequestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            var response = (await HTTPUtil.AppHttpClient.SendAsync(httpRequestMessage, HttpCompletionOption.ResponseHeadersRead)).EnsureSuccessStatusCode();
            long totalSizeBytes = response.Content.Headers.ContentLength ?? 0;

            return totalSizeBytes;
        }

        //此函数主要是切片下载逻辑
        private static List<Clip> GetAllClips(string url, long fileSize)
        {
            List<Clip> clips = new();
            int index = 0;
            long counter = 0;
            int perSize = 10 * 1024 * 1024;
            while (fileSize > 0)
            {
                Clip c = new()
                {
                    index = index,
                    from = counter,
                    to = counter + perSize
                };
                //没到最后
                if (fileSize - perSize > 0)
                {
                    fileSize -= perSize;
                    counter += perSize + 1;
                    index++;
                    clips.Add(c);
                }
                //已到最后
                else
                {
                    c.to = -1;
                    clips.Add(c);
                    break;
                }
            }
            return clips;
        }
    }
}
