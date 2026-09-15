using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace MusePico.Generation
{
    /// <summary>
    /// Speech to text via OpenAI's transcription endpoint.
    ///
    /// Tripo has no speech API, so voice input needs a second provider and a second key. That is
    /// worth stating plainly because it doubles the surface that has to be secured on device —
    /// see <see cref="ITripoKeySource"/> for why a key inside an APK is a development
    /// arrangement, not a shipping one.
    ///
    /// The endpoint and its multipart shape (<c>file</c> + <c>model</c>, answering
    /// <c>{ "text": ... }</c>) have been stable for a long time. The MODEL NAME has not:
    /// <see cref="model"/> is a field rather than a constant for exactly that reason, and it has
    /// not been verified against the account — muse-infinity pins its chat models in
    /// <c>.env.example</c> after measuring them, and this deserves the same treatment before it
    /// is trusted.
    ///
    /// Transcription is billed per minute of audio. <see cref="VoiceCapture"/> trims silence and
    /// downsamples to 16 kHz mono before this is called, which is the cheap part of keeping that
    /// bill small.
    /// </summary>
    public sealed class OpenAiSpeechToText : ISpeechToText
    {
        public const string Endpoint = "https://api.openai.com/v1/audio/transcriptions";

        /// <summary>Verify against the account before relying on it; newer transcription models exist.</summary>
        public string model = "whisper-1";

        /// <summary>Steers the recogniser toward the vocabulary this app actually uses.</summary>
        public string prompt =
            "The speaker is describing a sculpture or museum object to generate in 3D. " +
            "Names of artists such as Monet, Van Gogh, Picasso, Frida Kahlo, Hilma af Klint, Socrates.";

        readonly ITripoKeySource _keySource;
        string _key;

        public OpenAiSpeechToText(ITripoKeySource keySource = null) =>
            _keySource = keySource ?? FallbackKeySource.ForOpenAi();

        public bool IsConfigured => !string.IsNullOrEmpty(_key);

        public string Describe() => _keySource.Describe();

        /// <summary>Resolves the key once. Call before <see cref="TranscribeAsync"/>.</summary>
        public async Task<bool> PrepareAsync()
        {
            _key = await _keySource.GetKeyAsync();
            return IsConfigured;
        }

        /// <summary>
        /// Returns the transcript, or null when it could not be produced. Never throws: this is
        /// driven by someone holding a button in a headset, and an exception there is a dead app.
        /// </summary>
        public async Task<string> TranscribeAsync(byte[] wav)
        {
            if (wav == null || wav.Length == 0) return null;
            if (!IsConfigured && !await PrepareAsync()) return null;

            var form = new WWWForm();
            form.AddBinaryData("file", wav, "speech.wav", "audio/wav");
            form.AddField("model", model);
            form.AddField("response_format", "json");
            if (!string.IsNullOrEmpty(prompt)) form.AddField("prompt", prompt);

            using (var www = UnityWebRequest.Post(Endpoint, form))
            {
                www.SetRequestHeader("Authorization", "Bearer " + _key);
                www.timeout = 30;

                var operation = www.SendWebRequest();
                var completion = new TaskCompletionSource<bool>();
                operation.completed += _ => completion.TrySetResult(true);
                await completion.Task;

                if (www.result != UnityWebRequest.Result.Success)
                {
                    // Deliberately not the whole request: it carries the Authorization header.
                    Debug.LogWarning("[Speech] Transcription failed: HTTP " + www.responseCode + " " + www.error);
                    return null;
                }

                try
                {
                    var parsed = JsonUtility.FromJson<TranscriptionResponse>(www.downloadHandler.text);
                    return string.IsNullOrWhiteSpace(parsed?.text) ? null : parsed.text.Trim();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[Speech] Could not read the transcription response: " + ex.Message);
                    return null;
                }
            }
        }

        [Serializable]
        class TranscriptionResponse
        {
            public string text;
        }
    }

    /// <summary>
    /// Stands in for a recogniser when there is no speech key.
    ///
    /// Not a toy: it keeps the whole voice path — permission, capture, level meter, trimming,
    /// encoding — exercisable and testable on the emulator without a second paid provider, so
    /// "does the microphone work on Swan" can be answered separately from "does transcription
    /// work". Those are two different failures and debugging them together is how an afternoon
    /// disappears.
    /// </summary>
    public sealed class CannedSpeechToText : ISpeechToText
    {
        readonly string[] _phrases;
        int _next;

        public CannedSpeechToText(params string[] phrases)
        {
            _phrases = phrases != null && phrases.Length > 0
                ? phrases
                : new[] { "a marble bust of a philosopher" };
        }

        public bool IsConfigured => true;

        public string Describe() => "Canned transcripts — no speech provider configured. The microphone is still exercised.";

        public Task<string> TranscribeAsync(byte[] wav)
        {
            var phrase = _phrases[_next % _phrases.Length];
            _next++;
            return Task.FromResult(phrase);
        }
    }
}
