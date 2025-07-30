﻿using System.Text;

using N_m3u8DL_RE.Common.Util;

namespace N_m3u8DL_RE.StreamParser.Mp4
{
    public class ParsedMP4Info
    {
        public string? PSSH { get; set; }
        public string? KID { get; set; }
        public string? Scheme { get; set; }
        public bool IsMultiDRM { get; set; }
    }

    public static class MP4InitUtil
    {
        private static readonly byte[] SYSTEM_ID_WIDEVINE = [0xED, 0xEF, 0x8B, 0xA9, 0x79, 0xD6, 0x4A, 0xCE, 0xA3, 0xC8, 0x27, 0xDC, 0xD5, 0x1D, 0x21, 0xED];
#pragma warning disable IDE0052 // Remove unread private members
        private static readonly byte[] SYSTEM_ID_PLAYREADY = [0x9A, 0x04, 0xF0, 0x79, 0x98, 0x40, 0x42, 0x86, 0xAB, 0x92, 0xE6, 0x5B, 0xE0, 0x88, 0x5F, 0x95];
#pragma warning restore IDE0052 // Remove unread private members

        public static ParsedMP4Info ReadInit(byte[] data)
        {
            ParsedMP4Info info = new();

            // parse init
            new MP4Parser()
                .Box("moov", MP4Parser.Children)
                .Box("trak", MP4Parser.Children)
                .Box("mdia", MP4Parser.Children)
                .Box("minf", MP4Parser.Children)
                .Box("stbl", MP4Parser.Children)
                .FullBox("stsd", MP4Parser.SampleDescription)
                .FullBox("pssh", box =>
                {
                    if (box.Version is not (0 or 1))
                    {
                        throw new InvalidDataException($"PSSH version can only be 0 or 1, but got {box.Version}");
                    }

                    byte[] systemId = box.Reader.ReadBytes(16);
                    if (!SYSTEM_ID_WIDEVINE.SequenceEqual(systemId))
                    {
                        return;
                    }

                    uint dataSize = box.Reader.ReadUInt32();
                    byte[] psshData = box.Reader.ReadBytes((int)dataSize);
                    info.PSSH = Convert.ToBase64String(psshData);
                    if (info.KID != "00000000000000000000000000000000")
                    {
                        return;
                    }

                    info.KID = HexUtil.BytesToHex(psshData[2..18]).ToLowerInvariant();
                    info.IsMultiDRM = true;
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
            // find schm 
            byte[] schmBytes = [0x73, 0x63, 0x68, 0x6d];
            int schmIndex = 0;
            for (int i = 0; i < data.Length - 4; i++)
            {
                if (new[] { data[i], data[i + 1], data[i + 2], data[i + 3] }.SequenceEqual(schmBytes))
                {
                    schmIndex = i;
                    break;
                }
            }
            if (schmIndex + 8 < data.Length)
            {
                info.Scheme = Encoding.UTF8.GetString(data[schmIndex..][8..12]);
            }

            // if (info.Scheme != "cenc") return;

            // find KID
            byte[] tencBytes = [0x74, 0x65, 0x6E, 0x63];
            int tencIndex = -1;
            for (int i = 0; i < data.Length - 4; i++)
            {
                if (new[] { data[i], data[i + 1], data[i + 2], data[i + 3] }.SequenceEqual(tencBytes))
                {
                    tencIndex = i;
                    break;
                }
            }
            if (tencIndex != -1 && tencIndex + 12 < data.Length)
            {
                info.KID = HexUtil.BytesToHex(data[tencIndex..][12..28]).ToLowerInvariant();
            }
        }
    }
}
