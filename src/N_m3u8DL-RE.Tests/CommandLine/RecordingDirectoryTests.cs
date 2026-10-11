using System.Buffers.Binary;
using System.CommandLine;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Mp4SubtitleParser;
using N_m3u8DL_RE.CommandLine;
using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Entity;
using N_m3u8DL_RE.Util;
using static N_m3u8DL_RE.Tests.TestSupport.DownloadTestHelper;

namespace N_m3u8DL_RE.Tests.CommandLine;

[Collection("Download console")]
public sealed class RecordingDirectoryTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("re-recording-tests-").FullName;

    [Fact]
    public void SelectsMatchingInitializationAndDecryptedSegmentsInNaturalOrder()
    {
        var track = Directory.CreateDirectory(Path.Combine(directory, "0_video")).FullName;
        File.WriteAllBytes(Path.Combine(track, "_init.mp4"), [99]);
        File.WriteAllBytes(Path.Combine(track, "_init_dec.mp4"), Init("vide", "eng"));
        foreach (var name in new[] { "0000000010_dash-segment-100_dec.m4s", "2_20.m4s", "2_20_dec.m4s", "1_10_dec.m4s", "3_30.m4s.tmp", "old.mp4" })
            File.WriteAllBytes(Path.Combine(track, name), [1]);
        var input = RecordingTrack.Read(new OutputFile { Index = 0, FilePath = track, Description = "Custom" }, default);
        Assert.Equal(new[] { "_init_dec.mp4", "1_10_dec.m4s", "2_20_dec.m4s", "0000000010_dash-segment-100_dec.m4s" }, input.Inputs.Select(Path.GetFileName));
        Assert.Equal(input.Inputs, RecordingTrack.Read(new OutputFile { Index = 0, FilePath = track }, default, binaryConcat: true).Inputs);
        Assert.Equal("eng", input.Info.LangCode);
        Assert.Equal("Custom", input.Info.Description);
        Assert.Equal(new[] { "Video" }, MP4MediaInfoUtil.ReadTrackTypes(input.Inputs[0]));
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("missing-init")]
    [InlineData("mixed-decryption")]
    [InlineData("multiple-init")]
    [InlineData("parts")]
    public async Task AmbiguousOrIncompleteTracksFailWithoutCreatingOutput(string scenario)
    {
        var track = Directory.CreateDirectory(Path.Combine(directory, "0_video")).FullName;
        if (scenario != "empty")
            File.WriteAllBytes(Path.Combine(track, "1_10_dec.m4s"), [1]);
        if (scenario == "mixed-decryption")
            File.WriteAllBytes(Path.Combine(track, "2_20.m4s"), [2]);
        if (scenario == "multiple-init")
            File.WriteAllBytes(Path.Combine(track, "_init_1_dec.mp4"), Init("vide", "und"));
        if (scenario == "parts")
        {
            Directory.CreateDirectory(Path.Combine(track, "parts", "0000"));
            Directory.CreateDirectory(Path.Combine(track, "parts", "0001"));
        }
        var output = Path.Combine(directory, "out", "result.mkv");
        Assert.Equal(1, await Invoke("mux", "-i", scenario == "parts" ? directory : track, "-o", output));
        Assert.Equal(1, await Invoke("concat", "-i", track, "-o", output));
        Assert.False(Directory.Exists(Path.GetDirectoryName(output)));
        Assert.Empty(Directory.GetDirectories(directory, ".re-tracks-*"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcatPreservesEncryptedTracksWithoutMixingPartialDecryption(bool partialDecryption)
    {
        var track = Directory.CreateDirectory(Path.Combine(directory, "0_video")).FullName;
        var stsd = new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 }.Concat(Box("encv", new byte[4],
            Box("schm", new byte[4], "cenc"u8.ToArray(), new byte[4]))).ToArray();
        var init = Box("moov", Box("trak", Box("mdia", Box("minf", Box("stbl", Box("stsd", stsd))))));
        Assert.Equal("cenc", MP4InitUtil.ReadInit(init).Scheme);
        File.WriteAllBytes(Path.Combine(track, "_init.mp4"), init);
        File.WriteAllBytes(Path.Combine(track, "10_100.m4s"), [10]);
        File.WriteAllBytes(Path.Combine(track, "2_20.m4s"), [2]);
        File.WriteAllBytes(Path.Combine(track, "1_10.m4s"), [1]);
        File.WriteAllBytes(Path.Combine(track, "3_30.m4s.tmp"), [99]);
        if (partialDecryption)
        {
            File.WriteAllBytes(Path.Combine(track, "_init_dec.mp4"), Init("vide", "eng"));
            File.WriteAllBytes(Path.Combine(track, "1_10_dec.m4s"), [99]);
        }
        var sources = Directory.GetFiles(track).ToDictionary(path => path, File.ReadAllBytes);
        var output = Path.Combine(directory, "joined.mp4");
        Assert.Equal(0, await Invoke("concat", "-i", track, "-o", output));
        Assert.Equal(init.Concat(new byte[] { 1, 2, 10 }).ToArray(), File.ReadAllBytes(output));
        foreach (var (path, bytes) in sources)
            Assert.Equal(bytes, File.ReadAllBytes(path));
        foreach (var operation in new[] { "mux", "merge" })
            Assert.Equal(1, await Invoke(operation, "-i", track, "-o", Path.Combine(directory, operation + ".mp4"), "--dry-run"));
        Assert.Empty(Directory.GetFiles(directory, ".re-*"));
    }

    [Fact]
    public async Task TrackConcatPreservesSourcesAndProtectsUnselectedFilesAndAliases()
    {
        var track = Directory.CreateDirectory(Path.Combine(directory, "0_video")).FullName;
        File.WriteAllBytes(Path.Combine(track, "10.ts"), [10]);
        File.WriteAllBytes(Path.Combine(track, "2.ts"), [2]);
        var original = Path.Combine(track, "old.ts");
        File.WriteAllBytes(original, [99]);
        var alias = Path.Combine(directory, "alias.ts");
        await CreateLink(alias, original, directory: false);
        foreach (var output in new[] { original, alias })
            Assert.Equal(1, await Invoke("concat", "-i", track, "-o", output, "--overwrite"));
        var joined = Path.Combine(directory, "joined.ts");
        Assert.Equal(0, await Invoke("concat", "-i", track, "-o", joined));
        Assert.Equal(new byte[] { 2, 10 }, File.ReadAllBytes(joined));
        Assert.Equal(new byte[] { 99 }, File.ReadAllBytes(original));
        Assert.Equal(new byte[] { 2 }, File.ReadAllBytes(Path.Combine(track, "2.ts")));
    }

    [Fact]
    public async Task SinglePartTrackProtectsInitializationCacheAndConcatenatesSegments()
    {
        var track = Directory.CreateDirectory(Path.Combine(directory, "0_video")).FullName;
        var part = Directory.CreateDirectory(Path.Combine(track, "parts", "0000", "0_video")).FullName;
        File.WriteAllBytes(Path.Combine(part, "0.ts"), [1]);
        File.WriteAllBytes(Path.Combine(part, "1.ts"), [2]);
        var cache = Directory.CreateDirectory(Path.Combine(track, "parts", "_init-cache")).FullName;
        var cachedInit = Path.Combine(cache, "hash.mp4");
        File.WriteAllBytes(cachedInit, [99]);
        Assert.Equal(1, await Invoke("concat", "-i", track, "-o", cachedInit, "--overwrite"));
        Assert.Equal(new byte[] { 99 }, File.ReadAllBytes(cachedInit));
        var output = Path.Combine(directory, "joined.ts");
        Assert.Equal(0, await Invoke("concat", "-i", track, "-o", output));
        Assert.Equal(new byte[] { 1, 2 }, File.ReadAllBytes(output));
    }

    [Theory]
    [InlineData("srt", false)]
    [InlineData("vtt", false)]
    [InlineData("srt", true)]
    public async Task ConvertedSubtitlesPreserveSourcesAndSkipEmptyTracks(string extension, bool hasCues)
    {
        var track = Directory.CreateDirectory(Path.Combine(directory, "0_subtitles")).FullName;
        var input = Path.Combine(track, "0.fix." + extension);
        var empty = new WebVttSub();
        var text = hasCues ? "1\r\n00:00:00,500 --> 00:00:01,000\r\n<i>X-TIMESTAMP-MAP is text, not a header</i>\r\n\r\n"
            : extension == "srt" ? empty.ToSrt() : empty.ToVtt();
        File.WriteAllText(input, text);
        var recording = RecordingTrack.Read(new OutputFile { Index = 0, FilePath = track }, default);
        var result = await recording.PrepareAsync(directory, 0, default, _ => { });
        if (hasCues)
            Assert.Equal(input, result?.FilePath);
        else
            Assert.Null(result);
        Assert.Equal(text, File.ReadAllText(input));
        Assert.False(File.Exists(Path.Combine(directory, "0.srt")));
    }

    [Fact]
    public async Task RecordingDiscoveryAndDryRunDoNotRequireToolsOrWriteFiles()
    {
        foreach (var name in new[] { "10_audio", "2_video" })
        {
            var track = Directory.CreateDirectory(Path.Combine(directory, name)).FullName;
            File.WriteAllBytes(Path.Combine(track, "1.ts"), [1]);
        }
        Directory.CreateDirectory(Path.Combine(directory, "unrelated"));
        Directory.CreateDirectory(Path.Combine(directory, "2026"));
        var empty = Directory.CreateDirectory(Path.Combine(directory, "3_empty")).FullName;
        File.WriteAllBytes(Path.Combine(empty, "_init.mp4"), [99]);
        File.WriteAllBytes(Path.Combine(directory, "123.mp4"), [99]);
        Assert.Equal(new[] { "2_video", "10_audio" }, RecordingTrack.FindTracks(directory).Select(file => Path.GetFileName(file.FilePath)));
        var output = Path.Combine(directory, "out", "result.mkv");
        Assert.Equal(0, await Invoke("mux", "-i", directory, "-o", output,
            "--dry-run", "--ffmpeg-binary-path", "/missing/ffmpeg"));
        Assert.False(Directory.Exists(Path.GetDirectoryName(output)));
        var extra = Path.Combine(directory, "extra.srt");
        File.WriteAllText(extra, "1\n00:00:00,500 --> 00:00:01,000\nExternal\n\n");
        Assert.Equal(0, await Invoke("mux", "-i", directory, "-i", extra, "-o", output, "--dry-run"));
        Assert.NotEmpty(CommandInvoker.CreateRootCommand().Parse(["mux", "--input-dir", directory, "-o", output]).Errors);
    }

    [Fact]
    public async Task MixedMuxInputsExpandInSpecifiedOrderAndPreserveMetadata()
    {
        var task = Directory.CreateDirectory(Path.Combine(directory, "path=task")).FullName;
        foreach (var name in new[] { "10_audio", "2_video" })
        {
            var track = Directory.CreateDirectory(Path.Combine(task, name)).FullName;
            File.WriteAllBytes(Path.Combine(track, "0.ts"), [1]);
        }
        var single = Directory.CreateDirectory(Path.Combine(directory, "subtitles")).FullName;
        File.WriteAllText(Path.Combine(single, "0.fix.srt"), "1\n00:00:00,500 --> 00:00:01,000\nHello\n\n");
        var file = Path.Combine(directory, "video.mp4");
        File.WriteAllBytes(file, [1]);
        var output = Path.Combine(directory, "out", "result.mkv");
        var originalError = Console.Error;
        using var stderr = new StringWriter();
        try
        {
            Console.SetError(stderr);
            Assert.Equal(0, await Invoke("mux", "-i", file, "-i", task,
                "-i", "path=" + single.Replace(":", "\\:") + ":lang=chi:name=中文",
                "-o", output, "--dry-run"));
            var log = stderr.ToString();
            Assert.Contains($"[0] {file}", log);
            Assert.Contains($"[1] {Path.Combine(task, "2_video")}", log);
            Assert.Contains($"[2] {Path.Combine(task, "10_audio")}", log);
            Assert.Contains($"[3] {single}", log);
            Assert.Contains("lang=chi name=中文", log);
        }
        finally
        {
            Console.SetError(originalError);
        }
        Assert.False(Directory.Exists(Path.GetDirectoryName(output)));
    }

    [Theory]
    [InlineData("ffmpeg")]
    [InlineData("mkvmerge")]
    public async Task MuxesRecordedTracksWithAlignedSubtitlesAndPreservesFilesOnFailure(string muxer)
    {
        if (!HasTool("ffmpeg") || !HasTool("ffprobe") || muxer == "mkvmerge" && !OnPath("mkvmerge"))
            return;
        var video = await FragmentedTrack("0_video", audio: false);
        await FragmentedTrack("1_audio", audio: true);
        var subtitles = Directory.CreateDirectory(Path.Combine(directory, "2_subtitle")).FullName;
        File.WriteAllBytes(Path.Combine(subtitles, "_init.mp4"), Init("subt", "chi"));
        var xml = "<tt xmlns=\"http://www.w3.org/ns/ttml\"><head/><body><div>" +
            "<p begin=\"00:16:40.500\" end=\"00:16:41.000\"><span>Hello</span></p></div></body></tt>";
        File.WriteAllBytes(Path.Combine(subtitles, "1_1000000.m4s"), Box("mdat", Encoding.UTF8.GetBytes(xml)));
        var mapped = Directory.CreateDirectory(Path.Combine(directory, "3_mapped")).FullName;
        foreach (var index in new[] { 0, 1 })
            File.WriteAllText(Path.Combine(mapped, $"{index + 1}.vtt"),
                $"WEBVTT\nX-TIMESTAMP-MAP=LOCAL:00:00:05.000,MPEGTS:{90000000 + index * 90000}\n\n" +
                $"00:00:05.500 --> 00:00:06.000\nMapped {index}\n\n");
        var empty = Directory.CreateDirectory(Path.Combine(directory, "4_empty")).FullName;
        File.WriteAllText(Path.Combine(empty, "0.fix.srt"), new WebVttSub().ToSrt());
        var extra = Path.Combine(directory, "extra.srt");
        File.WriteAllText(extra, "1\n00:00:01,000 --> 00:00:02,000\nExternal\n\n");
        var sourceFiles = Directory.GetFiles(directory, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
        var output = Path.Combine(directory, "out", "result.mkv");
        Assert.Equal(0, await Invoke("mux", "-i", directory, "--muxer", muxer, "-o", output,
            "-i", "path=" + extra.Replace(":", "\\:") + ":lang=jpn:name=External", "--title", "Recording"));
        var text = await Run("ffmpeg", "-v", "error", "-i", output, "-map", "0:s:0", "-f", "srt", "-");
        Assert.Contains("00:00:00,500 --> 00:00:01,000", text);
        Assert.Contains("Hello", text);
        var mappedText = await Run("ffmpeg", "-v", "error", "-i", output, "-map", "0:s:1", "-f", "srt", "-");
        Assert.Contains("00:00:00,500 --> 00:00:01,000", mappedText);
        Assert.Contains("00:00:01,500 --> 00:00:02,000", mappedText);
        using var probe = JsonDocument.Parse(await Run("ffprobe", "-v", "error", "-show_entries",
            "format=duration:format_tags=title:stream=codec_type,start_time:stream_tags=language,title", "-of", "json", output));
        Assert.Equal("Recording", probe.RootElement.GetProperty("format").GetProperty("tags").GetProperty("title").GetString());
        var streams = probe.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        Assert.Equal(new[] { "video", "audio", "subtitle", "subtitle", "subtitle" }, streams.Select(stream => stream.GetProperty("codec_type").GetString()));
        Assert.Equal("chi", streams[2].GetProperty("tags").GetProperty("language").GetString());
        Assert.Equal("jpn", streams[4].GetProperty("tags").GetProperty("language").GetString());
        var audioStart = double.Parse(streams[1].GetProperty("start_time").GetString()!, CultureInfo.InvariantCulture);
        Assert.InRange(audioStart, 0.17, 0.21);
        // 混流失败时清理本次中间文件，并保留已存在的输出。
        var invalidSubtitle = Path.Combine(directory, "out", "invalid.srt");
        File.WriteAllText(invalidSubtitle, "Not a subtitle.");
        var previousOutput = File.ReadAllBytes(output);
        Assert.Equal(1, await Invoke("mux", "-i", directory, "--muxer", muxer, "-o", output,
            "--overwrite", "--mux-import", "path=" + invalidSubtitle.Replace(":", "\\:")));
        Assert.Equal(previousOutput, File.ReadAllBytes(output));
        // 视频轨道无法写入纯音频 M4A，失败时已有输出和源分片都必须保留。
        var failedOutput = Path.Combine(directory, "out", "old.m4a");
        File.WriteAllBytes(failedOutput, [9]);
        Assert.Equal(1, await Invoke("merge", "-i", video, "-o", failedOutput, "--overwrite"));
        Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(failedOutput));
        foreach (var (path, bytes) in sourceFiles)
            Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Empty(Directory.GetDirectories(Path.GetDirectoryName(output)!, ".re-tracks-*"));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(output)!, ".re-*"));
    }

    [Theory]
    [InlineData("LOCAL_HTTP")]
    [InlineData("PROTOCOL")]
    [InlineData("DEMUXER")]
    public async Task FragmentedTrackCanBeMergedInEveryConcatMode(string mode)
    {
        if (!HasTool("ffmpeg"))
            return;
        var video = await FragmentedTrack("0_video", audio: false);
        var output = Path.Combine(directory, "merged.mp4");
        Assert.Equal(0, await Invoke("merge", "-i", video, "-o", output, "--ffmpeg-concat-mode", mode));
        var expected = await FrameHashes(Path.Combine(directory, "0_video.mp4"));
        Assert.Equal(expected, await FrameHashes(output));
        Assert.Empty(Directory.GetFiles(directory, ".re-*"));
    }

    [Theory]
    [InlineData("mpegts", "hls", "ffmpeg")]
    [InlineData("fmp4", "hls", "mkvmerge")]
    [InlineData("fmp4", "dash", "ffmpeg")]
    public async Task VodDownloadsWithoutMergeRetainMediaAndConvertedSubtitles(string segmentType, string format, string muxer)
    {
        if (!HasTool("ffmpeg") || !HasTool("ffprobe") || muxer == "mkvmerge" && !OnPath("mkvmerge"))
            return;
        var source = Directory.CreateDirectory(Path.Combine(directory, "source")).FullName;
        await Run("ffmpeg", "-v", "error", "-f", "lavfi", "-i", "testsrc2=s=160x90:r=25",
            "-t", "6", "-c:v", "libx264", "-g", "50", "-bf", "0", "-f", "hls", "-hls_time", "2",
            "-hls_segment_type", segmentType, "-hls_fmp4_init_filename", "init.mp4",
            "-hls_segment_filename", Path.Combine(source, segmentType == "fmp4" ? "media-%d.m4s" : "media-%d.ts"),
            Path.Combine(source, "source.m3u8"));
        File.WriteAllText(Path.Combine(source, "subs.m3u8"),
            "#EXTM3U\n#EXT-X-TARGETDURATION:6\n#EXTINF:6,\nsub.vtt\n#EXT-X-ENDLIST\n");
        File.WriteAllText(Path.Combine(source, "sub.vtt"),
            $"WEBVTT\nX-TIMESTAMP-MAP=LOCAL:00:00:00.000,MPEGTS:{(segmentType == "mpegts" ? 126000 : 0)}\n\n" +
            "00:00:00.250 --> 00:00:01.000\n<i>Hello, VOD</i>\n\n");
        var master = Path.Combine(source, "master.m3u8");
        File.WriteAllText(master, """
            #EXTM3U
            #EXT-X-MEDIA:TYPE=SUBTITLES,GROUP-ID="s",LANGUAGE="en",NAME="English",URI="subs.m3u8"
            #EXT-X-STREAM-INF:BANDWIDTH=100000,CODECS="avc1.64001e",RESOLUTION=160x90,SUBTITLES="s"
            source.m3u8
            """);
        if (format == "dash")
        {
            File.WriteAllText(Path.Combine(source, "sub.ttml"),
                "<tt xmlns=\"http://www.w3.org/ns/ttml\"><head/><body><div>" +
                "<p begin=\"00:00:00.250\" end=\"00:00:01.000\"><span>Hello, VOD</span></p></div></body></tt>");
            master = Path.Combine(source, "master.mpd");
            File.WriteAllText(master, """
                <MPD xmlns="urn:mpeg:dash:schema:mpd:2011" type="static" mediaPresentationDuration="PT6S">
                  <Period duration="PT6S"><AdaptationSet mimeType="video/mp4"><Representation id="v" codecs="avc1.64001e">
                    <SegmentList duration="2"><Initialization sourceURL="init.mp4"/>
                      <SegmentURL media="media-0.m4s"/><SegmentURL media="media-1.m4s"/><SegmentURL media="media-2.m4s"/>
                    </SegmentList>
                  </Representation></AdaptationSet>
                  <AdaptationSet mimeType="text/ttml" lang="en"><Representation id="s"><BaseURL>sub.ttml</BaseURL></Representation></AdaptationSet>
                  </Period>
                </MPD>
                """);
        }
        await Run("dotnet", typeof(CommandInvoker).Assembly.Location, master, "--auto-select", "--skip-merge",
            "--no-config", "--disable-update-check", "--no-log", "--no-ansi-color", "--save-name", "vod",
            "--save-dir", Path.Combine(directory, "out"), "--tmp-dir", Path.Combine(directory, "tmp"),
            "--sub-format", "SRT");
        var recording = Path.Combine(directory, "tmp", "vod");
        Assert.Single(Directory.GetFiles(recording, format == "dash" ? "*.fix.srt" : "*.fix.vtt", SearchOption.AllDirectories));
        var originals = Directory.GetFiles(recording, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
        var output = Path.Combine(directory, "out", "recovered.mkv");
        Assert.Equal(0, await Invoke("mux", "-i", recording, "--muxer", muxer, "-o", output));
        await AssertVideo(output, 6, 150);
        var text = await Run("ffmpeg", "-v", "error", "-i", output, "-map", "0:s:0", "-f", "srt", "-");
        Assert.Contains("00:00:00,250 --> 00:00:01,000", text);
        Assert.Contains("Hello, VOD", text);
        var language = await Run("ffprobe", "-v", "error", "-select_streams", "s", "-show_entries",
            "stream_tags=language", "-of", "csv=p=0", output);
        Assert.Equal("eng", language.Trim());
        foreach (var (path, bytes) in originals)
            Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Empty(Directory.GetDirectories(Path.GetDirectoryName(output)!, ".re-tracks-*"));
    }

    private async Task<string> FragmentedTrack(string name, bool audio)
    {
        var track = Directory.CreateDirectory(Path.Combine(directory, name)).FullName;
        var original = Path.Combine(directory, name + ".mp4");
        await Run("ffmpeg", "-v", "error", "-f", "lavfi", "-i", audio ? "sine=duration=2.8" : "testsrc2=s=160x90:r=25",
            "-t", audio ? "2.8" : "3", "-c:v", "libx264", "-bf", "0", "-g", "25", "-c:a", "aac",
            "-metadata:s:0", "language=eng", "-output_ts_offset", audio ? "1000.2" : "1000",
            "-movflags", "frag_keyframe+empty_moov+default_base_moof+delay_moov", "-frag_duration", "1000000", original);
        var bytes = File.ReadAllBytes(original);
        var start = 0;
        var index = 0;
        for (var offset = 0; offset < bytes.Length;)
        {
            var size = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset));
            if (bytes.AsSpan(offset + 4, 4).SequenceEqual("moof"u8))
            {
                File.WriteAllBytes(Path.Combine(track, index == 0 ? "_init_dec.mp4" : $"{index}_1000_dec.m4s"), bytes[start..offset]);
                start = offset;
                index++;
            }
            offset += size;
        }
        File.WriteAllBytes(Path.Combine(track, $"{index}_1000_dec.m4s"), bytes[start..]);
        // 原始版本也存在时只使用解密副本，不应重复追加 init 或分片。
        File.WriteAllBytes(Path.Combine(track, "_init.mp4"), [99]);
        File.WriteAllBytes(Path.Combine(track, "1_1000.m4s"), [99]);
        return track;
    }

    private static byte[] Init(string handler, string language)
    {
        var mdhd = new byte[24];
        BinaryPrimitives.WriteUInt32BigEndian(mdhd.AsSpan(12), 1000);
        var code = (ushort)(((language[0] - 96) << 10) | ((language[1] - 96) << 5) | (language[2] - 96));
        BinaryPrimitives.WriteUInt16BigEndian(mdhd.AsSpan(20), code);
        var hdlr = new byte[12];
        Encoding.ASCII.GetBytes(handler).CopyTo(hdlr, 8);
        var stsd = new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 }.Concat(Box("stpp", new byte[8])).ToArray();
        return Box("moov", Box("trak", Box("mdia", Box("mdhd", mdhd), Box("hdlr", hdlr),
            Box("minf", Box("stbl", Box("stsd", stsd))))));
    }

    private static byte[] Box(string name, params byte[][] contents)
    {
        var result = new byte[8 + contents.Sum(content => content.Length)];
        BinaryPrimitives.WriteUInt32BigEndian(result, (uint)result.Length);
        Encoding.ASCII.GetBytes(name).CopyTo(result, 4);
        var offset = 8;
        foreach (var content in contents)
        {
            content.CopyTo(result, offset);
            offset += content.Length;
        }
        return result;
    }

    private static async Task<int> Invoke(params string[] args) => await CommandInvoker.CreateRootCommand().Parse(args,
        new ParserConfiguration { EnablePosixBundling = false, ResponseFileTokenReplacer = null }).InvokeAsync();
    public void Dispose() => Directory.Delete(directory, true);
}
