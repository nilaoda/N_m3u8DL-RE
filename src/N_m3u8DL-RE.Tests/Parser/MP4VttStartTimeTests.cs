using System.Buffers.Binary;
using Mp4SubtitleParser;
using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.DownloadManager;

namespace N_m3u8DL_RE.Tests.Parser;

public class MP4VttStartTimeTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void MediaTimestampDefinesOriginEvenWithoutACue(int version)
    {
        // 仅有媒体时间而没有对白的首片，也必须提供正确原点，不能用首句字幕替代。
        var media = new byte[version == 0 ? 32 : 36];
        BinaryPrimitives.WriteUInt32BigEndian(media.AsSpan(0, 4), (uint)media.Length);
        "moof"u8.CopyTo(media.AsSpan(4));
        BinaryPrimitives.WriteUInt32BigEndian(media.AsSpan(8, 4), (uint)media.Length - 8);
        "traf"u8.CopyTo(media.AsSpan(12));
        BinaryPrimitives.WriteUInt32BigEndian(media.AsSpan(16, 4), (uint)media.Length - 16);
        "tfdt"u8.CopyTo(media.AsSpan(20));
        media[24] = (byte)version;
        if (version == 0)
            BinaryPrimitives.WriteUInt32BigEndian(media.AsSpan(28, 4), 300000);
        else
            BinaryPrimitives.WriteUInt64BigEndian(media.AsSpan(28, 8), 300000);
        var part = new MediaPart { OutputInpoint = MP4VttUtil.ReadStartTime(media, 1000) };
        Assert.Equal(300, SimpleDownloadManager.GetPartInpoint(part));
        Assert.Null(MP4VttUtil.ReadStartTime(media, 0));
    }
}
