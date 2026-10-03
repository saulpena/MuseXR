using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MusePico.Dialogue
{
    /// <summary>Skylar's VR plan: nine stages, in her order. Her chapters A-D are the middle four.</summary>
    public enum VrStage { Gate, Company, Curate, Palace, Grotto, VanGogh, Monet, Table, YourWorld }

    /// <summary>
    /// Skylar's VR journey (her 3 Oct 2026 design; replaces <see cref="MuseumJourney"/>'s ten web
    /// stages once the scene runs on it). Pure C#: the whole route, demo and full, walks in EditMode.
    ///
    /// Her two routes:
    ///   full (9:50)  Gate, Company, Curate, A Palace, B Grotto, C Van Gogh, D Monet, Table, Your world
    ///   demo (3:00)  Gate (default trio, sample question preselected), C Van Gogh, D Monet, Table, Your world
    /// A and B are her P1: until their worlds exist they are skipped on the full route too.
    /// </summary>
    public sealed class VrJourney
    {
        /// <summary>Her demo trio, and the default for anyone who does not choose.</summary>
        public static readonly string[] DefaultCompany = { "monet", "van_gogh", "socrates" };

        /// <summary>Her Company stage: "ray-select 1-3".</summary>
        public const int MaxCompanions = 3;

        /// <summary>The six standees: the three she names, then three drawn from her roster (Saul, 3 Oct).</summary>
        public static readonly string[] Roster = { "monet", "van_gogh", "socrates", "frida", "hilma", "morisot" };

        public VrStage Current { get; private set; } = VrStage.Gate;
        public bool Demo { get; private set; }

        /// <summary>Her P1 chapters run only when their worlds are built.</summary>
        public bool PalaceAvailable { get; set; }
        public bool GrottoAvailable { get; set; }

        public JourneyRecord Record { get; } = new JourneyRecord();

        public event Action<VrStage, VrStage> StageChanged;

        public VrJourney(bool demo = false)
        {
            Demo = demo;
            Record.SetCompanions(DefaultCompany);
        }

        /// <summary>Whether this route stops at a stage at all.</summary>
        public bool Visits(VrStage stage)
        {
            switch (stage)
            {
                case VrStage.Company:
                case VrStage.Curate: return !Demo;
                case VrStage.Palace: return !Demo && PalaceAvailable;
                case VrStage.Grotto: return !Demo && GrottoAvailable;
                default: return true;
            }
        }

        public VrStage? Next
        {
            get
            {
                for (var s = Current + 1; s <= VrStage.YourWorld; s++)
                    if (Visits(s)) return s;
                return null;
            }
        }

        /// <summary>The next stage on this route. False at Your world.</summary>
        public bool Advance()
        {
            var next = Next;
            if (next == null) return false;
            GoTo(next.Value);
            return true;
        }

        public void GoTo(VrStage stage)
        {
            if (stage == Current) return;
            var from = Current;
            if (IsChapter(from)) Record.MarkChapterDone(from);
            Current = stage;
            StageChanged?.Invoke(from, stage);
        }

        /// <summary>Her four chapters: one main action each, one plinth each in Your world.</summary>
        public static bool IsChapter(VrStage s) =>
            s == VrStage.Palace || s == VrStage.Grotto || s == VrStage.VanGogh || s == VrStage.Monet;

        /// <summary>Choose or un-choose a companion. Never more than three, never fewer than one.</summary>
        public bool ToggleCompanion(string masterId)
        {
            if (string.IsNullOrWhiteSpace(masterId)) return false;
            var id = masterId.Trim().ToLowerInvariant();
            var list = new List<string>(Record.Companions);
            var at = list.IndexOf(id);
            if (at >= 0)
            {
                if (list.Count <= 1) return false;
                list.RemoveAt(at);
            }
            else
            {
                if (list.Count >= MaxCompanions) return false;
                list.Add(id);
            }
            Record.SetCompanions(list);
            return true;
        }

        /// <summary>Start again (her "Start again" arch behind the visitor).</summary>
        public void Reset()
        {
            Record.Reset();
            Record.SetCompanions(DefaultCompany);
            var from = Current;
            Current = VrStage.Gate;
            if (from != VrStage.Gate) StageChanged?.Invoke(from, VrStage.Gate);
        }
    }

    /// <summary>
    /// Her journey record: an extension of the session with <c>chapters</c>, <c>dwell</c> and
    /// <c>answer</c>. Stroke points stay here, for rebuilding the stroke in Your world; only the
    /// text summary goes to the roundtable, which keeps her ~2 KB payload cap (SESSION_CAPS).
    /// </summary>
    public sealed class JourneyRecord
    {
        public const int MaxSummaryBytes = 2048;
        public const int MaxStrokePoints = 256;
        public const float SeenSeconds = 4f;   // her rule: >= 4 s of gaze = seen
        public const int MaxDwell = 8;

        public sealed class PalaceChoice { public string Object = ""; public float YawDeg; public string Mode = "miniature"; public string Reason = ""; }
        public sealed class GrottoChoice { public string LampSlot = ""; public string ExhibitId = ""; }
        public sealed class VanGoghChoice { public string Color = ""; public string ArtworkId = ""; public readonly List<float[]> Points = new List<float[]>(); }
        public sealed class MonetChoice { public string Preset = ""; public string ArtworkId = ""; public string Reason = ""; }
        public sealed class Answer { public string Draft = ""; public string Final = ""; public string RewrittenBy = ""; }

        public string Question { get; private set; } = string.Empty;
        public IReadOnlyList<string> Companions => _companions;
        public PalaceChoice Palace { get; private set; }
        public GrottoChoice Grotto { get; private set; }
        public VanGoghChoice VanGogh { get; private set; }
        public MonetChoice Monet { get; private set; }
        public Answer FinalAnswer { get; } = new Answer();
        public IReadOnlyList<VrStage> ChaptersDone => _done;

        readonly List<string> _companions = new List<string>();
        readonly List<VrStage> _done = new List<VrStage>();
        readonly List<(string id, float sec)> _dwell = new List<(string, float)>();

        public void SetQuestion(string q) => Question = VisitSession.Clamp(q, VisitSession.QuestionChars);

        public void SetCompanions(IEnumerable<string> ids)
        {
            _companions.Clear();
            foreach (var id in ids) if (!string.IsNullOrWhiteSpace(id)) _companions.Add(id.Trim().ToLowerInvariant());
        }

        public void SetPalace(PalaceChoice c) => Palace = c;
        public void SetGrotto(GrottoChoice c) => Grotto = c;
        public void SetMonet(MonetChoice c) => Monet = c;

        /// <summary>The stroke: colour, linked work, and at most 256 points (the rest are dropped evenly).</summary>
        public void SetVanGogh(string color, string artworkId, IReadOnlyList<float[]> points)
        {
            var c = new VanGoghChoice { Color = color ?? "", ArtworkId = artworkId ?? "" };
            if (points != null && points.Count > 0)
            {
                int n = Math.Min(points.Count, MaxStrokePoints);
                for (var i = 0; i < n; i++)
                    c.Points.Add(points[n == points.Count ? i : (int)((long)i * (points.Count - 1) / (n - 1))]);
            }
            VanGogh = c;
        }

        /// <summary>Add gaze time on a work. Seconds accumulate per work.</summary>
        public void AddDwell(string artworkId, float seconds)
        {
            if (string.IsNullOrWhiteSpace(artworkId) || seconds <= 0f) return;
            for (var i = 0; i < _dwell.Count; i++)
                if (_dwell[i].id == artworkId) { _dwell[i] = (artworkId, _dwell[i].sec + seconds); return; }
            _dwell.Add((artworkId, seconds));
        }

        /// <summary>Works looked at for at least 4 s, longest first - her "seen", and Your world's frames.</summary>
        public List<(string id, float sec)> Seen()
        {
            var seen = _dwell.FindAll(d => d.sec >= SeenSeconds);
            seen.Sort((a, b) => b.sec.CompareTo(a.sec));
            if (seen.Count > MaxDwell) seen.RemoveRange(MaxDwell, seen.Count - MaxDwell);
            return seen;
        }

        public void MarkChapterDone(VrStage chapter)
        {
            if (!_done.Contains(chapter)) _done.Add(chapter);
        }

        public void Reset()
        {
            Question = string.Empty;
            _companions.Clear(); _done.Clear(); _dwell.Clear();
            Palace = null; Grotto = null; VanGogh = null; Monet = null;
            FinalAnswer.Draft = FinalAnswer.Final = FinalAnswer.RewrittenBy = string.Empty;
        }

        /// <summary>
        /// What the roundtable is sent: text only, never the stroke points, at most 2048 UTF-8 bytes.
        /// Free text is clamped to her SESSION_CAPS; if still over, the least important fields go first.
        /// </summary>
        public string SummaryJson()
        {
            for (int squeeze = 0; ; squeeze++)
            {
                var json = BuildSummary(squeeze);
                if (Encoding.UTF8.GetByteCount(json) <= MaxSummaryBytes || squeeze >= 3) return json;
            }
        }

        string BuildSummary(int squeeze)
        {
            int text = squeeze == 0 ? VisitSession.LineChars : squeeze == 1 ? 100 : 60;
            var sb = new StringBuilder(512);
            sb.Append('{');
            Field(sb, "question", VisitSession.Clamp(Question, VisitSession.QuestionChars)); sb.Append(',');
            sb.Append("\"companions\":["); for (var i = 0; i < _companions.Count; i++) { if (i > 0) sb.Append(','); Str(sb, _companions[i]); } sb.Append("],");
            sb.Append("\"chapters\":{");
            var first = true;
            if (Palace != null) { Sep(sb, ref first); sb.Append("\"palace\":{"); Field(sb, "object", Palace.Object); sb.Append(','); sb.Append("\"yawDeg\":").Append(Palace.YawDeg.ToString("0", CultureInfo.InvariantCulture)).Append(','); Field(sb, "mode", Palace.Mode); sb.Append(','); Field(sb, "reason", VisitSession.Clamp(Palace.Reason, text)); sb.Append('}'); }
            if (Grotto != null) { Sep(sb, ref first); sb.Append("\"grotto\":{"); Field(sb, "lampSlot", Grotto.LampSlot); sb.Append(','); Field(sb, "exhibitId", Grotto.ExhibitId); sb.Append('}'); }
            if (VanGogh != null) { Sep(sb, ref first); sb.Append("\"vangogh\":{"); Field(sb, "color", VanGogh.Color); sb.Append(','); Field(sb, "artworkId", VanGogh.ArtworkId); sb.Append('}'); }
            if (Monet != null) { Sep(sb, ref first); sb.Append("\"monet\":{"); Field(sb, "preset", Monet.Preset); sb.Append(','); Field(sb, "artworkId", Monet.ArtworkId); sb.Append(','); Field(sb, "reason", VisitSession.Clamp(Monet.Reason, text)); sb.Append('}'); }
            sb.Append("},");
            sb.Append("\"dwell\":[");
            var seen = Seen();
            int keep = squeeze >= 3 ? Math.Min(seen.Count, 3) : seen.Count;
            for (var i = 0; i < keep; i++) { if (i > 0) sb.Append(','); sb.Append('{'); Field(sb, "artworkId", seen[i].id); sb.Append(",\"sec\":").Append(((int)seen[i].sec).ToString(CultureInfo.InvariantCulture)).Append('}'); }
            sb.Append("],");
            sb.Append("\"answer\":{"); Field(sb, "draft", VisitSession.Clamp(FinalAnswer.Draft, text)); sb.Append(','); Field(sb, "final", VisitSession.Clamp(FinalAnswer.Final, text)); sb.Append('}');
            sb.Append('}');
            return sb.ToString();
        }

        static void Sep(StringBuilder sb, ref bool first) { if (!first) sb.Append(','); first = false; }
        static void Field(StringBuilder sb, string name, string value) { Str(sb, name); sb.Append(':'); Str(sb, value); }

        static void Str(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (var ch in s ?? string.Empty)
            {
                switch (ch)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (ch < 0x20) sb.Append("\\u").Append(((int)ch).ToString("x4"));
                        else sb.Append(ch);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
