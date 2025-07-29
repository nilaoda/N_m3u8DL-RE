using System.Text;
using System.Text.RegularExpressions;

using N_m3u8DL_RE.Common.CommonEnumerations;

namespace N_m3u8DL_RE.Entity
{
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
        public long? SegmentsMinCount { get; set; }
        public long? SegmentsMaxCount { get; set; }
        public double? PlaylistMinDur { get; set; }
        public double? PlaylistMaxDur { get; set; }
        public int? BandwidthMin { get; set; }
        public int? BandwidthMax { get; set; }
        public RoleType? Role { get; set; }

        public string For { get; set; } = "best";

        public override string? ToString()
        {
            StringBuilder sb = new();

            if (GroupIdReg != null)
            {
                _ = sb.Append($"GroupIdReg: {GroupIdReg} ");
            }

            if (LanguageReg != null)
            {
                _ = sb.Append($"LanguageReg: {LanguageReg} ");
            }

            if (NameReg != null)
            {
                _ = sb.Append($"NameReg: {NameReg} ");
            }

            if (CodecsReg != null)
            {
                _ = sb.Append($"CodecsReg: {CodecsReg} ");
            }

            if (ResolutionReg != null)
            {
                _ = sb.Append($"ResolutionReg: {ResolutionReg} ");
            }

            if (FrameRateReg != null)
            {
                _ = sb.Append($"FrameRateReg: {FrameRateReg} ");
            }

            if (ChannelsReg != null)
            {
                _ = sb.Append($"ChannelsReg: {ChannelsReg} ");
            }

            if (VideoRangeReg != null)
            {
                _ = sb.Append($"VideoRangeReg: {VideoRangeReg} ");
            }

            if (UrlReg != null)
            {
                _ = sb.Append($"UrlReg: {UrlReg} ");
            }

            if (SegmentsMinCount != null)
            {
                _ = sb.Append($"SegmentsMinCount: {SegmentsMinCount} ");
            }

            if (SegmentsMaxCount != null)
            {
                _ = sb.Append($"SegmentsMaxCount: {SegmentsMaxCount} ");
            }

            if (PlaylistMinDur != null)
            {
                _ = sb.Append($"PlaylistMinDur: {PlaylistMinDur} ");
            }

            if (PlaylistMaxDur != null)
            {
                _ = sb.Append($"PlaylistMaxDur: {PlaylistMaxDur} ");
            }

            if (BandwidthMin != null)
            {
                _ = sb.Append($"{nameof(BandwidthMin)}: {BandwidthMin} ");
            }

            if (BandwidthMax != null)
            {
                _ = sb.Append($"{nameof(BandwidthMax)}: {BandwidthMax} ");
            }

            if (Role.HasValue)
            {
                _ = sb.Append($"Role: {Role} ");
            }

            return sb + $"For: {For}";
        }
    }
}