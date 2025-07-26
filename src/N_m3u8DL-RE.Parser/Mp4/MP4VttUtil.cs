using System.Text;

using N_m3u8DL_RE.Common.Entity;

namespace N_m3u8DL_RE.Parser.Mp4
{
    public static class MP4VttUtil
    {
        public static (bool, uint) CheckInit(byte[] data)
        {
            uint timescale = 0;
            bool sawWVTT = false;

            // parse init
            new MP4Parser()
                .Box("moov", MP4Parser.Children)
                .Box("trak", MP4Parser.Children)
                .Box("mdia", MP4Parser.Children)
                .FullBox("mdhd", box =>
                {
                    if (box.Version is not (0 or 1))
                    {
                        throw new Exception("MDHD version can only be 0 or 1");
                    }

                    timescale = MP4Parser.ParseMDHD(box.Reader, box.Version);
                })
                .Box("minf", MP4Parser.Children)
                .Box("stbl", MP4Parser.Children)
                .FullBox("stsd", MP4Parser.SampleDescription)
                .Box("wvtt", _ =>
                {
                    // A valid vtt init segment, though we have no actual subtitles yet.
                    sawWVTT = true;
                })
                .Parse(data);

            return (sawWVTT, timescale);
        }

        public static WebVttSub ExtractSub(IEnumerable<string> files, uint timescale)
        {
            if (timescale == 0)
            {
                throw new Exception("Missing timescale for VTT content!");
            }

            List<SubCue> cues = [];

            foreach (string item in files)
            {
                byte[] dataSeg = File.ReadAllBytes(item);

                bool sawTFDT = false;
                bool sawTRUN = false;
                bool sawMDAT = false;
                byte[]? rawPayload = null;
                ulong baseTime = 0;
                ulong defaultDuration = 0;
                List<Sample> presentations = [];


                // parse media
                new MP4Parser()
                    .Box("moof", MP4Parser.Children)
                    .Box("traf", MP4Parser.Children)
                    .FullBox("tfdt", box =>
                    {
                        sawTFDT = true;
                        if (box.Version is not (0 or 1))
                        {
                            throw new Exception("TFDT version can only be 0 or 1");
                        }

                        baseTime = MP4Parser.ParseTFDT(box.Reader, box.Version);
                    })
                    .FullBox("tfhd", box =>
                    {
                        if (box.Flags == 1000)
                        {
                            throw new Exception("A TFHD box should have a valid flags value");
                        }

                        defaultDuration = MP4Parser.ParseTFHD(box.Reader, box.Flags).DefaultSampleDuration;
                    })
                    .FullBox("trun", box =>
                    {
                        sawTRUN = true;
                        if (box.Version == 1000)
                        {
                            throw new Exception("A TRUN box should have a valid version value");
                        }

                        if (box.Flags == 1000)
                        {
                            throw new Exception("A TRUN box should have a valid flags value");
                        }

                        presentations = MP4Parser.ParseTRUN(box.Reader, box.Version, box.Flags).SampleData;
                    })
                    .Box("mdat", MP4Parser.AllData(data =>
                    {
                        if (sawMDAT)
                        {
                            throw new Exception("VTT cues in mp4 with multiple MDAT are not currently supported");
                        }

                        sawMDAT = true;
                        rawPayload = data;
                    }))
                    .Parse(dataSeg,/* partialOkay= */ false);

                if (!sawMDAT && !sawTFDT && !sawTRUN)
                {
                    throw new Exception("A required box is missing");
                }

                ulong currentTime = baseTime;
                BinaryReader2 reader = new(new MemoryStream(rawPayload!));

                foreach (Sample presentation in presentations)
                {
                    ulong duration = presentation.SampleDuration == 0 ? defaultDuration : presentation.SampleDuration;
                    ulong startTime = presentation.SampleCompositionTimeOffset != 0 ?
                        baseTime + presentation.SampleCompositionTimeOffset :
                        currentTime;
                    currentTime = startTime + duration;
                    int totalSize = 0;
                    do
                    {
                        // Read the payload size.
                        int payloadSize = (int)reader.ReadUInt32();
                        totalSize += payloadSize;

                        // Skip the type.
                        uint payloadType = reader.ReadUInt32();
                        string payloadName = MP4Parser.TypeToString(payloadType);

                        // Read the data payload.
                        byte[]? payload = null;
                        if (payloadName == "vttc")
                        {
                            if (payloadSize > 8)
                            {
                                payload = reader.ReadBytes(payloadSize - 8);
                            }
                        }
                        else if (payloadName == "vtte")
                        {
                            // It's a vtte, which is a vtt cue that is empty. Ignore any data that
                            // does exist.
                            reader.ReadBytes(payloadSize - 8);
                        }
                        else
                        {
                            Console.WriteLine($"Unknown box {payloadName}! Skipping!");
                            reader.ReadBytes(payloadSize - 8);
                        }

                        if (duration != 0)
                        {
                            if (payload != null)
                            {
                                SubCue? cue = ParseVTTC(
                                    payload,
                                    0 + (double)startTime / timescale,
                                    0 + (double)currentTime / timescale);
                                // Check if same subtitle has been splitted
                                if (cue != null)
                                {
                                    int index = cues.FindLastIndex(s => s.EndTime == cue.StartTime && s.Settings == cue.Settings && s.Payload == cue.Payload);
                                    if (index != -1)
                                    {
                                        cues[index].EndTime = cue.EndTime;
                                    }
                                    else
                                    {
                                        cues.Add(cue);
                                    }
                                }
                            }
                        }
                        else
                        {
                            throw new Exception("WVTT sample duration unknown, and no default found!");
                        }

                        if (!(presentation.SampleSize == 0 || totalSize <= presentation.SampleSize))
                        {
                            throw new Exception("The samples do not fit evenly into the sample sizes given in the TRUN box!");
                        }

                    } while (presentation.SampleSize != 0 && totalSize < presentation.SampleSize);

                    if (reader.HasMoreData())
                    {
                        // throw new Exception("MDAT which contain VTT cues and non-VTT data are not currently supported!");
                    }
                }
            }

            return cues.Count > 0 ? new WebVttSub() { Cues = cues } : new WebVttSub();
        }

        private static SubCue? ParseVTTC(byte[] data, double startTime, double endTime)
        {
            string payload = string.Empty;
            string id = string.Empty;
            string settings = string.Empty;
            new MP4Parser()
                .Box("payl", MP4Parser.AllData(data =>
                {
                    payload = Encoding.UTF8.GetString(data);
                }))
                .Box("iden", MP4Parser.AllData(data =>
                {
                    id = Encoding.UTF8.GetString(data);
                }))
                .Box("sttg", MP4Parser.AllData(data =>
                {
                    settings = Encoding.UTF8.GetString(data);
                }))
                .Parse(data);

            return !string.IsNullOrEmpty(payload)
                ? new SubCue() { StartTime = TimeSpan.FromSeconds(startTime), EndTime = TimeSpan.FromSeconds(endTime), Payload = payload, Settings = settings }
                : null;
        }
    }
}