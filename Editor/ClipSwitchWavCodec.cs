using System;
using System.IO;
using System.Text;

namespace QuartzLab.ClipSwitch
{
    internal sealed class ClipSwitchWavData
    {
        public int SampleRate;
        public int Channels;
        public int BitsPerSample;
        public bool IsFloat;
        public float[] Samples;

        public int FrameCount
        {
            get { return Samples == null || Channels <= 0 ? 0 : Samples.Length / Channels; }
        }

        public double Duration
        {
            get { return SampleRate <= 0 ? 0.0 : (double)FrameCount / SampleRate; }
        }
    }

    internal static class ClipSwitchWavCodec
    {
        public static ClipSwitchWavData Read(string absolutePath)
        {
            using (FileStream stream = File.OpenRead(absolutePath))
            using (BinaryReader reader = new BinaryReader(stream, Encoding.ASCII))
            {
                string riff = ReadFourCC(reader);
                reader.ReadUInt32();
                string wave = ReadFourCC(reader);
                if (riff != "RIFF" || wave != "WAVE")
                    throw new InvalidDataException("The file is not a RIFF/WAVE file.");

                ushort formatTag = 0;
                ushort channels = 0;
                int sampleRate = 0;
                ushort bitsPerSample = 0;
                byte[] audioBytes = null;

                while (stream.Position + 8 <= stream.Length)
                {
                    string chunkId = ReadFourCC(reader);
                    uint chunkSize = reader.ReadUInt32();
                    long chunkStart = stream.Position;

                    if (chunkId == "fmt ")
                    {
                        if (chunkSize < 16)
                            throw new InvalidDataException("Invalid WAV format chunk.");
                        formatTag = reader.ReadUInt16();
                        channels = reader.ReadUInt16();
                        sampleRate = reader.ReadInt32();
                        reader.ReadUInt32();
                        reader.ReadUInt16();
                        bitsPerSample = reader.ReadUInt16();

                        if (formatTag == 0xFFFE && chunkSize >= 40)
                        {
                            reader.ReadUInt16();
                            reader.ReadUInt16();
                            reader.ReadUInt32();
                            byte[] subFormat = reader.ReadBytes(16);
                            if (subFormat.Length >= 2)
                                formatTag = BitConverter.ToUInt16(subFormat, 0);
                        }
                    }
                    else if (chunkId == "data")
                    {
                        if (chunkSize > int.MaxValue)
                            throw new InvalidDataException("WAV data chunk is too large.");
                        audioBytes = reader.ReadBytes((int)chunkSize);
                        if (audioBytes.Length != (int)chunkSize)
                            throw new EndOfStreamException("Unexpected end of WAV audio data.");
                    }

                    long next = chunkStart + chunkSize;
                    if ((chunkSize & 1) != 0)
                        next++;
                    stream.Position = Math.Min(next, stream.Length);
                }

                if (channels == 0 || sampleRate <= 0 || bitsPerSample == 0 || audioBytes == null)
                    throw new InvalidDataException("The WAV file is missing required format or audio data.");

                bool isFloat = formatTag == 3;
                if (formatTag != 1 && formatTag != 3)
                    throw new NotSupportedException("Only PCM and IEEE-float WAV files are supported.");

                int bytesPerSample = bitsPerSample / 8;
                int blockAlign = bytesPerSample * channels;
                if (bytesPerSample <= 0 || blockAlign <= 0 || audioBytes.Length % blockAlign != 0)
                    throw new InvalidDataException("Invalid WAV sample layout.");

                int sampleCount = audioBytes.Length / bytesPerSample;
                float[] samples = new float[sampleCount];
                using (MemoryStream audioStream = new MemoryStream(audioBytes, false))
                using (BinaryReader audioReader = new BinaryReader(audioStream))
                {
                    for (int i = 0; i < sampleCount; i++)
                        samples[i] = ReadSample(audioReader, bitsPerSample, isFloat);
                }

                return new ClipSwitchWavData
                {
                    SampleRate = sampleRate,
                    Channels = channels,
                    BitsPerSample = bitsPerSample,
                    IsFloat = isFloat,
                    Samples = samples
                };
            }
        }

        public static void Write(string absolutePath, ClipSwitchWavData data)
        {
            if (data == null || data.Samples == null)
                throw new ArgumentNullException("data");
            if (data.Channels <= 0 || data.SampleRate <= 0)
                throw new InvalidDataException("Invalid WAV output format.");

            int bits = data.BitsPerSample;
            if (data.IsFloat)
                bits = bits == 64 ? 64 : 32;
            else if (bits != 8 && bits != 16 && bits != 24 && bits != 32)
                bits = 16;

            int bytesPerSample = bits / 8;
            int dataSize = checked(data.Samples.Length * bytesPerSample);
            int riffSize = checked(36 + dataSize);
            int blockAlign = data.Channels * bytesPerSample;
            int byteRate = data.SampleRate * blockAlign;
            ushort formatTag = data.IsFloat ? (ushort)3 : (ushort)1;

            string directory = Path.GetDirectoryName(absolutePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            using (FileStream stream = File.Create(absolutePath))
            using (BinaryWriter writer = new BinaryWriter(stream, Encoding.ASCII))
            {
                WriteFourCC(writer, "RIFF");
                writer.Write(riffSize);
                WriteFourCC(writer, "WAVE");
                WriteFourCC(writer, "fmt ");
                writer.Write(16);
                writer.Write(formatTag);
                writer.Write((ushort)data.Channels);
                writer.Write(data.SampleRate);
                writer.Write(byteRate);
                writer.Write((ushort)blockAlign);
                writer.Write((ushort)bits);
                WriteFourCC(writer, "data");
                writer.Write(dataSize);

                for (int i = 0; i < data.Samples.Length; i++)
                    WriteSample(writer, data.Samples[i], bits, data.IsFloat);
            }
        }

        private static float ReadSample(BinaryReader reader, int bits, bool isFloat)
        {
            if (isFloat)
            {
                if (bits == 32)
                    return reader.ReadSingle();
                if (bits == 64)
                    return (float)reader.ReadDouble();
                throw new NotSupportedException("Unsupported floating-point WAV bit depth: " + bits);
            }

            switch (bits)
            {
                case 8:
                    return (reader.ReadByte() - 128) / 128f;
                case 16:
                    return reader.ReadInt16() / 32768f;
                case 24:
                {
                    int value = reader.ReadByte() | (reader.ReadByte() << 8) | (reader.ReadByte() << 16);
                    if ((value & 0x800000) != 0)
                        value |= unchecked((int)0xFF000000);
                    return value / 8388608f;
                }
                case 32:
                    return reader.ReadInt32() / 2147483648f;
                default:
                    throw new NotSupportedException("Unsupported PCM WAV bit depth: " + bits);
            }
        }

        private static void WriteSample(BinaryWriter writer, float sample, int bits, bool isFloat)
        {
            if (float.IsNaN(sample) || float.IsInfinity(sample)) sample = 0f;
            sample = Math.Max(-1f, Math.Min(1f, sample));
            if (isFloat)
            {
                if (bits == 64)
                    writer.Write((double)sample);
                else
                    writer.Write(sample);
                return;
            }

            switch (bits)
            {
                case 8:
                    writer.Write((byte)Math.Max(0, Math.Min(255, (int)Math.Round(sample * 127.5f + 127.5f))));
                    break;
                case 16:
                    writer.Write((short)Math.Max(short.MinValue, Math.Min(short.MaxValue, (int)Math.Round(sample * 32767f))));
                    break;
                case 24:
                {
                    int value = Math.Max(-8388608, Math.Min(8388607, (int)Math.Round(sample * 8388607f)));
                    writer.Write((byte)(value & 0xFF));
                    writer.Write((byte)((value >> 8) & 0xFF));
                    writer.Write((byte)((value >> 16) & 0xFF));
                    break;
                }
                case 32:
                {
                    long scaled = (long)Math.Round(sample * 2147483647.0);
                    if (scaled < int.MinValue) scaled = int.MinValue;
                    if (scaled > int.MaxValue) scaled = int.MaxValue;
                    writer.Write((int)scaled);
                    break;
                }
                default:
                    throw new NotSupportedException("Unsupported WAV output bit depth: " + bits);
            }
        }

        private static string ReadFourCC(BinaryReader reader)
        {
            return Encoding.ASCII.GetString(reader.ReadBytes(4));
        }

        private static void WriteFourCC(BinaryWriter writer, string value)
        {
            writer.Write(Encoding.ASCII.GetBytes(value));
        }
    }
}
