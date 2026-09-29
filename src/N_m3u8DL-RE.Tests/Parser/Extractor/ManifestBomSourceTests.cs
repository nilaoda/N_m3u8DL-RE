using System.Net;
using System.Net.Sockets;
using System.Text;
using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Parser;
using N_m3u8DL_RE.Parser.Config;

namespace N_m3u8DL_RE.Tests.Parser.Extractor;

public class ManifestBomSourceTests
{
    private const string Hls = "#EXTM3U\n#EXT-X-TARGETDURATION:4\n#EXTINF:4,\nsegment.ts\n#EXT-X-ENDLIST\n";
    private const string Mpd = """
        <MPD xmlns="urn:mpeg:dash:schema:mpd:2011" type="static" mediaPresentationDuration="PT4S">
          <Period>
            <AdaptationSet mimeType="video/mp4" contentType="video">
              <Representation id="video" bandwidth="500000">
                <SegmentList timescale="1" duration="4">
                  <Initialization sourceURL="init.mp4" />
                  <SegmentURL media="segment.m4s" />
                </SegmentList>
              </Representation>
            </AdaptationSet>
          </Period>
        </MPD>
        """;
    private const string Ism = """
        <SmoothStreamingMedia MajorVersion="2" MinorVersion="1" Duration="40000000" TimeScale="10000000">
          <StreamIndex Type="audio" Name="audio" Url="QualityLevels({bitrate})/Fragments(audio={start time})">
            <QualityLevel Index="0" Bitrate="128000" FourCC="AACL" SamplingRate="48000" Channels="2" />
            <c t="0" d="40000000" />
          </StreamIndex>
        </SmoothStreamingMedia>
        """;

    public static IEnumerable<object[]> Cases()
    {
        foreach (var format in new[] { "hls", "mpd", "ism" })
        foreach (var sourceKind in new[] { "path", "file", "http" })
        foreach (var encodingName in new[] { "utf8", "utf16le", "utf16be", "utf32le", "utf32be" })
        {
            yield return [format, sourceKind, encodingName];
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task BomPrefixedManifestIsParsedFromSource(string format, string sourceKind, string encodingName)
    {
        var (content, expectedType, expectedSegment, mediaType) = format switch
        {
            "hls" => (Hls, ExtractorType.HLS, "segment.ts", "application/vnd.apple.mpegurl"),
            "mpd" => (Mpd, ExtractorType.MPEG_DASH, "segment.m4s", "application/dash+xml"),
            "ism" => (Ism, ExtractorType.MSS, "Fragments(audio=0)", "application/vnd.ms-sstr+xml"),
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
        Encoding encoding = encodingName switch
        {
            "utf8" => new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
            "utf16le" => Encoding.Unicode,
            "utf16be" => Encoding.BigEndianUnicode,
            "utf32le" => Encoding.UTF32,
            "utf32be" => new UTF32Encoding(bigEndian: true, byteOrderMark: true),
            _ => throw new ArgumentOutOfRangeException(nameof(encodingName))
        };
        var bytes = encoding.GetPreamble().Concat(encoding.GetBytes(content)).ToArray();

        if (sourceKind == "http")
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var serverTask = ServeOnceAsync(listener, bytes, mediaType);
            try
            {
                await AssertManifestAsync($"http://127.0.0.1:{port}/manifest.{format}", format, expectedType, expectedSegment);
                await serverTask.WaitAsync(TimeSpan.FromSeconds(5));
            }
            finally
            {
                listener.Stop();
            }
        }
        else
        {
            var path = Path.Combine(Path.GetTempPath(), $"manifest-bom-{Guid.NewGuid():N}.{format}");
            try
            {
                await File.WriteAllBytesAsync(path, bytes);
                var source = sourceKind == "file" ? new Uri(path).AbsoluteUri : path;
                await AssertManifestAsync(source, format, expectedType, expectedSegment);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }

    private static async Task AssertManifestAsync(string source, string format, ExtractorType expectedType,
        string expectedSegment)
    {
        using var extractor = new StreamExtractor(new ParserConfig());
        await extractor.LoadSourceFromUrlAsync(source);

        Assert.Equal(expectedType, extractor.ExtractorType);
        var extension = format == "hls" ? "m3u8" : format;
        var raw = extractor.RawFiles[$"raw.{extension}"];
        Assert.NotEqual('\uFEFF', raw[0]);
        var stream = Assert.Single(await extractor.ExtractStreamsAsync());
        Assert.Equal(1, stream.SegmentsCount);
        Assert.EndsWith(expectedSegment, Assert.Single(stream.Playlist!.MediaParts.SelectMany(p => p.MediaSegments)).Url);
    }

    private static async Task ServeOnceAsync(TcpListener listener, byte[] bytes, string mediaType)
    {
        using var client = await listener.AcceptTcpClientAsync();
        await using var stream = client.GetStream();
        var requestBuffer = new byte[1024];
        await stream.ReadAtLeastAsync(requestBuffer, 1, throwOnEndOfStream: true);

        var headers = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 200 OK\r\nContent-Type: {mediaType}\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(headers);
        await stream.WriteAsync(bytes);
    }
}
