using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Spectre.Console;

namespace N_m3u8DL_RE.Entity
{
    internal class Mediainfo
    {
        public string? Id { get; set; }
        public string? Text { get; set; }
        public string? BaseInfo { get; set; }
        public string? Bitrate { get; set; }
        public string? Resolution { get; set; }
        public string? Fps { get; set; }
        public string? Type { get; set; }
        public TimeSpan? StartTime { get; set; }
        public bool DolbyVison { get; set; }
        public bool HDR { get; set; }

        public override string? ToString()
        {
            return $"{(string.IsNullOrEmpty(Id) ? "NaN" : Id)}: " + string.Join(", ", new List<string?> { Type, BaseInfo, Resolution, Fps, Bitrate }.Where(i => !string.IsNullOrEmpty(i)));
        }

        public string ToStringMarkUp()
        {
            return "[steelblue]" + ToString().EscapeMarkup() + ((HDR && !DolbyVison) ? " [darkorange3_1][[HDR]][/]" : "") + (DolbyVison ? " [darkorange3_1][[DOVI]][/]" : "") + "[/]";
        }
    }
}
