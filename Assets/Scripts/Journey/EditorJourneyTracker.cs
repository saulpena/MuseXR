using MusePico.Dialogue;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MusePico.Journey
{
    /// <summary>
    /// Editor-only overlay: everything the journey is keeping track of, in one place. TAB shows or
    /// hides it. It reads the same state the roundtable and the manifesto read, so what it lists is
    /// what the masters will be told.
    ///
    /// Not VR UI and never built into a player: the whole body is UNITY_EDITOR, and the runner only
    /// adds it in the Editor. Drawn with IMGUI because it is a developer readout on the Game view,
    /// not part of the experience (the experience's text is TextMeshPro).
    /// </summary>
    public sealed class EditorJourneyTracker : MonoBehaviour
    {
        public MuseumJourneyRunner runner;
        public bool visible = true;

#if UNITY_EDITOR
        Vector2 _scroll;
        GUIStyle _box, _head, _line;

        void Update()
        {
            if (Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame) visible = !visible;
        }

        void OnGUI()
        {
            if (!visible || runner == null || runner.Journey == null) return;
            if (_box == null)
            {
                _box = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, padding = new RectOffset(10, 10, 8, 8) };
                _head = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 12, richText = true };
                _line = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true, richText = true };
            }

            var j = runner.Journey;
            var s = j.Session;
            const float w = 380f;
            GUILayout.BeginArea(new Rect(Screen.width - w - 8, 8, w, Screen.height - 16), _box);   // right edge: the panel stands to the left
            _scroll = GUILayout.BeginScrollView(_scroll);
            GUILayout.Label("<color=#C9AA72>JOURNEY TRACKER</color>  ·  TAB hides", _head);

            Head("Where");
            Line("Stage: " + (int)j.Current + " " + j.Current);
            var chapter = j.Spine.Current;
            Line("Chapter: " + (j.Spine.InFinalWorld ? "final world" : (j.Spine.Index + 1) + " / " + j.Spine.Count) +
                 " · " + chapter.Title + " (" + chapter.CollectionId + ")");
            var world = runner.worlds != null ? runner.worlds.Current : null;
            Line("World: " + (world != null ? world.key : "none") + (runner.InPainting ? "  · INSIDE THE PAINTING" : ""));

            Head("The visitor");
            Line("Question: " + (string.IsNullOrEmpty(j.Question) ? "—" : "“" + j.Question + "”"));
            Line("Masters: " + (j.InvitedMasterIds.Count == 0 ? "—" : string.Join(", ", j.InvitedMasterIds)));

            Head("Sent to the roundtable");
            Line("<b>Paintings stopped at</b> (" + s.VisitedArtworks.Count + " / " + VisitSession.MaxArtworks + ")");
            if (s.VisitedArtworks.Count == 0) Line("   none");
            foreach (var a in s.VisitedArtworks) Line("   · " + a);
            Line("<b>Questions asked</b> (" + s.AskedQuestions.Count + " / " + VisitSession.MaxQuestions + ")");
            if (s.AskedQuestions.Count == 0) Line("   none");
            foreach (var q in s.AskedQuestions) Line("   · “" + q + "”");
            Line("<b>What each master last said</b>");
            if (s.PerspectiveLog.Count == 0) Line("   none");
            foreach (var p in s.PerspectiveLog) Line("   · " + p.Speaker + ": " + Short(p.Line, 110));

            Head("Kept here (never sent)");
            var axes = s.Philosophy;
            Line("Counters: perception " + axes.Perception + " · emotion " + axes.Emotion + " · invention " + axes.Invention);
            var key = axes.Key();
            Line("Top two: " + key + " → final world hangs " + (FinalWorldWall.ArtistFor(key) ?? "its own wall"));
            Line("<b>Answers at paintings</b> (" + s.AnswerLog.Count + ")");
            if (s.AnswerLog.Count == 0) Line("   none");
            foreach (var a in s.AnswerLog) Line("   · " + a);
            Line("Socrates' question: " + (string.IsNullOrEmpty(s.TransformationChoice) ? "not answered" :
                 s.TransformationChoice + " → particles: " + TransformationParticles.ModeFor(s.TransformationChoice)));

            Head("Roundtable and ending");
            var e = runner.Ending;
            Line("Status: " + (e.Loading ? "asking…" : !string.IsNullOrEmpty(e.Error) ? "<color=#E06C5A>failed: " + e.Error + "</color>"
                 : e.FromRoundtable ? (e.Live ? "ready (live)" : "ready (local fallback)") : "not asked yet"));
            if (e.FromRoundtable)
            {
                Line("World title: " + e.Title);
                if (e.Threads != null) foreach (var t in e.Threads) Line("   · " + t.Speaker + ": " + Short(t.Text, 110));
                Line("Synthesis: " + Short(e.Copy, 240));
            }
            else Line("Manifesto would show: the generic ending");

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void Head(string text) { GUILayout.Space(6); GUILayout.Label("<color=#C9AA72>" + text.ToUpperInvariant() + "</color>", _head); }
        void Line(string text) => GUILayout.Label(text, _line);
        static string Short(string s, int n) => string.IsNullOrEmpty(s) || s.Length <= n ? s : s.Substring(0, n) + "…";
#endif
    }
}
