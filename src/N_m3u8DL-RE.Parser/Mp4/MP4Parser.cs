using System.Text;

/**
 * Translated from shaka-player project
 * https://github.com/nilaoda/Mp4SubtitleParser
 * https://github.com/shaka-project/shaka-player
 */
namespace Mp4SubtitleParser
{
    class ParsedBox
    {
        public MP4Parser Parser { get; set; }
        public bool PartialOkay { get; set; }
        public long Start { get; set; }
        public uint Version { get; set; } = 1000;
        public uint Flags { get; set; } = 1000;
        public BinaryReader2 Reader { get; set; }
        public bool Has64BitSize { get; set; }
    }

    class TFHD
    {
        public uint TrackId { get; set; }
        public uint DefaultSampleDuration { get; set; }
        public uint DefaultSampleSize { get; set; }
    }

    class TRUN
    {
        public uint SampleCount { get; set; }
        public List<Sample> SampleData { get; set; } = new List<Sample>();
    }

    class Sample
    {
        public uint SampleDuration { get; set; }
        public uint SampleSize { get; set; }
        public uint SampleCompositionTimeOffset { get; set; }
    }

    enum BoxType
    {
        BASIC_BOX = 0,
        FULL_BOX = 1
    };

    class MP4Parser
    {
        public bool Done { get; set; } = false;
        public Dictionary<long, int> Headers { get; set; } = new Dictionary<long, int>();
        public Dictionary<long, BoxHandler> BoxDefinitions { get; set; } = new Dictionary<long, BoxHandler>();

        public delegate void BoxHandler(ParsedBox box);
        public delegate void DataHandler(byte[] data);

        public static BoxHandler AllData(DataHandler handler)
        {
            return (box) =>
            {
                var all = box.Reader.GetLength() - box.Reader.GetPosition();
                handler(box.Reader.ReadBytes((int)all));
            };
        }

        public static void Children(ParsedBox box)
        {
            var headerSize = HeaderSize(box);
            while (box.Reader.HasMoreData() && !box.Parser.Done)
            {
                box.Parser.ParseNext(box.Start + headerSize, box.Reader, box.PartialOkay);
            }
        }

        public static void SampleDescription(ParsedBox box)
        {
            var headerSize = HeaderSize(box);
            var count = box.Reader.ReadUInt32();
            for (int i = 0; i < count; i++)
            {
                box.Parser.ParseNext(box.Start + headerSize, box.Reader, box.PartialOkay);
                if (box.Parser.Done)
                {
                    break;
                }
            }
        }

        public void Parse(byte[] data, bool partialOkay = false, bool stopOnPartial = false)
        {
            var reader = new BinaryReader2(new MemoryStream(data));
            this.Done = false;
            while (reader.HasMoreData() && !this.Done) 
            {
                this.ParseNext(0, reader, partialOkay, stopOnPartial);
            }
        }

        private void ParseNext(long absStart, BinaryReader2 reader, bool partialOkay, bool stopOnPartial = false)
        {
            var start = reader.GetPosition();

            // size(4 bytes) + type(4 bytes) = 8 bytes
            if (stopOnPartial && start + 8 > reader.GetLength())
            {
                this.Done = true;
                return;
            }

            long size = reader.ReadUInt32();
            long type = reader.ReadUInt32();
            var name = TypeToString(type);
            var has64BitSize = false;

            //Console.WriteLine($"Parsing MP4 box: {name}");

            switch (size)
            {
                case 0:
                    size = reader.GetLength() - start;
                    break;
                case 1:
                    if (stopOnPartial && reader.GetPosition() + 8 > reader.GetLength())
                    {
                        this.Done = true;
                        return;
                    }
                    size = (long)reader.ReadUInt64();
                    has64BitSize = true;
                    break;
            }

            BoxHandler boxDefinition = null;
            this.BoxDefinitions.TryGetValue(type, out boxDefinition);

            if (boxDefinition != null)
            {
                uint version = 1000;
                uint flags = 1000;

                if (this.Headers[type] == (int)BoxType.FULL_BOX)
                {
                    if (stopOnPartial && reader.GetPosition() + 4 > reader.GetLength())
                    {
                        this.Done = true;
                        return;
                    }
                    var versionAndFlags = reader.ReadUInt32();
                    version = versionAndFlags >> 24;
                    flags = versionAndFlags & 0xFFFFFF;
                }
                var end = start + size;
                if (partialOkay && end > reader.GetLength())
                {
                    // For partial reads, truncate the payload if we must.
                    end = reader.GetLength();
                }

                if (stopOnPartial && end > reader.GetLength())
                {
                    this.Done = true;
                    return;
                }

                int payloadSize = (int)(end - reader.GetPosition());
                var payload = (payloadSize > 0) ? reader.ReadBytes(payloadSize) : new byte[0];
                var box = new ParsedBox()
                {
                    Parser = this,
                    PartialOkay = partialOkay || false,
                    Version = version,
                    Flags = flags,
                    Reader = new BinaryReader2(new MemoryStream(payload)),
                    Start = start + absStart,
                    Has64BitSize = has64BitSize,
                };

                boxDefinition(box);
            }
            else
            {
                // Move the read head to be at the end of the box.
                // If the box is longer than the remaining parts of the file, e.g. the
                // mp4 is improperly formatted, or this was a partial range request that
                // ended in the middle of a box, just skip to the end.
                var skipLength = Math.Min(
                  start + size - reader.GetPosition(),
                  reader.GetLength() - reader.GetPosition());
                reader.ReadBytes((int)skipLength);
            }
        }


        private static int HeaderSize(ParsedBox box)
        {
            return /* basic header */ 8
                + /* additional 64-bit size field */ (box.Has64BitSize ? 8 : 0)
                + /* version and flags for a "full" box */ (box.Flags != 0 ? 4 : 0);
        }

        public static string TypeToString(long type)
        {
            return Encoding.UTF8.GetString(new byte[]
            {
                 (byte)((type >> 24) & 0xff),
                 (byte)((type >> 16) & 0xff),
                 (byte)((type >> 8) & 0xff),
                 (byte)(type & 0xff)
            });
        }

        private static int TypeFromString(string name)
        {
            if (name.Length != 4)
                throw new Exception("Mp4 box names must be 4 characters long");
            var code = 0;
            foreach (var chr in name) {
                code = (code << 8) | chr;
            }
            return code;
        }

        public MP4Parser Box(string type, BoxHandler handler)
        {
            var typeCode = TypeFromString(type);
            this.Headers[typeCode] = (int)BoxType.BASIC_BOX;
            this.BoxDefinitions[typeCode] = handler;
            return this;
        }

        public MP4Parser FullBox(string type, BoxHandler handler)
        {
            var typeCode = TypeFromString(type);
            this.Headers[typeCode] = (int)BoxType.FULL_BOX;
            this.BoxDefinitions[typeCode] = handler;
            return this;
        }

        public static uint ParseMDHD(BinaryReader2 reader, uint version)
        {
            if (version == 1)
            {
                reader.ReadBytes(8); // Skip "creation_time"
                reader.ReadBytes(8); // Skip "modification_time"
            }
            else
            {
                reader.ReadBytes(4); // Skip "creation_time"
                reader.ReadBytes(4); // Skip "modification_time"
            }

            return reader.ReadUInt32();
        }

        public static ulong ParseTFDT(BinaryReader2 reader, uint version)
        {
            return version == 1 ? reader.ReadUInt64() : reader.ReadUInt32();
        }

        public static TFHD ParseTFHD(BinaryReader2 reader, uint flags)
        {
            var trackId = reader.ReadUInt32();
            uint defaultSampleDuration = 0;
            uint defaultSampleSize = 0;

            // Skip "base_data_offset" if present.
            if ((flags & 0x000001) != 0) 
            {
                reader.ReadBytes(8);
            }

            // Skip "sample_description_index" if present.
            if ((flags & 0x000002) != 0)
            {
                reader.ReadBytes(4);
            }

            // Read "default_sample_duration" if present.
            if ((flags & 0x000008) != 0)
            {
                defaultSampleDuration = reader.ReadUInt32();
            }

            // Read "default_sample_size" if present.
            if ((flags & 0x000010) != 0)
            {
                defaultSampleSize = reader.ReadUInt32();
            }

            return new TFHD() { TrackId = trackId, DefaultSampleDuration = defaultSampleDuration, DefaultSampleSize = defaultSampleSize };
        }

        public static TRUN ParseTRUN(BinaryReader2 reader, uint version, uint flags)
        {
            var trun = new TRUN();
            trun.SampleCount = reader.ReadUInt32();

            // Skip "data_offset" if present.
            if ((flags & 0x000001) != 0) 
            {
                reader.ReadBytes(4);
            }

            // Skip "first_sample_flags" if present.
            if ((flags & 0x000004) != 0)
            {
                reader.ReadBytes(4);
            }

            for (int i = 0; i < trun.SampleCount; i++)
            {
                var sample = new Sample();

                // Read "sample duration" if present.
                if ((flags & 0x000100) != 0)
                {
                    sample.SampleDuration = reader.ReadUInt32();
                }

                // Read "sample_size" if present.
                if ((flags & 0x000200) != 0)
                {
                    sample.SampleSize = reader.ReadUInt32();
                }

                // Skip "sample_flags" if present.
                if ((flags & 0x000400) != 0)
                {
                    reader.ReadBytes(4);
                }

                // Read "sample_time_offset" if present.
                if ((flags & 0x000800) != 0)
                {
                    sample.SampleCompositionTimeOffset = version == 0 ?
                          reader.ReadUInt32() :
                          (uint)reader.ReadInt32();
                }

                trun.SampleData.Add(sample);
            }

            return trun;
        }
    }
}
