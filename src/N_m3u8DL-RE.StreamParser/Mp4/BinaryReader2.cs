namespace N_m3u8DL_RE.StreamParser.Mp4
{
    // make BinaryReader in Big Endian
    internal sealed class BinaryReader2(Stream stream) : BinaryReader(stream)
    {
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
            byte[] data = ReadBytes(4);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(data);
            }

            return BitConverter.ToInt32(data, 0);
        }

        public override short ReadInt16()
        {
            byte[] data = ReadBytes(2);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(data);
            }

            return BitConverter.ToInt16(data, 0);
        }

        public override long ReadInt64()
        {
            byte[] data = ReadBytes(8);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(data);
            }

            return BitConverter.ToInt64(data, 0);
        }

        public override uint ReadUInt32()
        {
            byte[] data = ReadBytes(4);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(data);
            }

            return BitConverter.ToUInt32(data, 0);
        }

        public override ulong ReadUInt64()
        {
            byte[] data = ReadBytes(8);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(data);
            }

            return BitConverter.ToUInt64(data, 0);
        }
    }
}