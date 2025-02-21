using System.Security.Cryptography;
using MediaFormatLibrary.H264.Enums;

namespace N_m3u8DL_RE.Crypto;

class NalUnit
{
    private static readonly byte[] SyncByteSeqStart = [0x00, 0x00, 0x00, 0x01];
    private static readonly byte[] SyncByteSeq = [0x00, 0x00, 0x01];

    public NalUnitType Type;
    public byte[] Data;
    public int Length;
    public int StartCodeLength;

    public NalUnit(ref byte[] data)
    {
        var startCodeLength = GetStartCodeLength(data);
        
        var next = data.AsSpan(startCodeLength);

        var index1 = next.IndexOf(SyncByteSeqStart.AsSpan());
        var index2 = next.IndexOf(SyncByteSeq.AsSpan());
        
        var nextPos = index1 != -1 ? index1 + startCodeLength : index2 != -1 ? index2 + startCodeLength : data.Length;

        next = data.AsSpan(nextPos).ToArray();
        
        var payload = data.AsSpan(startCodeLength, nextPos - startCodeLength);
        Type = (NalUnitType)(payload[0] & 0x1f);
        Data = payload.ToArray();
        
        Length = nextPos - startCodeLength;
        StartCodeLength = startCodeLength;

        data = next.ToArray();
    }
    
    private static int GetStartCodeLength(ReadOnlySpan<byte> input)
    {
        return input.Length switch
        {
            > 4 when input[0] == SyncByteSeqStart[0] && 
                     input[1] == SyncByteSeqStart[1] && 
                     input[2] == SyncByteSeqStart[2] && 
                     input[3] == SyncByteSeqStart[3] => 4,
            > 3 when input[0] == SyncByteSeq[0] && 
                     input[1] == SyncByteSeq[1] && 
                     input[2] == SyncByteSeq[2] => 3,
            _ => throw new InvalidOperationException($"Invalid Start Code, {Convert.ToHexString(input[..4])}")
        };
    }

    private void RemoveScep3Bytes()
    {
        int i = 0, j = 0;
    
        while (i < Length)
        {
            if (Length - i > 3 && Data[i] == 0 && Data[i + 1] == 0 && Data[i + 2] == 3)
            {
                Data[j++] = Data[i++];
                Data[j++] = Data[i++];
                i++;
            }
            else
            {
                Data[j++] = Data[i++];
            }
        }

        var newData = new byte[j];
        Array.Copy(Data, newData, j);
        Data = newData;
        Length = j;
    }
    
    public void Decrypt(Aes aes)
    {
        if (Data.Length <= 48)
            return;

        RemoveScep3Bytes();

        if (Data.Length <= 32)
            return;

        using var transform = aes.CreateDecryptor(aes.Key, aes.IV);
        
        /* Required when running in N_m3u8DL-RE for some reason */
        transform.TransformBlock(new byte[16], 0, 16, new byte[16], 0);
        
        var remaining = Data.Length - 32;
        while (remaining > 0)
        {
            if (remaining > 16)
            {
                remaining -= transform.TransformBlock(Data, Data.Length - remaining, 16, Data, Data.Length - remaining);
            }
            remaining -= Math.Min(144, remaining);
        }
    }

    public byte[] Write()
    {
        var syncByte = StartCodeLength == 4 ? SyncByteSeqStart : SyncByteSeq;
        return [..syncByte, ..Data];
    }
}