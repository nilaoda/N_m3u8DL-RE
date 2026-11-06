namespace N_m3u8DL_RE.Crypto.TXVDRM;

internal static class TsProcessor
{
    internal static byte[] Decrypt(byte[] data, byte[] key, byte[] nonce, int forcedPid)
    {
        if (data.Length % MediaDecryptor.TsPacketSize != 0)
            throw new MediaDecryptException("TS length is not a multiple of 188");

        byte[] result = (byte[])data.Clone();

        int pid;
        CodecKind codec;
        if (forcedPid > 0)
        {
            pid = forcedPid;
            codec = DetectCodecForPid(result, pid);
        }
        else
        {
            if (!TryFindPayloadPid(result, out pid, out codec))
                throw new MediaDecryptException("Could not discover an H.264/H.265/H.266 video PID");
        }

        ProcessPackets(result, pid, codec, key, nonce);
        return result;
    }

    private static void ProcessPackets(byte[] data, int targetPid, CodecKind codec, byte[] key, byte[] nonce)
    {
        var slices = new List<(int Start, int End)>(64);

        for (int i = 0; i + MediaDecryptor.TsPacketSize <= data.Length; i += MediaDecryptor.TsPacketSize)
        {
            if (data[i] != MediaDecryptor.TsSyncByte)
                throw new MediaDecryptException("missing TS sync byte at offset " + i + " bytes");

            bool pusi = (data[i + 1] & 0x40) != 0;
            int pid = ((data[i + 1] & 0x1F) << 8) | data[i + 2];
            int afc = (data[i + 3] >> 4) & 0x03;

            bool hasPayload = afc == 1 || afc == 3;
            bool hasAdaptation = afc == 2 || afc == 3;

            int payloadStart = i + 4;
            if (hasAdaptation)
            {
                if (payloadStart >= i + MediaDecryptor.TsPacketSize)
                    continue;
                payloadStart += data[payloadStart] + 1;
            }

            int payloadEnd = i + MediaDecryptor.TsPacketSize;
            if (!hasPayload || payloadStart >= payloadEnd || pid != targetPid)
                continue;

            // A new PES/access unit starts here: flush the previous one first.
            if (pusi && slices.Count > 0)
            {
                FlushUnit(data, slices, codec, key, nonce);
                slices.Clear();
            }

            slices.Add((payloadStart, payloadEnd));
        }

        if (slices.Count > 0)
            FlushUnit(data, slices, codec, key, nonce);
    }

    private static void FlushUnit(byte[] data, List<(int Start, int End)> slices, CodecKind codec, byte[] key, byte[] nonce)
    {
        int total = 0;
        foreach (var slice in slices) total += slice.End - slice.Start;
        if (total == 0) return;

        byte[] unit = ConcatSlices(data, slices);

        int clearPrefix = ComputeClearPrefix(unit);
        if (clearPrefix < unit.Length)
            DecryptAnnexBBuffer(unit, clearPrefix, unit.Length, codec, key, nonce);

        int offset = 0;
        foreach (var slice in slices)
        {
            Buffer.BlockCopy(unit, offset, data, slice.Start, slice.End - slice.Start);
            offset += slice.End - slice.Start;
        }
    }

    private static int ComputeClearPrefix(byte[] unit)
    {
        if (unit.Length < 6 || unit[0] != 0 || unit[1] != 0 || unit[2] != 1)
            return 0;
        if (unit.Length < 9)
            return 6;
        int length = unit[8] + 9;
        return unit.Length > length ? length : unit.Length;
    }

    private static void DecryptAnnexBBuffer(byte[] data, int start, int end, CodecKind codec, byte[] key, byte[] nonce)
    {
        int pos = start;
        while (pos + 3 <= end)
        {
            int prefix = FindStartCode(data, pos, end, out int codeStart, out int codeLength);
            if (prefix < 0) break;

            int bodyStart = codeStart + codeLength;
            int nextCode = FindStartCode(data, bodyStart, end, out int nextStart, out _);
            int bodyEnd = nextCode < 0 ? end : nextStart;

            if (bodyEnd > bodyStart)
            {
                byte[] body = new byte[bodyEnd - bodyStart];
                Buffer.BlockCopy(data, bodyStart, body, 0, body.Length);
                byte[] decrypted = MediaDecryptor.DecryptNalUnit(body, codec, key, nonce);
                Buffer.BlockCopy(decrypted, 0, data, bodyStart, Math.Min(decrypted.Length, body.Length));
            }

            pos = bodyEnd;
        }
    }

    private static int FindStartCode(byte[] data, int from, int limit, out int codeStart, out int codeLength)
    {
        for (int i = from; i + 3 <= limit; i++)
        {
            if (data[i] == 0 && data[i + 1] == 0 && data[i + 2] == 1)
            {
                codeStart = i;
                codeLength = 3;
                return i;
            }
        }
        codeStart = -1;
        codeLength = 0;
        return -1;
    }

    private static bool TryFindPayloadPid(byte[] data, out int pid, out CodecKind codec)
    {
        pid = -1;
        codec = CodecKind.Unknown;

        foreach (var packet in EnumeratePackets(data))
        {
            if (packet.Pid == 0) continue;
            foreach (byte[] section in CollectSections(data, packet))
            {
                if (ParsePmtFirstVideo(section, out int streamPid, out CodecKind streamCodec))
                {
                    pid = streamPid;
                    codec = streamCodec;
                    return true;
                }
            }
        }

        return TryDetectByContent(data, out pid, out codec);
    }

    private static CodecKind DetectCodecForPid(byte[] data, int pid)
    {
        int bestScore = 0;
        CodecKind best = CodecKind.Unknown;
        foreach (byte[] unit in EnumeratePayloadUnits(data, pid))
        {
            int clear = ComputeClearPrefix(unit);
            int payloadLength = unit.Length - clear;
            if (payloadLength <= 0) continue;

            CodecKind kind = BestCodecFor(unit, clear, payloadLength, out int score);
            if (score > bestScore)
            {
                bestScore = score;
                best = kind;
            }
        }
        return best == CodecKind.Unknown ? CodecKind.Avc : best;
    }

    private static bool TryDetectByContent(byte[] data, out int pid, out CodecKind codec)
    {
        pid = -1;
        codec = CodecKind.Unknown;

        int bestScore = 0;
        int bestLength = 0;
        int bestPid = -1;
        CodecKind bestCodec = CodecKind.Unknown;

        int candidatePid = -1;
        var slices = new List<(int Start, int End)>();
        int unitSize = 0;

        void Consider()
        {
            if (slices.Count == 0 || candidatePid < 0) return;

            byte[] unit = ConcatSlices(data, slices);
            slices.Clear();
            unitSize = 0;

            int clear = ComputeClearPrefix(unit);
            int payloadLength = unit.Length - clear;
            if (payloadLength <= 0) return;

            CodecKind kind = BestCodecFor(unit, clear, payloadLength, out int score);
            if (score <= 0) return;
            if (score > bestScore || (score == bestScore && payloadLength > bestLength))
            {
                bestScore = score;
                bestLength = payloadLength;
                bestPid = candidatePid;
                bestCodec = kind;
            }
        }

        foreach (TsPacket packet in EnumeratePackets(data))
        {
            if (!packet.Pusi)
            {
                if (candidatePid == packet.Pid)
                {
                    slices.Add((packet.PayloadStart, packet.End));
                    unitSize += packet.End - packet.PayloadStart;
                    if (unitSize >= 0x10000) Consider();
                }
                continue;
            }

            Consider();

            int length = packet.End - packet.PayloadStart;
            if (length >= 3 && data[packet.PayloadStart] == 0 &&
                data[packet.PayloadStart + 1] == 0 && data[packet.PayloadStart + 2] == 1)
            {
                candidatePid = packet.Pid;
                slices.Clear();
                slices.Add((packet.PayloadStart, packet.End));
                unitSize = length;
            }
        }

        Consider();

        if (bestPid < 0 || bestScore <= 0) return false;
        pid = bestPid;
        codec = bestCodec;
        return true;
    }

    private static IEnumerable<byte[]> EnumeratePayloadUnits(byte[] data, int pid)
    {
        var slices = new List<(int Start, int End)>();
        foreach (TsPacket packet in EnumeratePackets(data))
        {
            if (packet.Pid != pid) continue;
            if (packet.Pusi && slices.Count > 0)
            {
                yield return ConcatSlices(data, slices);
                slices.Clear();
            }
            slices.Add((packet.PayloadStart, packet.End));
        }
        if (slices.Count > 0) yield return ConcatSlices(data, slices);
    }

    private static byte[] ConcatSlices(byte[] data, List<(int Start, int End)> slices)
    {
        int total = 0;
        foreach (var slice in slices) total += slice.End - slice.Start;

        byte[] unit = new byte[total];
        int offset = 0;
        foreach (var slice in slices)
        {
            Buffer.BlockCopy(data, slice.Start, unit, offset, slice.End - slice.Start);
            offset += slice.End - slice.Start;
        }
        return unit;
    }

    private static CodecKind BestCodecFor(byte[] buf, int start, int length, out int score)
    {
        CodecKind best = CodecKind.Unknown;
        score = 0;
        foreach (CodecKind candidate in new[] { CodecKind.Avc, CodecKind.Hevc, CodecKind.Vvc })
        {
            int current = ScoreCodec(buf, start, length, candidate);
            if (current > score)
            {
                score = current;
                best = candidate;
            }
        }
        return best;
    }

    private static int ScoreCodec(byte[] buf, int start, int length, CodecKind codec)
    {
        int end = start + length;
        int units = 0;
        int parameterSets = 0;
        int sliceTypes = 0;

        int pos = start;
        while (pos + 3 <= end)
        {
            if (FindStartCode(buf, pos, end, out int codeStart, out int codeLength) < 0) break;

            int bodyStart = codeStart + codeLength;
            int bodyEnd = FindStartCode(buf, bodyStart, end, out int nextStart, out _) < 0
                ? end
                : nextStart;

            if (bodyEnd > bodyStart)
            {
                byte[] body = new byte[bodyEnd - bodyStart];
                Buffer.BlockCopy(buf, bodyStart, body, 0, body.Length);

                if (NalPassesHeaderGate(body, codec))
                {
                    int type = MediaDecryptor.GetNalType(body, 0, body.Length, codec);
                    if (type >= 0)
                    {
                        units++;
                        if (IsParameterSetType(type, codec)) parameterSets++;
                        if (MediaDecryptor.IsValidNalType(type, codec)) sliceTypes++;
                        if (units >= 8) break;
                    }
                }
            }

            pos = bodyEnd;
        }

        if (units == 0 || (parameterSets | sliceTypes) == 0) return 0;
        return units + 30 * parameterSets + 10 * sliceTypes;
    }
    private static bool NalPassesHeaderGate(byte[] body, CodecKind codec)
    {
        if (body.Length == 0) return false;
        switch (codec)
        {
            case CodecKind.Avc:
                return (body[0] & 0x80) == 0;
            case CodecKind.Hevc:
                return body.Length >= 2 && (body[1] & 0x07) != 0;
            case CodecKind.Vvc:
                return body.Length >= 2 && (body[0] & 0xC0) == 0 && (body[1] & 0x07) != 0;
            default:
                return false;
        }
    }

    private static bool IsParameterSetType(int type, CodecKind codec)
    {
        switch (codec)
        {
            case CodecKind.Avc:
                return type == 7 || type == 8;

            case CodecKind.Hevc:
                return type == 32 || type == 33 || type == 34 || type == 35 ||
                       type == 39 || type == 40;

            case CodecKind.Vvc:
                return (type - 12 >= 0 && type - 12 <= 5) || type == 20 || type == 23 || type == 24;

            default:
                return false;
        }
    }

    private readonly struct TsPacket
    {
        public readonly int Pid;
        public readonly bool Pusi;
        public readonly int PayloadStart;
        public readonly int End;

        public TsPacket(int pid, bool pusi, int payloadStart, int end)
        {
            Pid = pid;
            Pusi = pusi;
            PayloadStart = payloadStart;
            End = end;
        }
    }

    private static IEnumerable<TsPacket> EnumeratePackets(byte[] data)
    {
        for (int i = 0; i + MediaDecryptor.TsPacketSize <= data.Length; i += MediaDecryptor.TsPacketSize)
        {
            if (data[i] != MediaDecryptor.TsSyncByte) yield break;

            bool pusi = (data[i + 1] & 0x40) != 0;
            int pid = ((data[i + 1] & 0x1F) << 8) | data[i + 2];
            int afc = (data[i + 3] >> 4) & 0x03;
            if (afc != 1 && afc != 3) continue;

            int payloadStart = i + 4;
            if (afc == 3)
            {
                if (payloadStart >= i + MediaDecryptor.TsPacketSize) continue;
                payloadStart += data[payloadStart] + 1;
            }

            yield return new TsPacket(pid, pusi, payloadStart, i + MediaDecryptor.TsPacketSize);
        }
    }

    private static IEnumerable<byte[]> CollectSections(byte[] data, TsPacket packet)
    {
        if (!packet.Pusi) yield break;

        int start = packet.PayloadStart;
        if (start >= packet.End) yield break;

        int pointerField = data[start];
        start += 1 + pointerField;
        if (start + 3 > packet.End) yield break;

        // Only single-packet sections are needed for PAT/PMT discovery.
        int sectionLength = ((data[start + 1] & 0x0F) << 8) | data[start + 2];
        int total = 3 + sectionLength;
        if (total <= 3 || start + total > packet.End) yield break;

        byte[] section = new byte[total];
        Buffer.BlockCopy(data, start, section, 0, total);
        yield return section;
    }
    private static bool ParsePmtFirstVideo(byte[] section, out int pid, out CodecKind codec)
    {
        pid = -1;
        codec = CodecKind.Unknown;
        if (section.Length < 12 || section[0] != 0x02) return false;

        int programInfoLength = ((section[10] & 0x0F) << 8) | section[11];
        int i = 12 + programInfoLength;
        int end = section.Length - 4; // CRC32

        while (i + 5 <= end)
        {
            int type = section[i];
            int esPid = ((section[i + 1] & 0x1F) << 8) | section[i + 2];
            int esInfoLength = ((section[i + 3] & 0x0F) << 8) | section[i + 4];

            CodecKind kind = type switch
            {
                0x33 => CodecKind.Vvc,
                0x24 => CodecKind.Hevc,
                0x1B => CodecKind.Avc,
                _ => CodecKind.Unknown,
            };
            if (kind != CodecKind.Unknown)
            {
                pid = esPid;
                codec = kind;
                return true;
            }

            i += 5 + esInfoLength;
        }

        return false;
    }
}
