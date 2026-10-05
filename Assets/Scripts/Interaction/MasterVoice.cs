using System.Collections;
using MusePico.Dialogue;
using MuseXR.Slots;
using UnityEngine;

namespace MuseXR.Interaction
{
    /// <summary>
    /// The masters' one voice in a chapter: each line is fetched in its master's MiniMax voice as its turn
    /// starts and played through ONE source, so two never talk over each other. Lines of a round queue
    /// behind the one speaking (a turn is timed from its text and a voice runs longer); a new round stops
    /// whatever is still speaking.
    ///
    /// Saul's live run, 4 Oct 2026: the masters were silent for the insight readings and the Palace and
    /// Grotto choice rounds - the group showed their lines and nothing spoke them - and tapping the next
    /// option started a second voice 2.4 s into the first. Silent when the scene has no voiced
    /// <see cref="MuseumDialogue"/>.
    /// </summary>
    public sealed class MasterVoice : MonoBehaviour
    {
        /// <summary>A line whose voice has not come back by then is dropped rather than played late.</summary>
        public const float FetchSeconds = 15f;

        static MasterVoice _instance;
        AudioSource _source;
        int _round;
        float _busyUntil;

        /// <summary>A master's voice is playing now (the music ducks under it).</summary>
        public static bool Speaking => _instance != null && _instance._source != null && _instance._source.isPlaying;

        void OnEnable() => CompanionGroup.LineSkipped += StopNow;
        void OnDisable() => CompanionGroup.LineSkipped -= StopNow;

        /// <summary>The line being said is cut (skipped): its voice stops; the next line plays when its turn starts.</summary>
        void StopNow(CompanionGroup _) { if (_source != null) _source.Stop(); }

        public static MasterVoice Get()
        {
            if (_instance != null) return _instance;
            _instance = FindAnyObjectByType<MasterVoice>();
            return _instance != null ? _instance : (_instance = new GameObject("Master Voice").AddComponent<MasterVoice>());
        }

        /// <summary>Her roster ids, as the voice service casts them.</summary>
        public static string ToRoster(string id) =>
            id == Masters.Frida ? "frida" : id == Masters.Hilma ? "hilma" : id == Masters.Morisot ? "morisot" : id;

        /// <summary>A new round: the voice speaking now, and any line of the last round still to come, stop.</summary>
        public void NewRound()
        {
            _round++;
            if (_source != null) _source.Stop();
        }

        /// <summary>Speak <paramref name="line"/> in <paramref name="masterId"/>'s voice, after the line before it in this round.</summary>
        public void Say(string masterId, string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            var dialogue = FindAnyObjectByType<MuseumDialogue>();
            if (dialogue == null || !dialogue.HasVoice) return;
            StartCoroutine(Line(dialogue, masterId, line, _round));
        }

        /// <summary>
        /// A new round, voiced: every line <paramref name="group"/> starts until its turns finish. For a
        /// round the group runs from <see cref="CompanionGroup.LineFor"/> (BeginTurns), as the Palace's and
        /// the Grotto's choice rounds do.
        /// </summary>
        public static void Follow(CompanionGroup group)
        {
            if (group == null) return;
            var voice = Get();
            voice.NewRound();
            var round = voice._round;
            System.Action<string, string> speak = null;
            System.Action done = null;
            speak = (id, line) => { if (round == voice._round) voice.Say(id, line); else Drop(); };
            done = Drop;
            void Drop() { group.LineStarted -= speak; group.TurnsFinished -= done; }
            group.LineStarted += speak;
            group.TurnsFinished += done;
        }

        IEnumerator Line(MuseumDialogue dialogue, string id, string line, int round)
        {
            var task = dialogue.VoiceAsync(ToRoster(id), line);
            for (float t = 0f; !task.IsCompleted && t < FetchSeconds; t += Time.deltaTime) yield return null;
            var clip = task.IsCompleted && !task.IsFaulted ? task.Result : null;
            if (clip == null) { Debug.LogWarning("[Voice] no voice for " + id + (task.IsFaulted ? ": " + task.Exception?.GetBaseException().Message : "")); yield break; }
            if (_source == null)
            {
                _source = gameObject.AddComponent<AudioSource>();
                _source.playOnAwake = false; _source.spatialBlend = 0f; _source.volume = 0.9f;
            }
            while (round == _round && (_source.isPlaying || _busyUntil > Time.time)) yield return null;
            if (round != _round) yield break;
            MusePico.Dialogue.VoiceGate.Play(_source, clip);   // one master's voice at a time
            _busyUntil = Time.time + 0.1f;   // isPlaying can read false in the frame Play() lands
            Debug.Log("[Voice] " + Masters.Name(id) + " (" + clip.length.ToString("F1") + " s)");
        }
    }
}
