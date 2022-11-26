using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mp4SubtitleParser
{
    //make BinaryWriter in Big Endian
    class BinaryWriter2 : BinaryWriter
    {
        private static bool IsLittleEndian = BitConverter.IsLittleEndian;
        public BinaryWriter2(System.IO.Stream stream) : base(stream) { }


        public void WriteUInt(decimal n, int offset = 0)
        {
            var arr = BitConverter.GetBytes((uint)n);
            if (IsLittleEndian)
                Array.Reverse(arr);
            if (offset != 0)
                arr = arr[offset..];
            BaseStream.Write(arr);
        }

        public override void Write(string text)
        {
            BaseStream.Write(Encoding.ASCII.GetBytes(text));
        }

        public void WriteInt(decimal n, int offset = 0)
        {
            var arr = BitConverter.GetBytes((int)n);
            if (IsLittleEndian)
                Array.Reverse(arr);
            if (offset != 0)
                arr = arr[offset..];
            BaseStream.Write(arr);
        }

        public void WriteULong(decimal n, int offset = 0)
        {
            var arr = BitConverter.GetBytes((ulong)n);
            if (IsLittleEndian)
                Array.Reverse(arr);
            if (offset != 0)
                arr = arr[offset..];
            BaseStream.Write(arr);
        }

        public void WriteUShort(decimal n, int padding = 0)
        {
            var arr = BitConverter.GetBytes((ushort)n);
            if (IsLittleEndian)
                Array.Reverse(arr);
            while (padding > 0)
            {
                arr = arr.Concat(new byte[] { 0x00 }).ToArray();
                padding--;
            }
            BaseStream.Write(arr);
        }

        public void WriteShort(decimal n, int padding = 0)
        {
            var arr = BitConverter.GetBytes((short)n);
            if (IsLittleEndian)
                Array.Reverse(arr);
            while (padding > 0)
            {
                arr = arr.Concat(new byte[] { 0x00 }).ToArray();
                padding--;
            }
            BaseStream.Write(arr);
        }

        public void WriteByte(byte n, int padding = 0)
        {
            var arr = new byte[] { n };
            while (padding > 0)
            {
                arr = arr.Concat(new byte[] { 0x00 }).ToArray();
                padding--;
            }
            BaseStream.Write(arr);
        }
    }
}
