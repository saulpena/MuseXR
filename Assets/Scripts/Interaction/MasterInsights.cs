using System.Collections.Generic;
using MuseXR.Slots;
using MuseXR.UI;
using TMPro;
using UnityEngine;

namespace MuseXR.Interaction
{
    /// <summary>
    /// The masters' insights in a scene: watches the visitor's head against every <see cref="InsightTarget"/>
    /// and, on a click or a close approach, has the next master say her opening line about it through
    /// the scene's companions (and so the waist panel). Never interrupts a chapter's own turns: while
    /// the companions are speaking, approaches wait and clicks are ignored.
    /// </summary>
    public sealed class MasterInsights : MonoBehaviour
    {
        [Tooltip("Assets/Dialogue/masters.json: the masters' lenses. With it (and OPENAI_API_KEY), three live readings follow each opening line, as in her web version.")]
        public TextAsset mastersJson;

        Insights _rule;
        CompanionGroup _group;
        MusePico.Dialogue.DialogueClient _client;
        MusePico.Dialogue.MasterRosterData _roster;
        List<KeyValuePair<string, string>> _pending;
        int _asking;
        bool _voicesOnly;   // our stand-in group, not a chapter's companions   // the latest insight's token: a reading for an older one is dropped (her dialogueToken)

        async void Start()
        {
            if (GetComponent<ChapterChimes>() == null) gameObject.AddComponent<ChapterChimes>();   // every scene with insights has the chapter chimes
            if (mastersJson == null) return;
            try { _roster = MusePico.Dialogue.MasterRoster.Parse(mastersJson.text); }
            catch (System.Exception ex) { Debug.LogWarning("[Insight] masters.json: " + ex.Message); return; }
            var key = await MusePico.Generation.FallbackKeySource.ForOpenAi().GetKeyAsync();
            if (string.IsNullOrEmpty(key)) { Debug.LogWarning("[Insight] no OpenAI key: openings only, no live readings"); return; }
            _client = new MusePico.Dialogue.DialogueClient(
                new MusePico.Tripo.TripoWebRequestTransport(key, MusePico.Dialogue.DialogueClient.DefaultEndpoint), _roster);
        }

        // Our master ids -> the roster's (masters.json) and back.
        static string ToRoster(string id) => id == Masters.Frida ? "frida" : id == Masters.Hilma ? "hilma" : id == Masters.Morisot ? "morisot" : id;
        static string FromRoster(string id) => id == "frida" ? Masters.Frida : id == "hilma" ? Masters.Hilma : id == "morisot" ? Masters.Morisot : id;

        /// <summary>
        /// Her openArtDialogue: alongside the opening line, ask the masters (the companions first,
        /// then her defaults, three in all) "Tell me how you see "{title}"." - each answers through
        /// their lens, under 50 words - and play the readings in turn once the opening has finished.
        /// </summary>
        async void AskLive(InsightTarget t)
        {
            if (_client == null || _group == null) return;
            var token = ++_asking;
            var ids = new List<string>(); foreach (var id in _group.Ids) ids.Add(ToRoster(id));
            var lenses = MusePico.Dialogue.MasterRoster.Select(_roster, ids);
            var art = new MusePico.Dialogue.ArtworkContext { Title = t.title, Artist = t.artist };
            var result = await _client.AskAsync("Tell me how you see “" + t.title + "”.", lenses, art);
            if (this == null || token != _asking) return;   // a newer insight took over
            if (!result.Live) { Debug.LogWarning("[Insight] live readings failed: " + result.Error); return; }
            var lines = new List<KeyValuePair<string, string>>();
            foreach (var p in result.Perspectives) lines.Add(new KeyValuePair<string, string>(FromRoster(p.speakerId), p.text));
            _pending = lines;
            Debug.Log("[Insight] " + lines.Count + " live readings on " + t.title + " in " + result.Seconds.ToString("F1") + " s");
        }

        public static MasterInsights Ensure()
        {
            var m = FindAnyObjectByType<MasterInsights>();
            return m != null ? m : new GameObject("Master Insights").AddComponent<MasterInsights>();
        }

        CompanionGroup Group()
        {
            // Our stand-in voices give way to the real companions the moment there are some.
            if (_group != null && _voicesOnly)
            {
                foreach (var g in FindObjectsByType<CompanionGroup>(FindObjectsSortMode.None))
                    if (g != _group && !g.Busy) { Destroy(_group.gameObject); _group = null; _voicesOnly = false; break; }
            }
            if (_group == null)
            {
                // A new chapter (in the chained journey the last one's companions went with its world):
                // its companions, and the opening speakers rotate among them.
                // Readings still pending or in flight were about the last chapter's works: drop them.
                _pending = null; _asking++;
                _group = FindAnyObjectByType<CompanionGroup>();
                if (_group == null) _group = BuildGroup();
                _rule = _group != null ? new Insights(_group.Ids) : null;
            }
            return _group;
        }

        public void Clicked(InsightTarget t)
        {
            if (Group() == null || t == null) return;
            // A tap while a companion is still speaking is kept and answered when they finish: ignored, it
            // read as a broken pointer (the turtle tapped during the crane's reading, 4 Oct 2026).
            if (_group.Busy) { _queued = t; return; }
            _rule.Clicked(t.id);
            Speak(t);
        }

        void Update()
        {
            UpdateReplies();
            // The round table is running: no gazing at a painting beside it starts a reading, and none
            // still in flight lands among the table's turns under their "Based on" lines.
            if (ArtworkCard.Hushed) { if (_pending != null) { _pending = null; _asking++; } return; }
            if (Group() == null || _group.Busy) return;
            if (_pending != null) { var p = _pending; _pending = null; _group.SayInTurn(p); return; }
            if (_queued != null) { var q = _queued; _queued = null; _rule.Clicked(q.id); Speak(q); return; }
            var head = Camera.main != null ? Camera.main.transform : null;
            if (head == null) return;
            foreach (var t in InsightTarget.All)
            {
                if (t == null) continue;
                var to = t.transform.position - head.position; var flat = new Vector3(to.x, 0f, to.z);
                var g = head.forward; g.y = 0f;
                var off = g.sqrMagnitude > 1e-6f && flat.sqrMagnitude > 1e-6f ? Vector3.Angle(g, flat) : 180f;
                if (_rule.Approached(t.id, flat.magnitude, off)) { Speak(t); return; }
            }
        }

        /// <summary>
        /// A scene with the masters' figures (the layout's "Mark *") but no companions yet: gather them,
        /// as the Palace does - a crowd beside the visitor, with the waist panel for their lines.
        /// </summary>
        CompanionGroup BuildGroup()
        {
            var figures = new Dictionary<string, Transform>();
            var order = new List<string>();
            foreach (var id in Masters.Row)
                foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
                    if (t.name == "Mark " + id) { figures[id] = t; order.Add(id); break; }
            var go = new GameObject("Companions");
            if (order.Count == 0)
            {
                // No masters stand here yet (the Gate, before they are chosen): her default three as
                // voices on the panel, standing just behind the visitor, so a work is never left unanswered.
                _voicesOnly = true;
                go.name = "Companions (voices)";
                go.transform.SetParent(transform, false);
                foreach (var id in Masters.DefaultTrio)
                {
                    var stand = new GameObject("Voice " + id).transform;
                    stand.SetParent(go.transform, false);
                    figures[id] = stand; order.Add(id);
                }
            }
            // With the marks' chapter: it goes when that chapter does, and the next chapter gathers its own.
            else go.transform.SetParent(figures[order[0]].root, false);
            var g = go.AddComponent<CompanionGroup>();
            g.FollowVisitor = false;
            g.Crowd = true;   // Saul, 3 Oct: always a crowd beside the visitor
            g.Head = Camera.main != null ? Camera.main.transform : null;
            g.Set(order, figures);
            var subtitles = System.Type.GetType("MuseXR.UI.SubtitleRig, MuseXR.UI.Interaction");
            if (subtitles != null) go.AddComponent(subtitles);
            return g;
        }

        /// <summary>A master has begun on this target (a tap or walking up to it): it has been heard about.</summary>
        public static event System.Action<InsightTarget> Spoke;
        InsightTarget _queued;

        void Speak(InsightTarget t)
        {
            var speaker = _rule.NextSpeaker();
            if (speaker == null) return;
            _group.Say(speaker, Insights.Opening(speaker, t.title, t.artist));
            _pending = null;
            AskLive(t);
            if (t.askReply) ShowReplies(t, speaker);
            Spoke?.Invoke(t);
            // Heard about counts as seen: the compass moves on (it kept pointing at a work a master had
            // just spoken about when the insight came from walking up rather than a click).
            var compass = t.GetComponent<CompassTarget>();
            if (compass != null) compass.MarkDone();
            Debug.Log("[Insight] " + Masters.Name(speaker) + " on " + t.title);
        }

        // ---- the visitor's reply (her artworkChoices) -------------------------------------------------

        public const string ReplyPrompt = "What is this painting to you?";
        public const string Disclaimer = "AI INTERPRETATION GROUNDED IN DOCUMENTED THEMES — NOT AN AUTHENTIC QUOTATION";
        const float ReplyLeave = 4.5f, ReplySeconds = 30f;

        GameObject _replies;
        InsightTarget _replyTo;
        string _opener;
        float _replyAge;

        /// <summary>
        /// Her popup's three replies (app.js artDialogueMarkup), in front of the visitor below eye level,
        /// with her disclaimer. Choosing one: the companion who champions that axis answers with her
        /// scripted reaction, and the reply goes to the journey record (her philosophy axes).
        /// </summary>
        void ShowReplies(InsightTarget t, string opener)
        {
            CloseReplies();
            if (ArtworkCard.Hushed) return;   // the round table is running
            var eye = Camera.main != null ? Camera.main.transform : null;
            if (eye == null) return;
            _replyTo = t; _opener = opener; _replyAge = 0f;
            var fwd = eye.forward; fwd.y = 0f; fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
            var right = Vector3.Cross(Vector3.up, fwd).normalized;
            var anchor = new GameObject("Replies · " + t.title).transform;
            anchor.SetParent(transform, false);
            // Right of the waist panel, below eye level: read at a glance, never across the work.
            anchor.SetPositionAndRotation(eye.position + fwd * 1.45f + right * 0.55f - Vector3.up * 0.3f, Quaternion.LookRotation(fwd, Vector3.up));
            var c = MuseUi.Canvas(anchor, "Replies", 1.5f, 300f);
            var glass = MuseUi.Card(c, MuseTheme.Paper, MuseTheme.OptionRadius, MuseTheme.Line, 1f, padX: 12f, padY: 10f, gap: 6f, name: "Replies");
            MuseUi.Text(glass, ReplyPrompt, MuseUi.Face.Serif, 13f, MuseTheme.Ink, name: "Prompt");
            for (var i = 0; i < Insights.Replies.Count; i++)
            {
                var (axis, label) = Insights.Replies[i];
                var chip = MuseUi.Card(glass, MuseTheme.Paper, MuseTheme.OptionRadius, MuseTheme.Gold, 1f, padX: 9f, padY: 6f, gap: 6f, name: "Reply " + axis);
                var row = MuseUi.Row(chip, 7f, TextAnchor.MiddleLeft, "Row");
                var n = MuseUi.Text(row, "0" + (i + 1), MuseUi.Face.Mono, 9f, MuseTheme.Ink3, name: "Number"); n.enableWordWrapping = false;
                n.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().flexibleWidth = 0f;
                var l = MuseUi.Text(row, label, MuseUi.Face.Sans, 10f, MuseTheme.Ink, name: "Label");
                l.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().flexibleWidth = 1f;
                Canvas.ForceUpdateCanvases();
                var corners = new Vector3[4]; chip.GetWorldCorners(corners);
                var hit = new GameObject("Hit " + axis).transform;
                hit.SetParent(anchor, false);
                var lo = anchor.InverseTransformPoint(corners[0]); var hi = anchor.InverseTransformPoint(corners[2]);
                var box = hit.gameObject.AddComponent<BoxCollider>();
                box.center = (lo + hi) * 0.5f; box.size = new Vector3(Mathf.Abs(hi.x - lo.x), Mathf.Abs(hi.y - lo.y), 0.02f);
                var p = Pointable.Make(hit.gameObject, "reply " + axis);
                var chosen = axis;
                p.Selected += (_, __) => Reply(chosen);
            }
            MuseUi.Text(glass, Disclaimer, MuseUi.Face.Sans, 6.5f, MuseTheme.Ink3, 0.12f, true, name: "Disclaimer");
            _replies = anchor.gameObject;
        }

        /// <summary>The visitor's reply: her champion answers, the record keeps it.</summary>
        public bool Reply(string axis)
        {
            if (_replyTo == null || Group() == null) return false;
            var speaker = Insights.ReactionSpeaker(axis, _group.Ids, _opener);
            var line = Insights.Reaction(speaker, axis);
            JourneyMemory.Record.AddReply(_replyTo.id, axis);
            // Next, before any live readings still waiting: her popup swaps in the reaction at once.
            var reaction = new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>(speaker, line) };
            if (_pending != null) reaction.AddRange(_pending);
            if (_group.Busy) _group.StopTurns();
            _pending = null;
            _group.SayInTurn(reaction);
            Debug.Log("[Insight] reply '" + axis + "' to " + _replyTo.title + ": " + Masters.Name(speaker) + " answers");
            CloseReplies();
            return true;
        }

        void UpdateReplies()
        {
            if (_replies == null) return;
            _replyAge += Time.deltaTime;
            var eye = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
            var far = _replyTo == null || Vector3.Distance(new Vector3(eye.x, 0f, eye.z), new Vector3(_replyTo.transform.position.x, 0f, _replyTo.transform.position.z)) > ReplyLeave;
            if (far || _replyAge > ReplySeconds || ArtworkCard.Hushed) CloseReplies();
        }

        void CloseReplies()
        {
            if (_replies != null) Destroy(_replies);
            _replies = null; _replyTo = null;
        }
    }
}
