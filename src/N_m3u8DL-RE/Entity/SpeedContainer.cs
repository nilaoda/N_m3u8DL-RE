using NiL.JS.Statements;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace N_m3u8DL_RE.Entity
{
    internal class SpeedContainer
    {
        public bool SingleSegment { get; set; } = false;
        public long? ResponseLength { get; set; }
        public long RDownloaded { get; set; } = 0L;

        ///////////////////////////////////////////////////

        private long _downloaded = 0;
        public long Downloaded { get => _downloaded; }

        public long Add(long size)
        {
            if (SingleSegment) RDownloaded += size;
            return Interlocked.Add(ref _downloaded, size);
        }

        public void Reset()
        {
            Interlocked.Exchange(ref _downloaded, 0);
        }

        public void ResetVars()
        {
            Reset();
            SingleSegment = false;
            ResponseLength = null;
            RDownloaded = 0L;
        }
    }
}
