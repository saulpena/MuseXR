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
    ///   4 Saved     three reason chips appear; pick one (or speak one: <see cref="SpeakReason"/>).
    ///               A saves palace{object, yaw, reason, mode} to the journey record. A before a reason
    ///               is refused with a knock. B inside 3 s, or lifting the piece out, undoes all of it.
    ///
    /// The card fallback (two cards: point, flip, glow) runs the same steps with mode "card".
    /// </summary>
    public sealed class PalaceChapter : MonoBehaviour, IConfirmable
    {
        public PalaceFlow Flow { get; } = new PalaceFlow();
        public SlotStation Court { get; private set; }
        public CardChoiceStation Cards { get; private set; }
        public CompanyStage Company { get; private set; }
        public JourneyRecord Record { get; private set; }
        public IReadOnlyList<Pointable> Chips => _chips;
        public Light CourtLight { get; private set; }

        /// <summary>What a companion says about the kept piece. Canned until DialogueClient is wired here.</summary>
        public Func<string, string, string> LineFor = CannedLine;

        public event Action<string> Note;
        public event Action<PalaceFlow> Saved;

        public const float CourtLightIntensity = 2.6f, CourtLightRange = 0.9f;   // a pool on the court, not the room

        const float ChipW = 0.5f, ChipH = 0.15f, ChipStep = 0.56f;
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
                    s.Board.Choice.Retitle(Flow.Summary);
                    Respond(s.Pieces[e.Piece].Id);
                    break;
                case SlotCue.Undone:
                case SlotCue.Lifted:
                    Flow.Unplaced();
                    ShowChips(false);
                    Say("[Palace] taken back - choose again");
                    break;
            }
        }

        /// <summary>Step 3 and the start of 4: light, companions, chips, and A/B to this chapter.</summary>
        void Respond(string piece)
        {
            ShowChips(true);
            _wantFocus = true;
            Say("[Palace] " + piece + " placed at " + Flow.YawDeg + "°. Pick a reason, then A.");
            CallCompanions();
        }

        void CallCompanions()
        {
            if (Company == null) return;
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

        void StartTurns()
        {
            _turnsPending = false;
            var group = Company.Group;
            var piece = Flow.Piece;
            group.LineFor = id => LineFor(id, piece);
            group.BeginTurns();
            _wantFocus = true;   // the group took A for its turns; A here still means "keep"
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
                    cards.Logic.Choice.Retitle(Flow.Summary);
                    Respond(cards.Logic.Ids[i]);
                }
                else if (cards.Logic.FaceUp < 0)
                {
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
                m.SetColor("_BaseColor", new Color(0.96f, 0.94f, 0.89f));
                chip.GetComponent<Renderer>().sharedMaterial = m;
                var t = new GameObject("Text").AddComponent<TMPro.TextMeshPro>();
                t.transform.SetParent(chip.transform, false);
                t.transform.localPosition = new Vector3(0f, 0f, -0.002f);
                t.transform.localScale = new Vector3(1f / ChipW, 1f / ChipH, 1f);
                t.rectTransform.sizeDelta = new Vector2(ChipW - 0.04f, ChipH - 0.03f);
                // Shrinks to fit rather than spilling off the chip (blind review: the longest wrapped
                // to three lines over a one-line chip).
                t.enableAutoSizing = true;
                t.fontSizeMax = 0.024f * (0.6f / 0.016f);
                t.fontSizeMin = 0.012f * (0.6f / 0.016f);
                t.alignment = TMPro.TextAlignmentOptions.Center;
                t.enableWordWrapping = true;
                t.color = new Color(0.2f, 0.16f, 0.12f);
                var p = Pointable.Make(chip, "reason-" + i);
                var index = i;
                p.Selected += (_, pointer) => PickChip(index, pointer);
                _chips.Add(p);
                _chipText.Add(t);
            }
            ShowChips(false);
        }

        void ShowChips(bool on)
        {
            _chipRoot.gameObject.SetActive(on);
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
            else _chipRoot.SetPositionAndRotation(_courtChipsAt, _courtChipsRot);
            var reasons = PalaceFlow.ReasonsFor(Flow.Piece);
            for (var i = 0; i < _chipText.Count; i++)
            {
                _chipText[i].text = reasons[i];
                _chipText[i].fontStyle = TMPro.FontStyles.Normal;
                _chips[i].transform.localPosition = new Vector3((i - 1) * ChipStep, 0f, 0f);
                _chips[i].transform.localScale = new Vector3(ChipW, ChipH, 1f);
            }
        }

        public bool PickChip(int index, Pointer pointer = null)
        {
            if (index < 0 || index >= _chipText.Count) return false;
            if (!Flow.ChooseReason(PalaceFlow.ReasonsFor(Flow.Piece)[index])) return false;
            // The chosen chip gets a "»" mark, bold type, a larger size and steps forward: shape and
            // weight, not colour alone. The others stay as they were, so the choice reads at a glance.
            var reasons = PalaceFlow.ReasonsFor(Flow.Piece);
            for (var i = 0; i < _chips.Count; i++)
            {
                var on = i == index;
                _chipText[i].text = (on ? "» " : "") + reasons[i];
                _chipText[i].fontStyle = on ? TMPro.FontStyles.Bold : TMPro.FontStyles.Normal;
                _chips[i].transform.localPosition = new Vector3((i - 1) * ChipStep, 0f, on ? -0.06f : 0f);
                _chips[i].transform.localScale = new Vector3(ChipW, ChipH, 1f) * (on ? 1.15f : 1f);
            }
            pointer?.Source.Buzz(SlotRules.LightAmplitude * 1.5f, SlotRules.LightSeconds);
            Retitle();
            _wantFocus = true;
            Say("[Palace] reason: " + Flow.Reason + " - A keeps it");
            return true;
        }

        /// <summary>Hold-X speech, from whatever dictation the scene has.</summary>
        public bool SpeakReason(string transcript)
        {
            if (!Flow.SpeakReason(transcript)) return false;
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
        }

        void Update()
        {
            var on = Flow.Current != PalaceFlow.Phase.Choosing && Flow.Kind == PalaceFlow.Mode.Miniature;
            _light = Mathf.MoveTowards(_light, on ? 1f : 0f, Time.deltaTime / 0.6f);
            if (CourtLight != null) CourtLight.intensity = CourtLightIntensity * Mathf.SmoothStep(0f, 1f, _light);
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
