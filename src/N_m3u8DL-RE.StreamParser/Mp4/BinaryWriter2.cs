using System.Text;

namespace N_m3u8DL_RE.StreamParser.Mp4
{
    // make BinaryWriter in Big Endian
    internal sealed class BinaryWriter2(Stream stream) : BinaryWriter(stream)
    {
        private static readonly bool IsLittleEndian = BitConverter.IsLittleEndian;

        public void WriteUInt(decimal n, int offset = 0)
        {
            byte[] arr = BitConverter.GetBytes((uint)n);
            if (IsLittleEndian)
            {
                Array.Reverse(arr);
            }

            if (offset != 0)
            {
                arr = arr[offset..];
            }

            BaseStream.Write(arr);
        }

        public override void Write(string text)
        {
            BaseStream.Write(Encoding.ASCII.GetBytes(text));
        }

        public void WriteInt(decimal n, int offset = 0)
        {
            byte[] arr = BitConverter.GetBytes((int)n);
            if (IsLittleEndian)
            {
                Array.Reverse(arr);
            }

            if (offset != 0)
            {
                arr = arr[offset..];
            }

            BaseStream.Write(arr);
        }

        public void WriteULong(decimal n, int offset = 0)
        {
            byte[] arr = BitConverter.GetBytes((ulong)n);
            if (IsLittleEndian)
            {
                Array.Reverse(arr);
            }

            if (offset != 0)
            {
                arr = arr[offset..];
            }

            BaseStream.Write(arr);
        }

        public void WriteUShort(decimal n, int padding = 0)
        {
            byte[] arr = BitConverter.GetBytes((ushort)n);
            if (IsLittleEndian)
            {
                Array.Reverse(arr);
            }

            while (padding > 0)
            {
                arr = [.. arr, .. new byte[] { 0x00 }];
                padding--;
            }
            BaseStream.Write(arr);
        }

        public void WriteShort(decimal n, int padding = 0)
        {
            byte[] arr = BitConverter.GetBytes((short)n);
            if (IsLittleEndian)
            {
                Array.Reverse(arr);
            }

            while (padding > 0)
            {
                arr = [.. arr, .. new byte[] { 0x00 }];
                padding--;
            }
            BaseStream.Write(arr);
        }

        public void WriteByte(byte n, int padding = 0)
        {
            byte[] arr = [n];
            while (padding > 0)
            {
                arr = [.. arr, .. new byte[] { 0x00 }];
                padding--;
            }
            BaseStream.Write(arr);
        }
    }
}