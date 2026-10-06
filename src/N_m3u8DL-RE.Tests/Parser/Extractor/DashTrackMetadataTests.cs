using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Entity;
using N_m3u8DL_RE.Parser.Config;
using N_m3u8DL_RE.Parser.Extractor;
using N_m3u8DL_RE.Tests.TestSupport;
using N_m3u8DL_RE.Util;

namespace N_m3u8DL_RE.Tests.Parser.Extractor;

[Collection("Download console")]
public class DashTrackMetadataTests
{
    [Theory]
    [InlineData("", "English")]
    [InlineData("<Label>   </Label>", "English")]
    [InlineData("<Label> Dialogue Boost: High </Label>", "Dialogue Boost: High")]
    public async Task LabelsUseNearestNonEmptyValueAndSupportNameSelection(string label, string expectedName)
    {
        var stream = Assert.Single(await Parse(Adaptation("audio", "<Label> English </Label>", label)));
        Assert.Equal(expectedName, stream.Name);
        Assert.Contains(expectedName, stream.ToString());
        Assert.Same(stream, Assert.Single(FilterUtil.DoFilterKeep([stream],
            new StreamFilter { NameReg = new("^" + expectedName + "$"), For = "all" })));
    }

    [Fact]
    public async Task MultipleRolesAreDisplayedAndBothRemainSelectable()
    {
        var stream = Assert.Single(await Parse(Adaptation("audio", """
            <Role schemeIdUri="urn:mpeg:dash:role:2011" value="main"/>
            <Role schemeIdUri="urn:mpeg:dash:role:2011" value="description"/>
            <Role schemeIdUri="urn:mpeg:dash:role:2011" value="description"/>
            """)));
        Assert.Equal([RoleType.Main, RoleType.Description], stream.Roles);
        Assert.Equal(RoleType.Description, stream.Role);
        Assert.Contains("Main, Description", stream.ToString());
        foreach (var role in stream.Roles)
        {
            Assert.Same(stream, Assert.Single(FilterUtil.DoFilterKeep([stream], new StreamFilter { Role = role, For = "all" })));
        }
    }

    [Fact]
    public async Task RepresentationAndAdaptationRolesAreCombinedWithoutReadingUnknownSchemes()
    {
        var stream = Assert.Single(await Parse(Adaptation("audio", """
            <Role schemeIdUri="urn:mpeg:dash:role:2011" value="main"/>
            """, """
            <Role schemeIdUri="urn:example:custom" value="subtitle"/>
            <Role value="commentary"/>
            """)));
        Assert.Equal([RoleType.Commentary, RoleType.Main], stream.Roles);
        Assert.Equal(RoleType.Commentary, stream.Role);
        Assert.Equal(MediaType.AUDIO, stream.MediaType);
    }

    [Theory]
    [InlineData("urn:tva:metadata:cs:AudioPurposeCS:2007", "1", RoleType.Description)]
    [InlineData("urn:tva:metadata:cs:AudioPurposeCS:2007", " 1 ", RoleType.Description)]
    [InlineData("urn:tva:metadata:cs:AudioPurposeCS:2007", "2", RoleType.Main)]
    [InlineData("urn:example:custom", "1", RoleType.Main)]
    [InlineData("urn:mpeg:dash:role:2011", "description", RoleType.Description)]
    [InlineData("urn:mpeg:dash:role:2011", " main ", RoleType.Main)]
    [InlineData("urn:example:custom", "description", RoleType.Main)]
    public async Task AudioDescriptionAccessibilityRequiresKnownSchemeAndValue(string scheme, string value, RoleType expectedRole)
    {
        var stream = Assert.Single(await Parse(Adaptation("audio", $"""
            <Role value="main"/>
            <Accessibility schemeIdUri="{scheme}" value="{value}"/>
            """)));
        Assert.Equal(expectedRole, stream.Role);
        Assert.Equal(expectedRole == RoleType.Description, stream.HasRole(RoleType.Description));
    }

    [Fact]
    public async Task DolbyAccessibilityDescriptionKeepsLabelAndAlternateRole()
    {
        // 来自 dash.js Dolby multiAudio.mpd：口述影像用途在 Accessibility 中，Role 仍是 alternate。
        var stream = Assert.Single(await Parse(Adaptation("audio/en/ec-3/3", """
            <Label lang="en">Dolby Digital Plus - Stereo - AudioDescription</Label>
            <Accessibility schemeIdUri="urn:mpeg:dash:role:2011" value="description"/>
            <Role schemeIdUri="urn:mpeg:dash:role:2011" value="alternate"/>
            """)));
        Assert.Equal("Dolby Digital Plus - Stereo - AudioDescription", stream.Name);
        Assert.Equal(RoleType.Description, stream.Role);
        Assert.True(stream.HasRole(RoleType.Alternate));
        Assert.Same(stream, Assert.Single(FilterUtil.DoFilterKeep([stream], new StreamFilter { Role = RoleType.Description, For = "all" })));
    }

    [Theory]
    [InlineData("subtitle", RoleType.Subtitle)]
    [InlineData("forced-subtitle", RoleType.ForcedSubtitle)]
    public async Task SubtitleRoleFollowingMainStillIdentifiesSubtitle(string value, RoleType expectedRole)
    {
        var stream = Assert.Single(await Parse($"""
            <AdaptationSet mimeType="application/ttml+xml">
              <Role value="main"/><Role value="{value}"/>
              <Representation id="sub"><BaseURL>sub.ttml</BaseURL></Representation>
            </AdaptationSet>
            """));
        Assert.Equal(MediaType.SUBTITLES, stream.MediaType);
        Assert.Equal(expectedRole, stream.Role);
        Assert.Equal("ttml", stream.Extension);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VolumeAdjustmentDistinguishesTrackWithoutChangingTemplateUrls(bool inherited)
    {
        var stream = Assert.Single(await Parse(Adaptation("audio", "", "",
            inherited ? "" : "volumeAdjust=\"high\"", inherited ? "volumeAdjust=\"high\"" : "")));
        Assert.Equal("audio-high", stream.GroupId);
        Assert.Equal("high", stream.VolumeAdjust);
        var part = Assert.Single(stream.Playlist!.MediaParts);
        Assert.Equal("audio", part.RepresentationId);
        Assert.Equal("https://example.test/audio-init.mp4", part.MediaInit!.Url);
        Assert.Equal(["https://example.test/audio-1.m4s", "https://example.test/audio-2.m4s"],
            part.MediaSegments.Select(segment => segment.Url));
    }

    [Fact]
    public async Task AudioVariantsStayDistinctInQualityChoicesAndAcrossPeriodsWithChangingIds()
    {
        (string Id, string Label, string Volume)[] variants =
        [
            ("normal", "", ""),
            ("label-high", "Dialogue Boost: High", ""),
            ("label-medium", "Dialogue Boost: Medium", ""),
            ("volume-high", "", "high"),
            ("volume-medium", "", "medium"),
            ("description", "Audio Description", ""),
        ];
        var periods = Enumerable.Range(0, 2).Select(period =>
        {
            var tracks = variants.Select(variant =>
            {
                var metadata = "<Role value=\"main\"/>";
                if (variant.Label.Length > 0)
                    metadata += $"<Label>{variant.Label}</Label>";
                if (variant.Id == "description")
                    metadata += period == 0 ? "<Role value=\"description\"/>" :
                        "<Accessibility schemeIdUri=\"urn:tva:metadata:cs:AudioPurposeCS:2007\" value=\"1\"/>";
                return Adaptation($"{variant.Id}{period}", metadata, "",
                    variant.Volume.Length == 0 ? "" : $"volumeAdjust=\"{variant.Volume}\"");
            });
            return $"<Period duration=\"PT4S\">{string.Join('\n', tracks)}</Period>";
        });
        var streams = await new DASHExtractor2(new ParserConfig { Url = "https://example.test/vod.mpd" }).ExtractStreamsAsync(
            $"<MPD xmlns=\"urn:mpeg:dash:schema:mpd:2011\" type=\"static\" mediaPresentationDuration=\"PT8S\">{string.Join('\n', periods)}</MPD>");
        Assert.Equal(variants.Length, VodPartSelector.QualityChoices(streams).Count);
        var seeds = streams.Where(stream => stream.Playlist!.MediaParts[0].PeriodIndex == 0).ToList();
        var plans = VodStreamPlanner.Build(streams, seeds);
        Assert.Equal(variants.Length, plans.Count);
        for (var i = 0; i < variants.Length; i++)
        {
            var parts = plans[i].Playlist!.MediaParts;
            Assert.Equal(2, parts.Count);
            // 相同语言、码率、编码不能使增强级别或口述影像轨道相互串接。
            Assert.Equal([$"{variants[i].Id}0", $"{variants[i].Id}1"], parts.Select(part => part.RepresentationId));
            Assert.Equal(8, plans[i].Playlist!.TotalDuration);
            Assert.Equal($"https://example.test/{variants[i].Id}1-init.mp4", parts[1].MediaInit!.Url);
        }
        var descriptions = FilterUtil.DoFilterKeep(streams, new StreamFilter { Role = RoleType.Description, For = "all" });
        var descriptionPlan = Assert.Single(VodStreamPlanner.Build(streams, descriptions,
            audioFilter: new StreamFilter { Role = RoleType.Description, For = "all" }));
        Assert.Equal(2, descriptionPlan.Playlist!.MediaParts.Count);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("main, description")]
    public async Task InvalidRoleValuesDoNotBecomeEnumNumbersOrFlags(string value)
    {
        var stream = Assert.Single(await Parse(Adaptation("audio", $"<Role value=\"{value}\"/>")));
        Assert.Empty(stream.Roles);
        Assert.Null(stream.Role);
        Assert.Equal(MediaType.AUDIO, stream.MediaType);
    }

    [Theory]
    [InlineData("English", "")]
    [InlineData("", "English")]
    public async Task MissingLabelDoesNotTruncateOtherwiseMatchingAudio(string firstLabel, string secondLabel)
    {
        var streams = await ParsePeriods(
            Adaptation("audio0", firstLabel.Length == 0 ? "" : $"<Label>{firstLabel}</Label>"),
            Adaptation("audio1", secondLabel.Length == 0 ? "" : $"<Label>{secondLabel}</Label>"));
        var plan = Assert.Single(VodStreamPlanner.Build(streams, [streams[0]]));
        Assert.Equal(2, plan.Playlist!.MediaParts.Count);
        Assert.Equal(8, plan.Playlist.TotalDuration);
    }

    [Fact]
    public async Task MissingLabelDoesNotMixVariantsAfterFiltering()
    {
        var streams = await ParsePeriods(
            Adaptation("normal0", "<Label>English</Label>") + Adaptation("high0", "<Label>Boost High</Label>"),
            Adaptation("normal1") + Adaptation("high1", "<Label>Boost High</Label>"));
        var source = VodPartSelector.SnapshotStreams(streams);
        var high = streams.Where(s => s.Name == "Boost High").ToList();
        var highPlan = Assert.Single(VodStreamPlanner.Build(streams, high, sourceStreams: source));
        Assert.Equal(["high0", "high1"], highPlan.Playlist!.MediaParts.Select(p => p.RepresentationId));
        // 后续 Label 缺失且源中存在不同命名的音轨时，不能因为过滤掉其中一条就猜测身份。
        var normalPlan = Assert.Single(VodStreamPlanner.Build([streams[0], streams[2]], [streams[0]], sourceStreams: source));
        Assert.Single(normalPlan.Playlist!.MediaParts);
    }

    [Fact]
    public async Task NameFilterStillExcludesUnlabelledPeriods()
    {
        var streams = await ParsePeriods(Adaptation("audio0", "<Label>English</Label>"), Adaptation("audio1"));
        var filter = new StreamFilter { NameReg = new("^English$"), For = "all" };
        var selected = FilterUtil.DoFilterKeep(streams, filter);
        var plan = Assert.Single(VodStreamPlanner.Build(streams, selected, audioFilter: filter));
        Assert.Single(plan.Playlist!.MediaParts);
    }

    [Fact]
    public async Task UnlabelledSeedDoesNotJoinDifferentLaterNamedVariants()
    {
        string[] tracks =
        [
            Adaptation("audio0"),
            Adaptation("audio1", "<Label>Boost High</Label>"),
            Adaptation("audio2", "<Label>Boost Medium</Label>"),
        ];
        var periods = tracks.Select(track => $"<Period duration=\"PT4S\">{track}</Period>");
        var streams = await new DASHExtractor2(new ParserConfig { Url = "https://example.test/vod.mpd" }).ExtractStreamsAsync(
            $"<MPD xmlns=\"urn:mpeg:dash:schema:mpd:2011\" type=\"static\" mediaPresentationDuration=\"PT12S\">{string.Join('\n', periods)}</MPD>");
        var plan = Assert.Single(VodStreamPlanner.Build(streams, [streams[0]]));
        Assert.Equal(["audio0", "audio1"], plan.Playlist!.MediaParts.Select(p => p.RepresentationId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LiveRefreshKeepsStableTrackWhenItsLabelAppears(bool withSharedInit)
    {
        var root = Directory.CreateTempSubdirectory("re-dash-label-refresh-").FullName;
        try
        {
            string Manifest(int version) => $$"""
                <MPD xmlns="urn:mpeg:dash:schema:mpd:2011" type="dynamic">
                  <Period><AdaptationSet mimeType="audio/mp4" lang="en">
                    <Representation id="normal" bandwidth="64000" codecs="mp4a.40.2">
                      <SegmentList duration="2">{{(withSharedInit ? "<Initialization sourceURL=\"shared-init.mp4\"/>" : "")}}<SegmentURL media="normal-{{version}}.m4s"/></SegmentList>
                    </Representation>
                    <Representation id="boost" bandwidth="64000" codecs="mp4a.40.2">
                      {{(version == 0 ? "" : version == 1 ? "<Label>Boost High</Label>" : "<Label>Boost Medium</Label>")}}
                      <SegmentList duration="2">{{(withSharedInit ? "<Initialization sourceURL=\"shared-init.mp4\"/>" : "")}}<SegmentURL media="boost-{{version}}.m4s"/></SegmentList>
                    </Representation>
                  </AdaptationSet></Period>
                </MPD>
                """;
            await using var server = new MediaFixtureServer(root, (path, version) => path == "vod.mpd" ? Manifest(version) : null);
            var extractor = new DASHExtractor2(new ParserConfig { Url = server.Url + "vod.mpd" });
            var streams = await extractor.ExtractStreamsAsync(Manifest(0));
            var boost = streams.Single(stream => stream.GroupId == "boost");
            await extractor.RefreshPlayListAsync([boost]);
            await extractor.RefreshPlayListAsync([boost]);
            Assert.EndsWith("boost-1.m4s", Assert.Single(boost.Playlist!.MediaParts[0].MediaSegments).Url);
            Assert.Equal("Boost High", boost.Name);
            await extractor.RefreshPlayListAsync([boost]);
            Assert.EndsWith("boost-1.m4s", Assert.Single(boost.Playlist!.MediaParts[0].MediaSegments).Url);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task RoleOrderDoesNotChangeTrackIdentityButDifferentRoleSetsStayDistinct()
    {
        const string alternate = "<Role value=\"alternate\"/>";
        const string commentary = "<Role value=\"commentary\"/>";
        var streams = await ParsePeriods(Adaptation("audio0", alternate + commentary), Adaptation("audio1", commentary + alternate));
        Assert.Single(VodPartSelector.QualityChoices(streams));
        Assert.Equal(2, Assert.Single(VodStreamPlanner.Build(streams, [streams[0]])).Playlist!.MediaParts.Count);

        streams = await ParsePeriods(Adaptation("audio0", alternate + commentary), Adaptation("audio1", alternate));
        Assert.Equal(2, VodPartSelector.QualityChoices(streams).Count);
        Assert.Single(Assert.Single(VodStreamPlanner.Build(streams, [streams[0]])).Playlist!.MediaParts);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public async Task LiveInitFallbackDoesNotGuessTrackWhenIdsChangeAndLabelWasMissing(bool knownLabel, bool sharedInit)
    {
        var root = Directory.CreateTempSubdirectory("re-dash-init-identity-").FullName;
        try
        {
            string Manifest(int version) => $$"""
                <MPD xmlns="urn:mpeg:dash:schema:mpd:2011" type="dynamic">
                  <Period><AdaptationSet mimeType="audio/mp4" lang="en">
                    <Representation id="normal-{{version}}" bandwidth="64000" codecs="mp4a.40.2">
                      <SegmentList duration="2"><Initialization sourceURL="{{(sharedInit ? "shared-init.mp4" : "normal-init.mp4")}}"/><SegmentURL media="normal-{{version}}.m4s"/></SegmentList>
                    </Representation>
                    <Representation id="boost-{{version}}" bandwidth="64000" codecs="mp4a.40.2">
                      {{(knownLabel || version > 0 ? "<Label>Boost High</Label>" : "")}}
                      <SegmentList duration="2"><Initialization sourceURL="{{(sharedInit ? "shared-init.mp4" : "boost-init.mp4")}}"/><SegmentURL media="boost-{{version}}.m4s"/></SegmentList>
                    </Representation>
                  </AdaptationSet></Period>
                </MPD>
                """;
            await using var server = new MediaFixtureServer(root, (path, version) => path == "vod.mpd" ? Manifest(version) : null);
            var extractor = new DASHExtractor2(new ParserConfig { Url = server.Url + "vod.mpd" });
            var streams = await extractor.ExtractStreamsAsync(Manifest(0));
            var boost = streams.Single(stream => stream.GroupId == "boost-0");
            await extractor.RefreshPlayListAsync([boost]);
            await extractor.RefreshPlayListAsync([boost]);
            // 已知 Label 或独立 init 可以确定身份；未知 Label 且共享 init 时不能猜普通音频。
            Assert.EndsWith(knownLabel || !sharedInit ? "boost-1.m4s" : "boost-0.m4s", Assert.Single(boost.Playlist!.MediaParts[0].MediaSegments).Url);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task LiveRefreshDoesNotSwitchVolumeVariant(bool sameBandwidth, bool missingLabel, bool withoutInit)
    {
        var root = Directory.CreateTempSubdirectory("re-dash-audio-refresh-").FullName;
        try
        {
            string Manifest(int version) => $$"""
                <MPD xmlns="urn:mpeg:dash:schema:mpd:2011" type="dynamic">
                  <Period><AdaptationSet mimeType="audio/mp4" lang="en">
                    <Role value="main"/>
                    <Representation id="audio" bandwidth="{{64000 + version}}" codecs="mp4a.40.2">
                      <SegmentList duration="2">{{(withoutInit ? "" : "<Initialization sourceURL=\"shared-init.mp4\"/>")}}<SegmentURL media="normal-{{version}}.m4s"/></SegmentList>
                    </Representation>
                    <Representation id="audio" volumeAdjust="high" bandwidth="{{(sameBandwidth ? 64000 : 96000) + version}}" codecs="mp4a.40.2">
                      {{(missingLabel && version == 0 ? "<Label>Boost High</Label>" : "")}}
                      <SegmentList duration="2">{{(withoutInit ? "" : "<Initialization sourceURL=\"shared-init.mp4\"/>")}}<SegmentURL media="high-{{version}}.m4s"/></SegmentList>
                    </Representation>
                  </AdaptationSet></Period>
                </MPD>
                """;
            await using var server = new MediaFixtureServer(root, (path, version) => path == "vod.mpd" ? Manifest(version) : null);
            var extractor = new DASHExtractor2(new ParserConfig { Url = server.Url + "vod.mpd" });
            var streams = await extractor.ExtractStreamsAsync(Manifest(0));
            var high = streams.Single(stream => stream.VolumeAdjust == "high");
            // 首次 HTTP 请求使用版本 0；第二次刷新改变码率，验证刷新不会切换音轨。
            await extractor.RefreshPlayListAsync([high]);
            await extractor.RefreshPlayListAsync([high]);
            Assert.EndsWith("high-1.m4s", Assert.Single(high.Playlist!.MediaParts[0].MediaSegments).Url);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static Task<List<StreamSpec>> ParsePeriods(string first, string second) =>
        new DASHExtractor2(new ParserConfig { Url = "https://example.test/vod.mpd" }).ExtractStreamsAsync(
            $"<MPD xmlns=\"urn:mpeg:dash:schema:mpd:2011\" type=\"static\" mediaPresentationDuration=\"PT8S\"><Period duration=\"PT4S\">{first}</Period><Period duration=\"PT4S\">{second}</Period></MPD>");

    private static string Adaptation(string id, string metadata = "", string representationMetadata = "",
        string representationAttributes = "", string adaptationAttributes = "") => $$"""
        <AdaptationSet mimeType="audio/mp4" lang="en" {{adaptationAttributes}}>
          {{metadata}}
          <Representation id="{{id}}" bandwidth="64000" codecs="mp4a.40.2" {{representationAttributes}}>
            {{representationMetadata}}
            <SegmentTemplate timescale="1" duration="2" initialization="$RepresentationID$-init.mp4" media="$RepresentationID$-$Number$.m4s"/>
          </Representation>
        </AdaptationSet>
        """;

    private static Task<List<StreamSpec>> Parse(string adaptations) =>
        new DASHExtractor2(new ParserConfig { Url = "https://example.test/vod.mpd" }).ExtractStreamsAsync(
            $"<MPD xmlns=\"urn:mpeg:dash:schema:mpd:2011\" type=\"static\" mediaPresentationDuration=\"PT4S\"><Period duration=\"PT4S\">{adaptations}</Period></MPD>");
}
