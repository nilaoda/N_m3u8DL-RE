﻿namespace N_m3u8DL_RE.Entity
{
    internal sealed class SpeedContainer
    {
        public bool SingleSegment { get; set; }
        public long NowSpeed { get; set; }  // 当前每秒速度
        public long SpeedLimit { get; set; } = long.MaxValue; // 限速设置
        public long? ResponseLength { get; set; }
        public long RDownloaded => _Rdownloaded;
        private int _zeroSpeedCount;
        public int LowSpeedCount => _zeroSpeedCount;
        public bool ShouldStop => LowSpeedCount >= 20;

        ///////////////////////////////////////////////////

        private long _downloaded;
        private long _Rdownloaded;
        public long Downloaded => _downloaded;

        public int AddLowSpeedCount()
        {
            return Interlocked.Add(ref _zeroSpeedCount, 1);
        }

        public int ResetLowSpeedCount()
        {
            return Interlocked.Exchange(ref _zeroSpeedCount, 0);
        }

        public long Add(long size)
        {
            _ = Interlocked.Add(ref _Rdownloaded, size);
            return Interlocked.Add(ref _downloaded, size);
        }

        public void Reset()
        {
            _ = Interlocked.Exchange(ref _downloaded, 0);
        }

        public void ResetVars()
        {
            Reset();
            _ = ResetLowSpeedCount();
            SingleSegment = false;
            ResponseLength = null;
            _Rdownloaded = 0L;
        }
    }
}