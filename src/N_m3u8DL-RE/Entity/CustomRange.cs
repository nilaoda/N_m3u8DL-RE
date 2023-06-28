using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace N_m3u8DL_RE.Entity
{
    public class CustomRange
    {
        public required string InputStr { get; set; }
        public double? StartSec { get; set; }
        public double? EndSec { get; set; }

        public long? StartSegIndex { get; set; }
        public long? EndSegIndex { get; set;}

        public override string? ToString()
        {
            return $"StartSec: {StartSec}, EndSec: {EndSec}, StartSegIndex: {StartSegIndex}, EndSegIndex: {EndSegIndex}";
        }
    }
}
