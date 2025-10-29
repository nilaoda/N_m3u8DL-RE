namespace N_m3u8DL_RE.Common.Util;

public static class BinaryContentCheckUtil
{
    public static bool LooksLikeBinary(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0) return false;

        int nonTextCount = 0;
        int total = 0;

        for (int i = 0; i < data.Length;)
        {
            byte b = data[i];
            total++;

            // NULL 字节 → 几乎可以肯定是二进制
            if (b == 0x00)
                return true;

            // 可打印 ASCII
            if (b is >= 0x20 and <= 0x7E)
            {
                i++;
                continue;
            }

            // 常见控制符（\n \r \t）
            if (b is 0x09 or 0x0A or 0x0D)
            {
                i++;
                continue;
            }

            // UTF-8 多字节字符（包括中文）
            int seqLen = GetUtf8SequenceLength(b);
            if (seqLen > 1 && i + seqLen <= data.Length && IsValidUtf8Sequence(data.Slice(i, seqLen)))
            {
                i += seqLen;
                continue;
            }

            // 其他都算非文本
            nonTextCount++;
            i++;
        }

        // 计算比例：非文本字节超过30% 视为二进制
        double ratio = (double)nonTextCount / total;
        return ratio > 0.3;
    }

    private static int GetUtf8SequenceLength(byte b)
    {
        if ((b & 0x80) == 0x00) return 1; // 0xxxxxxx
        if ((b & 0xE0) == 0xC0) return 2; // 110xxxxx
        if ((b & 0xF0) == 0xE0) return 3; // 1110xxxx
        if ((b & 0xF8) == 0xF0) return 4; // 11110xxx
        return 1;
    }

    private static bool IsValidUtf8Sequence(ReadOnlySpan<byte> seq)
    {
        if (seq.Length <= 1) return false;
        for (int i = 1; i < seq.Length; i++)
        {
            if ((seq[i] & 0xC0) != 0x80)
                return false;
        }
        return true;
    }
    
    public static bool IsMpeg2TsBuffer(ReadOnlySpan<byte> buffer)
    {
        const int packetSize = 188;
        if (buffer.Length < packetSize) return false;
        int syncCount = 0;
        for (int i = 0; i < Math.Min(buffer.Length / packetSize, 5); i++)
        {
            if (buffer[i * packetSize] == 0x47) syncCount++;
        }
        return syncCount >= 3;
    }
}