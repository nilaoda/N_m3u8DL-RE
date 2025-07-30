﻿using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Util;

// https://github.com/canalplus/rx-player/blob/48d1f845064cea5c5a3546d2c53b1855c2be149d/src/parsers/manifest/smooth/get_codecs.ts
// https://github.dev/Dash-Industry-Forum/dash.js/blob/2aad3e79079b4de0bcd961ce6b4957103d98a621/src/mss/MssFragmentMoovProcessor.js
// https://github.com/yt-dlp/yt-dlp/blob/3639df54c3298e35b5ae2a96a25bc4d3c38950d0/yt_dlp/downloader/ism.py
// https://github.com/google/ExoPlayer/blob/a9444c880230d2c2c79097e89259ce0b9f80b87d/library/extractor/src/main/java/com/google/android/exoplayer2/video/HevcConfig.java#L38
// https://github.com/sannies/mp4parser/blob/master/isoparser/src/main/java/org/mp4parser/boxes/iso14496/part15/HevcDecoderConfigurationRecord.java
namespace N_m3u8DL_RE.StreamParser.Mp4
{
    public partial class MSSMoovProcessor
    {
        [GeneratedRegex(@"\<KID\>(.*?)\<")]
        private static partial Regex KIDRegex();

        private static readonly string StartCode = "00000001";
        private readonly StreamSpec StreamSpec;
        private int TrackId = 2;
        private readonly string FourCC;
        private string CodecPrivateData;
        private readonly int Timesacle;
        private readonly long Duration;
        private string Language => StreamSpec.Language ?? "und";
        private int Width => int.Parse((StreamSpec.Resolution ?? "0x0").Split('x').First(), CultureInfo.InvariantCulture);
        private int Height => int.Parse((StreamSpec.Resolution ?? "0x0").Split('x').Last(), CultureInfo.InvariantCulture);
        private readonly string StreamType;
        private readonly int Channels;
        private readonly int BitsPerSample;
        private readonly int SamplingRate;
        private readonly int NalUnitLengthField;
        private readonly long CreationTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        private readonly bool IsProtection;
        private readonly string ProtectionSystemId;
        private readonly string ProtectionData;
        private string? ProtecitonKID;
#pragma warning disable IDE0052 // Remove unread private members
        private string? ProtecitonKID_PR;
#pragma warning restore IDE0052 // Remove unread private members
        private static byte[] UnityMatrix
        {
            get
            {
                using MemoryStream stream = new();
                using BinaryWriter2 writer = new(stream);
                writer.WriteInt(0x10000);
                writer.WriteInt(0);
                writer.WriteInt(0);
                writer.WriteInt(0);
                writer.WriteInt(0x10000);
                writer.WriteInt(0);
                writer.WriteInt(0);
                writer.WriteInt(0);
                writer.WriteInt(0x40000000);
                return stream.ToArray();
            }
        }
        private static readonly byte TRACK_ENABLED = 0x1;
        private static readonly byte TRACK_IN_MOVIE = 0x2;
        private static readonly byte TRACK_IN_PREVIEW = 0x4;
        private static readonly byte SELF_CONTAINED = 0x1;

        private static readonly List<string> SupportedFourCC =
            ["HVC1", "HEV1", "AACL", "AACH", "EC-3", "H264", "AVC1", "DAVC", "AVC1", "TTML", "DVHE", "DVH1"];

        public MSSMoovProcessor(StreamSpec streamSpec)
        {
            StreamSpec = streamSpec;
            MSSData data = streamSpec.MSSData!;
            NalUnitLengthField = data.NalUnitLengthField;
            CodecPrivateData = data.CodecPrivateData;
            FourCC = data.FourCC;
            Timesacle = data.Timesacle;
            Duration = data.Duration;
            StreamType = data.Type;
            Channels = data.Channels;
            SamplingRate = data.SamplingRate;
            BitsPerSample = data.BitsPerSample;
            IsProtection = data.IsProtection;
            ProtectionData = data.ProtectionData;
            ProtectionSystemId = data.ProtectionSystemID;

            // 需要手动生成CodecPrivateData
            if (string.IsNullOrEmpty(CodecPrivateData))
            {
                GenCodecPrivateDataForAAC();
            }

            // 解析KID
            if (IsProtection)
            {
                ExtractKID();
            }
        }

        private static readonly string[] HEVC_GENERAL_PROFILE_SPACE_STRINGS = ["", "A", "B", "C"];
        private static int SamplingFrequencyIndex(int samplingRate)
        {
            return samplingRate switch
            {
                96000 => 0x0,
                88200 => 0x1,
                64000 => 0x2,
                48000 => 0x3,
                44100 => 0x4,
                32000 => 0x5,
                24000 => 0x6,
                22050 => 0x7,
                16000 => 0x8,
                12000 => 0x9,
                11025 => 0xA,
                8000 => 0xB,
                7350 => 0xC,
                _ => 0x0
            };
        }

        private void GenCodecPrivateDataForAAC()
        {
            int objectType = 0x02; // AAC Main Low Complexity => object Type = 2
            int indexFreq = SamplingFrequencyIndex(SamplingRate);

            if (FourCC == "AACH")
            {
                // 4 bytes :     XXXXX         XXXX          XXXX             XXXX                  XXXXX      XXX   XXXXXXX
                //           ' ObjectType' 'Freq Index' 'Channels value'   'Extens Sampl Freq'  'ObjectType'  'GAS' 'alignment = 0'
                objectType = 0x05; // High Efficiency AAC Profile = object Type = 5 SBR
                byte[] codecPrivateData = new byte[4];
                int extensionSamplingFrequencyIndex = SamplingFrequencyIndex(SamplingRate * 2); // in HE AAC Extension Sampling frequence
                // equals to SamplingRate*2
                // Freq Index is present for 3 bits in the first byte, last bit is in the second
                codecPrivateData[0] = (byte)((objectType << 3) | (indexFreq >> 1));
                codecPrivateData[1] = (byte)((indexFreq << 7) | (Channels << 3) | (extensionSamplingFrequencyIndex >> 1));
                codecPrivateData[2] = (byte)((extensionSamplingFrequencyIndex << 7) | (0x02 << 2)); // origin object type equals to 2 => AAC Main Low Complexity
                codecPrivateData[3] = 0x0; // alignment bits

                ushort[] arr16 =
                [
                    (ushort)((codecPrivateData[0] << 8) + codecPrivateData[1]),
                    (ushort)((codecPrivateData[2] << 8) + codecPrivateData[3]),
                ];

                // convert decimal to hex value
                CodecPrivateData = HexUtil.BytesToHex(BitConverter.GetBytes(arr16[0])).PadLeft(16, '0');
                CodecPrivateData += HexUtil.BytesToHex(BitConverter.GetBytes(arr16[1])).PadLeft(16, '0');
            }
            else if (FourCC.StartsWith("AAC", StringComparison.OrdinalIgnoreCase))
            {
                // 2 bytes :     XXXXX         XXXX          XXXX              XXX
                //           ' ObjectType' 'Freq Index' 'Channels value'   'GAS = 000'
                byte[] codecPrivateData =
                [
                    // Freq Index is present for 3 bits in the first byte, last bit is in the second
                    (byte)((objectType << 3) | (indexFreq >> 1)),
                    (byte)((indexFreq << 7) | (Channels << 3)),
                ];
                // put the 2 bytes in an 16 bits array
                ushort[] arr16 = [(ushort)((codecPrivateData[0] << 8) + codecPrivateData[1])];

                // convert decimal to hex value
                CodecPrivateData = HexUtil.BytesToHex(BitConverter.GetBytes(arr16[0])).PadLeft(16, '0');
            }
        }

        private void ExtractKID()
        {
            // playready
            if (ProtectionSystemId.Equals("9A04F079-9840-4286-AB92-E65BE0885F95", StringComparison.OrdinalIgnoreCase))
            {
                byte[] bytes = HexUtil.HexToBytes(ProtectionData.Replace("00", ""));
                string text = Encoding.ASCII.GetString(bytes);
                byte[] kidBytes = Convert.FromBase64String(KIDRegex().Match(text).Groups[1].Value);
                // save kid for playready
                ProtecitonKID_PR = HexUtil.BytesToHex(kidBytes);
                // fix byte order
                byte[] reverse1 = [kidBytes[3], kidBytes[2], kidBytes[1], kidBytes[0]];
                byte[] reverse2 = [kidBytes[5], kidBytes[4], kidBytes[7], kidBytes[6]];
                Array.Copy(reverse1, 0, kidBytes, 0, reverse1.Length);
                Array.Copy(reverse2, 0, kidBytes, 4, reverse1.Length);
                ProtecitonKID = HexUtil.BytesToHex(kidBytes);
            }
            // widevine
            else if (ProtectionSystemId.Equals("EDEF8BA9-79D6-4ACE-A3C8-27DCD51D21ED", StringComparison.OrdinalIgnoreCase))
            {
                throw new NotSupportedException();
            }
        }

        public static bool CanHandle(string fourCC)
        {
            return SupportedFourCC.Contains(fourCC);
        }

        private static byte[] Box(string boxType, byte[] payload)
        {
            using MemoryStream stream = new();
            using BinaryWriter2 writer = new(stream);

            writer.WriteUInt(8 + (uint)payload.Length);
            writer.Write(boxType);
            writer.Write(payload);

            return stream.ToArray();
        }

        private static byte[] FullBox(string boxType, byte version, uint flags, byte[] payload)
        {
            using MemoryStream stream = new();
            using BinaryWriter2 writer = new(stream);

            writer.Write(version);
            writer.WriteUInt(flags, offset: 1);
            writer.Write(payload);

            return Box(boxType, stream.ToArray());
        }

        private byte[] GenSinf(string codec)
        {
            byte[] frmaBox = Box("frma", Encoding.ASCII.GetBytes(codec));

            List<byte> sinfPayload = [.. frmaBox];

            List<byte> schmPayload =
            [
                .. Encoding.ASCII.GetBytes("cenc"), // scheme_type 'cenc' => common encryption
0,
1,
0,
0,
            ];
            byte[] schmBox = FullBox("schm", 0, 0, [.. schmPayload]);

            sinfPayload.AddRange(schmBox);

            List<byte> tencPayload =
            [
0,
0,
                0x1, // default_IsProtected
                0x8, // default_Per_Sample_IV_size
                .. HexUtil.HexToBytes(ProtecitonKID ?? "00000000000000000000000000000000"), // default_KID
            ];
            // tencPayload.Add(0x8);// default_constant_IV_size
            // tencPayload.AddRange(new byte[8]);// default_constant_IV
            byte[] tencBox = FullBox("tenc", 0, 0, [.. tencPayload]);

            byte[] schiBox = Box("schi", tencBox);
            sinfPayload.AddRange(schiBox);

            byte[] sinfBox = Box("sinf", [.. sinfPayload]);

            return sinfBox;
        }

        private static byte[] GenFtyp()
        {
            using MemoryStream stream = new();
            using BinaryWriter2 writer = new(stream);

            writer.Write("isml"); // major brand
            writer.WriteUInt(1); // minor version
            writer.Write("iso5"); // compatible brand
            writer.Write("iso6"); // compatible brand
            writer.Write("piff"); // compatible brand
            writer.Write("msdh"); // compatible brand

            return Box("ftyp", stream.ToArray());
        }

        private byte[] GenMvhd()
        {
            using MemoryStream stream = new();
            using BinaryWriter2 writer = new(stream);

            writer.WriteULong(CreationTime); // creation_time
            writer.WriteULong(CreationTime); // modification_time
            writer.WriteUInt(Timesacle); // timescale
            writer.WriteULong(Duration); // duration
            writer.WriteUShort(1, padding: 2); // rate
            writer.WriteByte(1, padding: 1); // volume
            writer.WriteUShort(0); // reserved
            writer.WriteUInt(0);
            writer.WriteUInt(0);

            writer.Write(UnityMatrix);

            writer.WriteUInt(0); // pre defined
            writer.WriteUInt(0);
            writer.WriteUInt(0);
            writer.WriteUInt(0);
            writer.WriteUInt(0);
            writer.WriteUInt(0);

            writer.WriteUInt(0xffffffff); // next track id


            return FullBox("mvhd", 1, 0, stream.ToArray());
        }

        private byte[] GenTkhd()
        {
            using MemoryStream stream = new();
            using BinaryWriter2 writer = new(stream);

            writer.WriteULong(CreationTime); // creation_time
            writer.WriteULong(CreationTime); // modification_time
            writer.WriteUInt(TrackId); // track id
            writer.WriteUInt(0); // reserved
            writer.WriteULong(Duration); // duration
            writer.WriteUInt(0); // reserved
            writer.WriteUInt(0);
            writer.WriteShort(0); // layer
            writer.WriteShort(0); // alternate group
            writer.WriteByte(StreamType == "audio" ? (byte)1 : (byte)0, padding: 1); // volume
            writer.WriteUShort(0); // reserved

            writer.Write(UnityMatrix);

            writer.WriteUShort(Width, padding: 2); // width
            writer.WriteUShort(Height, padding: 2); // height

            return FullBox("tkhd", 1, (uint)TRACK_ENABLED | TRACK_IN_MOVIE | TRACK_IN_PREVIEW, stream.ToArray());
        }


        private byte[] GenMdhd()
        {
            using MemoryStream stream = new();
            using BinaryWriter2 writer = new(stream);

            writer.WriteULong(CreationTime); // creation_time
            writer.WriteULong(CreationTime); // modification_time
            writer.WriteUInt(Timesacle); // timescale
            writer.WriteULong(Duration); // duration
            writer.WriteUShort(((Language[0] - 0x60) << 10) | ((Language[1] - 0x60) << 5) | (Language[2] - 0x60)); // language
            writer.WriteUShort(0); // pre defined

            return FullBox("mdhd", 1, 0, stream.ToArray());
        }

        private byte[] GenHdlr()
        {
            using MemoryStream stream = new();
            using BinaryWriter2 writer = new(stream);

            writer.WriteUInt(0); // pre defined
            if (StreamType == "audio")
            {
                writer.Write("soun");
            }
            else if (StreamType == "video")
            {
                writer.Write("vide");
            }
            else if (StreamType == "text")
            {
                writer.Write("subt");
            }
            else
            {
                throw new NotSupportedException();
            }

            writer.WriteUInt(0); // reserved
            writer.WriteUInt(0);
            writer.WriteUInt(0);
            writer.Write($"{StreamSpec.GroupId ?? "RE Handler"}\0"); // name

            return FullBox("hdlr", 0, 0, stream.ToArray());
        }

        private byte[] GenMinf()
        {
            using MemoryStream stream = new();
            using BinaryWriter2 writer = new(stream);

            List<byte> minfPayload = [];
            if (StreamType == "audio")
            {
                List<byte> smhd =
                [
                    0,
                    0, // balance
                    0,
                    0 // reserved
                ];

                minfPayload.AddRange(FullBox("smhd", 0, 0, [.. smhd])); // Sound Media Header
            }
            else if (StreamType == "video")
            {
                List<byte> vmhd =
                [
                    0,
                    0, // graphics mode
                    0,
                    0,
                    0,
                    0,
                    0,
                    0// opcolor
                ];

                minfPayload.AddRange(FullBox("vmhd", 0, 1, [.. vmhd])); // Video Media Header
            }
            else if (StreamType == "text")
            {
                minfPayload.AddRange(FullBox("sthd", 0, 0, [])); // Subtitle Media Header
            }
            else
            {
                throw new NotSupportedException();
            }

            List<byte> drefPayload =
            [
                0,
                0,
                0,
                1,
                .. FullBox("url ", 0, SELF_CONTAINED, []), // entry count
            ];

            byte[] dinfPayload = FullBox("dref", 0, 0, [.. drefPayload]); // Data Reference Box
            minfPayload.AddRange(Box("dinf", [.. dinfPayload])); // Data Information Box

            return [.. minfPayload];
        }

        private byte[] GenEsds(byte[] audioSpecificConfig)
        {
            using MemoryStream stream = new();
            using BinaryWriter2 writer = new(stream);

            // ESDS length = esds box header length (= 12) +
            //               ES_Descriptor header length (= 5) +
            //               DecoderConfigDescriptor header length (= 15) +
            //               decoderSpecificInfo header length (= 2) +
            //               AudioSpecificConfig length (= codecPrivateData length)
            // esdsLength = 34 + len(audioSpecificConfig)

            // ES_Descriptor (see ISO/IEC 14496-1 (Systems))
            writer.WriteByte(0x03); // tag = 0x03 (ES_DescrTag)
            writer.WriteByte((byte)(20 + audioSpecificConfig.Length)); // size
            writer.WriteByte((byte)((TrackId & 0xFF00) >> 8)); // ES_ID = track_id
            writer.WriteByte((byte)(TrackId & 0x00FF));
            writer.WriteByte(0); // flags and streamPriority

            // DecoderConfigDescriptor (see ISO/IEC 14496-1 (Systems))
            writer.WriteByte(0x04); // tag = 0x04 (DecoderConfigDescrTag)
            writer.WriteByte((byte)(15 + audioSpecificConfig.Length)); // size
            writer.WriteByte(0x40); // objectTypeIndication = 0x40 (MPEG-4 AAC)
            writer.WriteByte((0x05 << 2) | (0 << 1) | 1); // reserved = 1
            writer.WriteByte(0xFF); // buffersizeDB = undefined
            writer.WriteByte(0xFF);
            writer.WriteByte(0xFF);

            int? bandwidth = StreamSpec.Bandwidth!;
            writer.WriteByte((byte)((bandwidth & 0xFF000000) >> 24)); // maxBitrate
            writer.WriteByte((byte)((bandwidth & 0x00FF0000) >> 16));
            writer.WriteByte((byte)((bandwidth & 0x0000FF00) >> 8));
            writer.WriteByte((byte)(bandwidth & 0x000000FF));
            writer.WriteByte((byte)((bandwidth & 0xFF000000) >> 24)); // avgbitrate
            writer.WriteByte((byte)((bandwidth & 0x00FF0000) >> 16));
            writer.WriteByte((byte)((bandwidth & 0x0000FF00) >> 8));
            writer.WriteByte((byte)(bandwidth & 0x000000FF));

            // DecoderSpecificInfo (see ISO/IEC 14496-1 (Systems))
            writer.WriteByte(0x05); // tag = 0x05 (DecSpecificInfoTag)
            writer.WriteByte((byte)audioSpecificConfig.Length); // size
            writer.Write(audioSpecificConfig); // AudioSpecificConfig bytes

            return FullBox("esds", 0, 0, stream.ToArray());
        }

        private byte[] GetSampleEntryBox()
        {
            using MemoryStream stream = new();
            using BinaryWriter2 writer = new(stream);

            writer.WriteByte(0); // reserved
            writer.WriteByte(0);
            writer.WriteByte(0);
            writer.WriteByte(0);
            writer.WriteByte(0);
            writer.WriteByte(0);
            writer.WriteUShort(1); // data reference index

            if (StreamType == "audio")
            {
                writer.WriteUInt(0); // reserved2
                writer.WriteUInt(0);
                writer.WriteUShort(Channels); // channels
                writer.WriteUShort(BitsPerSample); // bits_per_sample
                writer.WriteUShort(0); // pre defined
                writer.WriteUShort(0); // reserved3
                writer.WriteUShort(SamplingRate, padding: 2); // sampling_rate

                byte[] audioSpecificConfig = HexUtil.HexToBytes(CodecPrivateData);
                byte[] esdsBox = GenEsds(audioSpecificConfig);
                writer.Write(esdsBox);

                if (FourCC.StartsWith("AAC", StringComparison.OrdinalIgnoreCase))
                {
                    if (IsProtection)
                    {
                        byte[] sinfBox = GenSinf("mp4a");
                        writer.Write(sinfBox);
                        return Box("enca", stream.ToArray()); // Encrypted Audio
                    }
                    return Box("mp4a", stream.ToArray());
                }
                if (FourCC == "EC-3")
                {
                    if (IsProtection)
                    {
                        byte[] sinfBox = GenSinf("ec-3");
                        writer.Write(sinfBox);
                        return Box("enca", stream.ToArray()); // Encrypted Audio
                    }
                    return Box("ec-3", stream.ToArray());
                }
            }
            else if (StreamType == "video")
            {
                writer.WriteUShort(0); // pre defined
                writer.WriteUShort(0); // reserved
                writer.WriteUInt(0); // pre defined
                writer.WriteUInt(0);
                writer.WriteUInt(0);
                writer.WriteUShort(Width); // width
                writer.WriteUShort(Height); // height
                writer.WriteUShort(0x48, padding: 2); // horiz resolution 72 dpi
                writer.WriteUShort(0x48, padding: 2); // vert resolution 72 dpi
                writer.WriteUInt(0); // reserved
                writer.WriteUShort(1); // frame count
                for (int i = 0; i < 32; i++) // compressor name
                {
                    writer.WriteByte(0);
                }
                writer.WriteUShort(0x18); // depth
                writer.WriteUShort(65535); // pre defined

                byte[] codecPrivateData = HexUtil.HexToBytes(CodecPrivateData);

                if (FourCC is "H264" or "AVC1" or "DAVC")
                {
                    string[] arr = CodecPrivateData.Split([StartCode], StringSplitOptions.RemoveEmptyEntries);
                    byte[] sps = HexUtil.HexToBytes(arr.First(x => (HexUtil.HexToBytes(x[0..2])[0] & 0x1F) == 7));
                    byte[] pps = HexUtil.HexToBytes(arr.First(x => (HexUtil.HexToBytes(x[0..2])[0] & 0x1F) == 8));
                    // make avcC
                    byte[] avcC = GetAvcC(sps, pps);
                    writer.Write(avcC);
                    if (IsProtection)
                    {
                        byte[] sinfBox = GenSinf("avc1");
                        writer.Write(sinfBox);
                        return Box("encv", stream.ToArray()); // Encrypted Video
                    }
                    return Box("avc1", stream.ToArray()); // AVC Simple Entry
                }
                if (FourCC is "HVC1" or "HEV1")
                {
                    string[] arr = CodecPrivateData.Split([StartCode], StringSplitOptions.RemoveEmptyEntries);
                    byte[] vps = HexUtil.HexToBytes(arr.First(x => (HexUtil.HexToBytes(x[0..2])[0] >> 1) == 0x20));
                    byte[] sps = HexUtil.HexToBytes(arr.First(x => (HexUtil.HexToBytes(x[0..2])[0] >> 1) == 0x21));
                    byte[] pps = HexUtil.HexToBytes(arr.First(x => (HexUtil.HexToBytes(x[0..2])[0] >> 1) == 0x22));
                    // make hvcC
                    byte[] hvcC = GetHvcC(sps, pps, vps);
                    writer.Write(hvcC);
                    if (IsProtection)
                    {
                        byte[] sinfBox = GenSinf("hvc1");
                        writer.Write(sinfBox);
                        return Box("encv", stream.ToArray()); // Encrypted Video
                    }
                    return Box("hvc1", stream.ToArray()); // HEVC Simple Entry
                }
                // 杜比视界也按照hevc处理
                if (FourCC is "DVHE" or "DVH1")
                {
                    string[] arr = CodecPrivateData.Split([StartCode], StringSplitOptions.RemoveEmptyEntries);
                    byte[] vps = HexUtil.HexToBytes(arr.First(x => (HexUtil.HexToBytes(x[0..2])[0] >> 1) == 0x20));
                    byte[] sps = HexUtil.HexToBytes(arr.First(x => (HexUtil.HexToBytes(x[0..2])[0] >> 1) == 0x21));
                    byte[] pps = HexUtil.HexToBytes(arr.First(x => (HexUtil.HexToBytes(x[0..2])[0] >> 1) == 0x22));
                    // make hvcC
                    byte[] hvcC = GetHvcC(sps, pps, vps, "dvh1");
                    writer.Write(hvcC);
                    if (IsProtection)
                    {
                        byte[] sinfBox = GenSinf("dvh1");
                        writer.Write(sinfBox);
                        return Box("encv", stream.ToArray()); // Encrypted Video
                    }
                    return Box("dvh1", stream.ToArray()); // HEVC Simple Entry
                }

                throw new NotSupportedException();
            }
            else if (StreamType == "text")
            {
                if (FourCC == "TTML")
                {
                    writer.Write("http://www.w3.org/ns/ttml\0"); // namespace
                    writer.Write("\0"); // schema location
                    writer.Write("\0"); // auxilary mime types(??)
                    return Box("stpp", stream.ToArray()); // TTML Simple Entry
                }
                throw new NotSupportedException();
            }
            else
            {
                throw new NotSupportedException();
            }

            throw new NotSupportedException();
        }

        private byte[] GetAvcC(byte[] sps, byte[] pps)
        {
            using MemoryStream stream = new();
            using BinaryWriter2 writer = new(stream);

            writer.WriteByte(1); // configuration version
            writer.Write(sps[1..4]); // avc profile indication + profile compatibility + avc level indication
            writer.WriteByte((byte)(0xfc | (NalUnitLengthField - 1))); // complete representation (1) + reserved (11111) + length size minus one
            writer.WriteByte(1); // reserved (0) + number of sps (0000001)
            writer.WriteUShort(sps.Length);
            writer.Write(sps);
            writer.WriteByte(1); // number of pps
            writer.WriteUShort(pps.Length);
            writer.Write(pps);

            return Box("avcC", stream.ToArray()); // AVC Decoder Configuration Record
        }

        private byte[] GetHvcC(byte[] sps, byte[] pps, byte[] vps, string code = "hvc1")
        {
            List<byte> oriSps = [.. sps];
            // https://www.itu.int/rec/dologin.asp?lang=f&id=T-REC-H.265-201504-S!!PDF-E&type=items
            // Read generalProfileSpace, generalTierFlag, generalProfileIdc,
            // generalProfileCompatibilityFlags, constraintBytes, generalLevelIdc
            // from sps
            List<byte> encList = [];
            /**
             * 处理payload, 有00 00 03 0,1,2,3的情况 统一换成00 00 XX 即丢弃03
             * 注意：此处采用的逻辑是直接简单粗暴地判断列表末尾3字节，如果是0x000003就删掉最后的0x03，可能会导致以下情况
             * 00 00 03 03 03 03 03 01 会被直接处理成 => 00 00 01
             * 此处经过测试只有直接跳过才正常，如果处理成 00 00 03 03 03 03 01 是有问题的
             *
             * 测试的数据如下：
             *   原始：42 01 01 01 60 00 00 03 00 90 00 00 03 00 00 03 00 96 a0 01 e0 20 06 61 65 95 9a 49 30 bf fc 0c 7c 0c 81 a8 08 08 08 20 00 00 03 00 20 00 00 03 03 01
             * 处理后：42 01 01 01 60 00 00 00 90 00 00 00 00 00 96 A0 01 E0 20 06 61 65 95 9A 49 30 BF FC 0C 7C 0C 81 A8 08 08 08 20 00 00 00 20 00 00 01
             */
            using (BinaryReader _reader = new(new MemoryStream(sps)))
            {
                while (_reader.BaseStream.Position < _reader.BaseStream.Length)
                {
                    encList.Add(_reader.ReadByte());
                    if (encList is [.., 0x00, 0x00, 0x03])
                    {
                        encList.RemoveAt(encList.Count - 1);
                    }
                }
            }
            sps = [.. encList];

            using BinaryReader2 reader = new(new MemoryStream(sps));
            _ = reader.ReadBytes(2); // Skip 2 bytes unit header
            byte firstByte = reader.ReadByte();
            // int maxSubLayersMinus1 = (firstByte & 0xe) >> 1;
            byte nextByte = reader.ReadByte();
            int generalProfileSpace = (nextByte & 0xc0) >> 6;
            int generalTierFlag = (nextByte & 0x20) >> 5;
            int generalProfileIdc = nextByte & 0x1f;
            uint generalProfileCompatibilityFlags = reader.ReadUInt32();
            byte[] constraintBytes = reader.ReadBytes(6);
            byte generalLevelIdc = reader.ReadByte();

            /*var skipBit = 0;
            for (int i = 0; i < maxSubLayersMinus1; i++)
            {
                skipBit += 2; // sub_layer_profile_present_flag sub_layer_level_present_flag
            }
            if (maxSubLayersMinus1 > 0)
            {
                for (int i = maxSubLayersMinus1; i < 8; i++)
                {
                    skipBit += 2; // reserved_zero_2bits
                }
            }
            for (int i = 0; i < maxSubLayersMinus1; i++)
            {
                skipBit += 2; // sub_layer_profile_present_flag sub_layer_level_present_flag
            }*/

            // 生成编码信息
            string codecs = code +
                         $".{HEVC_GENERAL_PROFILE_SPACE_STRINGS[generalProfileSpace]}{generalProfileIdc}" +
                         $".{Convert.ToString(generalProfileCompatibilityFlags, 16)}" +
                         $".{(generalTierFlag == 1 ? 'H' : 'L')}{generalLevelIdc}" +
                         $".{HexUtil.BytesToHex([.. constraintBytes.Where(b => b != 0)])}";
            StreamSpec.Codecs = codecs;


            ///////////////////////


            using MemoryStream stream = new();
            using BinaryWriter2 writer = new(stream);

            // var reserved1 = 0xF;

            writer.WriteByte(1); // configuration version
            writer.WriteByte((byte)(((generalProfileSpace << 6) + (generalTierFlag == 1 ? 0x20 : 0)) | generalProfileIdc)); // general_profile_space + general_tier_flag + general_profile_idc
            writer.WriteUInt(generalProfileCompatibilityFlags); // general_profile_compatibility_flags
            writer.Write(constraintBytes); // general_constraint_indicator_flags
            writer.WriteByte((byte)generalProfileIdc); // general_level_idc
            writer.WriteUShort(0xf000); // reserved + min_spatial_segmentation_idc
            writer.WriteByte(0xfc); // reserved + parallelismType
            writer.WriteByte(0 | 0xfc); // reserved + chromaFormat 
            writer.WriteByte(0 | 0xf8); // reserved + bitDepthLumaMinus8
            writer.WriteByte(0 | 0xf8); // reserved + bitDepthChromaMinus8
            writer.WriteUShort(0); // avgFrameRate
            writer.WriteByte((byte)((0 << 6) | (0 << 3) | (0 << 2) | (NalUnitLengthField - 1))); // constantFrameRate + numTemporalLayers + temporalIdNested + lengthSizeMinusOne
            writer.WriteByte(0x03); // numOfArrays (vps sps pps)

            sps = [.. oriSps];
            writer.WriteByte(0x20); // array_completeness + reserved + NAL_unit_type
            writer.WriteUShort(1); // numNalus 
            writer.WriteUShort(vps.Length);
            writer.Write(vps);
            writer.WriteByte(0x21);
            writer.WriteUShort(1); // numNalus
            writer.WriteUShort(sps.Length);
            writer.Write(sps);
            writer.WriteByte(0x22);
            writer.WriteUShort(1); // numNalus
            writer.WriteUShort(pps.Length);
            writer.Write(pps);

            return Box("hvcC", stream.ToArray()); // HEVC Decoder Configuration Record
        }

        private byte[] GetStsd()
        {
            using MemoryStream stream = new();
            using BinaryWriter2 writer = new(stream);

            writer.WriteUInt(1); // entry count
            byte[] sampleEntryData = GetSampleEntryBox();
            writer.Write(sampleEntryData);

            return stream.ToArray();
        }

        private byte[] GetMehd()
        {
            using MemoryStream stream = new();
            using BinaryWriter2 writer = new(stream);

            writer.WriteULong(Duration);

            return FullBox("mehd", 1, 0, stream.ToArray()); // Movie Extends Header Box
        }
        private byte[] GetTrex()
        {
            using MemoryStream stream = new();
            using BinaryWriter2 writer = new(stream);

            writer.WriteUInt(TrackId); // track id
            writer.WriteUInt(1); // default sample description index
            writer.WriteUInt(0); // default sample duration
            writer.WriteUInt(0); // default sample size
            writer.WriteUInt(0); // default sample flags

            return FullBox("trex", 0, 0, stream.ToArray()); // Track Extends Box
        }

        private byte[] GenPsshBoxForPlayReady()
        {
            using MemoryStream _stream = new();
            using BinaryWriter2 _writer = new(_stream);
            byte[] sysIdData = HexUtil.HexToBytes(ProtectionSystemId.Replace("-", ""));
            byte[] psshData = HexUtil.HexToBytes(ProtectionData);

            _writer.Write(sysIdData);  // SystemID 16 bytes
            _writer.WriteUInt(psshData.Length); // Size of Data 4 bytes
            _writer.Write(psshData); // Data
            byte[] psshBox = FullBox("pssh", 0, 0, _stream.ToArray());
            return psshBox;
        }

        private byte[] GenPsshBoxForWideVine()
        {
            using MemoryStream _stream = new();
            using BinaryWriter2 _writer = new(_stream);
            byte[] sysIdData = HexUtil.HexToBytes("edef8ba9-79d6-4ace-a3c8-27dcd51d21ed".Replace("-", ""));
            // var kid = HexUtil.HexToBytes(ProtecitonKID);

            _writer.Write(sysIdData);  // SystemID 16 bytes
            byte[] psshData = HexUtil.HexToBytes($"08011210{ProtecitonKID}1A046E647265220400000000");
            _writer.WriteUInt(psshData.Length); // Size of Data 4 bytes
            _writer.Write(psshData); // Data
            byte[] psshBox = FullBox("pssh", 0, 0, _stream.ToArray());
            return psshBox;
        }

        public byte[] GenHeader(byte[] firstSegment)
        {
            new MP4Parser()
                .Box("moof", MP4Parser.Children)
                .Box("traf", MP4Parser.Children)
                .FullBox("tfhd", box =>
                {
                    TrackId = (int)box.Reader.ReadUInt32();
                })
                .Parse(firstSegment);

            return GenHeader();
        }

        public byte[] GenHeader()
        {
            using MemoryStream stream = new();

            byte[] ftyp = GenFtyp(); // File Type Box
            stream.Write(ftyp);

            byte[] moovPayload = GenMvhd(); // Movie Header Box

            byte[] trakPayload = GenTkhd(); // Track Header Box

            byte[] mdhdPayload = GenMdhd(); // Media Header Box

            byte[] hdlrPayload = GenHdlr(); // Handler Reference Box

            byte[] mdiaPayload = [.. mdhdPayload, .. hdlrPayload];

            byte[] minfPayload = GenMinf();


            byte[] sttsPayload = [0, 0, 0, 0]; // entry count
            byte[] stblPayload = FullBox("stts", 0, 0, sttsPayload); // Decoding Time to Sample Box

            byte[] stscPayload = [0, 0, 0, 0]; // entry count
            byte[] stscBox = FullBox("stsc", 0, 0, stscPayload); // Sample To Chunk Box

            byte[] stcoPayload = [0, 0, 0, 0]; // entry count
            byte[] stcoBox = FullBox("stco", 0, 0, stcoPayload); // Chunk Offset Box

            byte[] stszPayload = [0, 0, 0, 0, 0, 0, 0, 0]; // sample size, sample count
            byte[] stszBox = FullBox("stsz", 0, 0, stszPayload); // Sample Size Box

            byte[] stsdPayload = GetStsd();
            byte[] stsdBox = FullBox("stsd", 0, 0, stsdPayload); // Sample Description Box

            stblPayload = [.. stblPayload, .. stscBox, .. stcoBox, .. stszBox, .. stsdBox];


            byte[] stblBox = Box("stbl", stblPayload); // Sample Table Box
            minfPayload = [.. minfPayload, .. stblBox];

            byte[] minfBox = Box("minf", minfPayload); // Media Information Box
            mdiaPayload = [.. mdiaPayload, .. minfBox];

            byte[] mdiaBox = Box("mdia", mdiaPayload); // Media Box
            trakPayload = [.. trakPayload, .. mdiaBox];

            byte[] trakBox = Box("trak", trakPayload); // Track Box
            moovPayload = [.. moovPayload, .. trakBox];

            byte[] mvexPayload = GetMehd();
            byte[] trexBox = GetTrex();
            mvexPayload = [.. mvexPayload, .. trexBox];

            byte[] mvexBox = Box("mvex", mvexPayload); // Movie Extends Box
            moovPayload = [.. moovPayload, .. mvexBox];

            if (IsProtection)
            {
                byte[] psshBox1 = GenPsshBoxForPlayReady();
                byte[] psshBox2 = GenPsshBoxForWideVine();
                moovPayload = [.. moovPayload, .. psshBox1, .. psshBox2];
            }

            byte[] moovBox = Box("moov", moovPayload); // Movie Box

            stream.Write(moovBox);

            // var moofBox = GenMoof(); // Movie Extends Box
            // stream.Write(moofBox);

            return stream.ToArray();
        }
    }
}