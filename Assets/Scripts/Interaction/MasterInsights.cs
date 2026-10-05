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
        float _nextSweep;
        CompanyStage _opening;   // looked up once a second, with the sweep
        bool OpeningUnderway => _opening != null && _opening.isActiveAndEnabled && _opening.Current != CompanyStage.Phase.Done;
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

        // ---- the masters' voices -----------------------------------------------------------------------

        // Each line queued HERE is voiced as its own turn starts, through MasterVoice: a new piece starts a new
        // round, which stops the voice of the last one instead of talking over it.
        readonly List<System.Action<string, string>> _listening = new List<System.Action<string, string>>();
        CompanionGroup _listeningOn;

        /// <summary>Say <paramref name="lines"/> through <paramref name="say"/>, and voice each as its turn starts.</summary>
        void Voiced(List<KeyValuePair<string, string>> lines, System.Func<bool> say, bool cut = true)
        {
            // The live readings follow their own piece's opening line: they queue behind its voice, not cut it.
            if (cut) StopVoice();
            var mine = new Dictionary<string, string>();
            foreach (var kv in lines) mine[kv.Key] = kv.Value;
            var group = _group;
            System.Action<string, string> speak = null;
            speak = (id, line) =>
            {
                if (mine.TryGetValue(id, out var l) && l == line) { mine.Remove(id); MasterVoice.Get().Say(id, line); }
                if (mine.Count == 0) { group.LineStarted -= speak; _listening.Remove(speak); }
            };
            group.LineStarted += speak; _listening.Add(speak); _listeningOn = group;
            if (!say()) { group.LineStarted -= speak; _listening.Remove(speak); }
        }

        /// <summary>The voice playing and any still to come of the last round: gone.</summary>
        void StopVoice()
        {
            MasterVoice.Get().NewRound();
            if (_listeningOn != null) foreach (var s in _listening) _listeningOn.LineStarted -= s;
            _listening.Clear();
        }

        public static MasterInsights Ensure()
        {
            var m = FindAnyObjectByType<MasterInsights>();
            return m != null ? m : new GameObject("Master Insights").AddComponent<MasterInsights>();
        }

        CompanionGroup Group()
        {
            // A group we built ourselves gives way to the real companions the moment there are some: the
            // Gate's stand-in voices, and the marks we gather in the frame before a chapter's own Start()
            // makes its companions. Kept, two groups drove the same three figures and talked over each
            // other (Grotto and Monet, full walk 4 Oct 2026). Adopted directly: cleared to null, the find
            // below met our own group again, since Destroy only lands at the end of the frame.
            // A group switched off with its chapter's frame never ticks again: it stays "speaking" for good,
            // Busy forever, and every approach after it went unanswered (walk-up test, 4 Oct). Let it go.
            if (_group != null && !_group.isActiveAndEnabled)
            {
                if (_built) Destroy(_group.gameObject);
                _group = null; _built = false;
            }
            if (_group != null && _built && !_group.Busy)
            {
                foreach (var g in FindObjectsByType<CompanionGroup>(FindObjectsSortMode.None))
                    if (g != _group && g.Ids.Count > 0 && !g.Busy)
                    {
                        Destroy(_group.gameObject);
                        _group = g; _built = false; _voicesOnly = false;
                        _rule = new Insights(g.Ids);
                        break;
                    }
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
            if (_group.Busy || OpeningUnderway) { _queued = t; return; }
            _rule.Clicked(t.id);
            Speak(t);
        }

        void Update()
        {
            UpdateReplies();
            if (Time.time >= _nextSweep) { _nextSweep = Time.time + 1f; Exhibit.Sweep(); MakeAskable(); _opening = FindAnyObjectByType<CompanyStage>(); }
            UpdateAskTalk();   // every piece on show answers and is tracked
            // The round table is running: no gazing at a painting beside it starts a reading, and none
            // still in flight lands among the table's turns under their "Based on" lines.
            if (ArtworkCard.Hushed) { if (_pending != null) { _pending = null; _asking++; } return; }
            if (Group() == null || _group.Busy) return;
            // The Gate's company is still being chosen, stepping out or answering the visitor's question: no
            // reading starts. One did (the Pissarros now stand by the start): Socrates on The Crystal Palace took
            // the companions' turns, their answers never played, the round never finished, and the lanterns,
            // which wait for it, never came (Saul's run, 5 Oct). A tap meanwhile waits in _queued.
            if (OpeningUnderway) return;
            if (_pending != null) { var p = _pending; _pending = null; Voiced(p, () => _group.SayInTurn(p), cut: false); return; }
            if (_queued != null) { var q = _queued; _queued = null; _rule.Clicked(q.id); Speak(q); return; }
            var head = Camera.main != null ? Camera.main.transform : null;
            if (head == null) return;
            foreach (var t in InsightTarget.All)
            {
                if (t == null || !t.onApproach) continue;   // anything else answers a click
                var to = t.transform.position - head.position; var flat = new Vector3(to.x, 0f, to.z);
                var g = head.forward; g.y = 0f;
                var off = g.sqrMagnitude > 1e-6f && flat.sqrMagnitude > 1e-6f ? Vector3.Angle(g, flat) : 180f;
                if (_rule.Approached(t.id, Mathf.Max(0f, flat.magnitude - t.reach), off)) { Speak(t); return; }
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
            _built = true;
            return g;
        }

        /// <summary>True while <see cref="_group"/> is one <see cref="BuildGroup"/> made, not a chapter's own.</summary>
        bool _built;

        /// <summary>A master has begun on this target (a tap or walking up to it): it has been heard about.</summary>
        public static event System.Action<InsightTarget> Spoke;
        InsightTarget _queued;

        void Speak(InsightTarget t)
        {
            _lastTarget = t;
            var speaker = _rule.NextSpeaker();
            if (speaker == null) return;
            DialogueContext.On(t.title);
            var opening = Insights.Opening(speaker, t.title, t.artist);
            Voiced(new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>(speaker, opening) }, () => _group.Say(speaker, opening));
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
            // Her web popup: the question, and an x to close it (Saul, 5 Oct: no timer - it stays until a reply or the x).
            var top = MuseUi.Row(glass, 6f, TextAnchor.MiddleLeft, "Top");
            var prompt = MuseUi.Text(top, ReplyPrompt, MuseUi.Face.Serif, 13f, MuseTheme.Ink, name: "Prompt");
            prompt.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().flexibleWidth = 1f;
            var x = MuseUi.Card(top, MuseTheme.Paper, MuseTheme.OptionRadius, MuseTheme.Line, 1f, padX: 7f, padY: 2f, gap: 0f, name: "Close");
            MuseUi.Text(x, "×", MuseUi.Face.Sans, 13f, MuseTheme.Ink3, name: "X").enableWordWrapping = false;
            x.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().flexibleWidth = 0f;
            _closeChip = x;
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
            // The x's hit box, sized from its laid-out corners like the reply chips.
            Canvas.ForceUpdateCanvases();
            var xc = new Vector3[4]; _closeChip.GetWorldCorners(xc);
            var xh = new GameObject("Hit close").transform; xh.SetParent(anchor, false);
            var xlo = anchor.InverseTransformPoint(xc[0]); var xhi = anchor.InverseTransformPoint(xc[2]);
            var xb = xh.gameObject.AddComponent<BoxCollider>();
            xb.center = (xlo + xhi) * 0.5f; xb.size = new Vector3(Mathf.Abs(xhi.x - xlo.x) + 0.02f, Mathf.Abs(xhi.y - xlo.y) + 0.02f, 0.02f);
            Pointable.Make(xh.gameObject, "replies close").Selected += (_, __) => CloseReplies();
            _replies = anchor.gameObject;
            FollowVisitor.Attach(_replies);   // follows you like the masters' card (Saul, 5 Oct)
            Appear.In(_replies, 0.3f);   // eased, never popped (Saul, 5 Oct)
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
            Voiced(reaction, () => _group.SayInTurn(reaction));
            Debug.Log("[Insight] reply '" + axis + "' to " + _replyTo.title + ": " + Masters.Name(speaker) + " answers");
            CloseReplies();
            return true;
        }

        void UpdateReplies()
        {
            if (_replies == null) return;
            _replyAge += Time.deltaTime;
            if (ArtworkCard.Hushed) { CloseReplies(); return; }   // only the round table takes it away; no timer, no distance
        }

        RectTransform _closeChip;

        // ---- ask the masters (her web openAskDialogue; her VR doc: point at a companion for 3 short options, hold X to speak) ----

        InsightTarget _lastTarget;
        bool _askOpen;
        UnityEngine.InputSystem.InputAction _askTalk;
        MusePico.Dialogue.MuseumDialogue _askDialogue;

        /// <summary>Each companion walking with the visitor can be pointed at to ask a question; not while they are still
        /// being chosen at the Gate, where pointing invites them.</summary>
        void MakeAskable()
        {
            if (_group == null || _voicesOnly) return;
            var company = FindAnyObjectByType<CompanyStage>();
            if (company != null && company.Current == CompanyStage.Phase.Choosing) return;
            foreach (var id in _group.Ids)
            {
                if (!_group.Figures.TryGetValue(id, out var f) || f == null) continue;
                var existing = f.Find("Ask hit");
                if (existing != null)
                {
                    // The lanterns switch every collider on the masters off so the laser reaches past them; the slim
                    // ask target stays live - they walk at the visitor's sides now, never between them and a lantern.
                    var col = existing.GetComponent<Collider>(); if (col != null && !col.enabled) col.enabled = true;
                    var pt = existing.GetComponent<Pointable>(); if (pt != null && !pt.enabled) pt.enabled = true;
                    continue;
                }
                var hit = new GameObject("Ask hit");
                hit.transform.SetParent(f, false);
                var cap = hit.AddComponent<CapsuleCollider>();
                cap.isTrigger = true; cap.center = new Vector3(0f, 0.9f, 0f); cap.height = 1.8f; cap.radius = 0.3f;
                var who = id;
                Pointable.Make(hit, "ask " + id).Selected += (_, __) => OpenAsk(who);
            }
        }

        static string[] Suggestions(InsightTarget t) => t != null
            ? new[] { "What do you see in " + t.title + "?", "What would you change in " + t.title + "?", "How does " + t.title + " answer my question?" }
            : new[] { "What should I look at here?", "How would you answer my question?", "What have you noticed about me so far?" };

        /// <summary>The ask panel: her "ASK - ALL THREE MASTERS ANSWER", three short questions, hold X to say your own, an x.</summary>
        public void OpenAsk(string masterId)
        {
            if (Group() == null || ArtworkCard.Hushed) return;
            CloseReplies();
            var eye = Camera.main != null ? Camera.main.transform : null;
            if (eye == null) return;
            var anchor = new GameObject("Replies \u00b7 ask").transform;   // shares the replies slot: one question panel at a time
            anchor.SetParent(transform, false);
            var c = MuseUi.Canvas(anchor, "Replies", 1.5f, 300f);
            var glass = MuseUi.Card(c, MuseTheme.Paper, MuseTheme.OptionRadius, MuseTheme.Line, 1f, padX: 12f, padY: 10f, gap: 6f, name: "Replies");
            var top = MuseUi.Row(glass, 6f, TextAnchor.MiddleLeft, "Top");
            var head = MuseUi.Text(top, "ASK  \u00b7  ALL THREE MASTERS ANSWER", MuseUi.Face.Sans, 8f, MuseTheme.Ink3, 0.16f, true, name: "Kicker");
            head.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().flexibleWidth = 1f;
            var x = MuseUi.Card(top, MuseTheme.Paper, MuseTheme.OptionRadius, MuseTheme.Line, 1f, padX: 7f, padY: 2f, gap: 0f, name: "Close");
            MuseUi.Text(x, "\u00d7", MuseUi.Face.Sans, 13f, MuseTheme.Ink3, name: "X").enableWordWrapping = false;
            x.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().flexibleWidth = 0f;
            var who = Masters.Name(masterId);
            MuseUi.Text(glass, _lastTarget != null ? who + " turns toward " + _lastTarget.title + "." : who + " turns toward you.", MuseUi.Face.Serif, 12f, MuseTheme.Ink, name: "Prompt");
            var asks = Suggestions(_lastTarget);
            var chips = new List<(RectTransform rect, string q)>();
            for (var i = 0; i < asks.Length; i++)
            {
                var chip = MuseUi.Card(glass, MuseTheme.Paper, MuseTheme.OptionRadius, MuseTheme.Gold, 1f, padX: 9f, padY: 6f, gap: 6f, name: "Ask " + i);
                var row = MuseUi.Row(chip, 7f, TextAnchor.MiddleLeft, "Row");
                var n = MuseUi.Text(row, "0" + (i + 1), MuseUi.Face.Mono, 9f, MuseTheme.Ink3, name: "Number"); n.enableWordWrapping = false;
                n.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().flexibleWidth = 0f;
                var l = MuseUi.Text(row, asks[i], MuseUi.Face.Sans, 10f, MuseTheme.Ink, name: "Label");
                l.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().flexibleWidth = 1f;
                chips.Add((chip, asks[i]));
            }
            MuseUi.Text(glass, "Hold X and say your own question", MuseUi.Face.Sans, 8f, MuseTheme.Ink3, 0.12f, true, name: "Hint");
            Canvas.ForceUpdateCanvases();
            var corners = new Vector3[4];
            foreach (var (rect, q) in chips)
            {
                rect.GetWorldCorners(corners);
                var h = new GameObject("Hit ask").transform; h.SetParent(anchor, false);
                var lo = anchor.InverseTransformPoint(corners[0]); var hi = anchor.InverseTransformPoint(corners[2]);
                var box = h.gameObject.AddComponent<BoxCollider>();
                box.center = (lo + hi) * 0.5f; box.size = new Vector3(Mathf.Abs(hi.x - lo.x), Mathf.Abs(hi.y - lo.y), 0.02f);
                var question = q;
                Pointable.Make(h.gameObject, "ask question").Selected += (_, __) => AskQuestion(question);
            }
            x.GetWorldCorners(corners);
            var xh = new GameObject("Hit close").transform; xh.SetParent(anchor, false);
            var xlo = anchor.InverseTransformPoint(corners[0]); var xhi = anchor.InverseTransformPoint(corners[2]);
            var xb = xh.gameObject.AddComponent<BoxCollider>();
            xb.center = (xlo + xhi) * 0.5f; xb.size = new Vector3(Mathf.Abs(xhi.x - xlo.x) + 0.02f, Mathf.Abs(xhi.y - xlo.y) + 0.02f, 0.02f);
            Pointable.Make(xh.gameObject, "ask close").Selected += (_, __) => CloseReplies();
            _replies = anchor.gameObject; _askOpen = true;
            FollowVisitor.Attach(_replies);
            Appear.In(_replies, 0.3f);
        }

        /// <summary>Hold X while the ask panel is open: the visitor's own question, transcribed.</summary>
        void UpdateAskTalk()
        {
            if (_askTalk == null)
            {
                _askTalk = new UnityEngine.InputSystem.InputAction("ask-talk", UnityEngine.InputSystem.InputActionType.Button);
                UnityEngine.InputSystem.InputActionSetupExtensions.AddBinding(_askTalk, "<XRController>{LeftHand}/primaryButton");
                UnityEngine.InputSystem.InputActionSetupExtensions.AddBinding(_askTalk, "<Keyboard>/x");
                _askTalk.Enable();
            }
            if (!_askOpen) return;
            if (_askTalk.WasPressedThisFrame())
            {
                _askDialogue = FindAnyObjectByType<MusePico.Dialogue.MuseumDialogue>();
                if (_askDialogue == null) return;
                _askDialogue.TextDictated -= OnAskDictated; _askDialogue.TextDictated += OnAskDictated;
                _askDialogue.ListenForText();
            }
            if (_askTalk.WasReleasedThisFrame() && _askDialogue != null) _askDialogue.FinishListening();
        }

        void OnAskDictated(string text)
        {
            if (_askDialogue != null) _askDialogue.TextDictated -= OnAskDictated;
            if (!string.IsNullOrWhiteSpace(text)) AskQuestion(text.Trim());
        }

        /// <summary>The visitor's question to all three: kept for the round table, answered live in turn.</summary>
        public async void AskQuestion(string question)
        {
            CloseReplies();
            if (Group() == null || string.IsNullOrWhiteSpace(question)) return;
            JourneyMemory.AddAsked(question);
            DialogueContext.Set("You asked  \u00b7  " + question);
            if (_client == null) { Debug.LogWarning("[Insight] no live dialogue: the question goes unanswered"); return; }
            var token = ++_asking;
            var ids = new List<string>(); foreach (var id in _group.Ids) ids.Add(ToRoster(id));
            var lenses = MusePico.Dialogue.MasterRoster.Select(_roster, ids);
            var art = _lastTarget != null ? new MusePico.Dialogue.ArtworkContext { Title = _lastTarget.title, Artist = _lastTarget.artist } : default(MusePico.Dialogue.ArtworkContext);
            var result = await _client.AskAsync(question, lenses, art);
            if (this == null || token != _asking) return;
            if (!result.Live) { Debug.LogWarning("[Insight] asked question failed: " + result.Error); return; }
            var lines = new List<KeyValuePair<string, string>>();
            foreach (var p in result.Perspectives) lines.Add(new KeyValuePair<string, string>(FromRoster(p.speakerId), p.text));
            DialogueContext.Set("You asked  \u00b7  " + question);
            if (_group.Busy) _group.StopTurns();
            _pending = lines;
            Debug.Log("[Insight] asked: " + question + " -> " + lines.Count + " answers in " + result.Seconds.ToString("F1") + " s");
        }

        void CloseReplies()
        {
            if (_replies != null) Appear.Out(_replies, 0.25f, destroy: true);
            _replies = null; _replyTo = null; _askOpen = false;
        }
    }
}
