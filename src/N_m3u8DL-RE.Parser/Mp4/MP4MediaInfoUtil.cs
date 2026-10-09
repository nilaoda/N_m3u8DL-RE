namespace Mp4SubtitleParser;

public static class MP4MediaInfoUtil
{
    private const int MaxMetadataSize = 64 * 1024 * 1024;

    private sealed class Track
    {
        public uint Id { get; set; }
        public uint Timescale { get; set; }
        public string Handler { get; set; } = "";
        public uint DefaultDuration { get; set; }
        public List<(uint Count, uint Duration)> Durations { get; } = [];
        public List<(uint Count, long Offset)> Compositions { get; } = [];
        public List<(ulong Duration, long MediaTime)> Edits { get; } = [];
        public double Start { get; set; } = double.PositiveInfinity;
        public double End { get; set; } = double.NegativeInfinity;
        public double DecodeEnd { get; set; }
        public bool Invalid { get; set; }
    }

    public static (double Start, double Duration)? ReadTiming(string file, CancellationToken token = default)
    {
        using var stream = File.OpenRead(file);
        using var reader = new BinaryReader2(stream);
        var fragments = new List<long>();
        var moov = ReadMovie(reader, token, fragments);
        if (moov == null)
            return null;
        var tracks = new List<Track>();
        uint movieTimescale = 0;
        var defaults = new Dictionary<uint, uint>();
        new MP4Parser()
            .Box("moov", MP4Parser.Children)
            .FullBox("mvhd", box =>
            {
                if (box.Version is 0 or 1)
                    movieTimescale = MP4Parser.ParseMDHD(box.Reader, box.Version);
            })
            .Box("trak", box => tracks.Add(ReadTrack(box, token)))
            .Box("mvex", MP4Parser.Children)
            .FullBox("trex", box =>
            {
                var id = box.Reader.ReadUInt32();
                box.Reader.ReadUInt32(); // default_sample_description_index
                defaults[id] = box.Reader.ReadUInt32();
            })
            .Parse(moov);
        foreach (var track in tracks)
        {
            if (defaults.TryGetValue(track.Id, out var duration))
                track.DefaultDuration = duration;
            ReadSamples(track, token);
        }
        foreach (var position in fragments)
        {
            token.ThrowIfCancellationRequested();
            stream.Position = position;
            var (_, size) = ReadHeader(reader);
            if (size > MaxMetadataSize)
                return null;
            stream.Position = position;
            ReadFragment(reader.ReadBytes((int)size), tracks, token);
        }
        var intervals = new List<(double Start, double End)>();
        foreach (var track in tracks.Where(t => t.Handler is "vide" or "soun"))
        {
            if (track.Invalid || track.Timescale == 0 || !double.IsFinite(track.Start) || !double.IsFinite(track.End))
                return null;
            if (track.Edits.Count == 0)
            {
                intervals.Add((track.Start / track.Timescale, track.End / track.Timescale));
                continue;
            }
            if (movieTimescale == 0)
                return null;
            double elapsed = 0;
            foreach (var edit in track.Edits)
            {
                var duration = edit.Duration / (double)movieTimescale;
                if (edit.MediaTime >= 0)
                {
                    // elst 的空白段用于广播源时钟，media_time 则可能裁掉编码预滚。
                    var start = Math.Max(track.Start, edit.MediaTime);
                    var end = Math.Min(track.End, edit.MediaTime + duration * track.Timescale);
                    if (end > start)
                        intervals.Add((elapsed + (start - edit.MediaTime) / track.Timescale,
                            elapsed + (end - edit.MediaTime) / track.Timescale));
                }
                else if (edit.MediaTime != -1)
                {
                    return null;
                }
                elapsed += duration;
            }
        }
        if (intervals.Count == 0)
            return null;
        var origin = intervals.Min(i => i.Start);
        var endTime = intervals.Max(i => i.End);
        return endTime > origin ? (origin, endTime - origin) : null;
    }

    public static string[]? ReadTrackTypes(string file, CancellationToken token = default)
    {
        using var stream = File.OpenRead(file);
        using var reader = new BinaryReader2(stream);
        var moov = ReadMovie(reader, token);
        if (moov == null)
            return null;
        var types = new List<string>();
        new MP4Parser()
            .Box("moov", MP4Parser.Children)
            .Box("trak", MP4Parser.Children)
            .Box("mdia", MP4Parser.Children)
            .FullBox("hdlr", box =>
            {
                token.ThrowIfCancellationRequested();
                box.Reader.ReadUInt32();
                var handler = MP4Parser.TypeToString(box.Reader.ReadUInt32());
                var type = handler switch
                {
                    "vide" => "Video",
                    "soun" => "Audio",
                    "subt" or "sbtl" or "text" or "clcp" => "Subtitle",
                    _ => null
                };
                if (type != null)
                    types.Add(type);
            })
            .Parse(moov);
        return types.ToArray();
    }

    private static byte[]? ReadMovie(BinaryReader2 reader, CancellationToken token, List<long>? fragments = null)
    {
        var stream = reader.BaseStream;
        byte[]? moov = null;
        while (stream.Position + 8 <= stream.Length)
        {
            token.ThrowIfCancellationRequested();
            var position = stream.Position;
            var (type, size) = ReadHeader(reader);
            if (size < stream.Position - position || size > stream.Length - position)
                return null;
            if (type == "moov")
            {
                if (size > MaxMetadataSize)
                    return null;
                stream.Position = position;
                var data = reader.ReadBytes((int)size);
                // 直播分片拼接可能重复写入相同初始化信息，后续 moof 仍使用同一套轨道和时钟。
                // 初始化信息不同则不能共用轨道定义，保留无法确定时间轴的处理。
                if (moov != null && !moov.AsSpan().SequenceEqual(data))
                    return null;
                moov ??= data;
            }
            else if (type == "moof")
            {
                fragments?.Add(position);
            }
            // mdat 只 seek 跳过，不把完整媒体文件读入内存。
            stream.Position = position + size;
        }
        return moov;
    }

    private static (string Type, long Size) ReadHeader(BinaryReader2 reader)
    {
        var position = reader.BaseStream.Position;
        long size = reader.ReadUInt32();
        var type = MP4Parser.TypeToString(reader.ReadUInt32());
        if (size == 1)
            size = checked((long)reader.ReadUInt64());
        else if (size == 0)
            size = reader.BaseStream.Length - position;
        return (type, size);
    }

    private static Track ReadTrack(ParsedBox box, CancellationToken token)
    {
        var track = new Track();
        var data = box.Reader.ReadBytes((int)box.Reader.GetLength());
        new MP4Parser()
            .FullBox("tkhd", header =>
            {
                if (header.Version is not (0 or 1))
                {
                    track.Invalid = true;
                    return;
                }
                header.Reader.ReadBytes(header.Version == 1 ? 16 : 8);
                track.Id = header.Reader.ReadUInt32();
            })
            .Box("mdia", MP4Parser.Children)
            .FullBox("mdhd", header =>
            {
                if (header.Version is 0 or 1)
                    track.Timescale = MP4Parser.ParseMDHD(header.Reader, header.Version);
            })
            .FullBox("hdlr", header =>
            {
                header.Reader.ReadUInt32();
                track.Handler = MP4Parser.TypeToString(header.Reader.ReadUInt32());
            })
            .Box("minf", MP4Parser.Children)
            .Box("stbl", MP4Parser.Children)
            .FullBox("stts", header =>
            {
                var count = header.Reader.ReadUInt32();
                for (var i = 0u; i < count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    track.Durations.Add((header.Reader.ReadUInt32(), header.Reader.ReadUInt32()));
                }
            })
            .FullBox("ctts", header =>
            {
                if (header.Version is not (0 or 1))
                {
                    track.Invalid = true;
                    return;
                }
                var count = header.Reader.ReadUInt32();
                for (var i = 0u; i < count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    var samples = header.Reader.ReadUInt32();
                    var offset = header.Version == 1 ? (long)header.Reader.ReadInt32() : header.Reader.ReadUInt32();
                    track.Compositions.Add((samples, offset));
                }
            })
            .Box("edts", MP4Parser.Children)
            .FullBox("elst", header =>
            {
                if (header.Version is not (0 or 1))
                {
                    track.Invalid = true;
                    return;
                }
                var count = header.Reader.ReadUInt32();
                for (var i = 0u; i < count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    var duration = header.Version == 1 ? header.Reader.ReadUInt64() : header.Reader.ReadUInt32();
                    var mediaTime = header.Version == 1 ? header.Reader.ReadInt64() : header.Reader.ReadInt32();
                    var rate = header.Reader.ReadInt16();
                    var fraction = header.Reader.ReadInt16();
                    if (rate != 1 || fraction != 0)
                        track.Invalid = true;
                    track.Edits.Add((duration, mediaTime));
                }
            })
            .Parse(data);
        return track;
    }

    private static void ReadSamples(Track track, CancellationToken token)
    {
        var compositionIndex = 0;
        uint compositionRemaining = 0;
        long offset = 0;
        double decode = 0;
        foreach (var entry in track.Durations)
        {
            var remaining = entry.Count;
            while (remaining > 0)
            {
                token.ThrowIfCancellationRequested();
                if (track.Compositions.Count > 0 && compositionRemaining == 0)
                {
                    if (compositionIndex >= track.Compositions.Count)
                    {
                        track.Invalid = true;
                        return;
                    }
                    var composition = track.Compositions[compositionIndex++];
                    compositionRemaining = composition.Count;
                    offset = composition.Offset;
                    if (compositionRemaining == 0)
                    {
                        track.Invalid = true;
                        return;
                    }
                }
                var count = track.Compositions.Count == 0 ? remaining : Math.Min(remaining, compositionRemaining);
                track.Start = Math.Min(track.Start, decode + offset);
                decode += count * (double)entry.Duration;
                track.End = Math.Max(track.End, decode + offset);
                remaining -= count;
                compositionRemaining -= track.Compositions.Count == 0 ? 0 : count;
            }
        }
        if (compositionRemaining != 0 || compositionIndex < track.Compositions.Count)
            track.Invalid = true;
        track.DecodeEnd = decode;
    }

    private static void ReadFragment(byte[] data, List<Track> tracks, CancellationToken token)
    {
        new MP4Parser()
            .Box("moof", MP4Parser.Children)
            .Box("traf", box =>
            {
                TFHD? header = null;
                ulong? baseTime = null;
                var invalid = false;
                var runs = new List<(uint Version, TRUN Run)>();
                new MP4Parser()
                    .FullBox("tfhd", child => header = MP4Parser.ParseTFHD(child.Reader, child.Flags))
                    .FullBox("tfdt", child =>
                    {
                        if (child.Version is 0 or 1)
                            baseTime = MP4Parser.ParseTFDT(child.Reader, child.Version);
                        else
                            invalid = true;
                    })
                    .FullBox("trun", child =>
                    {
                        if (child.Version is 0 or 1)
                            runs.Add((child.Version, MP4Parser.ParseTRUN(child.Reader, child.Version, child.Flags)));
                        else
                            invalid = true;
                    })
                    .Parse(box.Reader.ReadBytes((int)box.Reader.GetLength()));
                var track = tracks.FirstOrDefault(t => t.Id == header?.TrackId);
                if (track == null || header == null)
                    return;
                if (invalid)
                {
                    track.Invalid = true;
                    return;
                }
                var decode = baseTime == null ? track.DecodeEnd : baseTime.Value;
                foreach (var (version, run) in runs)
                {
                    foreach (var sample in run.SampleData)
                    {
                        token.ThrowIfCancellationRequested();
                        var duration = sample.SampleDuration != 0 ? sample.SampleDuration :
                            header.DefaultSampleDuration != 0 ? header.DefaultSampleDuration : track.DefaultDuration;
                        if (duration == 0)
                        {
                            track.Invalid = true;
                            return;
                        }
                        var offset = version == 1 ? (long)unchecked((int)sample.SampleCompositionTimeOffset) : sample.SampleCompositionTimeOffset;
                        track.Start = Math.Min(track.Start, decode + offset);
                        track.End = Math.Max(track.End, decode + offset + duration);
                        decode += duration;
                    }
                }
                track.DecodeEnd = decode;
            })
            .Parse(data);
    }
}
