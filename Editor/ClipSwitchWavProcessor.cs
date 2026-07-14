using System;

namespace QuartzLab.ClipSwitch
{
    internal sealed class ClipSwitchWavProcessSettings
    {
        public double TrimStartSeconds;
        public double TrimEndSeconds;
        public float GainDb;
        public bool Normalize;
        public float NormalizeTargetDb = -0.5f;
        public bool Reverse;
        public bool RemoveDcOffset;
        public bool ConvertToMono;
        public float FadeInSeconds;
        public float FadeOutSeconds;
        public float PitchSemitones;
    }

    internal static class ClipSwitchWavProcessor
    {
        public static ClipSwitchWavData Process(ClipSwitchWavData source, ClipSwitchWavProcessSettings settings)
        {
            if (source == null || source.Samples == null)
                throw new ArgumentNullException("source");
            if (settings == null)
                throw new ArgumentNullException("settings");

            ClipSwitchWavData data = CreateTrimmedCopy(source, settings.TrimStartSeconds, settings.TrimEndSeconds);

            if (settings.ConvertToMono && data.Channels > 1)
                ConvertToMono(data);
            if (settings.RemoveDcOffset)
                RemoveDcOffset(data);
            if (settings.Reverse)
                Reverse(data);
            if (Math.Abs(settings.PitchSemitones) > 0.001f)
                ResamplePitchAndSpeed(data, settings.PitchSemitones);
            if (Math.Abs(settings.GainDb) > 0.001f)
                ApplyGain(data, settings.GainDb);
            if (settings.Normalize)
                Normalize(data, settings.NormalizeTargetDb);

            ApplyFades(data, settings.FadeInSeconds, settings.FadeOutSeconds);
            Clamp(data);
            return data;
        }

        public static void DetectNonSilentRange(
            ClipSwitchWavData data,
            float thresholdDb,
            out double startSeconds,
            out double endSeconds)
        {
            startSeconds = 0.0;
            endSeconds = data == null ? 0.0 : data.Duration;
            if (data == null || data.Samples == null || data.FrameCount == 0)
                return;

            float threshold = (float)Math.Pow(10.0, thresholdDb / 20.0);
            int first = -1;
            int last = -1;
            for (int frame = 0; frame < data.FrameCount; frame++)
            {
                bool audible = false;
                int offset = frame * data.Channels;
                for (int channel = 0; channel < data.Channels; channel++)
                {
                    if (Math.Abs(data.Samples[offset + channel]) >= threshold)
                    {
                        audible = true;
                        break;
                    }
                }

                if (audible)
                {
                    if (first < 0)
                        first = frame;
                    last = frame;
                }
            }

            if (first >= 0)
            {
                startSeconds = (double)first / data.SampleRate;
                endSeconds = (double)(last + 1) / data.SampleRate;
            }
        }

        private static ClipSwitchWavData CreateTrimmedCopy(ClipSwitchWavData source, double startSeconds, double endSeconds)
        {
            int startFrame = Clamp((int)Math.Round(startSeconds * source.SampleRate), 0, source.FrameCount);
            int endFrame = Clamp((int)Math.Round(endSeconds * source.SampleRate), startFrame, source.FrameCount);
            int frames = endFrame - startFrame;
            if (frames <= 0)
                throw new InvalidOperationException("Trim range is empty.");
            float[] samples = new float[checked(frames * source.Channels)];
            Array.Copy(source.Samples, startFrame * source.Channels, samples, 0, samples.Length);
            return new ClipSwitchWavData
            {
                SampleRate = source.SampleRate,
                Channels = source.Channels,
                BitsPerSample = source.BitsPerSample,
                IsFloat = source.IsFloat,
                Samples = samples
            };
        }

        private static void ConvertToMono(ClipSwitchWavData data)
        {
            int frames = data.FrameCount;
            float[] output = new float[frames];
            for (int frame = 0; frame < frames; frame++)
            {
                float sum = 0f;
                int offset = frame * data.Channels;
                for (int channel = 0; channel < data.Channels; channel++)
                    sum += data.Samples[offset + channel];
                output[frame] = sum / data.Channels;
            }
            data.Channels = 1;
            data.Samples = output;
        }

        private static void RemoveDcOffset(ClipSwitchWavData data)
        {
            double[] averages = new double[data.Channels];
            int frames = data.FrameCount;
            for (int frame = 0; frame < frames; frame++)
            {
                int offset = frame * data.Channels;
                for (int channel = 0; channel < data.Channels; channel++)
                    averages[channel] += data.Samples[offset + channel];
            }

            for (int channel = 0; channel < data.Channels; channel++)
                averages[channel] /= Math.Max(1, frames);

            for (int frame = 0; frame < frames; frame++)
            {
                int offset = frame * data.Channels;
                for (int channel = 0; channel < data.Channels; channel++)
                    data.Samples[offset + channel] -= (float)averages[channel];
            }
        }

        private static void Reverse(ClipSwitchWavData data)
        {
            int left = 0;
            int right = data.FrameCount - 1;
            while (left < right)
            {
                for (int channel = 0; channel < data.Channels; channel++)
                {
                    int a = left * data.Channels + channel;
                    int b = right * data.Channels + channel;
                    float value = data.Samples[a];
                    data.Samples[a] = data.Samples[b];
                    data.Samples[b] = value;
                }
                left++;
                right--;
            }
        }

        private static void ResamplePitchAndSpeed(ClipSwitchWavData data, float semitones)
        {
            double factor = Math.Pow(2.0, semitones / 12.0);
            int sourceFrames = data.FrameCount;
            int outputFrames = Math.Max(1, (int)Math.Round(sourceFrames / factor));
            float[] output = new float[checked(outputFrames * data.Channels)];

            for (int frame = 0; frame < outputFrames; frame++)
            {
                double sourcePosition = Math.Min(sourceFrames - 1.0, frame * factor);
                int first = Math.Min(sourceFrames - 1, (int)Math.Floor(sourcePosition));
                int previous = Math.Max(0, first - 1);
                int second = Math.Min(sourceFrames - 1, first + 1);
                int third = Math.Min(sourceFrames - 1, first + 2);
                float t = (float)(sourcePosition - first);
                for (int channel = 0; channel < data.Channels; channel++)
                {
                    float p0 = data.Samples[previous * data.Channels + channel];
                    float p1 = data.Samples[first * data.Channels + channel];
                    float p2 = data.Samples[second * data.Channels + channel];
                    float p3 = data.Samples[third * data.Channels + channel];
                    float t2 = t * t;
                    float t3 = t2 * t;
                    output[frame * data.Channels + channel] = 0.5f * ((2f * p1) + (-p0 + p2) * t +
                        (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
                }
            }

            data.Samples = output;
        }

        private static void ApplyGain(ClipSwitchWavData data, float gainDb)
        {
            float gain = (float)Math.Pow(10.0, gainDb / 20.0);
            for (int i = 0; i < data.Samples.Length; i++)
                data.Samples[i] *= gain;
        }

        private static void Normalize(ClipSwitchWavData data, float targetDb)
        {
            float peak = 0f;
            for (int i = 0; i < data.Samples.Length; i++)
                peak = Math.Max(peak, Math.Abs(data.Samples[i]));
            if (peak <= 0.0000001f)
                return;

            float target = (float)Math.Pow(10.0, targetDb / 20.0);
            float multiplier = target / peak;
            for (int i = 0; i < data.Samples.Length; i++)
                data.Samples[i] *= multiplier;
        }

        private static void ApplyFades(ClipSwitchWavData data, float fadeInSeconds, float fadeOutSeconds)
        {
            int frames = data.FrameCount;
            int fadeInFrames = Clamp((int)Math.Round(fadeInSeconds * data.SampleRate), 0, frames);
            int fadeOutFrames = Clamp((int)Math.Round(fadeOutSeconds * data.SampleRate), 0, frames);

            for (int frame = 0; frame < fadeInFrames; frame++)
            {
                float multiplier = fadeInFrames <= 1 ? 0f : (float)frame / (fadeInFrames - 1);
                MultiplyFrame(data, frame, multiplier);
            }

            for (int i = 0; i < fadeOutFrames; i++)
            {
                int frame = frames - fadeOutFrames + i;
                float multiplier = fadeOutFrames <= 1 ? 0f : 1f - (float)i / (fadeOutFrames - 1);
                MultiplyFrame(data, frame, multiplier);
            }
        }

        private static void MultiplyFrame(ClipSwitchWavData data, int frame, float multiplier)
        {
            int offset = frame * data.Channels;
            for (int channel = 0; channel < data.Channels; channel++)
                data.Samples[offset + channel] *= multiplier;
        }

        private static void Clamp(ClipSwitchWavData data)
        {
            for (int i = 0; i < data.Samples.Length; i++)
            {
                float value = data.Samples[i];
                data.Samples[i] = float.IsNaN(value) || float.IsInfinity(value)
                    ? 0f
                    : Math.Max(-1f, Math.Min(1f, value));
            }
        }

        private static int Clamp(int value, int min, int max)
        {
            return Math.Max(min, Math.Min(max, value));
        }
    }
}
