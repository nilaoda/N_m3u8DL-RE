using System.Text;

namespace N_m3u8DL_RE.Util;

// 从有限的 TS 样本读取节目元数据；坏包、残缺 section 和未知描述符不会影响录制。
internal static class TSInfoUtil
{
    internal sealed record ServiceInfo(int ProgramId, string ServiceName, string ServiceProvider);
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);
    private static readonly Encoding ChineseEncoding = CreateChineseEncoding();

    internal static ServiceInfo[] ReadServices(ReadOnlySpan<byte> data)
    {
        var programs = new HashSet<int>();
        var services = new Dictionary<int, ServiceInfo>();
        var pat = new SectionBuffer(section => ReadSection(0, section));
        var sdt = new SectionBuffer(section => ReadSection(0x11, section));
        for (var offset = 0; offset + 188 <= data.Length;)
        {
            if (data[offset] != 0x47)
            {
                // 重新同步必须有连续包作为依据，不能把载荷中的 0x47 当成包头。
                pat.Reset();
                sdt.Reset();
                offset++;
                while (offset + 188 * 2 < data.Length &&
                       (data[offset] != 0x47 || data[offset + 188] != 0x47 || data[offset + 376] != 0x47))
                    offset++;
                if (offset + 188 * 2 >= data.Length)
                    break;
            }
            var packet = data.Slice(offset, 188);
            offset += 188;
            var pid = ((packet[1] & 0x1f) << 8) | packet[2];
            if (pid is not (0 or 0x11))
                continue;
            var state = pid == 0 ? pat : sdt;
            var control = (packet[3] >> 4) & 3;
            if ((packet[1] & 0x80) != 0 || (packet[3] & 0xc0) != 0 || control == 0)
            {
                state.Reset();
                continue;
            }
            var payloadOffset = 4;
            if ((control & 2) != 0)
            {
                payloadOffset += packet[4] + 1;
                if (payloadOffset > 188)
                {
                    state.Reset();
                    continue;
                }
                if (packet[4] > 0 && (packet[5] & 0x80) != 0)
                    state.Reset();
            }
            if ((control & 1) != 0 && payloadOffset < 188)
                state.Add(packet[payloadOffset..], (packet[1] & 0x40) != 0, packet[3] & 15);
        }
        // 用 service_id 对应 PAT 的 program_number，不能将首个 SDT 名称配给另一个节目。
        return (programs.Count > 0 ? programs : services.Keys.AsEnumerable())
            .Select(id => services.GetValueOrDefault(id) ?? new ServiceInfo(id, "", "")).ToArray();

        void ReadSection(int pid, ReadOnlySpan<byte> section)
        {
            if (section.Length < 12 || (section[1] & 0x80) == 0 || (section[5] & 1) == 0 || !HasValidCrc(section))
                return;
            var end = section.Length - 4;
            // PAT 的 program_number=0 是网络信息，不是可录制节目。
            if (pid == 0 && section[0] == 0)
            {
                for (var i = 8; i + 4 <= end; i += 4)
                {
                    var id = (section[i] << 8) | section[i + 1];
                    if (id != 0)
                        programs.Add(id);
                }
            }
            // PID 0x11 也承载 BAT 和其他 TS 的 SDT，只读取当前 TS 的 SDT actual。
            else if (pid == 0x11 && section[0] == 0x42 && section.Length >= 15)
            {
                for (var i = 11; i + 5 <= end;)
                {
                    var id = (section[i] << 8) | section[i + 1];
                    var length = ((section[i + 3] & 15) << 8) | section[i + 4];
                    var next = i + 5 + length;
                    if (next > end)
                        return;
                    for (var descriptor = i + 5; descriptor + 2 <= next;)
                    {
                        var size = section[descriptor + 1];
                        if (descriptor + 2 + size > next)
                            break;
                        if (section[descriptor] == 0x48 && size >= 3)
                        {
                            var body = section.Slice(descriptor + 2, size);
                            var providerLength = body[1];
                            if (providerLength + 2 < body.Length)
                            {
                                var nameLength = body[providerLength + 2];
                                if (providerLength + 3 + nameLength <= body.Length)
                                    services[id] = new ServiceInfo(id, DecodeText(body.Slice(providerLength + 3, nameLength)),
                                        DecodeText(body.Slice(2, providerLength)));
                            }
                        }
                        descriptor += 2 + size;
                    }
                    i = next;
                }
            }
        }
    }

    private sealed class SectionBuffer(Action<ReadOnlySpan<byte>> read)
    {
        // PAT/SDT 的 section_length 最大为 1021，包含 3 字节头时上限是 1024。
        private readonly byte[] buffer = new byte[1024];
        private int count;
        private int length;
        private int continuity = -1;

        internal void Reset()
        {
            count = length = 0;
            continuity = -1;
        }

        internal void Add(ReadOnlySpan<byte> payload, bool start, int counter)
        {
            if (counter == continuity)
                return;
            if (continuity >= 0 && counter != ((continuity + 1) & 15))
                count = length = 0;
            continuity = counter;
            if (!start)
            {
                Append(payload, false);
                return;
            }
            var pointer = payload[0];
            if (pointer >= payload.Length)
            {
                Reset();
                return;
            }
            // pointer_field 前的字节只可补完上一 section，之后才是新 section 的起点。
            Append(payload.Slice(1, pointer), false);
            count = length = 0;
            Append(payload[(pointer + 1)..], true);
        }

        private void Append(ReadOnlySpan<byte> payload, bool allowNew)
        {
            while (!payload.IsEmpty && (count > 0 || allowNew))
            {
                if (count == 0 && payload[0] == 0xff)
                    return;
                var needed = length == 0 ? 3 - count : length - count;
                var take = Math.Min(needed, payload.Length);
                payload[..take].CopyTo(buffer.AsSpan(count));
                count += take;
                payload = payload[take..];
                if (count == 3 && length == 0)
                {
                    length = 3 + (((buffer[1] & 15) << 8) | buffer[2]);
                    if (length < 12 || length > buffer.Length)
                    {
                        Reset();
                        return;
                    }
                }
                if (length > 0 && count == length)
                {
                    read(buffer.AsSpan(0, length));
                    count = length = 0;
                }
            }
        }
    }

    private static bool HasValidCrc(ReadOnlySpan<byte> section)
    {
        uint crc = uint.MaxValue;
        foreach (var value in section)
        {
            crc ^= (uint)value << 24;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc & 0x80000000) == 0 ? crc << 1 : (crc << 1) ^ 0x04c11db7;
        }
        return crc == 0;
    }

    private static Encoding CreateChineseEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding("GB18030", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }

    internal static string DecodeText(ReadOnlySpan<byte> value)
    {
        if (value.IsEmpty)
            return "";
        try
        {
            var selector = value[0];
            Encoding encoding = Encoding.Latin1;
            var offset = 1;
            switch (selector)
            {
                case 0x11: encoding = Encoding.BigEndianUnicode; break;
                case 0x12: encoding = Encoding.GetEncoding(949); break;
                case 0x13: encoding = ChineseEncoding; break;
                case 0x14: encoding = Encoding.GetEncoding(950); break;
                case 0x15: encoding = StrictUtf8; break;
                case >= 0x01 and <= 0x0b when selector != 0x08:
                    encoding = Encoding.GetEncoding($"ISO-8859-{selector + 4}");
                    break;
                case 0x10 when value.Length >= 3 && value[1] == 0 && value[2] is >= 1 and <= 15 && value[2] != 12:
                    encoding = Encoding.GetEncoding($"ISO-8859-{value[2]}");
                    offset = 3;
                    break;
                default:
                    if (selector < 0x20)
                        return "";
                    offset = 0;
                    break;
            }
            var bytes = value[offset..];
            var text = encoding.GetString(bytes);
            if (offset == 0)
            {
                // 兼容未声明字符集的 UTF-8，以及国内前端常用的 GB2312/GBK 文本。
                try { text = StrictUtf8.GetString(bytes); }
                catch (DecoderFallbackException)
                {
                    try
                    {
                        var chinese = ChineseEncoding.GetString(bytes);
                        if (chinese.Count(character => character is >= '\u3400' and <= '\u9fff') >= 2)
                            text = chinese;
                    }
                    catch (DecoderFallbackException) { }
                }
            }
            return string.Concat(text.Where(character => !char.IsControl(character))).Trim();
        }
        catch (ArgumentException)
        {
            // 不支持或损坏的编码仅跳过名称，不能让元数据探测中断录制。
            return "";
        }
    }
}
