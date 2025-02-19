using System.Reflection;
using FluentAssertions;
using N_m3u8DL_RE.Parser.Config;
using N_m3u8DL_RE.Parser.Extractor;


namespace N_m3u8DL_RE.Parser.Tests.Extractor;

[TestClass]
public class MSSExtractorTest
{
    [TestMethod]
    public void TestMethod1()
    {
        var uri = getResourceUri("Extractor/SuperSpeedway_720.ism.manifest.xml");
        var rawText = File.ReadAllText(uri.LocalPath);
        var parserConfig = new ParserConfig
        {
            Url = uri.AbsoluteUri,
            OriginalUrl = uri.AbsoluteUri
        };
        var extractor = new MSSExtractor(parserConfig);
        var streamSpecs = extractor.ExtractStreamsAsync(rawText).Result;
        streamSpecs.Should().HaveCount(9);
        var streamSpec0 = streamSpecs[0];
        streamSpec0.PeriodId.Should().Be("0");
        streamSpec0.GroupId.Should().Be("video");
        streamSpec0.Bandwidth.Should().Be(2962000);
        streamSpec0.Codecs.Should().Be("avc1.64001F");
        streamSpec0.Resolution.Should().Be("1280x720");
        streamSpec0.Channels.Should().Be(null);
        streamSpec0.MSSData!.SamplingRate.Should().Be(48000);
        streamSpec0.MSSData.BitsPerSample.Should().Be(16);
        streamSpec0.MSSData.NalUnitLengthField.Should().Be(4);
        streamSpec0.MSSData.Duration.Should().Be(1209510000L);
        streamSpec0.MSSData.Timesacle.Should().Be(10000000);
        streamSpec0.Playlist!.IsLive.Should().BeFalse();
        streamSpec0.Playlist!.MediaParts[0].MediaSegments.Should().HaveCount(61);

        var streamSpec1 = streamSpecs[1];
        streamSpec1.PeriodId.Should().Be("1");
        streamSpec1.GroupId.Should().Be("video");
        streamSpec1.Bandwidth.Should().Be(2056000);
        streamSpec1.Codecs.Should().Be("avc1.64001F");
        streamSpec1.Resolution.Should().Be("992x560");
        streamSpec1.Channels.Should().Be(null);
        streamSpec1.MSSData!.SamplingRate.Should().Be(48000);
        streamSpec1.MSSData.BitsPerSample.Should().Be(16);
        streamSpec1.MSSData.NalUnitLengthField.Should().Be(4);
        streamSpec1.MSSData.Duration.Should().Be(1209510000L);
        streamSpec1.MSSData.Timesacle.Should().Be(10000000);

        var streamSpec5 = streamSpecs[5];
        streamSpec5.PeriodId.Should().Be("5");
        streamSpec5.GroupId.Should().Be("video");
        streamSpec5.Bandwidth.Should().Be(477000);
        streamSpec5.Codecs.Should().Be("avc1.64000D");
        streamSpec5.Resolution.Should().Be("368x208");
        streamSpec5.Channels.Should().Be(null);
        streamSpec5.MSSData!.SamplingRate.Should().Be(48000);
        streamSpec5.MSSData.BitsPerSample.Should().Be(16);
        streamSpec5.MSSData.NalUnitLengthField.Should().Be(4);
        streamSpec5.MSSData.Duration.Should().Be(1209510000L);
        streamSpec5.MSSData.Timesacle.Should().Be(10000000);
        
        var streamSpec8 = streamSpecs[8];
        streamSpec8.PeriodId.Should().Be(null);
        streamSpec8.GroupId.Should().Be("audio");
        streamSpec8.Bandwidth.Should().Be(128000);
        streamSpec8.Codecs.Should().Be("mp4a.40.2");
        streamSpec8.Resolution.Should().Be(null);
        streamSpec8.Channels.Should().Be("2");
        streamSpec8.MSSData!.SamplingRate.Should().Be(44100);
        streamSpec8.MSSData.BitsPerSample.Should().Be(16);
        streamSpec8.MSSData.NalUnitLengthField.Should().Be(4);
        streamSpec8.MSSData.Duration.Should().Be(1209510000L);
        streamSpec8.MSSData.Timesacle.Should().Be(10000000);
    }

    private Uri getResourceUri(string resourceName)
    {
        var directory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        var path = System.IO.Path.Combine(directory, resourceName);
        return new Uri("file://" + path);
    }
}