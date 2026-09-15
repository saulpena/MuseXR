using System;
using System.Threading;
using System.Threading.Tasks;
using MusePico.Tripo;
using UnityEngine;

namespace MusePico.Dialogue
{
    /// <summary>
    /// Speaks a master's line in that master's cast voice, through MiniMax T2A v2.
    ///
    /// Same provider, same model and the same <c>voice_id</c> per master as muse-infinity, so
    /// Monet sounds like Monet in both projects. The casting table is carried in
    /// <c>masters.json</c> alongside the lens, exported from her <c>config/masterVoices.js</c>
    /// where every id was verified against a live 332-voice system list — <b>do not invent ids</b>.
    ///
    /// One deliberate difference from her server: <b>PCM, not mp3</b>. Hers returns hex-encoded
    /// mp3 because a browser decodes mp3 for free. Unity does not — <c>AudioClip.Create</c> takes
    /// float samples, so raw PCM becomes a clip with no decoder, no temp file and no platform
    /// caveats. Asking for mp3 here would mean shipping a decoder to save nothing.
    ///
    /// Requires <c>MINIMAX_API_KEY</c>. It is a different provider from OpenAI and needs its own
    /// key; there is no sharing with the dialogue key.
    /// </summary>
    public class MiniMaxVoice
    {
        public const string Endpoint = "https://api.minimax.io/v1/t2a_v2";

        /// <summary>One narrated segment per request. The caller splits longer text on sentences.</summary>
        public const int TextLimit = 600;

        /// <summary>MiniMax returns PCM as signed 16-bit little-endian at this rate.</summary>
        public const int SampleRate = 32000;

        public string Model = "speech-2.8-turbo";
        public int TimeoutSeconds = 20;

        readonly ITripoTransport _transport;

        public MiniMaxVoice(ITripoTransport transport) =>
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));

        /// <summary>
        /// Synthesises one segment. Returns null on any failure — narration is decoration, and a
        /// visitor should never lose the reading because the voice service was unavailable.
        /// </summary>
        public async Task<AudioClip> SpeakAsync(string text, MasterLens master, CancellationToken ct = default)
        {
            var trimmed = (text ?? string.Empty).Trim();
            if (trimmed.Length == 0) return null;
            if (trimmed.Length > TextLimit) trimmed = trimmed.Substring(0, TextLimit);

            var voiceId = master != null && !string.IsNullOrEmpty(master.voiceId) ? master.voiceId : null;
            if (voiceId == null) return null;

            // One request per sentence-bounded segment, as muse-infinity does. T2A synthesises a
            // single utterance per call and a long reply sent whole comes back cut off. A
            // compliant 50-word reading is ~265 characters and stays in one segment, so this is
            // the safety net for the readings that breach the word limit, not the usual path.
            var segments = SpeechSegmenter.Split(trimmed);
            if (segments.Count > 1) return await SpeakSegmentsAsync(segments, master, ct);

            var samples = await SynthesiseAsync(trimmed, master, ct);
            return ToClip(samples, master, 1);
        }

        /// <summary>
        /// Synthesises every segment at once and joins them into one clip.
        ///
        /// Concurrent because a segment costs 1.5-2.4s and a two-segment reading synthesised in
        /// series would add that again to a turn nobody wants longer. Joined into one clip rather
        /// than queued as several, so playback cannot develop a gap between halves of a sentence
        /// — which is what a per-segment AudioSource queue does the first time a frame hitches.
        ///
        /// A failed segment is skipped rather than failing the reading: three quarters of a line
        /// spoken beats silence.
        /// </summary>
        async Task<AudioClip> SpeakSegmentsAsync(
            System.Collections.Generic.List<string> segments, MasterLens master, CancellationToken ct)
        {
            var tasks = new Task<float[]>[segments.Count];
            for (var i = 0; i < segments.Count; i++) tasks[i] = SynthesiseAsync(segments[i], master, ct);

            var parts = await Task.WhenAll(tasks);

            var total = 0;
            foreach (var part in parts) if (part != null) total += part.Length;
            if (total == 0) return null;

            var joined = new float[total];
            var at = 0;
            foreach (var part in parts)
            {
                if (part == null) continue;
                System.Array.Copy(part, 0, joined, at, part.Length);
                at += part.Length;
            }

            return ToClip(joined, master, segments.Count);
        }

        /// <summary>
        /// <b>Main thread only.</b> <c>AudioClip.Create</c> is a Unity API and throws
        /// "Construct_Internal can only be called from the main thread" anywhere else.
        ///
        /// That is why nothing on the path to here uses <c>ConfigureAwait(false)</c>. It was
        /// added out of habit and cost a device build: every await in this class resumed on a
        /// worker thread, so the readings arrived, the voices synthesised, and the clip could
        /// never be constructed. <b>Do not add it back</b> — the awaits here are IO waits that do
        /// not block the main thread anyway, so there is nothing to win.
        /// </summary>
        AudioClip ToClip(float[] samples, MasterLens master, int segments)
        {
            if (samples == null || samples.Length == 0) return null;
            var clip = AudioClip.Create(
                "say-" + master.id + (segments > 1 ? "-x" + segments : ""),
                samples.Length, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        /// <summary>One request, one utterance. Returns null on any failure.</summary>
        async Task<float[]> SynthesiseAsync(string text, MasterLens master, CancellationToken ct)
        {
            var voiceId = master.voiceId;

            var voiceSetting = new JsonBuilder()
                .Add("voice_id", voiceId)
                .AddRaw("speed", (master.voiceSpeed <= 0f ? 1f : master.voiceSpeed).ToString("0.##",
                    System.Globalization.CultureInfo.InvariantCulture))
                .AddRaw("vol", "1.0")
                .Add("pitch", 0);

            var audioSetting = new JsonBuilder()
                .Add("sample_rate", SampleRate)
                .Add("format", "pcm");

            var body = new JsonBuilder()
                .Add("model", Model)
                .Add("text", text)
                .Add("stream", false)
                .Add("language_boost", "auto")
                .Add("output_format", "hex")
                .Add("voice_setting", voiceSetting)
                .Add("audio_setting", audioSetting)
                .ToString();

            var response = await _transport.SendAsync(new TripoHttpRequest
            {
                Method = "POST",
                Url = Endpoint,
                ContentType = "application/json",
                Body = System.Text.Encoding.UTF8.GetBytes(body),
            }, ct);

            if (!string.IsNullOrEmpty(response.TransportError))
            {
                Debug.LogWarning("[Voice] MiniMax unreachable: " + response.TransportError);
                return null;
            }
            if (response.Status < 200 || response.Status >= 300)
            {
                Debug.LogWarning("[Voice] MiniMax returned HTTP " + response.Status);
                return null;
            }

            var parsed = MiniMaxResponse.Parse(response.Body);
            if (parsed.Error != null)
            {
                Debug.LogWarning("[Voice] " + parsed.Error);
                return null;
            }

            var samples = PcmCodec.DecodeHexPcm16(parsed.AudioHex);
            return samples == null || samples.Length == 0 ? null : samples;
        }
    }

    /// <summary>MiniMax's envelope. Pure, so the failure paths are testable without a key.</summary>
    public struct MiniMaxResponse
    {
        public string AudioHex;
        public string Error;

        [Serializable] class Envelope { public Data data; public BaseResp base_resp; }
        [Serializable] class Data { public string audio; }
        [Serializable] class BaseResp { public int status_code; public string status_msg; }

        public static MiniMaxResponse Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new MiniMaxResponse { Error = "MiniMax returned an empty body." };

            Envelope envelope;
            try { envelope = JsonUtility.FromJson<Envelope>(json); }
            catch (Exception ex) { return new MiniMaxResponse { Error = "MiniMax body was not JSON: " + ex.Message }; }

            // status_code lives in its own envelope and is 0 on success — an HTTP 200 with a
            // non-zero status_code is a failure, exactly as with Tripo's `code`.
            var status = envelope?.base_resp?.status_code ?? 0;
            if (status != 0)
                return new MiniMaxResponse
                {
                    Error = "MiniMax status " + status + ": " +
                            (string.IsNullOrEmpty(envelope?.base_resp?.status_msg) ? "unknown" : envelope.base_resp.status_msg) +
                            Hint(status),
                };

            var audio = envelope?.data?.audio;
            return string.IsNullOrEmpty(audio)
                ? new MiniMaxResponse { Error = "MiniMax response carried no audio." }
                : new MiniMaxResponse { AudioHex = audio };
        }

        /// <summary>
        /// Guidance for the codes that have actually been seen, so a failure at demo time names
        /// its own fix instead of being a number to go and look up.
        ///
        /// <b>1008 was hit for real on 14 Sep 2026.</b> It is worth calling out specifically
        /// because it is the one failure that looks like a broken integration and is not: the key
        /// authenticates, the free voice-list call succeeds, and only the paid call is refused.
        /// Listing voices proves the key; it does not prove there is any money behind it.
        /// </summary>
        static string Hint(int status)
        {
            switch (status)
            {
                case 1008:
                    return " — the key is valid and the account has no credit. Top it up at " +
                           "platform.minimax.io/console. Listing voices is free and will keep " +
                           "working regardless, so it cannot tell you about this.";
                case 1004:
                case 1001:
                    return " — authentication. Check the key is from the global console " +
                           "(platform.minimax.io), not the mainland-China one.";
                case 2013:
                    return " — invalid parameters. The voice_id or the model may not exist on this account.";
                default:
                    return string.Empty;
            }
        }
    }

    /// <summary>Hex-encoded signed 16-bit PCM to Unity's normalised floats. Pure and tested.</summary>
    public static class PcmCodec
    {
        public static float[] DecodeHexPcm16(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return Array.Empty<float>();

            // Two hex characters per byte, two bytes per sample: four characters per sample.
            var sampleCount = hex.Length / 4;
            var samples = new float[sampleCount];

            for (var i = 0; i < sampleCount; i++)
            {
                var at = i * 4;
                var lo = FromHex(hex[at]) << 4 | FromHex(hex[at + 1]);
                var hi = FromHex(hex[at + 2]) << 4 | FromHex(hex[at + 3]);
                if (lo < 0 || hi < 0) return Array.Empty<float>();

                // Little-endian: the first byte is the low half.
                var value = (short)((hi << 8) | lo);
                samples[i] = value / 32768f;
            }

            return samples;
        }

        static int FromHex(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return -1;
        }
    }
}
