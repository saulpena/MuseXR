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

        const float ChipW = 0.5f, ChipH = 0.15f, ChipStep = 0.56f;
        // At the court the chips are a narrow row over the piece, above the confirm strip: her 4.3
        // prompt - the options, then A/B - centred on what was placed (Saul, headset test: "the UI is
        // all over the place, not centred where I'm placing it").
        const float CourtW = 0.3f, CourtH = 0.11f, CourtStep = 0.32f;
        // Type ceilings in TMP world units (0.6 ~ 16 mm cap height). Measured: at the card chips' 0.9
        // the court chips' text spilled off the bottom.
        const float CourtFontMax = 0.5f, CourtFontMin = 0.25f;
        // Not-chosen chips stay legible (dark ink on a paler stone), so they read "not chosen, still
        // pickable" rather than disabled (blind review: grey on grey at ~2:1).
        static readonly Color ChipPaper = new Color(0.96f, 0.94f, 0.89f), ChipGold = new Color(0.86f, 0.66f, 0.26f),
                              ChipDim = new Color(0.78f, 0.76f, 0.72f), InkDark = new Color(0.2f, 0.16f, 0.12f);
        readonly List<Material> _chipMats = new List<Material>();
        readonly List<Pointable> _chips = new List<Pointable>();
        readonly List<TMPro.TextMeshPro> _chipText = new List<TMPro.TextMeshPro>();
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

        void StartTurns(CompanionGroup group)
        {
            _turnsPending = false;
            var piece = Flow.Piece;
            group.LineFor = id => LineFor(id, piece);
            group.TurnsFinished -= OnTurnsFinished;
            group.TurnsFinished += OnTurnsFinished;
            MasterVoice.Follow(group);   // voiced, each line as its turn starts (silent before, live run 4 Oct)
            group.BeginTurns();   // the group takes A while they speak: A is "next"
        }

        public static string CannedLine(string master, string piece)
        {
            var crane = !string.Equals(piece, "Turtle", StringComparison.OrdinalIgnoreCase);
            switch (master)
            {
                case Masters.Monet: return crane ? "Bronze holds the hour. By dusk this crane will be a darker thing than now."
                                                 : "Its shell has caught this same light for a thousand evenings.";
                case Masters.VanGogh: return crane ? "You turned it until it faced you. What were you looking for?"
                                                   : "You chose the slow one. You know what it costs to keep going.";
                case Masters.Socrates: return crane ? "It still looks up." : "What does it carry that you would carry too?";
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
            for (var i = 0; i < 3; i++)
            {
                var chip = GameObject.CreatePrimitive(PrimitiveType.Quad);
                chip.name = "Reason " + i;
                DestroyImmediate(chip.GetComponent<Collider>());
                chip.transform.SetParent(_chipRoot, false);
                chip.transform.localPosition = new Vector3((i - 1) * ChipStep, 0f, 0f);
                chip.transform.localScale = new Vector3(ChipW, ChipH, 1f);
                var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                m.SetColor("_BaseColor", ChipPaper);
                chip.GetComponent<Renderer>().sharedMaterial = m;
                _chipMats.Add(m);
                var t = new GameObject("Text").AddComponent<TMPro.TextMeshPro>();
                t.transform.SetParent(chip.transform, false);
                t.transform.localPosition = new Vector3(0f, 0f, -0.002f);
                t.transform.localScale = Vector3.one;   // set per layout in Lay()
                // Shrinks to fit rather than spilling off the chip (blind review: the longest wrapped
                // to three lines over a one-line chip).
                t.enableAutoSizing = true;
                t.fontSizeMax = 0.024f * (0.6f / 0.016f);
                t.fontSizeMin = 0.012f * (0.6f / 0.016f);
                t.alignment = TMPro.TextAlignmentOptions.Center;
                t.enableWordWrapping = true;
                t.color = new Color(0.2f, 0.16f, 0.12f);
                var p = Pointable.Make(chip, "reason-" + i);
                HoverTint.Bind(p, chip.GetComponent<Renderer>());
                var index = i;
                p.Selected += (_, pointer) => PickChip(index, pointer);
                _chips.Add(p);
                _chipText.Add(t);
            }
            ShowChips(false);
        }

        void ShowChips(bool on)
        {
            Appear.Set(_chipRoot.gameObject, on, 0.3f);   // eased, never popped (Saul, 5 Oct)
            if (!on) return;
            // Beside whichever was chosen: under the cards for the fallback, before the court otherwise.
            if (Flow.Kind == PalaceFlow.Mode.Card && Cards != null && Cards.Cards.Length > 0)
            {
                var mid = Vector3.zero; foreach (var c in Cards.Cards) mid += c.position; mid /= Cards.Cards.Length;
                var at = mid - Vector3.up * 0.36f;
                var eye = Camera.main != null ? Camera.main.transform.position : at - Cards.Cards[0].forward;
                var away = at - eye; away.y = 0f;
                _chipRoot.SetPositionAndRotation(at, Quaternion.LookRotation(away.normalized, Vector3.up));
            }
            else
            {
                // Facing wherever the visitor stands NOW (they walked up to place it), not the spawn.
                var eye = Camera.main != null ? Camera.main.transform.position : _courtChipsAt - _courtChipsRot * Vector3.forward;
                var away = _courtChipsAt - eye; away.y = 0f;
                _chipRoot.SetPositionAndRotation(_courtChipsAt,
                    away.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(away.normalized, Vector3.up) : _courtChipsRot);
            }
            // Saul, 5 Oct: the reasons follow the visitor like the masters' card, at a reading size for 1.3 m.
            _chipRoot.localScale = Vector3.one * 0.6f;
            FollowVisitor.Attach(_chipRoot.gameObject, 1.3f, 0.15f);
            var reasons = PalaceFlow.ReasonsFor(Flow.Piece);
            for (var i = 0; i < _chipText.Count; i++)
            {
                _chipText[i].text = reasons[i];
                _chipText[i].fontStyle = TMPro.FontStyles.Normal;
                _chipText[i].color = InkDark;
                _chipMats[i].SetColor("_BaseColor", ChipPaper);
                Lay(i, false);
            }
        }

        /// <summary>Narrow chips behind the court; wider ones under the cards in the card fallback.</summary>
        bool AtCourt => Flow.Kind != PalaceFlow.Mode.Card;

        void Lay(int i, bool chosen)
        {
            float w = AtCourt ? CourtW : ChipW, h = AtCourt ? CourtH : ChipH;
            var at = new Vector3((i - 1) * (AtCourt ? CourtStep : ChipStep), 0f, 0f);
            if (chosen) at.z = -0.04f;   // steps towards the visitor
            _chips[i].transform.localPosition = at;
            _chips[i].transform.localScale = new Vector3(w, h, 1f) * (chosen ? 1.05f : 1f);
            _chipText[i].transform.localScale = new Vector3(1f / w, 1f / h, 1f);
            _chipText[i].rectTransform.sizeDelta = new Vector2(w - 0.03f, h - 0.015f);
            _chipText[i].fontSizeMax = AtCourt ? CourtFontMax : 0.024f * (0.6f / 0.016f);
            _chipText[i].fontSizeMin = AtCourt ? CourtFontMin : 0.012f * (0.6f / 0.016f);
        }

        public bool PickChip(int index, Pointer pointer = null)
        {
            if (index < 0 || index >= _chipText.Count || Listening) return false;   // the reasons come after the companions
            if (!Flow.ChooseReason(PalaceFlow.ReasonsFor(Flow.Piece)[index])) return false;
            // The chosen chip turns solid gold, bold, with a "»", a little larger and nearer; the other
            // two dim. Shape and weight as well as colour, so the choice reads at a glance (Saul: "I can
            // keep clicking on them, I don't know if it's doing anything").
            var reasons = PalaceFlow.ReasonsFor(Flow.Piece);
            for (var i = 0; i < _chips.Count; i++)
            {
                var on = i == index;
                _chipText[i].text = (on ? "» " : "") + reasons[i];
                _chipText[i].fontStyle = on ? TMPro.FontStyles.Bold : TMPro.FontStyles.Normal;
                _chipText[i].color = on ? InkDark : new Color(0.28f, 0.25f, 0.22f);
                _chipMats[i].SetColor("_BaseColor", on ? ChipGold : ChipDim);
                Lay(i, on);
            }
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
