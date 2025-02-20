using System.Security.Cryptography;
using MediaFormatLibrary.AAC.ADTS;
using MediaFormatLibrary.H264.Enums;
using MediaFormatLibrary.Mpeg2;
using MediaFormatLibrary.Mpeg2.Enums;
using MediaFormatLibrary.Mpeg2.Helpers;
using MediaFormatLibrary.Mpeg2.PES;
using MediaFormatLibrary.Mpeg2.PES.Enums;
using MediaFormatLibrary.Mpeg2.PSI;

namespace N_m3u8DL_RE.Crypto;

public static class SampleAesUtil
{
    public static void DecryptSampleAes(string filePath, byte[] keyByte, byte[] ivByte)
    {
        using var aes = Aes.Create();

        aes.Key = keyByte;
        aes.IV = ivByte;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;
        
        var readStream = Mpeg2TransportStream.OpenTs(filePath, FileMode.Open, FileAccess.Read);
        var streamReader = new Mpeg2TransportStreamReader(readStream);

        var memoryStream = new MemoryStream();
        var writeStream = new Mpeg2TransportStream(memoryStream, false);
        var streamWriter = new Mpeg2TransportStreamWriter(writeStream);

        var pidMap = new Dictionary<ushort, StreamType>();
        
        while (streamReader.ReadPacketSequence() is { } tsPayload)
        {
            var packets = tsPayload.Packets!;
            var first = packets.First();

            switch (tsPayload.Type)
            {
                case TsPayloadType.Pmt:
                    var psis = ProgramSpecificInformationSection.ReadSection(first.Payload!);
                    
                    if (psis is ProgramMapSection pmt)
                    {
                        foreach (var entry in pmt.StreamEntries)
                        {
                            pidMap[entry.Pid] = entry.StreamType;
                                
                            entry.StreamType = entry.StreamType switch
                            {
                                (StreamType)0xDB => StreamType.H264,
                                (StreamType)0xCF => StreamType.Mpeg2AdtsAac,
                                (StreamType)0xC1 => StreamType.DolbyDigital,
                                (StreamType)0xC2 => StreamType.DolbyDigitalPlus,
                                _ => entry.StreamType
                            };
                        }
                    }
                    
                    writeStream.WritePacket(first);
                    break;
                case TsPayloadType.Pes:
                    var payload = TransportPacketHelper.AssemblePayload(packets);
                    var pesPacket = new PesPacket(payload);

                    if (pesPacket.Data!.Length < 3)
                    {
                        streamWriter.WritePesPacket(first.Header.Pid, pesPacket);
                        break;
                    }
                    
                    var streamType = pidMap[first.Header.Pid];
                    switch (streamType)
                    {
                        case StreamType.H264:
                        case (StreamType)0xDB:
                            pesPacket.DecryptH264Video(aes);
                            break;
                        case StreamType.Mpeg2AdtsAac:
                        case (StreamType)0xCF:
                            pesPacket.DecryptAacAudio(aes);
                            break;
                        case StreamType.DolbyDigital:
                        case (StreamType)0xC1:
                            pesPacket.DecryptAc3Audio(aes);
                            break;
                        case StreamType.DolbyDigitalPlus:
                        case (StreamType)0xC2:
                            pesPacket.DecryptEac3Audio(aes);
                            break;
                    }

                    streamWriter.WritePesPacket(first.Header.Pid, pesPacket);
                    break;
                default:
                    writeStream.WritePacket(first);
                    break;
            }
        }

        readStream.BaseStream.Dispose();

        File.WriteAllBytes(filePath, [..memoryStream.GetBuffer().Take((int)memoryStream.Length)]);

        writeStream.BaseStream.Dispose();
    }

    private static void DecryptH264Video(this PesPacket packet, Aes aes)
    {
        var bytes = new List<byte>();
        
        var data = packet.Data!;
        while (data.Length != 0 && NalUnit.GetNext(data) is var next)
        {
            data = next.Data;
            
            if (next.Unit.Type is NalUnitType.CodedSliceNonIdr or NalUnitType.CodedSliceIdr)
            {
                next.Unit.Decrypt(aes);
            }

            bytes.AddRange(next.Unit.Write());
        }

        packet.Data = bytes.ToArray();
    }

    private static void DecryptAacAudio(this PesPacket packet, Aes aes)
    {
        using var transform = aes.CreateDecryptor(aes.Key, aes.IV);
        
        /* Required when running in N_m3u8DL-RE for some reason */
        transform.TransformBlock(new byte[16], 0, 16, new byte[16], 0);
        
        using var stream = new MemoryStream(packet.Data!);
        
        var frame = new AdtsFrame(stream);
        
        foreach (var block in frame.RawDataBlocks)
        {
            var remaining = block!.Length - 16;
            while (remaining >= 16)
            {
                remaining -= transform.TransformBlock(block, block.Length - remaining, 16, block, block.Length - remaining);
            }
        }
        
        stream.Seek(0, SeekOrigin.Begin);
        frame.WriteBytes(stream);
    }

    private static void DecryptAc3Audio(this PesPacket packet, Aes aes)
    {
        using var transform = aes.CreateDecryptor(aes.Key, aes.IV);
        
        /* Required when running in N_m3u8DL-RE for some reason */
        transform.TransformBlock(new byte[16], 0, 16, new byte[16], 0);
        
        var readStream = new MemoryStream(packet.Data!);
        var writeStream = new MemoryStream(packet.Data!);
                            
        while (readStream.Position < readStream.Length && new MediaFormatLibrary.AC3.SyncFrame(readStream) is var frame)
        {
            var data = frame.GetBytes();
            
            var remaining = data.Length - 16;
            while (remaining >= 16)
            {
                remaining -= transform.TransformBlock(data, data.Length - remaining, 16, data, data.Length - remaining);
            }
            
            writeStream.Write(data, 0, data.Length);
        }
    }

    private static void DecryptEac3Audio(this PesPacket packet, Aes aes)
    {
        using var transform = aes.CreateDecryptor(aes.Key, aes.IV);
        
        /* Required when running in N_m3u8DL-RE for some reason */
        transform.TransformBlock(new byte[16], 0, 16, new byte[16], 0);
        
        var readStream = new MemoryStream(packet.Data!);
        var writeStream = new MemoryStream(packet.Data!);
                            
        while (readStream.Position < readStream.Length && new MediaFormatLibrary.E_AC3.SyncFrame(readStream) is var frame)
        {
            var data = frame.GetBytes();
            
            var remaining = data.Length - 16;
            while (remaining >= 16)
            {
                remaining -= transform.TransformBlock(data, data.Length - remaining, 16, data, data.Length - remaining);
            }
            
            writeStream.Write(data, 0, data.Length);
        }
    }
}