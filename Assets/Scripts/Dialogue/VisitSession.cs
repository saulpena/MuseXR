using System;
using System.Collections.Generic;
using System.Text;

namespace MusePico.Dialogue
{
    /// <summary>
    /// What the visitor actually did, accumulated across the walk and read back by the closing
    /// roundtable.
    ///
    /// <b>The field names are muse-infinity's, not ours.</b> Her server describes the payload as
    /// <c>{ visitedArtworks, askedQuestions, perspectiveLog }</c> (<c>app.js:99-140</c>,
    /// <c>server.mjs:563-607</c>), and <c>clampDigest</c> re-clamps every one of them server-side
    /// because "a client cap is a product decision and not a boundary". Renaming any of these
    /// produces a request her contract does not describe.
    ///
    /// <b>Caps are applied AT CONSTRUCTION</b>, exactly as hers are, so the payload has a fixed
    /// ceiling of roughly 2 KB no matter how long someone walks. Nothing here trims on the way out.
    ///
    /// The philosophy axes live here too. They are NOT sent to the roundtable — hers never leaves
    /// the client — but they choose the finale's collection through <c>PHILOSOPHY_QUERIES</c> and
    /// are shown as a score on the manifesto.
    ///
    /// Pure C#: no UnityEngine, so all of it is EditMode-testable with no scene and no key.
    /// </summary>
    public sealed class VisitSession
    {
        public const int MaxArtworks = 5;
        public const int MaxQuestions = 3;
        public const int MaxPerspectives = 3;

        public const int NameChars = 120;
        public const int QuestionChars = 200;
        public const int LineChars = 160;

        readonly List<VisitedArtwork> _artworks = new List<VisitedArtwork>();
        readonly List<string> _questions = new List<string>();
        readonly List<PerspectiveLine> _log = new List<PerspectiveLine>();

        public IReadOnlyList<VisitedArtwork> VisitedArtworks => _artworks;
        public IReadOnlyList<string> AskedQuestions => _questions;
        public IReadOnlyList<PerspectiveLine> PerspectiveLog => _log;

        public PhilosophyAxes Philosophy { get; private set; } = new PhilosophyAxes();

        readonly List<string> _answers = new List<string>();

        /// <summary>
        /// Each answer given at a painting, as "work — answer — who replied". Hers never stores
        /// these (only the axes move); this is for the Editor tracker, and is sent nowhere.
        /// </summary>
        public IReadOnlyList<string> AnswerLog => _answers;

        /// <summary>The stage-07 answer id (her state.transformationChoice), or empty before it.</summary>
        public string TransformationChoice { get; private set; } = string.Empty;

        public void RecordAnswer(string work, string answer, string replier)
        {
            _answers.Add((work ?? "?") + " — " + (answer ?? "?") + " — " + (replier ?? "?"));
        }

        public void SetTransformationChoice(string id) => TransformationChoice = id ?? string.Empty;

        /// <summary>True once anything has been recorded. The roundtable is offered only then.</summary>
        public bool HasAnything => _artworks.Count > 0 || _questions.Count > 0 || _log.Count > 0;

        /// <summary>
        /// Record a stop. Deduplicated case-insensitively on title+artist, and a repeat visit MOVES
        /// TO THE END rather than being ignored — hers does the same, so "in order" means the order
        /// they last mattered, not the order first seen.
        /// </summary>
        public void RecordArtwork(string title, string artist)
        {
            var clean = new VisitedArtwork(Clamp(title, NameChars), Clamp(artist, NameChars));
            if (clean.Title.Length == 0) return; // hers drops entries with an empty title

            _artworks.RemoveAll(a => Same(a.Title, clean.Title) && Same(a.Artist, clean.Artist));
            _artworks.Add(clean);
            TrimFront(_artworks, MaxArtworks);
        }

        /// <summary>
        /// Record a question the visitor actually asked. Her machine-generated artwork question
        /// ("Tell me how you see X") is deliberately NOT recorded — only what a person chose to ask.
        /// </summary>
        public void RecordQuestion(string question)
        {
            var clean = Clamp(question, QuestionChars);
            if (clean.Length == 0) return;

            _questions.RemoveAll(q => Same(q, clean));
            _questions.Add(clean);
            TrimFront(_questions, MaxQuestions);
        }

        /// <summary>
        /// Record what a master last said. One line per speaker, newest wins — so the roundtable
        /// reads their most recent reading rather than their first.
        /// </summary>
        public void RecordPerspective(string speakerId, string speaker, string line)
        {
            var id = Clamp(speakerId, NameChars);
            var text = Clamp(line, LineChars);
            if (id.Length == 0 || text.Length == 0) return;

            var name = Clamp(speaker, NameChars);
            if (name.Length == 0) name = id; // hers falls back to the id

            _log.RemoveAll(p => Same(p.SpeakerId, id));
            _log.Add(new PerspectiveLine(id, name, text));
            TrimFront(_log, MaxPerspectives);
        }

        /// <summary>Applies one of the stage-07 answers. See <see cref="PhilosophyAxes"/>.</summary>
        public void ApplyChoice(PhilosophyAxes delta) => Philosophy = Philosophy.Plus(delta);

        /// <summary>
        /// Clears everything. Stage 09's ENTER AGAIN calls this: the digest AND the axes go, or the
        /// next visitor inherits a stranger's walk.
        /// </summary>
        public void Reset()
        {
            _artworks.Clear();
            _questions.Clear();
            _log.Clear();
            _answers.Clear();
            TransformationChoice = string.Empty;
            Philosophy = new PhilosophyAxes();
        }

        /// <summary>
        /// Trim, collapse every run of whitespace to a single space, then slice. Hers is
        /// <c>String(value ?? "").trim().replace(/\s+/g, " ").slice(0, limit)</c>; a null or a
        /// non-string becomes the empty string rather than "null".
        /// </summary>
        public static string Clamp(string value, int limit)
        {
            if (string.IsNullOrEmpty(value) || limit <= 0) return string.Empty;

            var sb = new StringBuilder(value.Length);
            var space = false;
            foreach (var c in value)
            {
                if (char.IsWhiteSpace(c)) { space = sb.Length > 0; continue; }
                if (space) { sb.Append(' '); space = false; }
                sb.Append(c);
            }

            var s = sb.ToString();
            return s.Length <= limit ? s : s.Substring(0, limit);
        }

        static bool Same(string a, string b) =>
            string.Equals(a ?? string.Empty, b ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        /// <summary>Keeps the LAST <paramref name="max"/> entries — a rolling window, as hers is.</summary>
        static void TrimFront<T>(List<T> list, int max)
        {
            if (list.Count > max) list.RemoveRange(0, list.Count - max);
        }
    }

    public readonly struct VisitedArtwork
    {
        public readonly string Title;
        public readonly string Artist;
        public VisitedArtwork(string title, string artist) { Title = title ?? string.Empty; Artist = artist ?? string.Empty; }
        public override string ToString() => Artist.Length > 0 ? Title + " by " + Artist : Title;
    }

    public readonly struct PerspectiveLine
    {
        public readonly string SpeakerId;
        public readonly string Speaker;
        public readonly string Line;
        public PerspectiveLine(string speakerId, string speaker, string line)
        {
            SpeakerId = speakerId ?? string.Empty;
            Speaker = speaker ?? string.Empty;
            Line = line ?? string.Empty;
        }
        public override string ToString() => Speaker + ": " + Line;
    }

    /// <summary>
    /// The three axes stage 07 moves. Deltas are hers verbatim (<c>app.js:180-184</c>):
    /// perception 3/1/0, emotion 0/3/1, invention 1/0/3 — never a clean one-axis bump, so two
    /// answers can rank differently from either alone.
    /// </summary>
    public readonly struct PhilosophyAxes
    {
        public readonly int Perception;
        public readonly int Emotion;
        public readonly int Invention;

        public PhilosophyAxes(int perception, int emotion, int invention)
        {
            Perception = perception; Emotion = emotion; Invention = invention;
        }

        public static readonly PhilosophyAxes PerceptionChoice = new PhilosophyAxes(3, 1, 0);
        public static readonly PhilosophyAxes EmotionChoice = new PhilosophyAxes(0, 3, 1);
        public static readonly PhilosophyAxes InventionChoice = new PhilosophyAxes(1, 0, 3);

        public PhilosophyAxes Plus(PhilosophyAxes other) =>
            new PhilosophyAxes(Perception + other.Perception, Emotion + other.Emotion, Invention + other.Invention);

        /// <summary>
        /// The top two axes, sorted alphabetically and joined — her <c>philosophyKey()</c>. This is
        /// the key into <c>PHILOSOPHY_QUERIES</c>, which chooses the finale's collection.
        ///
        /// Ties break in her iteration order: perception, emotion, invention.
        /// </summary>
        public string Key()
        {
            var ranked = new List<KeyValuePair<string, int>>
            {
                new KeyValuePair<string, int>("perception", Perception),
                new KeyValuePair<string, int>("emotion", Emotion),
                new KeyValuePair<string, int>("invention", Invention),
            };
            // Stable descending sort: List.Sort is unstable, so compare the original index on ties.
            var order = new Dictionary<string, int> { { "perception", 0 }, { "emotion", 1 }, { "invention", 2 } };
            ranked.Sort((a, b) => b.Value != a.Value ? b.Value.CompareTo(a.Value) : order[a.Key].CompareTo(order[b.Key]));

            var top = new List<string> { ranked[0].Key, ranked[1].Key };
            top.Sort(StringComparer.Ordinal);
            return top[0] + "+" + top[1];
        }

        public override string ToString() =>
            "perception " + Perception + " · emotion " + Emotion + " · invention " + Invention;
    }
}
