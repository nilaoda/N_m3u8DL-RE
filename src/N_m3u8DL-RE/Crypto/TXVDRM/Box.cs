using System.Buffers.Binary;

namespace N_m3u8DL_RE.Crypto.TXVDRM;

internal readonly struct Box
{
    public const uint TypeFtyp = 0x66747970; // 'ftyp'
    public const uint TypeStyp = 0x73747970; // 'styp'
    public const uint TypeMoov = 0x6D6F6F76; // 'moov'
    public const uint TypeSidx = 0x73696478; // 'sidx'
    public const uint TypeMoof = 0x6D6F6F66; // 'moof'
    public const uint TypeMdat = 0x6D646174; // 'mdat'
    public const uint TypeTraf = 0x74726166; // 'traf'
    public const uint TypeTfhd = 0x74666864; // 'tfhd'
    public const uint TypeTrun = 0x7472756E; // 'trun'
    public const uint TypeTrak = 0x7472616B; // 'trak'
    public const uint TypeTkhd = 0x746B6864; // 'tkhd'
    public const uint TypeMdia = 0x6D646961; // 'mdia'
    public const uint TypeHdlr = 0x68646C72; // 'hdlr'
    public const uint TypeMinf = 0x6D696E66; // 'minf'
    public const uint TypeStbl = 0x7374626C; // 'stbl'
    public const uint TypeStsd = 0x73747364; // 'stsd'
    public const uint TypeAvc1 = 0x61766331; // 'avc1'
    public const uint TypeAvc3 = 0x61766333; // 'avc3'
    public const uint TypeHvc1 = 0x68766331; // 'hvc1'
    public const uint TypeHev1 = 0x68657631; // 'hev1'
    public const uint TypeVvc1 = 0x76766331; // 'vvc1'
    public const uint TypeVvi1 = 0x76766931; // 'vvi1'
    public const uint TypeAvcC = 0x61766343; // 'avcC'
    public const uint TypeHvcC = 0x68766343; // 'hvcC'
    public const uint TypeVvcC = 0x76766343; // 'vvcC'

    public readonly uint Type;
    public readonly int Start;
    public readonly int Size;
    public readonly int HeaderSize;
    public readonly int PayloadStart;
    public readonly int End;

    private Box(uint type, int start, int size, int headerSize)
    {
        Type = type;
        Start = start;
        Size = size;
        HeaderSize = headerSize;
        PayloadStart = start + headerSize;
        End = start + size;
    }

    public static bool TryParse(byte[] data, int offset, int limit, out Box box)
    {
        box = default;
        if (offset < 0 || offset + 8 > limit) return false;

        uint size32 = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, 4));
        uint type = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset + 4, 4));

        if (type == 0) return false;

        long size;
        int header = 8;
        if (size32 == 1)
        {
            if (offset + 16 > limit) return false;
            size = (long)BinaryPrimitives.ReadUInt64BigEndian(data.AsSpan(offset + 8, 8));
            header = 16;
        }
        else if (size32 == 0)
        {
            size = limit - offset;
        }
        else
        {
            size = size32;
        }

        if (size < header || offset + size > limit) return false;

        box = new Box(type, offset, (int)size, header);
        return true;
    }
}
