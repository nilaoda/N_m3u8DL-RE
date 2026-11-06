namespace N_m3u8DL_RE.Crypto.TXVDRM;

public static class MediaDecryptor
{
    public const int TsPacketSize = 188;
    public const byte TsSyncByte = 0x47;

    public static byte[] DecryptPer(
        byte[] encryptedBuff,
        byte[] v3Key,
        byte[] v3Nonce,
        byte[]? v2Key = null,
        byte[]? v2Nonce = null,
        int targetPidOrTrack = 0)
    {
        if (encryptedBuff == null) throw new ArgumentNullException(nameof(encryptedBuff));
        if (encryptedBuff.Length == 0) throw new ArgumentException("buffer is empty", nameof(encryptedBuff));
        RequireKey(v3Key, "V3 key must be 32 bytes");
        RequireNonce(v3Nonce, "V3 nonce must be 12 bytes");

        byte[] level1 = (byte[])encryptedBuff.Clone();
        if (LooksLikeTs(level1) || LooksLikeFmp4(level1))
            throw new MediaDecryptException("Not TS/MP4 after V3 decrypt (err 7)");

        GateCipher.XorKeystream(level1, 0, level1.Length, v3Key, v3Nonce, GateCipherKind.V3Outer);
        if (!LooksLikeTs(level1) && !LooksLikeFmp4(level1))
            throw new MediaDecryptException(
                "Local transform completed, but the segment is still not MPEG-TS/fMP4; head=" + HexHead(level1, 16));
        bool hasInner = v2Key != null && v2Key.Length != 0;
        if (!hasInner)
            return level1;

        RequireKey(v2Key!, "V2 key must be 32 bytes");
        RequireNonce(v2Nonce, "V2 nonce must be 12 bytes");
        return DecryptInner(level1, v2Key!, v2Nonce!, targetPidOrTrack);
    }

    private static byte[] DecryptInner(byte[] data, byte[] key, byte[] nonce, int target)
    {
        if (LooksLikeTs(data))
            return TsProcessor.Decrypt(data, key, nonce, target);

        return Fmp4Processor.Decrypt(data, key, nonce, target);
    }

    private static void RequireKey(byte[]? key, string message)
    {
        if (key == null || key.Length != 32)
            throw new MediaDecryptException(message);
    }

    private static void RequireNonce(byte[]? nonce, string message)
    {
        if (nonce == null || nonce.Length != 12)
            throw new MediaDecryptException(message);
    }

    private static string HexHead(byte[] data, int maxBytes)
    {
        int count = Math.Min(data.Length, maxBytes);
        var sb = new System.Text.StringBuilder(count * 2);
        for (int i = 0; i < count; i++)
            sb.Append(data[i].ToString("X2"));
        return sb.ToString();
    }

    internal static bool LooksLikeTs(byte[] data)
    {
        if (data.Length < 4 * TsPacketSize) return false;
        for (int i = 0; i < 4 * TsPacketSize; i += TsPacketSize)
            if (data[i] != TsSyncByte) return false;
        return true;
    }

    internal static bool LooksLikeFmp4(byte[] data)
    {
        if (data.Length < 8) return false;
        if (!Box.TryParse(data, 0, data.Length, out Box box)) return false;
        return box.Type == Box.TypeFtyp || box.Type == Box.TypeStyp ||
               box.Type == Box.TypeMoov || box.Type == Box.TypeSidx ||
               box.Type == Box.TypeMoof || box.Type == Box.TypeMdat;
    }

    internal static byte[] DecryptNalUnit(byte[] buf, CodecKind codec, byte[] key, byte[] nonce)
    {
        byte[] result = new byte[buf.Length];
        if (buf.Length == 0) return result;

        int nalType = GetNalType(buf, codec);
        if (!IsValidNalType(nalType, codec))
        {
            Buffer.BlockCopy(buf, 0, result, 0, buf.Length);
            return result;
        }

        byte[] stripped = RemoveEmulationPrevention(buf);
        XorNalBody(stripped, codec, key, nonce);
        Buffer.BlockCopy(stripped, 0, result, 0, Math.Min(stripped.Length, result.Length));
        return result;
    }

    internal static void DecryptNalUnitInPlace(byte[] buf, int offset, int length, CodecKind codec, byte[] key, byte[] nonce)
    {
        if (length <= 0) return;
        int nalType = GetNalType(buf, offset, length, codec);
        if (!IsValidNalType(nalType, codec)) return;

        int start = (codec == CodecKind.Avc) ? 32 : 48;
        for (int off = start; off + 64 <= length; off += 192)
            GateCipher.XorKeystream(buf, offset + off, 64, key, nonce, GateCipherKind.V2Inner);
    }

    private static void XorNalBody(byte[] buf, CodecKind codec, byte[] key, byte[] nonce)
    {
        int start = (codec == CodecKind.Avc) ? 32 : 48;
        for (int off = start; off + 64 <= buf.Length; off += 192)
            GateCipher.XorKeystream(buf, off, 64, key, nonce, GateCipherKind.V2Inner);
    }

    internal static int GetNalType(byte[] buf, CodecKind codec) =>
        GetNalType(buf, 0, buf.Length, codec);

    internal static int GetNalType(byte[] buf, int offset, CodecKind codec) =>
        GetNalType(buf, offset, buf.Length - offset, codec);

    internal static int GetNalType(byte[] buf, int offset, int length, CodecKind codec)
    {
        if (length <= 0) return -1;
        switch (codec)
        {
            case CodecKind.Avc:
                return buf[offset] & 0x1F;
            case CodecKind.Hevc:
                return length < 2 ? -1 : (buf[offset] >> 1) & 0x3F;
            case CodecKind.Vvc:
                return length < 2 ? -1 : buf[offset + 1] >> 3;
            default:
                return -1;
        }
    }

    internal static bool IsValidNalType(int nalType, CodecKind codec)
    {
        switch (codec)
        {
            case CodecKind.Avc:
                return nalType >= 1 && nalType <= 5;
            case CodecKind.Hevc:
                return (nalType >= 0 && nalType <= 9) || (nalType >= 16 && nalType <= 21);
            case CodecKind.Vvc:
                return nalType >= 0 && nalType <= 11;
            default:
                return false;
        }
    }

    internal static byte[] RemoveEmulationPrevention(byte[] input)
    {
        var output = new List<byte>(input.Length);
        int i = 0;
        while (i < input.Length)
        {
            if (i + 3 < input.Length && input[i] == 0 && input[i + 1] == 0 &&
                input[i + 2] == 3 && input[i + 3] <= 3)
            {
                output.Add(0);
                output.Add(0);
                i += 3;
            }
            else
            {
                output.Add(input[i]);
                i++;
            }
        }
        return output.ToArray();
    }
}
