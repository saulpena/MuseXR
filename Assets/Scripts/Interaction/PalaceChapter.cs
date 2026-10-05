using System;
using System.Collections.Generic;
using MusePico.Dialogue;
using MuseXR.Slots;
using UnityEngine;

namespace MuseXR.Interaction
{
    /// <summary>
    /// Her chapter A, Palace · Court of Keeping, end to end in a scene (storyboard A, chatplan §2.4):
    ///
    ///   1 Before    the miniatures idle-turn on their plinths (Holdable).
    ///   2 During    grip takes one, it follows the hand, the stick turns it in 15° steps (Holdable).
    ///   3 Feedback  it snaps in with the bronze bell (SlotStation); the court lights; the companions
    ///               respond in turn - if nobody has been invited yet, her default trio is called.
    ///               Nothing else is on show while they speak, and A means "next".
    ///   4 Saved     when they have finished, three reason chips and the confirm strip appear; pick
    ///               one (or speak one: <see cref="SpeakReason"/>). A saves palace{object, yaw, reason,
    ///               mode} to the journey record. A before a reason is refused with a knock. Lifting
    ///               the piece out undoes all of it.
    ///
    /// One thing at a time, in her storyboard's order (Saul, headset test 3 Oct 2026: with the chips,
    /// the strip and the subtitles all up at once, and A meaning both "next" and "keep", it was
    /// unclear what anything did).
    ///
    /// The card fallback (two cards: point, flip, glow) runs the same steps with mode "card".
    /// </summary>
    public sealed class PalaceChapter : MonoBehaviour, IConfirmable
    {
        public PalaceFlow Flow { get; } = new PalaceFlow();
        public SlotStation Court { get; private set; }
        public CardChoiceStation Cards { get; private set; }
        public CompanyStage Company { get; private set; }
        /// <summary>Companions already standing in the chapter (her diagram's marks), when there is no Company stage.</summary>
        public CompanionGroup Group { get; set; }
        public JourneyRecord Record { get; private set; }
        public IReadOnlyList<Pointable> Chips => _chips;
        public Light CourtLight { get; private set; }
        /// <summary>
        /// The visible half of "the court lights": a warm glow on the court's surface. Splat floors take
        /// no light, so the point light alone shows only on the piece (blind review: "no warm light").
        /// </summary>
        public Renderer CourtGlow { get; private set; }

        /// <summary>What a companion says about the kept piece. Canned until DialogueClient is wired here.</summary>
        public Func<string, string, string> LineFor = CannedLine;

        public event Action<string> Note;
        public event Action<PalaceFlow> Saved;

        /// <summary>True while the companions are responding to the placed piece (step 3).</summary>
        public bool Listening { get; private set; }

        public const float CourtLightIntensity = 2.6f, CourtLightRange = 0.9f;   // a pool on the court, not the room

        /// <summary>Her reasons prompt (MUSE-VR-design, Palace: "Leave one reason for your choice").</summary>
        public const string ReasonsKicker = "Stop 1  ·  Your reason", ReasonsPrompt = "Leave one reason for your choice";
        public const string ReasonsHint = "Point at a reason and pull the trigger", ReasonsKeep = "A keeps it  ·  choose another to change it";
        ChoicePanel _panel;
        readonly List<Pointable> _chips = new List<Pointable>();
        readonly Dictionary<Transform, GameObject> _glows = new Dictionary<Transform, GameObject>();
        Transform _chipRoot;
        Vector3 _courtChipsAt;
        Quaternion _courtChipsRot;
        float _light;
        bool _wantFocus, _turnsPending;

        public static PalaceChapter Make(GameObject host, SlotStation court, CardChoiceStation cards,
                                         CompanyStage company, JourneyRecord record, Vector3 chipsAt, Quaternion chipsAwayFromViewer)
        {
            var c = host.AddComponent<PalaceChapter>();
            c.Court = court; c.Cards = cards; c.Company = company; c.Record = record ?? new JourneyRecord();
            if (court != null) court.Cue += c.OnCourtCue;
            if (cards != null) c.HookCards(cards);
            c.BuildCourtLight();
            c.BuildChips(chipsAt, chipsAwayFromViewer);
            return c;
        }

        // ---- the court -------------------------------------------------------------------

        void OnCourtCue(SlotStation s, SlotEvent e)
        {
            switch (e.Cue)
            {
                case SlotCue.Placed:
                    Flow.Placed(s.Pieces[e.Piece].Id, s.YawOf(s.Pieces[e.Piece]));
                    // Until a reason is chosen the strip says so: A here is refused (blind review: a
                    // bare "Keep this moment?" read as if A could skip the choice).
                    s.Board.Choice.Retitle(Flow.Summary + " · pick a reason above");
                    Respond(s.Pieces[e.Piece].Id);
                    break;
                case SlotCue.Undone:
                case SlotCue.Lifted:
                    StopListening();
                    Flow.Unplaced();
                    ShowChips(false);
                    Say("[Palace] taken back - choose again");
                    break;
            }
        }

        /// <summary>Step 3: the court lights and the companions respond. The reasons wait for them.</summary>
        void Respond(string piece)
        {
            Say("[Palace] " + piece + " placed at " + Flow.YawDeg + "°. Your companions respond.");
            Listening = true;
            _wantFocus = true;   // A comes here while they speak, and never keeps the piece early
            if (Court != null) Court.HideStrip = true;
            ShowChips(false);
            CallCompanions();
            if (Listening && Company == null && (Group == null || Group.Ids.Count == 0)) AskForReason();   // nobody to listen to
        }

        /// <summary>Step 4: the reasons and the confirm strip, and A to this chapter.</summary>
        void AskForReason()
        {
            Listening = false;
            if (Court != null) Court.HideStrip = false;
            ShowChips(true);
            _wantFocus = true;
            Say("[Palace] pick a reason, then A to keep it");
        }

        void OnTurnsFinished()
        {
            if (Listening && (Flow.Current == PalaceFlow.Phase.Placed || Flow.Current == PalaceFlow.Phase.Ready)) AskForReason();
        }

        /// <summary>Back to choosing: stop whoever is speaking, hide the reasons.</summary>
        void StopListening()
        {
            Listening = false;
            _turnsPending = false;
            if (Court != null) Court.HideStrip = false;
            var g = Company != null ? Company.Group : Group;
            if (g != null) g.StopTurns();
        }

        void CallCompanions()
        {
            if (Company == null)
            {
                if (Group != null && Group.Ids.Count > 0) StartTurns(Group);
                return;
            }
            if (Company.Current == CompanyStage.Phase.Choosing)
            {
                // Nobody invited yet: her demo route's default trio comes to the court.
                if (!Company.Invitation.CanProceed) Company.Preselect(Masters.DefaultTrio);
                Company.Send(answer: false);
                _turnsPending = true;
                Say("[Palace] calling your companions");
                return;
            }
            if (Company.Current == CompanyStage.Phase.Done) StartTurns();
            else _turnsPending = true;   // still stepping or answering at the Company: after that
        }

        void StartTurns() => StartTurns(Company.Group);

        int _asked;

        /// <summary>
        /// Her "companions respond in turn" - genuinely: each master answers the visitor's choice live, in their own
        /// voice, knowing what was kept, how it was set, and the question the visitor came in with (Saul, 5 Oct: "the
        /// response from the masters should be genuine"). The fixed lines are only the fallback when there is no live
        /// dialogue. Starts when the lines are in; dropped if the piece was lifted out meanwhile.
        /// </summary>
        async void StartTurns(CompanionGroup group)
        {
            _turnsPending = false;
            var piece = Flow.Piece;
            var token = ++_asked;
            DialogueContext.Set("You kept the " + piece.ToLowerInvariant());
            System.Collections.Generic.Dictionary<string, string> live = null;
            try { live = await MasterInsights.Ensure().AskMasters(ReactionQuestion(piece, Flow.YawDeg), group.Ids, "the bronze " + piece.ToLowerInvariant(), PieceAbout(piece)); }
            catch (Exception ex) { Debug.LogWarning("[Palace] live reactions: " + ex.Message); }
            if (this == null || token != _asked || group == null || Flow.Piece != piece || Flow.Current != PalaceFlow.Phase.Placed) return;
            group.LineFor = id => live != null && live.TryGetValue(id, out var l) ? l : LineFor(id, piece);
            group.TurnsFinished -= OnTurnsFinished;
            group.TurnsFinished += OnTurnsFinished;
            MasterVoice.Follow(group);   // voiced, each line as its turn starts (silent before, live run 4 Oct)
            group.BeginTurns();   // the group takes A while they speak: A is "next"
        }

        /// <summary>What the masters are asked: the room's question, the visitor's choice and how they set it, and the
        /// question they came in with - so the answer is to this visitor, not to the object.</summary>
        static string ReactionQuestion(string piece, int yawDeg)
        {
            var crane = !string.Equals(piece, "Turtle", StringComparison.OrdinalIgnoreCase);
            var other = crane ? "turtle" : "crane";
            var yaw = ((yawDeg % 360) + 360) % 360;
            // In words, not degrees: read as a number it came back as "turned 71 degrees from your gaze" (live run, 5 Oct).
            var facing = yaw <= 30 || yaw >= 330 ? "facing them" : yaw >= 150 && yaw <= 210 ? "turned away from them" : "turned aside, half away from them";
            var asked = JourneyMemory.Record != null ? JourneyMemory.Record.Question : "";
            return "In the Palace, the Court of Keeping, the room asks: 'Of what I inherited, what is worth keeping?' "
                 + "Of two bronzes the visitor kept the " + piece.ToLowerInvariant() + ", not the " + other + ", and set it in the court " + facing + ". "
                 + (string.IsNullOrWhiteSpace(asked) ? "" : "They came into the museum asking: \"" + asked.Trim() + "\". ")
                 + "Respond to that choice, to them, in one or two short sentences, under 35 words. "
                 + "No numbers, dates, dynasties or catalogue details.";
        }

        // What the piece means, not where it comes from: given its catalogue sources, the masters recited them.
        static string PieceAbout(string piece) =>
            string.Equals(piece, "Turtle", StringComparison.OrdinalIgnoreCase)
                ? "a small bronze turtle - slow, enduring, a home carried on its back"
                : "a small bronze crane - long life, standing still, looking up";

        /// <summary>Only when there is no live dialogue (offline, no key, the call failed): fixed lines, never presented
        /// as more than that.</summary>
        public static string CannedLine(string master, string piece)
        {
            var crane = !string.Equals(piece, "Turtle", StringComparison.OrdinalIgnoreCase);
            switch (master)
            {
                // The turtle's are hers, word for word (MUSE-VR-design, the Palace storyboard); the crane's are ours.
                case Masters.Monet: return crane ? "Bronze holds the hour. By dusk this crane will be a darker thing than now."
                                                 : "In the hall the light on its shell is gold. Out under the open sky it turns back to stone grey. What you keep is the version of it that never stops changing";
                case Masters.VanGogh: return crane ? "You turned it until it faced you. What were you looking for?"
                                                   : "Your hand slowed down as you set it down. You are carrying all those years of weight for it, and that weight is on your shoulders now too";
                case Masters.Socrates: return crane ? "You kept the one that looks up. Is that who you are, or who you were told to be?"
                                                    : "You say it is worth keeping. Because it lasts, or because it is yours? If you had not inherited it, would you still choose it?";
                default: return Masters.Name(master) + " considers what you kept.";
            }
        }

        // ---- the card fallback -------------------------------------------------------------

        void HookCards(CardChoiceStation cards)
        {
            foreach (var card in cards.Cards) _glows[card] = Glow(card);
            cards.Logic.Flipped += (i, up) =>
            {
                _glows[cards.Cards[i]].SetActive(up);
                if (up)
                {
                    Flow.Placed(cards.Logic.Ids[i], 0, PalaceFlow.Mode.Card);
                    cards.Logic.Choice.Retitle(Flow.Summary + " · pick a reason");
                    Respond(cards.Logic.Ids[i]);
                }
                else if (cards.Logic.FaceUp < 0)
                {
                    StopListening();
                    Flow.Unplaced();
                    ShowChips(false);
                }
            };
        }

        /// <summary>A warm card-sized glow behind a card, shown while it is face up.</summary>
        static GameObject Glow(Transform card)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Quad);
            g.name = "Glow";
            DestroyImmediate(g.GetComponent<Collider>());
            g.transform.SetParent(card, false);
            g.transform.localPosition = new Vector3(0f, 0f, -0.01f);   // between the faces: shows round the face-up edge
            g.transform.localScale = new Vector3(0.38f, 0.5f, 1f);
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 2f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_ZWrite", 0f); m.SetFloat("_Cull", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            m.SetColor("_BaseColor", new Color(0.55f, 0.4f, 0.15f, 1f));
            g.GetComponent<Renderer>().sharedMaterial = m;
            g.SetActive(false);
            return g;
        }

        // ---- reasons -----------------------------------------------------------------------

        void BuildChips(Vector3 at, Quaternion awayFromViewer)
        {
            _chipRoot = new GameObject("Reason Chips").transform;
            _chipRoot.SetParent(transform, true);
            _chipRoot.SetPositionAndRotation(at, awayFromViewer);
            _courtChipsAt = at; _courtChipsRot = awayFromViewer;
            _panel = ChoicePanel.Make(_chipRoot, "Reasons");
            _chipRoot.gameObject.SetActive(false);
        }

        /// <summary>Whether the reasons should be up: the piece is placed, the companions have spoken, nothing kept yet.</summary>
        bool ReasonsWanted => !Listening && _reasonsOpen && (Flow.Current == PalaceFlow.Phase.Placed || Flow.Current == PalaceFlow.Phase.Ready);
        bool _reasonsOpen;

        void ShowChips(bool on)
        {
            _reasonsOpen = on;
            if (Court != null) Court.HideStrip = on;   // the panel says what the strip said; two of them overlapped (Saul, 5 Oct)
            Appear.Set(_chipRoot.gameObject, on, 0.3f);   // eased, never popped (Saul, 5 Oct)
            if (!on) return;
            // Over the court, above the placed piece, turned to the visitor wherever they stand (Saul, 5 Oct); under
            // the cards in the card fallback.
            var at = _courtChipsAt;
            if (Flow.Kind == PalaceFlow.Mode.Card && Cards != null && Cards.Cards.Length > 0)
            {
                var mid = Vector3.zero; foreach (var c in Cards.Cards) mid += c.position; mid /= Cards.Cards.Length;
                at = mid - Vector3.up * 0.36f;
            }
            _chipRoot.position = at;
            var follow = _chipRoot.GetComponent<FollowVisitor>(); if (follow != null) Destroy(follow);
            TurnToVisitor.Attach(_chipRoot.gameObject);
            var reasons = PalaceFlow.ReasonsFor(Flow.Piece);
            _panel.Build(ReasonsKicker, ReasonsPrompt, reasons, ReasonsHint, 2.2f, (i, pointer) => PickChip(i, pointer));
            _chips.Clear(); _chips.AddRange(_panel.Options);
            // Already chosen (shown again after something hid it): keep the choice marked.
            var already = -1; for (var i = 0; i < reasons.Count; i++) if (reasons[i] == Flow.Reason) already = i;
            if (already >= 0) _panel.Mark(already, ReasonsKeep);
        }

        /// <summary>
        /// The soft-lock's guard: while a reason is wanted the panel is up. Something else opened mid-choice once hid it
        /// for good and left the visitor stuck in the Palace (Saul, 5 Oct); whatever hides it now, it comes back, and
        /// A comes back here when nothing else holds it.
        /// </summary>
        void KeepReasonsUp()
        {
            if (_chipRoot == null || !ReasonsWanted) return;
            if (!_chipRoot.gameObject.activeSelf || _panel.Options.Count == 0) { Debug.Log("[Palace] the reasons were hidden while one is wanted: shown again"); ShowChips(true); }
            if (ConfirmInput.Focus == null) ConfirmInput.Take(this);
        }

        public bool PickChip(int index, Pointer pointer = null)
        {
            if (index < 0 || index >= _chips.Count || Listening) return false;   // the reasons come after the companions
            if (!Flow.ChooseReason(PalaceFlow.ReasonsFor(Flow.Piece)[index])) return false;
            // The chosen chip fills with her soft gold and its number turns to a tick; the others quieten. Colour only,
            // so the panel never changes size (Saul, 5 Oct: the grown, bolded choice overlapped the text beside it).
            _panel.Mark(index, ReasonsKeep);
            ChimePlayer.Play(ChimePlayer.TickClip(), _chips[index].transform.position, 0.4f);
            pointer?.Source.Buzz(SlotRules.LightAmplitude * 1.5f, SlotRules.LightSeconds);
            Retitle();
            _wantFocus = true;
            Say("[Palace] reason: " + Flow.Reason + " - A keeps it");
            return true;
        }

        /// <summary>Hold-X speech, from whatever dictation the scene has.</summary>
        public bool SpeakReason(string transcript)
        {
            if (Listening || !Flow.SpeakReason(transcript)) return false;
            Retitle();
            _wantFocus = true;
            Say("[Palace] reason (spoken): " + Flow.Reason);
            return true;
        }

        void Retitle()
        {
            if (Flow.Kind == PalaceFlow.Mode.Card) Cards?.Logic.Choice.Retitle(Flow.Summary);
            else Court?.Board.Choice.Retitle(Flow.Summary);
        }

        // ---- A and B -----------------------------------------------------------------------

        public bool Confirm()
        {
            if (Listening)
            {
                // While the companions speak, A is "next"; before anyone has started, it is refused.
                var g = Company != null ? Company.Group : Group;
                if (g != null && g.Turns != null) return g.Confirm();
                ChimePlayer.Play(ChimePlayer.RefuseClip(), transform.position, 0.5f);
                return false;
            }
            if (Flow.Current == PalaceFlow.Phase.Placed)
            {
                ChimePlayer.Play(ChimePlayer.RefuseClip(), transform.position, 0.5f);
                Say("[Palace] pick a reason first");
                return false;
            }
            if (!Flow.CanSave) return false;
            var kept = Flow.Kind == PalaceFlow.Mode.Card ? Cards != null && Cards.Confirm() : Court != null && Court.Confirm();
            if (!kept) return false;
            Flow.Save();
            Record.SetPalace(new JourneyRecord.PalaceChoice
            {
                Object = Flow.Piece.ToLowerInvariant(), YawDeg = Flow.YawDeg, Mode = Flow.ModeName, Reason = Flow.Reason,
            });
            ShowChips(false);
            ConfirmInput.Drop(this);
            Say("[Palace] saved: palace{object " + Flow.Piece.ToLowerInvariant() + ", yaw " + Flow.YawDeg + ", mode " + Flow.ModeName + ", reason \"" + Flow.Reason + "\"}");
            Saved?.Invoke(Flow);
            return true;
        }

        public bool Redo() => Flow.Kind == PalaceFlow.Mode.Card ? Cards != null && Cards.Redo() : Court != null && Court.Redo();

        // ---- the court light ---------------------------------------------------------------

        void BuildCourtLight()
        {
            if (Court == null || Court.Slots.Count == 0) return;
            var go = new GameObject("Court Light");
            go.transform.SetParent(transform, false);
            go.transform.position = Court.Slots[0].position + Vector3.up * 0.55f;
            CourtLight = go.AddComponent<Light>();
            CourtLight.type = LightType.Point;
            CourtLight.color = new Color(1f, 0.82f, 0.55f);
            CourtLight.range = CourtLightRange;
            CourtLight.shadows = LightShadows.None;
            CourtLight.intensity = 0f;

            var glow = GameObject.CreatePrimitive(PrimitiveType.Quad);
            glow.name = "Court Glow";
            DestroyImmediate(glow.GetComponent<Collider>());
            glow.transform.SetParent(transform, false);
            glow.transform.position = Court.Slots[0].position + Vector3.up * 0.004f;
            glow.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            glow.transform.localScale = Vector3.one * 0.7f;
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 2f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_ZWrite", 0f); m.SetFloat("_Cull", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            m.SetTexture("_BaseMap", RadialTexture());
            m.SetColor("_BaseColor", Color.black);
            CourtGlow = glow.GetComponent<Renderer>();
            CourtGlow.sharedMaterial = m;
        }

        static Texture2D RadialTexture()
        {
            const int n = 64;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "court-glow" };
            var px = new Color[n * n];
            for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
            {
                var d = new Vector2(x + 0.5f - n / 2f, y + 0.5f - n / 2f).magnitude / (n / 2f);
                var a = Mathf.Clamp01(1f - d); a *= a;
                px[y * n + x] = new Color(a, a, a, a);
            }
            t.SetPixels(px); t.Apply();
            return t;
        }

        void Update()
        {
            var on = Flow.Current != PalaceFlow.Phase.Choosing && Flow.Kind == PalaceFlow.Mode.Miniature;
            _light = Mathf.MoveTowards(_light, on ? 1f : 0f, Time.deltaTime / 0.6f);
            var k = Mathf.SmoothStep(0f, 1f, _light);
            if (CourtLight != null) CourtLight.intensity = CourtLightIntensity * k;
            if (CourtGlow != null) CourtGlow.sharedMaterial.SetColor("_BaseColor", new Color(1f, 0.72f, 0.38f) * (0.85f * k));
            if (_turnsPending && Company != null && Company.Current == CompanyStage.Phase.Done) StartTurns();
            KeepReasonsUp();
        }

        void LateUpdate()
        {
            // After whatever station or group took A this frame: while a Palace choice is open, A is ours.
            if (!_wantFocus) return;
            _wantFocus = false;
            if (Flow.Current == PalaceFlow.Phase.Placed || Flow.Current == PalaceFlow.Phase.Ready) ConfirmInput.Take(this);
        }

        void Say(string line) { Debug.Log(line); Note?.Invoke(line); }
    }
}
