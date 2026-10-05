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

        [Tooltip("Ask exactly the invited masters, with no topping up to three. On for scenes whose " +
                 "masters stand in the room as bodies; off for the journey, which always hears three.")]
        public bool exactlyInvited;

        [Header("Scene")]
        public VoiceCapture voice;
        public AudioSource speaker;

        [Header("Artwork in focus")]
        public string artworkTitle = "Water Lilies";
        public string artworkArtist = "Claude Monet";
        public string artworkDate = "1906";


        public bool speakReplies = true;

        /// <summary>Fires as each master's reading arrives, in the order they are spoken.</summary>
        public event System.Action<Perspective> PerspectiveReady;

        /// <summary>A master's voice starts: who is speaking now (the text was shown before).</summary>
        public event System.Action<Perspective> SpeakerStarted;

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

            Masters = ChooseMasters();

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
                _dialogue = new DialogueClient(new TripoWebRequestTransport(openAiKey, DialogueClient.DefaultEndpoint), _roster);
                // The model is NOT an Inspector field. A public/[SerializeField] field is
                // written into every scene that holds the component, and the scene copy wins
                // over the code default - which is exactly how four projects sat pointing at
                // a model string that had been changed in C# and silently ignored at runtime.
                // DialogueClient.Model is a plain C# field on a non-MonoBehaviour, so it is
                // never serialized and there is exactly one place to change it.
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

        /// <summary>
        /// Raised with the words heard, when listening was started by <see cref="ListenForText"/>.
        ///
        /// This is the microphone's ONLY job outside the gallery: it replaces the keyboard. The
        /// words go into whatever field asked for them and stop there. Nothing is sent anywhere
        /// until the visitor presses the button that sends it, exactly as typing works.
        /// </summary>
        public event System.Action<string> TextDictated;

        /// <summary>True while the open microphone is filling a text field rather than asking.</summary>
        bool _dictating;

        /// <summary>
        /// Listen, transcribe, and hand the words back through <see cref="TextDictated"/>.
        /// <b>No master is asked and nothing is billed beyond the transcription.</b>
        /// </summary>
        public void ListenForText()
        {
            _dictating = true;
            Listen();
        }

        /// <summary>Hold-to-talk released (her "hold X to speak"): send what was said so far.</summary>
        public void FinishListening()
        {
            if (voice != null) voice.FinishUtterance();
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

            // Dictation stops here. The words are handed back and the caller decides what, if
            // anything, to do with them - which on the question screen is "put them in the box".
            if (_dictating) { _dictating = false; TextDictated?.Invoke(text); Report(""); return; }

            await AskAsync(text);
        }

        /// <summary>Asks the invited masters a question, then speaks the readings in turn.</summary>
        public async Task<DialogueResult> AskAsync(string question)
        {
            if (IsBusy || _dialogue == null) return null;

            IsBusy = true;
            _cancel = new CancellationTokenSource();
            // Re-read who is invited on every question. Computing this once in Start meant a caller
            // changing invitedMasterIds afterwards (the journey does, before every ask) was ignored
            // and the defaults answered instead (found 29 Sep 2026).
            Masters = ChooseMasters();
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

                // Every reading's text at once, the moment the model answers: each appears above its
                // master straight away, and nobody waits for the voice before them to finish
                // (Saul, 1 Oct 2026). The voices still play one after another.
                for (var i = 0; i < result.Perspectives.Count; i++)
                    PerspectiveReady?.Invoke(result.Perspectives[i]);
                for (var i = 0; pending != null && i < result.Perspectives.Count; i++)
                {
                    SpeakerStarted?.Invoke(result.Perspectives[i]);
                    await PlayAsync(await pending[i], _cancel.Token);
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

        List<MasterLens> ChooseMasters() => exactlyInvited
            ? MasterRoster.SelectExactly(_roster, invitedMasterIds)
            : MasterRoster.Select(_roster, invitedMasterIds);

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

            VoiceGate.Play(speaker, clip);   // one master's voice at a time

            // Wait it out rather than overlapping the next master. Three masters talking at once
            // is noise, and telling them apart is the whole point of asking three.
            var until = Time.realtimeSinceStartup + clip.length + 0.15f;
            while (Time.realtimeSinceStartup < until && !ct.IsCancellationRequested)
                await Task.Yield();
        }

        /// <summary>
        /// Stop whatever is running AND whatever is being heard. Cancelling the token alone left
        /// the current clip playing to its end, so choosing an answer mid-reading had the masters
        /// "talking non-stop" over the reply (Saul, 27 Sep).
        /// </summary>
        public void Cancel()
        {
            _cancel?.Cancel();
            _sayCancel?.Cancel();
            if (speaker != null) speaker.Stop();
        }

        CancellationTokenSource _sayCancel;

        /// <summary>True when lines can be voiced (a MiniMax key was found).</summary>
        public bool HasVoice => _voiceService != null;

        /// <summary>One line in one master's voice, as a clip, without playing it - so a caller can
        /// fetch ahead and play it from where that master stands. Null without a voice key or on failure.</summary>
        public async Task<AudioClip> VoiceAsync(string masterId, string text, CancellationToken ct = default)
        {
            if (_voiceService == null || string.IsNullOrWhiteSpace(text)) return null;
            // Saul, 4 Oct: the same fixed lines (the masters' introductions, the lanterns, the takes) were billed again
            // every play. Each line is synthesised once and kept on disk: a repeat is free and instant.
            var cached = VoiceCache.Load(masterId, text);
            if (cached != null) return cached;
            try
            {
                var clip = await _voiceService.SpeakAsync(text, MasterRoster.Find(_roster, masterId), ct);
                if (clip != null) VoiceCache.Save(masterId, text, clip);
                return clip;
            }
            catch (System.Exception ex) { Debug.LogWarning("[Dialogue] voice for " + masterId + " failed: " + ex.Message); return null; }
        }

        /// <summary>
        /// Speak one line in one master's voice — the reply to the visitor's answer. Stops anything
        /// playing first, so exactly one voice is ever heard. Text-only (silently) without a voice key.
        /// </summary>
        public async Task SayAsync(string masterId, string text)
        {
            if (_voiceService == null || string.IsNullOrWhiteSpace(text)) return;
            Cancel();
            var cts = new CancellationTokenSource();
            _sayCancel = cts;
            try
            {
                var clip = await VoiceAsync(masterId, text, cts.Token);   // cached like every line
                if (!cts.IsCancellationRequested) await PlayAsync(clip, cts.Token);
            }
            catch (System.OperationCanceledException) { }
            finally { if (_sayCancel == cts) _sayCancel = null; cts.Dispose(); }
        }

        void Report(string status) => StatusChanged?.Invoke(status);
    }
}
