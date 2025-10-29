﻿using N_m3u8DL_RE.Common.Enum;
using System.Text;
using System.Text.RegularExpressions;

namespace N_m3u8DL_RE.Entity;

public class StreamFilter
{
    public Regex? GroupIdReg { get; set; }
    public Regex? LanguageReg { get; set; }
    public Regex? NameReg { get; set; }
    public Regex? CodecsReg { get; set; }
    public Regex? ResolutionReg { get; set; }
    public Regex? FrameRateReg { get; set; }
    public Regex? ChannelsReg { get; set; }
    public Regex? VideoRangeReg { get; set; }
    public Regex? UrlReg { get; set; }
    public Regex? PeriodReg { get; set; }
    public long? SegmentsMinCount { get; set; }
    public long? SegmentsMaxCount { get; set; }
    public double? PlaylistMinDur {  get; set; }
    public double? PlaylistMaxDur {  get; set; }
    public int? BandwidthMin { get; set; }
    public int? BandwidthMax { get; set; }
    public RoleType? Role { get; set; }

    public string For { get; set; } = "best";

    public override string? ToString()
    {
        var sb = new StringBuilder();

        if (GroupIdReg != null) sb.Append($"GroupIdReg: {GroupIdReg} ");
        if (LanguageReg != null) sb.Append($"LanguageReg: {LanguageReg} ");
        if (NameReg != null) sb.Append($"NameReg: {NameReg} ");
        if (CodecsReg != null) sb.Append($"CodecsReg: {CodecsReg} ");
        if (ResolutionReg != null) sb.Append($"ResolutionReg: {ResolutionReg} ");
        if (FrameRateReg != null) sb.Append($"FrameRateReg: {FrameRateReg} ");
        if (ChannelsReg != null) sb.Append($"ChannelsReg: {ChannelsReg} ");
        if (VideoRangeReg != null) sb.Append($"VideoRangeReg: {VideoRangeReg} ");
        if (UrlReg != null) sb.Append($"UrlReg: {UrlReg} ");
        if (PeriodReg != null) sb.Append($"PeriodReg: {PeriodReg} ");
        if (SegmentsMinCount != null) sb.Append($"SegmentsMinCount: {SegmentsMinCount} ");
        if (SegmentsMaxCount != null) sb.Append($"SegmentsMaxCount: {SegmentsMaxCount} ");
        if (PlaylistMinDur != null) sb.Append($"PlaylistMinDur: {PlaylistMinDur} ");
        if (PlaylistMaxDur != null) sb.Append($"PlaylistMaxDur: {PlaylistMaxDur} ");
        if (BandwidthMin != null) sb.Append($"{nameof(BandwidthMin)}: {BandwidthMin} ");
        if (BandwidthMax != null) sb.Append($"{nameof(BandwidthMax)}: {BandwidthMax} ");
        if (Role.HasValue) sb.Append($"Role: {Role} ");

        return sb + $"For: {For}";
    }
}