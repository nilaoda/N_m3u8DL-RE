namespace N_m3u8DL_RE.Crypto.TXVDRM;

public enum CodecKind
{
    Unknown = 0,
    Avc = 1,  // avc1/avc3, PMT 0x1B
    Hevc = 2, // hvc1/hev1, PMT 0x24
    Vvc = 3,  // vvc1/vvi1, PMT 0x33
}
