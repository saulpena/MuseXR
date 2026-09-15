using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MusePico.Generation;
using MusePico.Tripo;
using UnityEngine;

namespace MusePico.Dialogue
{
    /// <summary>
    /// The salon: ask a question out loud, three masters answer in their own voices.
    ///
    /// Assembled from the best piece of each project rather than written fresh:
    ///
    ///   <b>muse-infinity</b> — the lens prompts, the shared-rules-once structure, the three-way
    ///   fan-out, and the honesty rule that a failure is reported as a failure rather than
    ///   disguised as a quiet answer. This is the irreplaceable part.
    ///
    ///   <b>DuckyMayhem</b> — <c>whisper-1</c> transcription and voice activity detection, so an
    ///   utterance ends itself instead of needing a button held through a sentence.
    ///
    ///   <b>PersonalAssistant</b> — hand-built JSON (never reflection, which IL2CPP strips), a
    ///   transport seam so everything above it is testable, and keys resolved from the
    ///   environment and never displayed.
    ///
    /// The readings are spoken one after another, not together: three masters talking over each
    /// other is noise, and the whole point of asking three is that you can tell them apart.
    /// </summary>
    public class MuseumDialogue : MonoBehaviour
    {
        [Header("Data")]
        [Tooltip("Assets/Dialogue/masters.json — exported verbatim from muse-infinity.")]
        public TextAsset mastersJson;

        [Tooltip("Which masters to invite. Empty uses the authored defaults (Monet, Van Gogh, Socrates).")]
        public List<string> invitedMasterIds = new List<string>();

        [Header("Scene")]
        public VoiceCapture voice;
        public AudioSource speaker;

        [Header("Artwork in focus")]
        public string artworkTitle = "Water Lilies";
        public string artworkArtist = "Claude Monet";
        public string artworkDate = "1906";

        [Header("Model")]
        [Tooltip("Not verified against this account — measure it before trusting it.")]
        public string dialogueModel = "gpt-5.6";

        public bool speakReplies = true;

        /// <summary>Fires as each master's reading arrives, in the order they are spoken.</summary>
        public event System.Action<Perspective> PerspectiveReady;

        /// <summary>Fires with a one-line status for a panel: listening, thinking, speaking, failed.</summary>
        public event System.Action<string> StatusChanged;

        MasterRosterData _roster;
        DialogueClient _dialogue;
        MiniMaxVoice _voiceService;
        ISpeechToText _speech;
        CancellationTokenSource _cancel;

        public bool IsBusy { get; private set; }
        public DialogueResult LastResult { get; private set; }
        public string LastTranscript { get; private set; } = "";

        public IReadOnlyList<MasterLens> Masters { get; private set; } = new List<MasterLens>();

        async void Start()
        {
            if (mastersJson == null)
            {
                Report("No masters.json assigned — the lenses are the whole feature, so nothing will run.");
                return;
            }

            try { _roster = MasterRoster.Parse(mastersJson.text); }
            catch (System.Exception ex) { Report("masters.json could not be read: " + ex.Message); return; }

            Masters = MasterRoster.Select(_roster, invitedMasterIds);

            var openAiKey = await FallbackKeySource.ForOpenAi().GetKeyAsync();
            var miniMaxKey = await new FallbackKeySource(
                new EnvironmentKeySource("MINIMAX_API_KEY"),
                new StreamingAssetsKeySource("minimax.key")).GetKeyAsync();

            if (string.IsNullOrEmpty(openAiKey))
            {
                Report("No OpenAI key. The masters cannot speak until one is set.");
            }
            else
            {
                _dialogue = new DialogueClient(new TripoWebRequestTransport(openAiKey, DialogueClient.DefaultEndpoint), _roster) { Model = dialogueModel };
                _speech = new OpenAiSpeechToText();
            }

            // Narration is optional on purpose: a missing MiniMax key must cost the voices, never
            // the readings.
            _voiceService = string.IsNullOrEmpty(miniMaxKey)
                ? null
                : new MiniMaxVoice(new TripoWebRequestTransport(miniMaxKey, MiniMaxVoice.Endpoint)) { Model = _roster.ttsModel };

            if (voice != null)
            {
                voice.RequestPermission();
                voice.UtteranceEnded += OnUtteranceEnded;
            }

            Report(Ready());
        }

        void OnDestroy()
        {
            if (voice != null) voice.UtteranceEnded -= OnUtteranceEnded;
            _cancel?.Cancel();
        }

        string Ready()
        {
            var who = new List<string>();
            foreach (var master in Masters) who.Add(master.fullName);
            return "Ready — " + string.Join(", ", who) +
                   (_voiceService == null ? " (no MINIMAX_API_KEY, so text only)" : "");
        }

        /// <summary>Starts listening. The utterance ends itself when they stop speaking.</summary>
        public void Listen()
        {
            if (IsBusy || voice == null) return;
            Report(voice.StartRecording() ? "Listening…" : "Microphone unavailable. " + voice.Describe());
        }

        void OnUtteranceEnded(byte[] wav, SilenceDetector.StopReason reason)
        {
            if (reason == SilenceDetector.StopReason.NeverHeardAnything)
            {
                Report("Nothing heard — the microphone is open but no audio is reaching it.");
                return;
            }
            if (wav == null) { Report("Too short to send."); return; }

            _ = TranscribeAndAskAsync(wav);
        }

        async Task TranscribeAndAskAsync(byte[] wav)
        {
            if (_speech == null) { Report("No transcription provider configured."); return; }

            Report("Transcribing…");
            var text = await _speech.TranscribeAsync(wav);
            if (string.IsNullOrWhiteSpace(text)) { Report("No transcript."); return; }

            LastTranscript = text;
            await AskAsync(text);
        }

        /// <summary>Asks the invited masters a question, then speaks the readings in turn.</summary>
        public async Task<DialogueResult> AskAsync(string question)
        {
            if (IsBusy || _dialogue == null) return null;

            IsBusy = true;
            _cancel = new CancellationTokenSource();
            Report("“" + question + "” — asking " + Masters.Count + " masters…");

            try
            {
                var artwork = new ArtworkContext
                {
                    Title = artworkTitle,
                    Artist = artworkArtist,
                    Date = artworkDate,
                };

                var result = await _dialogue.AskAsync(question, Masters, artwork, _cancel.Token);
                LastResult = result;

                if (!result.Success)
                {
                    // No canned prose behind a failure. A visitor is better served by "the masters
                    // could not be reached" than by something invented in their names.
                    Report("The masters could not be reached: " + result.Error);
                    return result;
                }

                Report("Answered in " + result.Seconds.ToString("0.0") + "s");

                // Synthesis starts for ALL of them at once, playback stays in order.
                //
                // Measured 14 Sep: MiniMax takes 1.5-2.4s per line. Synthesising inside the
                // playback loop would pay that three times in series and add ~5s of silence to
                // every turn; started together, only the first one's wait is ever heard, because
                // the other two finish while the first master is still speaking.
                var pending = speakReplies && _voiceService != null
                    ? StartSynthesis(result.Perspectives, _cancel.Token)
                    : null;

                for (var i = 0; i < result.Perspectives.Count; i++)
                {
                    // Text first, always. The reading appears above the master whether or not a
                    // voice ever arrives for it.
                    PerspectiveReady?.Invoke(result.Perspectives[i]);
                    if (pending != null) await PlayAsync(await pending[i], _cancel.Token);
                }

                return result;
            }
            catch (System.OperationCanceledException)
            {
                Report("Cancelled.");
                return null;
            }
            finally
            {
                IsBusy = false;
                _cancel?.Dispose();
                _cancel = null;
            }
        }

        /// <summary>Kicks off every synthesis at once and returns the tasks in speaking order.</summary>
        Task<AudioClip>[] StartSynthesis(List<Perspective> perspectives, CancellationToken ct)
        {
            var tasks = new Task<AudioClip>[perspectives.Count];
            for (var i = 0; i < perspectives.Count; i++)
            {
                var master = MasterRoster.Find(_roster, perspectives[i].speakerId);
                tasks[i] = _voiceService.SpeakAsync(perspectives[i].text, master, ct);
            }
            return tasks;
        }

        async Task PlayAsync(AudioClip clip, CancellationToken ct)
        {
            // Null is the ordinary case when narration is unavailable — no key, no credit, a
            // refused voice. The turn continues in text.
            if (clip == null || speaker == null) return;

            speaker.clip = clip;
            speaker.Play();

            // Wait it out rather than overlapping the next master. Three masters talking at once
            // is noise, and telling them apart is the whole point of asking three.
            var until = Time.realtimeSinceStartup + clip.length + 0.15f;
            while (Time.realtimeSinceStartup < until && !ct.IsCancellationRequested)
                await Task.Yield();
        }

        public void Cancel() => _cancel?.Cancel();

        void Report(string status) => StatusChanged?.Invoke(status);
    }
}
