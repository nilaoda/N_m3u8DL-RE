using N_m3u8DL_RE.Common.Util;
using System.Security.Cryptography;

namespace Mp4SubtitleParser
{
    public class ParsedMP4Info
    {
        public string? PSSH;
        public string? KID;
        public string? Scheme;
    }

    public class MP4InitUtil
    {
        private static readonly byte[] SYSTEM_ID_WIDEVINE = { 0xED, 0xEF, 0x8B, 0xA9, 0x79, 0xD6, 0x4A, 0xCE, 0xA3, 0xC8, 0x27, 0xDC, 0xD5, 0x1D, 0x21, 0xED };
        private static readonly byte[] SYSTEM_ID_PLAYREADY = { 0x9A, 0x04, 0xF0, 0x79, 0x98, 0x40, 0x42, 0x86, 0xAB, 0x92, 0xE6, 0x5B, 0xE0, 0x88, 0x5F, 0x95 };

        public static ParsedMP4Info ReadInit(byte[] data)
        {
            var info = new ParsedMP4Info();


            //parse init
            new MP4Parser()
                .Box("moov", MP4Parser.Children)
                .Box("trak", MP4Parser.Children)
                .Box("mdia", MP4Parser.Children)
                .Box("minf", MP4Parser.Children)
                .Box("stbl", MP4Parser.Children)
                .FullBox("stsd", MP4Parser.SampleDescription)
                .FullBox("pssh", (box) =>
                {
                    if (!(box.Version == 0 || box.Version == 1))
                        throw new Exception("PSSH version can only be 0 or 1");
                    var systemId = box.Reader.ReadBytes(16);
                    if (SYSTEM_ID_WIDEVINE.SequenceEqual(systemId))
                    {
                        var dataSize = box.Reader.ReadUInt32();
                        info.PSSH = Convert.ToBase64String(box.Reader.ReadBytes((int)dataSize));
                    }
                })
                .FullBox("encv", MP4Parser.AllData(data => ReadBox(data, info)))
                .FullBox("enca", MP4Parser.AllData(data => ReadBox(data, info)))
                .FullBox("enct", MP4Parser.AllData(data => ReadBox(data, info)))
                .FullBox("encs", MP4Parser.AllData(data => ReadBox(data, info)))
                .Parse(data, stopOnPartial: true);

            return info;
        }

        private static void ReadBox(byte[] data, ParsedMP4Info info)
        {
            //find schm 
            var schmBytes = new byte[4] { 0x73, 0x63, 0x68, 0x6d };
            var schmIndex = 0;
            for (int i = 0; i < data.Length - 4; i++) 
            {
                if (new byte[4] { data[i], data[i + 1], data[i + 2], data[i + 3] }.SequenceEqual(schmBytes))
                {
                    schmIndex = i;
                    break;
                }
            }
            if (schmIndex + 8 < data.Length)
            {
                info.Scheme = System.Text.Encoding.UTF8.GetString(data[schmIndex..][8..12]);
            }

            //if (info.Scheme != "cenc") return;

            //find KID
            var tencBytes = new byte[4] { 0x74, 0x65, 0x6E, 0x63 };
            var tencIndex = -1;
            for (int i = 0; i < data.Length - 4; i++)
            {
                if (new byte[4] { data[i], data[i + 1], data[i + 2], data[i + 3] }.SequenceEqual(tencBytes))
                {
                    tencIndex = i;
                    break;
                }
            }
            if (tencIndex != -1 && tencIndex + 12 < data.Length) 
            {
                info.KID = HexUtil.BytesToHex(data[tencIndex..][12..28]).ToLower();
            }
        }
    }
}
