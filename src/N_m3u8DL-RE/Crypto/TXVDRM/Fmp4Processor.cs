using System.Buffers.Binary;

namespace N_m3u8DL_RE.Crypto.TXVDRM;

internal static class Fmp4Processor
{
    private sealed class TrackInfo
    {
        public int TrackId = -1;
        public CodecKind Codec = CodecKind.Unknown;
        public int NalLengthSize = 4;
    }

    internal static byte[] Decrypt(byte[] data, byte[] key, byte[] nonce, int forcedTrackId)
    {
        TrackInfo track = ReadVideoTrack(data);
        if (forcedTrackId > 0)
        {
            track.TrackId = forcedTrackId;
            if (track.Codec == CodecKind.Unknown) track.Codec = CodecKind.Avc;
        }
        if (track.TrackId < 0)
            throw new MediaDecryptException("Could not find a video track in the fMP4 init segment");

        using var output = new MemoryStream(data.Length);
        int offset = 0;

        while (offset + 8 <= data.Length)
        {
            if (!Box.TryParse(data, offset, data.Length, out Box box))
                break; // trailing garbage: stop, mirroring the native loop guard

            if (box.Type != Box.TypeMoof)
            {
                output.Write(data, box.Start, box.Size);
                offset = box.End;
                continue;
            }

            int nextOffset = box.End;
            if (!Box.TryParse(data, nextOffset, data.Length, out Box mdat) || mdat.Type != Box.TypeMdat)
                throw new MediaDecryptException("fMP4 fragment is missing mdat after moof");

            byte[] moof = new byte[box.Size];
            Buffer.BlockCopy(data, box.Start, moof, 0, box.Size);

            byte[] payload = new byte[mdat.Size - mdat.HeaderSize];
            Buffer.BlockCopy(data, mdat.PayloadStart, payload, 0, payload.Length);

            DecryptFragment(moof, box.Start, payload, mdat, track, key, nonce);

            output.Write(moof, 0, moof.Length);
            output.Write(BuildMdatHeader(mdat.Size), 0, 8);
            output.Write(payload, 0, payload.Length);

            offset = mdat.End;
        }

        return output.ToArray();
    }

    private static byte[] BuildMdatHeader(int totalSize)
    {
        byte[] header = new byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0, 4), (uint)totalSize);
        header[4] = (byte)'m';
        header[5] = (byte)'d';
        header[6] = (byte)'a';
        header[7] = (byte)'t';
        return header;
    }

    /// <summary>Processes traf/tfhd/trun of one moof and rewrites sample data in mdat.</summary>
    private static void DecryptFragment(byte[] moof, int moofStart, byte[] payload, Box mdat, TrackInfo track, byte[] key, byte[] nonce)
    {
        int moofLength = moof.Length;
        var runs = new List<TrackRun>();
        int pos = 8; // skip moof header

        while (pos + 8 <= moofLength)
        {
            if (!Box.TryParse(moof, pos, moofLength, out Box child)) break;
            if (child.Type != Box.TypeTraf)
            {
                pos = child.End;
                continue;
            }

            var run = new TrackRun();
            int trafPos = child.PayloadStart;
            while (trafPos + 8 <= child.End)
            {
                if (!Box.TryParse(moof, trafPos, child.End, out Box inner)) break;
                if (inner.Type == Box.TypeTfhd) ParseTfhd(moof, inner, run);
                else if (inner.Type == Box.TypeTrun) ParseTrun(moof, inner, run);
                trafPos = inner.End;
            }

            if (run.Samples.Count > 0)
            {
                AssignOffsets(run, moofStart, mdat);
                runs.Add(run);
            }

            pos = child.End;
        }

        ValidateNoOverlap(runs);

        foreach (TrackRun run in runs)
        {
            if (run.TrackId != track.TrackId) continue;
            foreach (SampleEntry sample in run.Samples)
            {
                if (sample.Size == 0)
                    throw new MediaDecryptException("fMP4 sample size is empty");
                DecryptSample(payload, sample.Offset, sample.Size, sample.SizeFieldPos, track, key, nonce);
            }
        }
    }

    private static void AssignOffsets(TrackRun run, int moofStart, Box mdat)
    {
        long cursor = run.HasDataOffset
            ? moofStart + run.DataOffset
            : run.BaseIsMoof ? moofStart
            : (run.HasBaseDataOffset ? run.BaseDataOffset : mdat.PayloadStart);

        foreach (SampleEntry sample in run.Samples)
        {
            if (cursor < int.MinValue || cursor > int.MaxValue)
                throw new MediaDecryptException("fMP4 data_offset exceeds int32");
            if (cursor < mdat.PayloadStart || cursor + sample.Size > mdat.End)
                throw new MediaDecryptException("fMP4 sample exceeds mdat bounds");

            sample.Offset = (int)(cursor - mdat.PayloadStart);
            cursor += sample.Size;
        }

        // The sample layout is preserved byte for byte, so the offsets the native code
        // rewrites (trun.data_offset / tfhd.base_data_offset) keep their old values and
        // no patch is required here.
    }

    private static void ValidateNoOverlap(List<TrackRun> runs)
    {
        var ranges = new List<(int Start, int End)>();
        foreach (TrackRun run in runs)
            foreach (SampleEntry sample in run.Samples)
                ranges.Add((sample.Offset, sample.Offset + sample.Size));

        ranges.Sort((a, b) => a.Start.CompareTo(b.Start));
        for (int i = 1; i < ranges.Count; i++)
        {
            if (ranges[i].Start < ranges[i - 1].End)
                throw new MediaDecryptException("fMP4 samples overlap inside mdat");
        }
    }

    private sealed class SampleEntry
    {
        public int Offset;       // offset of the sample data inside the mdat payload
        public int Size;
        public int SizeFieldPos; // position of the 32-bit size inside moof, or -1
    }

    private sealed class TrackRun
    {
        public int TrackId = -1;
        public int DefaultSampleSize;
        public long BaseDataOffset;
        public bool HasBaseDataOffset;
        public bool BaseIsMoof;
        public long DataOffset;
        public bool HasDataOffset;
        public int DataOffsetFieldPos = -1;
        public readonly List<SampleEntry> Samples = new List<SampleEntry>();
    }

    private static void ParseTfhd(byte[] moof, Box box, TrackRun run)
    {
        int p = box.PayloadStart;
        if (p + 8 > box.End) return;

        uint flags = BinaryPrimitives.ReadUInt32BigEndian(moof.AsSpan(p, 4)) & 0x00FFFFFFu;
        run.TrackId = (int)BinaryPrimitives.ReadUInt32BigEndian(moof.AsSpan(p + 4, 4));
        p += 8;

        if ((flags & 0x000001) != 0)
        {
            if (p + 8 > box.End) return;
            run.BaseDataOffset = (long)BinaryPrimitives.ReadUInt64BigEndian(moof.AsSpan(p, 8));
            run.HasBaseDataOffset = true;
            p += 8;
        }
        else if ((flags & 0x020000) != 0)
        {
            // default-base-is-moof: the moof start is the base (native stores a3 = moof base)
            run.BaseIsMoof = true;
            run.HasBaseDataOffset = true;
        }

        if ((flags & 0x000002) != 0) p += 4;              // sample_description_index
        if ((flags & 0x000008) != 0) p += 4;              // default_sample_duration
        if ((flags & 0x000010) != 0 && p + 4 <= box.End)  // default_sample_size
        {
            run.DefaultSampleSize = (int)BinaryPrimitives.ReadUInt32BigEndian(moof.AsSpan(p, 4));
            p += 4;
        }
    }

    private static void ParseTrun(byte[] moof, Box box, TrackRun run)
    {
        int p = box.PayloadStart;
        if (p + 8 > box.End) return;

        uint flags = BinaryPrimitives.ReadUInt32BigEndian(moof.AsSpan(p, 4));
        int sampleCount = (int)BinaryPrimitives.ReadUInt32BigEndian(moof.AsSpan(p + 4, 4));
        p += 8;

        if ((flags & 0x000001) != 0 && p + 4 <= box.End)
        {
            run.DataOffset = BinaryPrimitives.ReadInt32BigEndian(moof.AsSpan(p, 4));
            run.HasDataOffset = true;
            run.DataOffsetFieldPos = p;
            p += 4;
        }

        // first_sample_flags (native: if ((v6 & 4) != 0) v9 += 4)
        if ((flags & 0x000004) != 0) p += 4;

        bool hasDuration = (flags & 0x000100) != 0;
        bool hasSize = (flags & 0x000200) != 0;
        bool hasFlags = (flags & 0x000400) != 0;
        bool hasCto = (flags & 0x000800) != 0;

        int sizeFieldPos = -1;
        for (int i = 0; i < sampleCount; i++)
        {
            if (p > box.End) return;
            if (hasDuration) p += 4;
            int size = run.DefaultSampleSize;
            if (hasSize)
            {
                if (p + 4 > box.End) return;
                size = (int)BinaryPrimitives.ReadUInt32BigEndian(moof.AsSpan(p, 4));
                sizeFieldPos = p;
                p += 4;
            }
            if (hasFlags) p += 4;
            if (hasCto) p += 4;

            run.Samples.Add(new SampleEntry { Size = size, SizeFieldPos = sizeFieldPos });
        }
    }

    private static void DecryptSample(byte[] payload, int offset, int size, int sizeFieldPos,
        TrackInfo track, byte[] key, byte[] nonce)
    {
        bool stripEmulationPrevention = sizeFieldPos >= 0;
        int prefixSize = Math.Max(1, track.NalLengthSize);
        int pos = offset;
        int end = offset + size;

        while (pos + prefixSize <= end)
        {
            long nalSize = ReadBigEndian(payload, pos, prefixSize);
            int bodyStart = pos + prefixSize;

            if (nalSize <= 0 || bodyStart + nalSize > end)
                throw new MediaDecryptException("fMP4 NAL exceeds sample bounds");

            int bodyLength = (int)nalSize;

            if (stripEmulationPrevention)
            {
                byte[] body = new byte[bodyLength];
                Buffer.BlockCopy(payload, bodyStart, body, 0, bodyLength);
                byte[] decrypted = MediaDecryptor.DecryptNalUnit(body, track.Codec, key, nonce);
                Buffer.BlockCopy(decrypted, 0, payload, bodyStart, Math.Min(decrypted.Length, bodyLength));
            }
            else
            {
                MediaDecryptor.DecryptNalUnitInPlace(payload, bodyStart, bodyLength, track.Codec, key, nonce);
            }

            pos = bodyStart + bodyLength;
        }

        if (pos != end)
            throw new MediaDecryptException("fMP4 sample has trailing bytes after NAL parsing");
    }

    private static long ReadBigEndian(byte[] data, int offset, int size)
    {
        long value = 0;
        for (int i = 0; i < size; i++) value = (value << 8) | data[offset + i];
        return value;
    }
    private static TrackInfo ReadVideoTrack(byte[] data)
    {
        var info = new TrackInfo();
        int offset = 0;

        while (offset + 8 <= data.Length)
        {
            if (!Box.TryParse(data, offset, data.Length, out Box box)) break;
            if (box.Type == Box.TypeMoov)
            {
                ParseMoov(data, box, info);
                break;
            }
            offset = box.End;
        }

        return info;
    }

    private static void ParseMoov(byte[] data, Box moov, TrackInfo info)
    {
        int pos = moov.PayloadStart;
        while (pos + 8 <= moov.End)
        {
            if (!Box.TryParse(data, pos, moov.End, out Box child)) break;
            if (child.Type == Box.TypeTrak)
            {
                var candidate = new TrackInfo();
                ParseTrak(data, child, candidate);
                if (candidate.TrackId >= 0 && candidate.Codec != CodecKind.Unknown)
                {
                    info.TrackId = candidate.TrackId;
                    info.Codec = candidate.Codec;
                    info.NalLengthSize = candidate.NalLengthSize;
                    return;
                }
            }
            pos = child.End;
        }
    }

    private static void ParseTrak(byte[] data, Box trak, TrackInfo info)
    {
        int pos = trak.PayloadStart;
        while (pos + 8 <= trak.End)
        {
            if (!Box.TryParse(data, pos, trak.End, out Box child)) break;
            if (child.Type == Box.TypeTkhd) info.TrackId = ParseTkhdTrackId(data, child);
            else if (child.Type == Box.TypeMdia) ParseMdia(data, child, info);
            pos = child.End;
        }
    }

    private static int ParseTkhdTrackId(byte[] data, Box tkhd)
    {
        int p = tkhd.PayloadStart;
        if (p + 4 > tkhd.End) return -1;
        int version = data[p];
        int fieldPos = p + (version == 1 ? 20 : 12);
        if (fieldPos + 4 > tkhd.End) return -1;
        return (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(fieldPos, 4));
    }

    private static void ParseMdia(byte[] data, Box mdia, TrackInfo info)
    {
        int pos = mdia.PayloadStart;
        while (pos + 8 <= mdia.End)
        {
            if (!Box.TryParse(data, pos, mdia.End, out Box child)) break;
            if (child.Type == Box.TypeHdlr)
            {
                // handler_type must be 'vide'
                int p = child.PayloadStart + 8;
                if (p + 4 <= child.End &&
                    BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(p, 4)) != 0x76696465)
                    return;
            }
            else if (child.Type == Box.TypeMinf)
            {
                ParseMinf(data, child, info);
            }
            pos = child.End;
        }
    }

    private static void ParseMinf(byte[] data, Box minf, TrackInfo info)
    {
        int pos = minf.PayloadStart;
        while (pos + 8 <= minf.End)
        {
            if (!Box.TryParse(data, pos, minf.End, out Box child)) break;
            if (child.Type == Box.TypeStbl) ParseStbl(data, child, info);
            pos = child.End;
        }
    }

    private static void ParseStbl(byte[] data, Box stbl, TrackInfo info)
    {
        int pos = stbl.PayloadStart;
        while (pos + 8 <= stbl.End)
        {
            if (!Box.TryParse(data, pos, stbl.End, out Box child)) break;
            if (child.Type == Box.TypeStsd) ParseStsd(data, child, info);
            pos = child.End;
        }
    }

    private static void ParseStsd(byte[] data, Box stsd, TrackInfo info)
    {
        int pos = stsd.PayloadStart + 8; // version/flags + entry_count
        if (pos + 8 > stsd.End) return;

        if (!Box.TryParse(data, pos, stsd.End, out Box entry)) return;

        CodecKind codec = CodecForSampleEntry(entry.Type);
        if (codec == CodecKind.Unknown) return;

        info.Codec = codec;
        info.NalLengthSize = ReadNalLengthSize(data, entry, codec);
    }

    private static CodecKind CodecForSampleEntry(uint type)
    {
        if (type == Box.TypeAvc1 || type == Box.TypeAvc3) return CodecKind.Avc;
        if (type == Box.TypeHvc1 || type == Box.TypeHev1) return CodecKind.Hevc;
        if (type == Box.TypeVvc1 || type == Box.TypeVvi1) return CodecKind.Vvc;
        return CodecKind.Unknown;
    }

    /// <summary>Reads lengthSizeMinusOne from avcC / hvcC / vvcC inside a visual sample entry.</summary>
    private static int ReadNalLengthSize(byte[] data, Box entry, CodecKind codec)
    {
        uint wanted = codec == CodecKind.Avc ? Box.TypeAvcC :
                      codec == CodecKind.Hevc ? Box.TypeHvcC : Box.TypeVvcC;

        // VisualSampleEntry payload is 78 bytes; configuration box follows.
        int pos = entry.Start + 86;
        while (pos + 8 <= entry.End)
        {
            if (!Box.TryParse(data, pos, entry.End, out Box child)) break;
            if (child.Type == wanted)
            {
                int p = child.PayloadStart;
                if (codec == CodecKind.Avc)
                {
                    if (p + 5 <= child.End) return (data[p + 4] & 0x03) + 1;
                }
                else
                {
                    if (p + 22 <= child.End) return (data[p + 21] & 0x03) + 1;
                }
            }
            pos = child.End;
        }

        return 4;
    }
}
