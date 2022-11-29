using Mp4SubtitleParser;
using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Util;
using System.Text;
using System.Text.RegularExpressions;

//https://github.com/canalplus/rx-player/blob/48d1f845064cea5c5a3546d2c53b1855c2be149d/src/parsers/manifest/smooth/get_codecs.ts
//https://github.dev/Dash-Industry-Forum/dash.js/blob/2aad3e79079b4de0bcd961ce6b4957103d98a621/src/mss/MssFragmentMoovProcessor.js
//https://github.com/yt-dlp/yt-dlp/blob/3639df54c3298e35b5ae2a96a25bc4d3c38950d0/yt_dlp/downloader/ism.py
//https://github.com/google/ExoPlayer/blob/a9444c880230d2c2c79097e89259ce0b9f80b87d/library/extractor/src/main/java/com/google/android/exoplayer2/video/HevcConfig.java#L38
//https://github.com/sannies/mp4parser/blob/master/isoparser/src/main/java/org/mp4parser/boxes/iso14496/part15/HevcDecoderConfigurationRecord.java
namespace N_m3u8DL_RE.Parser.Mp4
{
    public partial class MSSMoovProcessor
    {
        [GeneratedRegex("\\<KID\\>(.*?)\\<")]
        private static partial Regex KIDRegex();

        private static string StartCode = "00000001";
        private StreamSpec StreamSpec;
        private int TrackId = 2;
        private string FourCC;
        private string CodecPrivateData;
        private int Timesacle;
        private long Duration;
        private string Language { get => StreamSpec.Language ?? "und"; }
        private int Width { get => int.Parse((StreamSpec.Resolution ?? "0x0").Split('x').First()); }
        private int Height { get => int.Parse((StreamSpec.Resolution ?? "0x0").Split('x').Last()); }
        private string StreamType;
        private int Channels;
        private int BitsPerSample;
        private int SamplingRate;
        private int NalUnitLengthField;
        private long CreationTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        private bool IsProtection;
        private string ProtectionSystemId;
        private string ProtectionData;
        private string ProtecitonKID;
        private string ProtecitonKID_PR;
        private byte[] UnityMatrix
        {
            get
            {
                using var stream = new MemoryStream();
                using var writer = new BinaryWriter2(stream);
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
        private static byte TRACK_ENABLED = 0x1;
        private static byte TRACK_IN_MOVIE = 0x2;
        private static byte TRACK_IN_PREVIEW = 0x4;
        private static byte SELF_CONTAINED = 0x1;
        private static List<string> SupportedFourCC = new List<string>()
        {
            "HVC1","HEV1","AACL","AACH","EC-3","H264","AVC1","DAVC","AVC1","TTML"
        };

        public MSSMoovProcessor(StreamSpec streamSpec)
        {
            this.StreamSpec = streamSpec;
            var data = streamSpec.MSSData!;
            this.NalUnitLengthField = data.NalUnitLengthField;
            this.CodecPrivateData = data.CodecPrivateData;
            this.FourCC = data.FourCC;
            this.Timesacle = data.Timesacle;
            this.Duration = data.Duration;
            this.StreamType = data.Type;
            this.Channels = data.Channels;
            this.SamplingRate = data.SamplingRate;
            this.BitsPerSample = data.BitsPerSample;
            this.IsProtection = data.IsProtection;
            this.ProtectionData = data.ProtectionData;
            this.ProtectionSystemId = data.ProtectionSystemID;

            //需要手动生成CodecPrivateData
            if (string.IsNullOrEmpty(CodecPrivateData))
            {
                GenCodecPrivateDataForAAC();
            }

            //解析KID
            if (IsProtection)
            {
                ExtractKID();
            }
        }

        private static string[] HEVC_GENERAL_PROFILE_SPACE_STRINGS = new string[] { "", "A", "B", "C" };
        private int SamplingFrequencyIndex(int samplingRate) => samplingRate switch
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

        private void GenCodecPrivateDataForAAC()
        {
            var objectType = 0x02; //AAC Main Low Complexity => object Type = 2
            var indexFreq = SamplingFrequencyIndex(SamplingRate);

            if (FourCC == "AACH")
            {
                // 4 bytes :     XXXXX         XXXX          XXXX             XXXX                  XXXXX      XXX   XXXXXXX
                //           ' ObjectType' 'Freq Index' 'Channels value'   'Extens Sampl Freq'  'ObjectType'  'GAS' 'alignment = 0'
                objectType = 0x05; // High Efficiency AAC Profile = object Type = 5 SBR
                var codecPrivateData = new byte[4];
                var extensionSamplingFrequencyIndex = SamplingFrequencyIndex(SamplingRate * 2); // in HE AAC Extension Sampling frequence
                // equals to SamplingRate*2
                //Freq Index is present for 3 bits in the first byte, last bit is in the second
                codecPrivateData[0] = (byte)((objectType << 3) | (indexFreq >> 1));
                codecPrivateData[1] = (byte)((indexFreq << 7) | (Channels << 3) | (extensionSamplingFrequencyIndex >> 1));
                codecPrivateData[2] = (byte)((extensionSamplingFrequencyIndex << 7) | (0x02 << 2)); // origin object type equals to 2 => AAC Main Low Complexity
                codecPrivateData[3] = 0x0; //alignment bits

                var arr16 = new ushort[2];
                arr16[0] = (ushort)((codecPrivateData[0] << 8) + codecPrivateData[1]);
                arr16[1] = (ushort)((codecPrivateData[2] << 8) + codecPrivateData[3]);

                //convert decimal to hex value
                this.CodecPrivateData = HexUtil.BytesToHex(BitConverter.GetBytes(arr16[0])).PadLeft(16, '0');
                this.CodecPrivateData += HexUtil.BytesToHex(BitConverter.GetBytes(arr16[1])).PadLeft(16, '0');
            }
            else if (FourCC.StartsWith("AAC")) 
            {
                // 2 bytes :     XXXXX         XXXX          XXXX              XXX
                //           ' ObjectType' 'Freq Index' 'Channels value'   'GAS = 000'
                var codecPrivateData = new byte[2];
                //Freq Index is present for 3 bits in the first byte, last bit is in the second
                codecPrivateData[0] = (byte)((objectType << 3) | (indexFreq >> 1));
                codecPrivateData[1] = (byte)((indexFreq << 7) | Channels << 3);
                // put the 2 bytes in an 16 bits array
                var arr16 = new ushort[1];
                arr16[0] = (ushort)((codecPrivateData[0] << 8) + codecPrivateData[1]);

                //convert decimal to hex value
                this.CodecPrivateData = HexUtil.BytesToHex(BitConverter.GetBytes(arr16[0])).PadLeft(16, '0');
            }
        }

        private void ExtractKID()
        {
            //playready
            if (ProtectionSystemId.ToUpper() == "9A04F079-9840-4286-AB92-E65BE0885F95")
            {
                var bytes = HexUtil.HexToBytes(ProtectionData.Replace("00", ""));
                var text = Encoding.ASCII.GetString(bytes);
                var kidBytes = Convert.FromBase64String(KIDRegex().Match(text).Groups[1].Value);
                //save kid for playready
                this.ProtecitonKID_PR = HexUtil.BytesToHex(kidBytes);
                //fix byte order
                var reverse1 = new byte[4] { kidBytes[3], kidBytes[2], kidBytes[1], kidBytes[0] };
                var reverse2 = new byte[4] { kidBytes[5], kidBytes[4], kidBytes[7], kidBytes[6] };
                Array.Copy(reverse1, 0, kidBytes, 0, reverse1.Length);
                Array.Copy(reverse2, 0, kidBytes, 4, reverse1.Length);
                this.ProtecitonKID = HexUtil.BytesToHex(kidBytes);
            }
            //widevine
            else if (ProtectionSystemId.ToUpper() == "EDEF8BA9-79D6-4ACE-A3C8-27DCD51D21ED")
            {
                throw new NotSupportedException();
            }
        }

        public static bool CanHandle(string fourCC) => SupportedFourCC.Contains(fourCC);

        private byte[] Box(string boxType, byte[] payload)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter2(stream);

            writer.WriteUInt(8 + (uint)payload.Length);
            writer.Write(boxType);
            writer.Write(payload);

            return stream.ToArray();
        }

        private byte[] FullBox(string boxType, byte version, uint flags, byte[] payload)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter2(stream);

            writer.Write(version);
            writer.WriteUInt(flags, offset: 1);
            writer.Write(payload);

            return Box(boxType, stream.ToArray());
        }

        private byte[] GenSinf(string codec)
        {
            var frmaBox = Box("frma", Encoding.ASCII.GetBytes(codec));

            var sinfPayload = new List<byte>();
            sinfPayload.AddRange(frmaBox);

            var schmPayload = new List<byte>();
            schmPayload.AddRange(Encoding.ASCII.GetBytes("cenc")); //scheme_type 'cenc' => common encryption
            schmPayload.AddRange(new byte[] { 0, 1, 0, 0 }); //scheme_version Major version 1, Minor version 0
            var schmBox = FullBox("schm", 0, 0, schmPayload.ToArray());

            sinfPayload.AddRange(schmBox);

            var tencPayload = new List<byte>();
            tencPayload.AddRange(new byte[] { 0, 0 });
            tencPayload.Add(0x1); //default_IsProtected
            tencPayload.Add(0x8); //default_Per_Sample_IV_size
            tencPayload.AddRange(HexUtil.HexToBytes(ProtecitonKID)); //default_KID
            //tencPayload.Add(0x8);//default_constant_IV_size
            //tencPayload.AddRange(new byte[8]);//default_constant_IV
            var tencBox = FullBox("tenc", 0, 0, tencPayload.ToArray());

            var schiBox = Box("schi", tencBox);
            sinfPayload.AddRange(schiBox);

            var sinfBox = Box("sinf", sinfPayload.ToArray());

            return sinfBox;
        }

        private byte[] GenFtyp()
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter2(stream);

            writer.Write("isml"); //major brand
            writer.WriteUInt(1); //minor version
            writer.Write("iso5"); //compatible brand
            writer.Write("iso6"); //compatible brand
            writer.Write("piff"); //compatible brand
            writer.Write("msdh"); //compatible brand

            return Box("ftyp", stream.ToArray());
        }

        private byte[] GenMvhd()
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter2(stream);

            writer.WriteULong(CreationTime); //creation_time
            writer.WriteULong(CreationTime); //modification_time
            writer.WriteUInt(Timesacle); //timescale
            writer.WriteULong(Duration); //duration
            writer.WriteUShort(1, padding: 2); //rate
            writer.WriteByte(1, padding: 1); //volume
            writer.WriteUShort(0); //reserved
            writer.WriteUInt(0);
            writer.WriteUInt(0);

            writer.Write(UnityMatrix);

            writer.WriteUInt(0); //pre defined
            writer.WriteUInt(0);
            writer.WriteUInt(0);
            writer.WriteUInt(0);
            writer.WriteUInt(0);
            writer.WriteUInt(0);

            writer.WriteUInt(0xffffffff); //next track id


            return FullBox("mvhd", 1, 0, stream.ToArray());
        }

        private byte[] GenTkhd()
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter2(stream);

            writer.WriteULong(CreationTime); //creation_time
            writer.WriteULong(CreationTime); //modification_time
            writer.WriteUInt(TrackId); //track id
            writer.WriteUInt(0); //reserved
            writer.WriteULong(Duration); //duration
            writer.WriteUInt(0); //reserved
            writer.WriteUInt(0);
            writer.WriteShort(0); //layer
            writer.WriteShort(0); //alternate group
            writer.WriteByte(StreamType == "audio" ? (byte)1 : (byte)0, padding: 1); //volume
            writer.WriteUShort(0); //reserved

            writer.Write(UnityMatrix);

            writer.WriteUShort(Width, padding: 2); //width
            writer.WriteUShort(Height, padding: 2); //height

            return FullBox("tkhd", 1, (uint)TRACK_ENABLED | TRACK_IN_MOVIE | TRACK_IN_PREVIEW, stream.ToArray());
        }


        private byte[] GenMdhd()
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter2(stream);

            writer.WriteULong(CreationTime); //creation_time
            writer.WriteULong(CreationTime); //modification_time
            writer.WriteUInt(Timesacle); //timescale
            writer.WriteULong(Duration); //duration
            writer.WriteUShort((Language[0] - 0x60) << 10 | (Language[1] - 0x60) << 5 | (Language[2] - 0x60)); //language
            writer.WriteUShort(0); //pre defined

            return FullBox("mdhd", 1, 0, stream.ToArray());
        }

        private byte[] GenHdlr()
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter2(stream);

            writer.WriteUInt(0); //pre defined
            if (StreamType == "audio") writer.Write("soun");
            else if (StreamType == "video") writer.Write("vide");
            else if (StreamType == "text") writer.Write("subt");
            else throw new NotSupportedException();

            writer.WriteUInt(0); //reserved
            writer.WriteUInt(0);
            writer.WriteUInt(0);
            writer.Write($"{StreamSpec.GroupId ?? "RE Handler"}\0"); //name

            return FullBox("hdlr", 0, 0, stream.ToArray());
        }

        private byte[] GenMinf()
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter2(stream);

            var minfPayload = new List<byte>();
            if (StreamType == "audio")
            {
                var smhd = new List<byte>();
                smhd.Add(0); smhd.Add(0); //balance
                smhd.Add(0); smhd.Add(0); //reserved

                minfPayload.AddRange(FullBox("smhd", 0, 0, smhd.ToArray())); //Sound Media Header
            }
            else if (StreamType == "video")
            {
                var vmhd = new List<byte>();
                vmhd.Add(0); vmhd.Add(0); //graphics mode
                vmhd.Add(0); vmhd.Add(0); vmhd.Add(0); vmhd.Add(0); vmhd.Add(0); vmhd.Add(0);//opcolor

                minfPayload.AddRange(FullBox("vmhd", 0, 1, vmhd.ToArray())); //Video Media Header
            }
            else if (StreamType == "text")
            {
                minfPayload.AddRange(FullBox("sthd", 0, 0, new byte[0])); //Subtitle Media Header
            }
            else
            {
                throw new NotSupportedException();
            }

            var drefPayload = new List<byte>();
            drefPayload.Add(0); drefPayload.Add(0); drefPayload.Add(0); drefPayload.Add(1); //entry count
            drefPayload.AddRange(FullBox("url ", 0, SELF_CONTAINED, new byte[0])); //Data Entry URL Box

            var dinfPayload = FullBox("dref", 0, 0, drefPayload.ToArray()); //Data Reference Box
            minfPayload.AddRange(Box("dinf", dinfPayload.ToArray())); //Data Information Box

            return minfPayload.ToArray();
        }

        private byte[] GenEsds(byte[] audioSpecificConfig)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter2(stream);

            // ESDS length = esds box header length (= 12) +
            //               ES_Descriptor header length (= 5) +
            //               DecoderConfigDescriptor header length (= 15) +
            //               decoderSpecificInfo header length (= 2) +
            //               AudioSpecificConfig length (= codecPrivateData length)
            // esdsLength = 34 + len(audioSpecificConfig)

            // ES_Descriptor (see ISO/IEC 14496-1 (Systems))
            writer.WriteByte(0x03); //tag = 0x03 (ES_DescrTag)
            writer.WriteByte((byte)(20 + audioSpecificConfig.Length)); //size
            writer.WriteByte((byte)((TrackId & 0xFF00) >> 8)); //ES_ID = track_id
            writer.WriteByte((byte)(TrackId & 0x00FF));
            writer.WriteByte(0); //flags and streamPriority

            // DecoderConfigDescriptor (see ISO/IEC 14496-1 (Systems))
            writer.WriteByte(0x04); //tag = 0x04 (DecoderConfigDescrTag)
            writer.WriteByte((byte)(15 + audioSpecificConfig.Length)); //size
            writer.WriteByte(0x40); //objectTypeIndication = 0x40 (MPEG-4 AAC)
            writer.WriteByte((0x05 << 2) | (0 << 1) | 1); //reserved = 1
            writer.WriteByte(0xFF); //buffersizeDB = undefined
            writer.WriteByte(0xFF); 
            writer.WriteByte(0xFF);

            var bandwidth = StreamSpec.Bandwidth!;
            writer.WriteByte((byte)((bandwidth & 0xFF000000) >> 24)); //maxBitrate
            writer.WriteByte((byte)((bandwidth & 0x00FF0000) >> 16));
            writer.WriteByte((byte)((bandwidth & 0x0000FF00) >> 8));
            writer.WriteByte((byte)(bandwidth  & 0x000000FF));
            writer.WriteByte((byte)((bandwidth & 0xFF000000) >> 24)); //avgbitrate
            writer.WriteByte((byte)((bandwidth & 0x00FF0000) >> 16)); 
            writer.WriteByte((byte)((bandwidth & 0x0000FF00) >> 8));
            writer.WriteByte((byte)(bandwidth  & 0x000000FF));

            // DecoderSpecificInfo (see ISO/IEC 14496-1 (Systems))
            writer.WriteByte(0x05); //tag = 0x05 (DecSpecificInfoTag)
            writer.WriteByte((byte)audioSpecificConfig.Length); //size
            writer.Write(audioSpecificConfig); //AudioSpecificConfig bytes

            return FullBox("esds", 0, 0, stream.ToArray());
        }

        private byte[] GetSampleEntryBox()
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter2(stream);

            writer.WriteByte(0); //reserved
            writer.WriteByte(0);
            writer.WriteByte(0);
            writer.WriteByte(0);
            writer.WriteByte(0);
            writer.WriteByte(0);
            writer.WriteUShort(1); //data reference index

            if (StreamType == "audio")
            {
                writer.WriteUInt(0); //reserved2
                writer.WriteUInt(0);
                writer.WriteUShort(Channels); //channels
                writer.WriteUShort(BitsPerSample); //bits_per_sample
                writer.WriteUShort(0); //pre defined
                writer.WriteUShort(0); //reserved3
                writer.WriteUShort(SamplingRate, padding: 2); //sampling_rate

                var audioSpecificConfig = HexUtil.HexToBytes(CodecPrivateData);
                var esdsBox = GenEsds(audioSpecificConfig);
                writer.Write(esdsBox);

                if (FourCC.StartsWith("AAC")) 
                {
                    if (IsProtection)
                    {
                        var sinfBox = GenSinf("mp4a");
                        writer.Write(sinfBox);
                        return Box("enca", stream.ToArray()); //Encrypted Audio
                    }
                    else
                    {
                        return Box("mp4a", stream.ToArray());
                    }
                }
                if (FourCC == "EC-3")
                {
                    if (IsProtection)
                    {
                        var sinfBox = GenSinf("ec-3");
                        writer.Write(sinfBox);
                        return Box("enca", stream.ToArray()); //Encrypted Audio
                    }
                    else
                    {
                        return Box("ec-3", stream.ToArray());
                    }
                }
            }
            else if (StreamType == "video")
            {
                writer.WriteUShort(0); //pre defined
                writer.WriteUShort(0); //reserved
                writer.WriteUInt(0); //pre defined
                writer.WriteUInt(0);
                writer.WriteUInt(0);
                writer.WriteUShort(Width); //width
                writer.WriteUShort(Height); //height
                writer.WriteUShort(0x48, padding: 2); //horiz resolution 72 dpi
                writer.WriteUShort(0x48, padding: 2); //vert resolution 72 dpi
                writer.WriteUInt(0); //reserved
                writer.WriteUShort(1); //frame count
                for (int i = 0; i < 32; i++) //compressor name
                {
                    writer.WriteByte(0);
                }
                writer.WriteUShort(0x18); //depth
                writer.WriteUShort(65535); //pre defined

                var codecPrivateData = HexUtil.HexToBytes(CodecPrivateData);

                if (FourCC == "H264" || FourCC == "AVC1" || FourCC == "DAVC" || FourCC == "AVC1")
                {
                    var arr = CodecPrivateData.Split(new[] { StartCode }, StringSplitOptions.RemoveEmptyEntries);
                    var sps = HexUtil.HexToBytes(arr.Where(x => (HexUtil.HexToBytes(x[0..2])[0] & 0x1F) == 7).First());
                    var pps = HexUtil.HexToBytes(arr.Where(x => (HexUtil.HexToBytes(x[0..2])[0] & 0x1F) == 8).First());
                    //make avcC
                    var avcC = GetAvcC(sps, pps);
                    writer.Write(avcC);
                    if (IsProtection)
                    {
                        var sinfBox = GenSinf("avc1");
                        writer.Write(sinfBox);
                        return Box("encv", stream.ToArray()); //Encrypted Video
                    }
                    else
                    {
                        return Box("avc1", stream.ToArray()); //AVC Simple Entry
                    }
                }
                else if (FourCC == "HVC1" || FourCC == "HEV1")
                {
                    var arr = CodecPrivateData.Split(new[] { StartCode }, StringSplitOptions.RemoveEmptyEntries);
                    var vps = HexUtil.HexToBytes(arr.Where(x => (HexUtil.HexToBytes(x[0..2])[0] >> 1) == 0x20).First());
                    var sps = HexUtil.HexToBytes(arr.Where(x => (HexUtil.HexToBytes(x[0..2])[0] >> 1) == 0x21).First());
                    var pps = HexUtil.HexToBytes(arr.Where(x => (HexUtil.HexToBytes(x[0..2])[0] >> 1) == 0x22).First());
                    //make hvcC
                    var hvcC = GetHvcC(sps, pps, vps);
                    writer.Write(hvcC);
                    if (IsProtection)
                    {
                        var sinfBox = GenSinf("hvc1");
                        writer.Write(sinfBox);
                        return Box("encv", stream.ToArray()); //Encrypted Video
                    }
                    else
                    {
                        return Box("hvc1", stream.ToArray()); //HEVC Simple Entry
                    }
                }
                else
                {
                    throw new NotSupportedException();
                }
            }
            else if (StreamType == "text")
            {
                if (FourCC == "TTML")
                {
                    writer.Write("http://www.w3.org/ns/ttml\0"); //namespace
                    writer.Write("\0"); //schema location
                    writer.Write("\0"); //auxilary mime types(??)
                    return Box("stpp", stream.ToArray()); //TTML Simple Entry
                }
                else
                {
                    throw new NotSupportedException();
                }
            }
            else
            {
                throw new NotSupportedException();
            }

            throw new NotSupportedException();
        }

        private byte[] GetAvcC(byte[] sps, byte[] pps)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter2(stream);

            writer.WriteByte(1); //configuration version
            writer.Write(sps[1..4]); //avc profile indication + profile compatibility + avc level indication
            writer.WriteByte((byte)(0xfc | (NalUnitLengthField - 1))); //complete representation (1) + reserved (11111) + length size minus one
            writer.WriteByte(1); //reserved (0) + number of sps (0000001)
            writer.WriteUShort(sps.Length);
            writer.Write(sps);
            writer.WriteByte(1); //number of pps
            writer.WriteUShort(pps.Length);
            writer.Write(pps);

            return Box("avcC", stream.ToArray()); //AVC Decoder Configuration Record
        }

        private byte[] GetHvcC(byte[] sps, byte[] pps, byte[] vps)
        {
            var oriSps = new List<byte>(sps);
            //https://www.itu.int/rec/dologin.asp?lang=f&id=T-REC-H.265-201504-S!!PDF-E&type=items
            //Read generalProfileSpace, generalTierFlag, generalProfileIdc,
            //generalProfileCompatibilityFlags, constraintBytes, generalLevelIdc
            //from sps
            var encList = new List<byte>();
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
            using (var _reader = new BinaryReader(new MemoryStream(sps)))
            {
                while (_reader.BaseStream.Position < _reader.BaseStream.Length)
                {
                    encList.Add(_reader.ReadByte());
                    if (encList.Count >= 3 && encList[encList.Count - 3] == 0x00 && encList[encList.Count - 2] == 0x00 && encList[encList.Count - 1] == 0x03)
                    {
                        encList.RemoveAt(encList.Count - 1);
                    }
                }
            }
            sps = encList.ToArray();

            using var reader = new BinaryReader2(new MemoryStream(sps));
            reader.ReadBytes(2); //Skip 2 bytes unit header
            var firstByte = reader.ReadByte();
            var maxSubLayersMinus1 = (firstByte & 0xe) >> 1;
            var nextByte = reader.ReadByte();
            var generalProfileSpace = (nextByte & 0xc0) >> 6;
            var generalTierFlag = (nextByte & 0x20) >> 5;
            var generalProfileIdc = nextByte & 0x1f;
            var generalProfileCompatibilityFlags = reader.ReadUInt32();
            var constraintBytes = reader.ReadBytes(6);
            var generalLevelIdc = reader.ReadByte();

            /*var skipBit = 0;
            for (int i = 0; i < maxSubLayersMinus1; i++)
            {
                skipBit += 2; //sub_layer_profile_present_flag sub_layer_level_present_flag
            }
            if (maxSubLayersMinus1 > 0)
            {
                for (int i = maxSubLayersMinus1; i < 8; i++)
                {
                    skipBit += 2; //reserved_zero_2bits
                }
            }
            for (int i = 0; i < maxSubLayersMinus1; i++)
            {
                skipBit += 2; //sub_layer_profile_present_flag sub_layer_level_present_flag
            }*/

            //生成编码信息
            var codecs = $"hvc1" +
                $".{HEVC_GENERAL_PROFILE_SPACE_STRINGS[generalProfileSpace]}{generalProfileIdc}" +
                $".{Convert.ToString(generalProfileCompatibilityFlags, 16)}" +
                $".{(generalTierFlag == 1 ? 'H' : 'L')}{generalLevelIdc}" +
                $".{HexUtil.BytesToHex(constraintBytes.Where(b => b != 0).ToArray())}";
            StreamSpec.Codecs = codecs;


            ///////////////////////


            using var stream = new MemoryStream();
            using var writer = new BinaryWriter2(stream);

            //var reserved1 = 0xF;

            writer.WriteByte(1); //configuration version
            writer.WriteByte((byte)((generalProfileSpace << 6) + (generalTierFlag == 1 ? 0x20 : 0) | generalProfileIdc)); //general_profile_space + general_tier_flag + general_profile_idc
            writer.WriteUInt(generalProfileCompatibilityFlags); //general_profile_compatibility_flags
            writer.Write(constraintBytes); //general_constraint_indicator_flags
            writer.WriteByte((byte)generalProfileIdc); //general_level_idc
            writer.WriteUShort(0xf000); //reserved + min_spatial_segmentation_idc
            writer.WriteByte(0xfc); //reserved + parallelismType
            writer.WriteByte(0 | 0xfc); //reserved + chromaFormat 
            writer.WriteByte(0 | 0xf8); //reserved + bitDepthLumaMinus8
            writer.WriteByte(0 | 0xf8); //reserved + bitDepthChromaMinus8
            writer.WriteUShort(0); //avgFrameRate
            writer.WriteByte((byte)(0 << 6 | 0 << 3 | 0 << 2 | (NalUnitLengthField - 1))); //constantFrameRate + numTemporalLayers + temporalIdNested + lengthSizeMinusOne
            writer.WriteByte(0x03); //numOfArrays (vps sps pps)
            
            sps = oriSps.ToArray();
            writer.WriteByte(0x20); //array_completeness + reserved + NAL_unit_type
            writer.WriteUShort(1); //numNalus 
            writer.WriteUShort(vps.Length);
            writer.Write(vps);
            writer.WriteByte(0x21);
            writer.WriteUShort(1); //numNalus
            writer.WriteUShort(sps.Length);
            writer.Write(sps);
            writer.WriteByte(0x22); 
            writer.WriteUShort(1); //numNalus
            writer.WriteUShort(pps.Length);
            writer.Write(pps);

            return Box("hvcC", stream.ToArray()); //HEVC Decoder Configuration Record
        }

        private byte[] GetStsd()
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter2(stream);

            writer.WriteUInt(1); //entry count
            var sampleEntryData = GetSampleEntryBox();
            writer.Write(sampleEntryData);

            return stream.ToArray();
        }

        private byte[] GetMehd()
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter2(stream);

            writer.WriteULong(Duration);

            return FullBox("mehd", 1, 0, stream.ToArray()); //Movie Extends Header Box
        }
        private byte[] GetTrex()
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter2(stream);

            writer.WriteUInt(TrackId); //track id
            writer.WriteUInt(1); //default sample description index
            writer.WriteUInt(0); //default sample duration
            writer.WriteUInt(0); //default sample size
            writer.WriteUInt(0); //default sample flags

            return FullBox("trex", 0, 0, stream.ToArray()); //Track Extends Box
        }

        private byte[] GenPsshBoxForPlayReady()
        {
            using var _stream = new MemoryStream();
            using var _writer = new BinaryWriter2(_stream);
            var sysIdData = HexUtil.HexToBytes(ProtectionSystemId.Replace("-", ""));
            var psshData = HexUtil.HexToBytes(ProtectionData);

            _writer.Write(sysIdData);  // SystemID 16 bytes
            _writer.WriteUInt(psshData.Length); //Size of Data 4 bytes
            _writer.Write(psshData); //Data
            var psshBox = FullBox("pssh", 0, 0, _stream.ToArray());
            return psshBox;
        }

        private byte[] GenPsshBoxForWideVine()
        {
            using var _stream = new MemoryStream();
            using var _writer = new BinaryWriter2(_stream);
            var sysIdData = HexUtil.HexToBytes("edef8ba9-79d6-4ace-a3c8-27dcd51d21ed".Replace("-", ""));
            //var kid = HexUtil.HexToBytes(ProtecitonKID);

            _writer.Write(sysIdData);  // SystemID 16 bytes
            var psshData = HexUtil.HexToBytes($"08011210{ProtecitonKID}1A046E647265220400000000");
            _writer.WriteUInt(psshData.Length); //Size of Data 4 bytes
            _writer.Write(psshData); //Data
            var psshBox = FullBox("pssh", 0, 0, _stream.ToArray());
            return psshBox;
        }

        private byte[] GenMoof()
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter2(stream);

            //make senc
            writer.WriteUInt(1); //sample_count
            writer.Write(new byte[8]); //8 bytes IV

            var sencBox = FullBox("senc", 1, 0, stream.ToArray());

            var moofBox = Box("moof", sencBox); //Movie Extends Box

            return moofBox;
        }

        public byte[] GenHeader(byte[] firstSegment)
        {
            new MP4Parser()
                .Box("moof", MP4Parser.Children)
                .Box("traf", MP4Parser.Children)
                .FullBox("tfhd", (box) =>
                {
                    TrackId = (int)box.Reader.ReadUInt32();
                })
                .Parse(firstSegment);

            return GenHeader();
        }

        public byte[] GenHeader()
        {
            using var stream = new MemoryStream();

            var ftyp = GenFtyp(); // File Type Box
            stream.Write(ftyp);

            var moovPayload = GenMvhd(); // Movie Header Box

            var trakPayload = GenTkhd(); // Track Header Box

            var mdhdPayload = GenMdhd(); // Media Header Box

            var hdlrPayload = GenHdlr(); // Handler Reference Box

            var mdiaPayload = mdhdPayload.Concat(hdlrPayload).ToArray();

            var minfPayload = GenMinf();


            var sttsPayload = new byte[] { 0, 0, 0, 0 }; //entry count
            var stblPayload = FullBox("stts", 0, 0, sttsPayload); //Decoding Time to Sample Box

            var stscPayload = new byte[] { 0, 0, 0, 0 }; //entry count
            var stscBox = FullBox("stsc", 0, 0, stscPayload); //Sample To Chunk Box

            var stcoPayload = new byte[] { 0, 0, 0, 0 }; //entry count
            var stcoBox = FullBox("stco", 0, 0, stcoPayload); //Chunk Offset Box

            var stszPayload = new byte[] { 0, 0, 0, 0, 0, 0, 0, 0 }; //sample size, sample count
            var stszBox = FullBox("stsz", 0, 0, stszPayload); //Sample Size Box

            var stsdPayload = GetStsd();
            var stsdBox = FullBox("stsd", 0, 0, stsdPayload); //Sample Description Box

            stblPayload = stblPayload.Concat(stscBox).Concat(stcoBox).Concat(stszBox).Concat(stsdBox).ToArray();


            var stblBox = Box("stbl", stblPayload); //Sample Table Box
            minfPayload = minfPayload.Concat(stblBox).ToArray();

            var minfBox = Box("minf", minfPayload); //Media Information Box
            mdiaPayload = mdiaPayload.Concat(minfBox).ToArray();

            var mdiaBox = Box("mdia", mdiaPayload); //Media Box
            trakPayload = trakPayload.Concat(mdiaBox).ToArray();

            var trakBox = Box("trak", trakPayload); //Track Box
            moovPayload = moovPayload.Concat(trakBox).ToArray();

            var mvexPayload = GetMehd();
            var trexBox = GetTrex();
            mvexPayload = mvexPayload.Concat(trexBox).ToArray();

            var mvexBox = Box("mvex", mvexPayload); //Movie Extends Box
            moovPayload = moovPayload.Concat(mvexBox).ToArray();

            if (IsProtection)
            {
                var psshBox1 = GenPsshBoxForPlayReady();
                var psshBox2 = GenPsshBoxForWideVine();
                moovPayload = moovPayload.Concat(psshBox1).Concat(psshBox2).ToArray();
            }

            var moovBox = Box("moov", moovPayload); //Movie Box

            stream.Write(moovBox);

            //var moofBox = GenMoof(); //Movie Extends Box
            //stream.Write(moofBox);

            return stream.ToArray();
        }
    }
}
