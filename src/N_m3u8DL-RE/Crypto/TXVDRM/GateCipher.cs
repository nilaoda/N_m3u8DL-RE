using System.Buffers.Binary;

namespace N_m3u8DL_RE.Crypto.TXVDRM;

internal static class GateCipher
{
    
    private static readonly int[] V3KeySlot = { 3, 4, 5, 6, 7, 8, 9, 10 };
    private static readonly int[] V3KeyOrder = { 1, 2, 4, 7, 6, 5, 3, 0 };
    private static readonly int[] V3NonceSlot = { 11, 12, 13 };
    private static readonly int[] V3NonceOrder = { 1, 0, 2 };
    private const int V3CtrLoSlot = 14, V3CtrHiSlot = 15;
    private static readonly int[] V2KeySlot = { 3, 4, 5, 7, 8, 9, 10, 15 };
    private static readonly int[] V2KeyOrder = { 1, 3, 4, 7, 2, 0, 6, 5 };
    private static readonly int[] V2NonceSlot = { 11, 12, 13 };
    private static readonly int[] V2NonceOrder = { 0, 1, 2 };
    private const int V2CtrLoSlot = 6, V2CtrHiSlot = 14;
    private static readonly int[][] V3Columns =
    {
        new[] { 0, 4, 8, 13 }, new[] { 1, 2, 10, 12 },
        new[] { 3, 14, 9, 15 }, new[] { 5, 7, 11, 6 },
    };
    private static readonly int[][] V3Diagonals =
    {
        new[] { 0, 2, 9, 6 }, new[] { 1, 14, 11, 13 },
        new[] { 3, 7, 8, 12 }, new[] { 5, 4, 10, 15 },
    };
    private static readonly int[][] V2Columns =
    {
        new[] { 0, 4, 8, 12 }, new[] { 1, 5, 9, 13 },
        new[] { 2, 6, 10, 14 }, new[] { 3, 7, 11, 15 },
    };
    private static readonly int[][] V2Diagonals =
    {
        new[] { 0, 5, 10, 15 }, new[] { 1, 6, 11, 12 },
        new[] { 2, 7, 8, 13 }, new[] { 3, 4, 9, 14 },
    };

    private static readonly int[] V3OutputPerm =
    {
        0, 1, 3, 5, 4, 2, 14, 7, 8, 10, 9, 11, 13, 12, 15, 6,
    };
    public static void XorKeystream(byte[] buffer, int offset, int count, byte[] key, byte[] nonce, GateCipherKind kind)
    {
        if (count <= 0) return;
        if (key.Length != 32 || nonce.Length != 12)
            throw new ArgumentException("key must be 32 bytes and nonce 12 bytes");

        Span<uint> words = stackalloc uint[16];
        int pos = offset;
        int end = offset + count;

        while (pos < end)
        {
            ulong blockIndex = (ulong)(pos >> 6);
            ComputeBlock(key, nonce, blockIndex, kind, words);

            int inBlock = pos & 63;
            int take = Math.Min(64 - inBlock, end - pos);
            for (int i = 0; i < take; i++)
            {
                int word = (inBlock + i) >> 2;
                int shift = (((inBlock + i) & 3) * 8);
                buffer[pos + i] ^= (byte)(words[word] >> shift);
            }
            pos += take;
        }
    }

    internal static uint[] BlockWords(byte[] key, byte[] nonce, ulong blockIndex, GateCipherKind kind)
    {
        uint[] words = new uint[16];
        ComputeBlock(key, nonce, blockIndex, kind, words);
        return words;
    }

    private static void ComputeBlock(byte[] key, byte[] nonce, ulong blockIndex, GateCipherKind kind, Span<uint> output)
    {
        Span<uint> s = stackalloc uint[16];
        Span<uint> w = stackalloc uint[16];

        bool isV3 = kind == GateCipherKind.V3Outer;
        s[0] = isV3 ? 0x65278809u : 0x63713531u;
        s[1] = isV3 ? 0x74857523u : 0x78953552u;
        s[2] = isV3 ? 0x82626595u : 0x86392848u;

        int[] keySlots = isV3 ? V3KeySlot : V2KeySlot;
        int[] keyOrder = isV3 ? V3KeyOrder : V2KeyOrder;
        for (int i = 0; i < 8; i++)
            s[keySlots[i]] = BinaryPrimitives.ReadUInt32LittleEndian(key.AsSpan(keyOrder[i] * 4, 4));

        int[] nonceSlots = isV3 ? V3NonceSlot : V2NonceSlot;
        int[] nonceOrder = isV3 ? V3NonceOrder : V2NonceOrder;
        for (int i = 0; i < 3; i++)
            s[nonceSlots[i]] = BinaryPrimitives.ReadUInt32LittleEndian(nonce.AsSpan(nonceOrder[i] * 4, 4));

        s[isV3 ? V3CtrLoSlot : V2CtrLoSlot] = (uint)blockIndex;
        s[isV3 ? V3CtrHiSlot : V2CtrHiSlot] = (uint)(blockIndex >> 32);

        for (int i = 0; i < 16; i++) w[i] = s[i];

        int[][] columns = isV3 ? V3Columns : V2Columns;
        int[][] diagonals = isV3 ? V3Diagonals : V2Diagonals;

        for (int round = 0; round < 10; round++)
        {
            for (int i = 0; i < 4; i++)
                QuarterRound(w, columns[i], isV3);
            for (int i = 0; i < 4; i++)
                QuarterRound(w, diagonals[i], isV3);
        }

        Span<uint> sum = stackalloc uint[16];
        for (int i = 0; i < 16; i++) sum[i] = w[i] + s[i];

        if (isV3)
            for (int i = 0; i < 16; i++) output[i] = sum[V3OutputPerm[i]];
        else
            for (int i = 0; i < 16; i++) output[i] = sum[i];
    }

    private static void QuarterRound(Span<uint> m, int[] q, bool isV3)
    {
        uint a = m[q[0]], b = m[q[1]], c = m[q[2]], d = m[q[3]];

        a -= b; d = RotateLeft(d ^ a, 16);

        if (isV3)
        {
            c -= d; b = RotateLeft(b ^ c, 12);
            a += b; d = RotateLeft(d ^ a, 8);
            c += d; b = RotateLeft(b ^ c, 6);
        }
        else
        {
            c += d; b = RotateLeft(b ^ c, 12);
            a += b; d = RotateLeft(d ^ a, 8);
            c -= d; b = RotateLeft(b ^ c, 6);
        }

        m[q[0]] = a; m[q[1]] = b; m[q[2]] = c; m[q[3]] = d;
    }

    private static uint RotateLeft(uint value, int shift) =>
        (value << shift) | (value >> (32 - shift));
}
