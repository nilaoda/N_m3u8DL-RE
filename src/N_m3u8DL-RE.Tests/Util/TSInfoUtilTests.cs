using System.Buffers.Binary;
using System.Text;
using N_m3u8DL_RE.Util;

namespace N_m3u8DL_RE.Tests.Util;

public class TSInfoUtilTests
{
    [Fact]
    public void MatchesServicesByProgramIdAcrossPacketsWithAdaptationPointerAndDuplicatePackets()
    {
        var pat = Section(0, [0, 0, 0xe0, 0x10, 0, 1, 0xe1, 0, 0, 2, 0xe1, 1]);
        var sdt = Section(0x42, Service(99, "Unrelated", "Other")
            .Concat(Service(2, "频道乙", "供应商乙"))
            .Concat(Service(1, "频道甲", "供应商甲", largeDescriptor: true)).ToArray());
        var packets = Packetize(0x11, sdt);
        Assert.True(packets.Length > 1);
        var data = Packetize(0, pat).SelectMany(packet => packet)
            .Concat(packets[0]).Concat(packets[0]).Concat(packets.Skip(1).SelectMany(packet => packet)).ToArray();
        var services = TSInfoUtil.ReadServices(data);
        Assert.Equal(new[] { 1, 2 }, services.Select(service => service.ProgramId));
        Assert.Equal(new[] { "频道甲", "频道乙" }, services.Select(service => service.ServiceName));
        Assert.Equal(new[] { "供应商甲", "供应商乙" }, services.Select(service => service.ServiceProvider));
    }

    [Theory]
    [InlineData("crc")]
    [InlineData("gap")]
    [InlineData("truncated")]
    [InlineData("descriptor")]
    public void InvalidMetadataIsIgnoredAndLaterValidSectionsRecover(string damage)
    {
        var pat = Packetize(0, Section(0, [0, 1, 0xe1, 0])).SelectMany(packet => packet).ToArray();
        var section = Section(0x42, Service(1, "Service", "Provider", largeDescriptor: true));
        if (damage == "crc")
            section[^1] ^= 1;
        else if (damage == "descriptor")
            section = Section(0x42, [0, 1, 0xff, 0x80, 5, 0x48, 3, 1, 255, 0]);
        var packets = Packetize(0x11, section);
        if (damage == "gap")
            packets[1][3] = (byte)((packets[1][3] & 0xf0) | 5);
        else if (damage == "truncated")
            packets = packets[..1];
        var invalid = pat.Concat(packets.SelectMany(packet => packet)).ToArray();
        var before = Assert.Single(TSInfoUtil.ReadServices(invalid));
        Assert.Equal(1, before.ProgramId);
        Assert.Equal("", before.ServiceName);
        var recovery = Packetize(0x11, Section(0x42, Service(1, "Recovered", "Provider")), firstCounter: 8);
        var after = Assert.Single(TSInfoUtil.ReadServices(invalid.Concat(recovery.SelectMany(packet => packet)).ToArray()));
        Assert.Equal("Recovered", after.ServiceName);
        Assert.Equal("Provider", after.ServiceProvider);
    }

    [Theory]
    [InlineData("utf8")]
    [InlineData("utf16")]
    [InlineData("gb")]
    [InlineData("unmarked-gb")]
    [InlineData("latin1")]
    public void DecodesCommonDvbTextAndUnmarkedChinese(string format)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var expected = format == "latin1" ? "Café" : "测试频道";
        var bytes = format switch
        {
            "utf8" => new byte[] { 0x15 }.Concat(Encoding.UTF8.GetBytes(expected)).ToArray(),
            "utf16" => new byte[] { 0x11 }.Concat(Encoding.BigEndianUnicode.GetBytes(expected)).ToArray(),
            "gb" => new byte[] { 0x13 }.Concat(Encoding.GetEncoding("GB2312").GetBytes(expected)).ToArray(),
            "unmarked-gb" => Encoding.GetEncoding("GB2312").GetBytes(expected),
            _ => Encoding.Latin1.GetBytes(expected)
        };
        Assert.Equal(expected, TSInfoUtil.DecodeText(bytes));
        Assert.Equal("", TSInfoUtil.DecodeText([0x10]));
    }

    private static byte[] Service(int id, string name, string provider, bool largeDescriptor = false)
    {
        var nameBytes = new byte[] { 0x15 }.Concat(Encoding.UTF8.GetBytes(name)).ToArray();
        var providerBytes = new byte[] { 0x15 }.Concat(Encoding.UTF8.GetBytes(provider)).ToArray();
        byte[] body = [1, (byte)providerBytes.Length, .. providerBytes, (byte)nameBytes.Length, .. nameBytes];
        // 未知描述符在 service_descriptor 前，不能依赖描述符固定位置。
        var filler = new byte[largeDescriptor ? 180 : 3];
        byte[] descriptors = [0x80, (byte)filler.Length, .. filler, 0x48, (byte)body.Length, .. body];
        return [(byte)(id >> 8), (byte)id, 0xff, (byte)(0x80 | (descriptors.Length >> 8)), (byte)descriptors.Length, .. descriptors];
    }

    private static byte[] Section(byte table, byte[] entries)
    {
        byte[] extension = table == 0x42 ? [0, 1, 0xff] : [];
        byte[] section = [table, 0xb0, 0, 0, 1, 0xc1, 0, 0, .. extension, .. entries, 0, 0, 0, 0];
        section[1] |= (byte)((section.Length - 3) >> 8);
        section[2] = (byte)(section.Length - 3);
        uint crc = uint.MaxValue;
        foreach (var value in section.AsSpan(0, section.Length - 4))
        {
            crc ^= (uint)value << 24;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc & 0x80000000) != 0 ? (crc << 1) ^ 0x04c11db7 : crc << 1;
        }
        BinaryPrimitives.WriteUInt32BigEndian(section.AsSpan(section.Length - 4), crc);
        return section;
    }

    private static byte[][] Packetize(int pid, byte[] section, int firstCounter = 0)
    {
        var packets = new List<byte[]>();
        for (var offset = 0; offset < section.Length;)
        {
            var first = offset == 0;
            var packet = Enumerable.Repeat((byte)0xff, 188).ToArray();
            packet[0] = 0x47;
            packet[1] = (byte)((pid >> 8) | (first ? 0x40 : 0));
            packet[2] = (byte)pid;
            packet[3] = (byte)(0x30 | ((firstCounter + packets.Count) & 15));
            packet[4] = 3; // adaptation field
            packet[5] = 0;
            var payload = 8;
            if (first)
            {
                packet[payload++] = 2; // pointer field + stuffing
                payload += 2;
            }
            var take = Math.Min(section.Length - offset, 188 - payload);
            section.AsSpan(offset, take).CopyTo(packet.AsSpan(payload));
            offset += take;
            packets.Add(packet);
        }
        return packets.ToArray();
    }
}
