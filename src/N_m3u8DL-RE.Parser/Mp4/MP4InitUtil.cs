using N_m3u8DL_RE.Common.Util;

namespace Mp4SubtitleParser
{
    public class MP4InitUtil
    {
        private static readonly byte[] SYSTEM_ID_WIDEVINE = { 0xED, 0xEF, 0x8B, 0xA9, 0x79, 0xD6, 0x4A, 0xCE, 0xA3, 0xC8, 0x27, 0xDC, 0xD5, 0x1D, 0x21, 0xED };
        private static readonly byte[] SYSTEM_ID_PLAYREADY = { 0x9A, 0x04, 0xF0, 0x79, 0x98, 0x40, 0x42, 0x86, 0xAB, 0x92, 0xE6, 0x5B, 0xE0, 0x88, 0x5F, 0x95 };

        public static string? ReadWVPssh(byte[] data)
        {
            string? pssh = null;
            //parse init
            new MP4Parser()
                .Box("moov", MP4Parser.Children)
                .FullBox("pssh", (box) =>
                {
                    if (!(box.Version == 0 || box.Version == 1))
                        throw new Exception("PSSH version can only be 0 or 1");
                    var systemId = box.Reader.ReadBytes(16);
                    if (SYSTEM_ID_WIDEVINE.SequenceEqual(systemId))
                    {
                        var dataSize = box.Reader.ReadUInt32();
                        pssh = Convert.ToBase64String(box.Reader.ReadBytes((int)dataSize));
                    }
                })
                .Parse(data);
            return pssh;
        }

        public static string? ReadWVKid(byte[] data)
        {
            string? kid = null;
            //parse init
            new MP4Parser()
                .Box("moov", MP4Parser.Children)
                .Box("trak", MP4Parser.Children)
                .Box("mdia", MP4Parser.Children)
                .Box("minf", MP4Parser.Children)
                .Box("stbl", MP4Parser.Children)
                .FullBox("stsd", MP4Parser.SampleDescription)
                .FullBox("encv", MP4Parser.AllData((data) =>
                {
                    kid = HexUtil.BytesToHex(data[^16..]).ToLower();
                }))
                .FullBox("enca", MP4Parser.AllData((data) =>
                {
                    kid = HexUtil.BytesToHex(data[^16..]).ToLower();
                }))
                .FullBox("enct", MP4Parser.AllData((data) =>
                {
                    kid = HexUtil.BytesToHex(data[^16..]).ToLower();
                }))
                .FullBox("encs", MP4Parser.AllData((data) =>
                {
                    kid = HexUtil.BytesToHex(data[^16..]).ToLower();
                }))
                .Parse(data);
            return kid;
        }
    }
}
