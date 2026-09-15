using System;

namespace MusePico.Generation
{
    /// <summary>
    /// Unity's <c>AudioClip</c> samples as a 16-bit PCM WAV file.
    ///
    /// Needed because every speech-to-text service takes a container, not a float array, and
    /// Unity has no encoder in the box. WAV rather than a compressed format on purpose: no codec
    /// dependency, no licensing, and a few seconds of speech is around 100 KB at 16 kHz mono,
    /// which is nothing next to the model that comes back.
    ///
    /// 16 kHz mono is also not arbitrary — it is what speech recognisers are trained on, and
    /// sending a headset's 48 kHz stereo capture wastes four times the bandwidth for no accuracy.
    /// <see cref="Downmix"/> and <see cref="Resample"/> do that reduction.
    ///
    /// Pure arithmetic over arrays, so it is tested without a microphone or a device.
    /// </summary>
    public static class WavEncoder
    {
        /// <summary>What speech models expect. Higher rates cost bandwidth and buy nothing.</summary>
        public const int SpeechSampleRate = 16000;

        /// <summary>
        /// Encodes normalised -1..1 samples as a mono or multi-channel 16-bit PCM WAV.
        /// Samples outside the range are clamped rather than allowed to wrap, which would turn a
        /// loud syllable into a burst of noise.
        /// </summary>
        public static byte[] Encode(float[] samples, int channels, int sampleRate)
        {
            if (samples == null) throw new ArgumentNullException(nameof(samples));
            if (channels < 1) throw new ArgumentOutOfRangeException(nameof(channels));
            if (sampleRate < 1) throw new ArgumentOutOfRangeException(nameof(sampleRate));

            const int headerBytes = 44;
            const int bitsPerSample = 16;
            var dataBytes = samples.Length * 2;
            var bytes = new byte[headerBytes + dataBytes];

            var byteRate = sampleRate * channels * bitsPerSample / 8;
            var blockAlign = (short)(channels * bitsPerSample / 8);

            WriteAscii(bytes, 0, "RIFF");
            WriteInt32(bytes, 4, 36 + dataBytes);
            WriteAscii(bytes, 8, "WAVE");
            WriteAscii(bytes, 12, "fmt ");
            WriteInt32(bytes, 16, 16);                       // PCM subchunk size
            WriteInt16(bytes, 20, 1);                        // format: PCM
            WriteInt16(bytes, 22, (short)channels);
            WriteInt32(bytes, 24, sampleRate);
            WriteInt32(bytes, 28, byteRate);
            WriteInt16(bytes, 32, blockAlign);
            WriteInt16(bytes, 34, bitsPerSample);
            WriteAscii(bytes, 36, "data");
            WriteInt32(bytes, 40, dataBytes);

            var at = headerBytes;
            foreach (var sample in samples)
            {
                var clamped = sample < -1f ? -1f : sample > 1f ? 1f : sample;
                var value = (short)(clamped * short.MaxValue);
                bytes[at++] = (byte)(value & 0xff);
                bytes[at++] = (byte)((value >> 8) & 0xff);
            }

            return bytes;
        }

        /// <summary>Averages interleaved channels down to mono.</summary>
        public static float[] Downmix(float[] interleaved, int channels)
        {
            if (interleaved == null) throw new ArgumentNullException(nameof(interleaved));
            if (channels <= 1) return interleaved;

            var frames = interleaved.Length / channels;
            var mono = new float[frames];
            for (var frame = 0; frame < frames; frame++)
            {
                var sum = 0f;
                for (var c = 0; c < channels; c++) sum += interleaved[frame * channels + c];
                mono[frame] = sum / channels;
            }
            return mono;
        }

        /// <summary>
        /// Linear resample of mono samples. Good enough for speech: the recogniser is far more
        /// tolerant of interpolation artefacts than of the wrong sample rate.
        /// </summary>
        public static float[] Resample(float[] mono, int fromRate, int toRate)
        {
            if (mono == null) throw new ArgumentNullException(nameof(mono));
            if (fromRate == toRate || mono.Length == 0) return mono;
            if (fromRate < 1 || toRate < 1) throw new ArgumentOutOfRangeException(nameof(fromRate));

            var outLength = (int)((long)mono.Length * toRate / fromRate);
            if (outLength < 1) outLength = 1;
            var output = new float[outLength];
            var ratio = (double)(mono.Length - 1) / Math.Max(1, outLength - 1);

            for (var i = 0; i < outLength; i++)
            {
                var source = i * ratio;
                var low = (int)source;
                var high = low + 1 < mono.Length ? low + 1 : mono.Length - 1;
                var t = (float)(source - low);
                output[i] = mono[low] * (1f - t) + mono[high] * t;
            }

            return output;
        }

        /// <summary>
        /// Peak level, 0..1. The harness shows this as a meter — on the emulator it is the only
        /// way to tell "the microphone is recording silence" from "the microphone is not there",
        /// which otherwise look identical.
        /// </summary>
        public static float PeakLevel(float[] samples)
        {
            if (samples == null || samples.Length == 0) return 0f;
            var peak = 0f;
            foreach (var sample in samples)
            {
                var magnitude = sample < 0f ? -sample : sample;
                if (magnitude > peak) peak = magnitude;
            }
            return peak > 1f ? 1f : peak;
        }

        /// <summary>
        /// Mean absolute amplitude, 0..1 — the right signal for silence DETECTION, where
        /// <see cref="PeakLevel"/> is the right one for a meter. A single click or a knock on the
        /// desk pins the peak high and would reset a silence timer that should have expired.
        /// </summary>
        public static float MeanLevel(float[] samples)
        {
            if (samples == null || samples.Length == 0) return 0f;
            var sum = 0f;
            foreach (var sample in samples) sum += sample < 0f ? -sample : sample;
            var mean = sum / samples.Length;
            return mean > 1f ? 1f : mean;
        }

        /// <summary>
        /// Trims leading and trailing near-silence. Speech recognisers charge and time out by
        /// audio length, and a push-to-talk clip is mostly the pause before someone starts.
        /// </summary>
        public static float[] TrimSilence(float[] mono, float threshold = 0.01f, int paddingSamples = 1600)
        {
            if (mono == null || mono.Length == 0) return mono ?? Array.Empty<float>();

            int first = -1, last = -1;
            for (var i = 0; i < mono.Length; i++)
            {
                var magnitude = mono[i] < 0f ? -mono[i] : mono[i];
                if (magnitude < threshold) continue;
                if (first < 0) first = i;
                last = i;
            }

            if (first < 0) return Array.Empty<float>();

            var start = Math.Max(0, first - paddingSamples);
            var end = Math.Min(mono.Length - 1, last + paddingSamples);
            var trimmed = new float[end - start + 1];
            Array.Copy(mono, start, trimmed, 0, trimmed.Length);
            return trimmed;
        }

        static void WriteAscii(byte[] buffer, int offset, string text)
        {
            for (var i = 0; i < text.Length; i++) buffer[offset + i] = (byte)text[i];
        }

        static void WriteInt32(byte[] buffer, int offset, int value)
        {
            buffer[offset] = (byte)(value & 0xff);
            buffer[offset + 1] = (byte)((value >> 8) & 0xff);
            buffer[offset + 2] = (byte)((value >> 16) & 0xff);
            buffer[offset + 3] = (byte)((value >> 24) & 0xff);
        }

        static void WriteInt16(byte[] buffer, int offset, short value)
        {
            buffer[offset] = (byte)(value & 0xff);
            buffer[offset + 1] = (byte)((value >> 8) & 0xff);
        }
    }
}
