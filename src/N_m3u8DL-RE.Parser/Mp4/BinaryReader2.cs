using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mp4SubtitleParser
{
    //make BinaryReader in Big Endian
    class BinaryReader2 : BinaryReader
    {
        public BinaryReader2(System.IO.Stream stream) : base(stream) { }

        public bool HasMoreData()
        {
            return BaseStream.Position < BaseStream.Length;
        }

        public long GetLength()
        {
            return BaseStream.Length;
        }

        public long GetPosition()
        {
            return BaseStream.Position;
        }

        public override int ReadInt32()
        {
            var data = base.ReadBytes(4);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(data);
            return BitConverter.ToInt32(data, 0);
        }

        public override short ReadInt16()
        {
            var data = base.ReadBytes(2);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(data);
            return BitConverter.ToInt16(data, 0);
        }

        public override long ReadInt64()
        {
            var data = base.ReadBytes(8);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(data);
            return BitConverter.ToInt64(data, 0);
        }

        public override uint ReadUInt32()
        {
            var data = base.ReadBytes(4);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(data);
            return BitConverter.ToUInt32(data, 0);
        }

        public override ulong ReadUInt64()
        {
            var data = base.ReadBytes(8);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(data);
            return BitConverter.ToUInt64(data, 0);
        }
    }
}
