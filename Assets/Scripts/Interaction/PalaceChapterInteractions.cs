using System.Collections;
using System.Collections.Generic;
using MuseXR.Slots;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

namespace MuseXR.Interaction
{
    /// <summary>
    /// Brings her chapter A to life in the laid-out Palace (Tests/PalaceChapter.unity, built by
    /// ChapterLayout from her diagram A). It finds what the layout placed - "Prop crane", "Prop
    /// turtle", "Interaction Miniature court", "Mark monet / van_gogh / socrates" - and adds:
    ///
    ///   a teleport floor over the whole court (invisible), so the visitor can walk up to everything;
    ///   grip-grabbable miniatures that idle-turn, follow the hand and turn only with the stick;
    ///   the court as her 12 cm slot, with snap, bronze bell, haptics, float-home and the 3 s undo;
    ///   the companions answering in turn from where her diagram stands them, with subtitles;
    ///   once they have spoken, three reason chips over the court; A saves palace{object, yaw, reason, mode};
    ///   then her transition: the moon gate opens with the grotto beyond, and walking through it goes there.
    /// </summary>
    public sealed class PalaceChapterInteractions : MonoBehaviour
    {
        public PalaceChapter Chapter { get; private set; }
        public SlotStation Court { get; private set; }
        public CompanionGroup Companions { get; private set; }
        public ChoicePreview Preview { get; private set; }
        /// <summary>The "hear each first" card: beside the court at eye height, clear of the throne axis.</summary>
        public const float PreviewUp = 0.85f, PreviewAside = 2.1f;
        public MusePico.Dialogue.JourneyRecord Record { get; } = new MusePico.Dialogue.JourneyRecord();

        // The floor exists before the first physics frame: made after a yield, gravity had already
        // dropped the rig a hair below y 0 and it fell through for ever (Grotto run, 3 Oct 2026).
        void Awake() => TeleportFloor();

        IEnumerator Start()
        {
            yield return null;   // after the rig and the layout have woken
            var head = Camera.main != null ? Camera.main.transform : null;
            var spawn = head != null ? head.position : Vector3.zero;

            var crane = Find("Prop crane");
            var turtle = Find("Prop turtle");
            var courtT = Find("Interaction Miniature court");
            if (crane == null || turtle == null || courtT == null)
            {
                Debug.LogError("[Palace] layout objects missing: crane " + (crane != null) + ", turtle " + (turtle != null) + ", court " + (courtT != null));
                yield break;
            }

            RaiseCourt(courtT);

            // The court's slot sits on its surface; the station hangs off the court object.
            var slot = new GameObject("Court Slot").transform;
            slot.SetParent(courtT, false);
            var toViewer = spawn - courtT.position; toViewer.y = 0f; toViewer.Normalize();
            // The slot faces the visitor, so a piece set down facing them reads 0 degrees (facing away it
            // read "Crane · 178°" for a crane nobody had turned).
            slot.SetPositionAndRotation(courtT.position, Quaternion.LookRotation(toViewer, Vector3.up));

            var pieces = new[] { Holdable.Make(crane.gameObject, "Crane"), Holdable.Make(turtle.gameObject, "Turtle") };
            Court = SlotStation.Make(courtT.gameObject, global::MuseXR.Slots.Chapter.Palace, new[] { slot }, pieces);

            // The compass (musexr-bb, 3c2b476): the pieces, then the court, then the gate - before the
            // paintings (30). Done when the piece is set in the court.
            var targets = new[] { CompassTarget.Add(crane.gameObject, 21, "Crane: point at it to hear your companions"),
                                  CompassTarget.Add(turtle.gameObject, 21, "Turtle: point at it to hear your companions"),
                                  CompassTarget.Add(courtT.gameObject, 22, "The miniature court: set it here") };
            Court.Cue += (s, e) => { if (e.Cue == SlotCue.Placed) foreach (var t in targets) t.MarkDone(); };
            // The rule: every interactable object - click it or walk up to it, and a master speaks.
            // Her script's labels, so the companions talk about what the pieces are after.
            var craneTalk = InsightTarget.Add(crane.gameObject, "the bronze crane",
                "form after Foliate Dish with Crane and Deer (Yuan) and One Hundred Cranes by Shen Quan (Qing)", "palace-crane");
            var turtleTalk = InsightTarget.Add(turtle.gameObject, "the bronze turtle",
                "form after an inkstone shaped as a double-headed turtle (Han to Six Dynasties)", "palace-turtle");

            // Saul, 4 Oct: hear the companions on BOTH before being told to lift one. Until then the pieces
            // stay on their plinths and a reach for one only pulses the card.
            // Beside the plinths, not over them: on the axis it stood across the throne and its goddess.
            var side = Vector3.Cross(Vector3.up, toViewer).normalized;
            // On the open side: on the other it stood in front of a hanging scroll (capture, 4 Oct).
            var cardAt = courtT.position - side * PreviewAside + Vector3.up * PreviewUp;
            var cardFacing = cardAt - spawn;
            Preview = ChoicePreview.Make(transform, cardAt, cardFacing,
                "Stop 1  ·  the crane or the turtle",
                new[] { (craneTalk, "The crane"), (turtleTalk, "The turtle") },
                "Hold Grip to pick one up. Only one goes into the courtyard");
            Court.GrabGate = () => Preview.Ready;
            Court.Refused += _ => Preview.Nudge();
            Court.Cue += (s, e) => { if (e.Cue == SlotCue.Placed && Preview != null) Preview.Close(); };

            // The companions where her diagram stands them; they answer in turn, they do not walk.
            // Her diagram has three marks (named for the demo trio); the visitor's chosen companions stand
            // on them in speaking order (Masters.Company - the journey swaps the figures on the marks).
            var figures = new Dictionary<string, Transform>();
            var company = Masters.Company;
            for (var i = 0; i < Masters.DefaultTrio.Count && i < company.Count; i++)
            {
                var mark = Find("Mark " + Masters.DefaultTrio[i]);
                if (mark != null) figures[company[i]] = mark;
            }
            var groupGo = new GameObject("Companions");
            groupGo.transform.SetParent(transform, false);
            Companions = groupGo.AddComponent<CompanionGroup>();
            Companions.FollowVisitor = false;
            Companions.Crowd = true;   // Saul, 3 Oct: always a crowd beside the visitor, never in front
            Companions.Head = head;
            var order = new List<string>(); foreach (var id in company) if (figures.ContainsKey(id)) order.Add(id);
            Companions.Set(order, figures);
            var subtitles = System.Type.GetType("MuseXR.UI.SubtitleRig, MuseXR.UI.Interaction");
            if (subtitles != null) groupGo.AddComponent(subtitles);

            // Reason chips centred on the court: a row standing just behind the placed piece, so the
            // choice sits on what was chosen (Saul, headset test: "not centred where I'm placing it").
            // PalaceChapter turns them to face wherever the visitor stands when they appear.
            var chipsAt = courtT.position - toViewer * ChipsBehind + Vector3.up * ChipsUp;
            Chapter = PalaceChapter.Make(courtT.gameObject, Court, null, null, Record, chipsAt,
                                         Quaternion.LookRotation(-toViewer, Vector3.up));
            Chapter.Group = Companions;
            Chapter.Saved += _ => { Debug.Log("[Record] " + Record.SummaryJson()); if (Record.Palace != null) JourneyMemory.Record.SetPalace(Record.Palace); OpenMoonGate(); };
            ConfirmInput.Pressed += OnPressed;
            _courtTop = courtT.position;
        }

        void OnPressed(string button, string target, bool ok) =>
            Debug.Log("[Palace] " + button + " -> " + target + (ok ? "" : " (nothing to do)"));

        void OnDestroy() => ConfirmInput.Pressed -= OnPressed;

        /// <summary>Testing a journey: the chapter's interaction counts as done, and the moon gate opens.</summary>
        public void CompleteChapter() => OpenMoonGate();

        // ---- the moon gate: her transition to chapter B ----------------------------------------

        [Tooltip("The Moon Gate prefab instance standing in the capture's own moon gate, next world: the grotto.")]
        public MuseXR.Worlds.MoonGate moonGate;

        /// <summary>
        /// Where the scene's Moon Gate stands: the capture's own moon gate, on the floor at the
        /// threshold, in the palace's frame. Measured 3 Oct 2026 by triangulating two Editor captures
        /// of the right-hand wall (eye 1.6 m, from (-2, 0.6) and (-2, 3.0), both facing -X): both put
        /// the threshold 5.5 m deep at (-7.5, 0.0), opening along -X. The layout's "Exit Moon gate"
        /// marker, placed from the schematic, stands 3.5 m away in front of a column. The collider
        /// cannot locate it: above 1.4 m it reports a sloping surface across the opening.
        /// </summary>
        public static readonly Vector3 MoonGateFloor = new Vector3(-7.5f, 0f, 0f);

        public MuseXR.Worlds.SplatPortalDoor Door => moonGate != null ? moonGate.Door : null;

        Vector3 _courtTop;
        TMPro.TextMeshPro _afterKeep;

        /// <summary>
        /// After A keeps the choice: her transition, "through the moon gate the courtyard vista
        /// becomes grotto cliffs". The palace's layout goes with the palace; the companions come along.
        /// </summary>
        void OpenMoonGate()
        {
            if (moonGate == null) { Debug.LogError("[Palace] no Moon Gate in the scene"); return; }
            if (Vector3.Distance(moonGate.transform.position, MoonGateFloor) > 0.5f)
                Debug.LogWarning("[Palace] the Moon Gate stands at " + moonGate.transform.position + ", not in the capture's gate at " + MoonGateFloor);
            var here = FindAnyObjectByType<GaussianSplatting.Runtime.GaussianSplatRenderer>();

            // The companions leave the palace's layout before it is destroyed with the old world.
            foreach (var f in Companions.Figures.Values) f.SetParent(Companions.transform, true);
            var props = new List<GameObject>();
            foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
                if (t.name.StartsWith("Exit Moon gate") && t.parent != null) props.Add(t.parent.gameObject);   // the layout root
            foreach (var n in new[] { "Court Table", "Interaction Visuals" }) { var g = GameObject.Find(n); if (g != null) props.Add(g); }
            if (!moonGate.Open(here, props, null, showNow: true)) return;
            var gateTarget = CompassTarget.Add(moonGate.gameObject, 29, "The moon gate: walk through it");
            moonGate.Crossed += () =>
            {
                gateTarget.MarkDone();
                if (_afterKeep != null) Destroy(_afterKeep.gameObject);
                Companions.PlaceAll();   // round the visitor in the grotto
                Debug.Log("[Palace] through the moon gate: in the grotto");
            };

            // The chapter is over: the companions walk with the visitor again (§3.4), re-marked off the
            // path at the next teleport or turn. Left on her diagram's V mark, Van Gogh stood in the
            // capture's real gate, between the court and the door (Editor capture, 3 Oct 2026).
            Companions.StopTurns();
            Companions.FollowVisitor = true;

            AfterKeep("Kept.  The moon gate is open, on your right.\nWalk through it.");
        }

        /// <summary>One line over the court once the choice is kept: what to do next.</summary>
        void AfterKeep(string line)
        {
            var eye = Camera.main != null ? Camera.main.transform.position : _courtTop + Vector3.back;
            var at = _courtTop + Vector3.up * 0.75f;
            var away = at - eye; away.y = 0f;
            _afterKeep = new GameObject("After Keep").AddComponent<TMPro.TextMeshPro>();
            _afterKeep.transform.SetParent(transform, false);
            _afterKeep.transform.SetPositionAndRotation(at, Quaternion.LookRotation(away.normalized, Vector3.up));
            _afterKeep.rectTransform.sizeDelta = new Vector2(1.0f, 0.2f);
            _afterKeep.fontSize = 0.36f;   // ~10 mm cap height: read from the court, under a metre away
            _afterKeep.alignment = TMPro.TextAlignmentOptions.Center;
            _afterKeep.color = new Color(1f, 0.95f, 0.85f);
            _afterKeep.outlineWidth = 0.2f;
            _afterKeep.outlineColor = new Color32(40, 28, 16, 255);
            _afterKeep.text = line;
        }

        /// <summary>An invisible floor at the layout's ground, teleportable everywhere in the court.</summary>
        void TeleportFloor()
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Teleport Floor";
            floor.transform.SetParent(transform, false);
            floor.transform.position = Vector3.zero;
            floor.transform.localScale = new Vector3(4f, 1f, 4f);   // 40 x 40 m
            floor.GetComponent<Renderer>().enabled = false;          // the world is the splat; this only catches teleports
            floor.SetActive(false);                                  // XRI registers an area once, with its settings
            var area = floor.AddComponent<TeleportationArea>();
            // The rig's teleport rays select ONLY on the "Teleport" interaction layer (bit 31). Measured in
            // Saul's headset test: an area on the default layer is never a valid target, so teleport did
            // nothing at all.
            area.interactionLayers = TeleportLayer;
            area.filterSelectionByHitNormal = true;
            floor.SetActive(true);
        }

        /// <summary>
        /// The chip row's centre above the court's top: just over the confirm strip (which stands
        /// 0.5 m up, ~0.16 m tall), so it reads top to bottom as her 4.3 prompt - options, then A/B,
        /// then the piece. Measured: a row behind the court and lower was hidden behind the strip.
        /// </summary>
        public const float ChipsBehind = 0f, ChipsUp = 0.7f;

        /// <summary>The interaction layer the rig's teleport interactors select on.</summary>
        public const int TeleportLayer = 1 << 31;

        /// <summary>The court's top, at hand height: her rule puts what you handle at 0.8-1.3 m.</summary>
        public const float CourtHeight = 0.85f;

        /// <summary>
        /// Her "miniature court" is a small model of the palace court where the kept piece is set. The
        /// layout marks it on the floor; it stands on a pale-stone table at hand height instead.
        /// </summary>
        void RaiseCourt(Transform court)
        {
            var floorAt = court.position;
            var table = GameObject.CreatePrimitive(PrimitiveType.Cube);
            table.name = "Court Table";
            table.transform.SetParent(transform, false);
            table.transform.position = new Vector3(floorAt.x, CourtHeight * 0.5f, floorAt.z);
            table.transform.localScale = new Vector3(0.7f, CourtHeight, 0.5f);
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", new Color(0.86f, 0.83f, 0.77f));   // her pale-stone interaction zone
            table.GetComponent<Renderer>().sharedMaterial = m;
            court.position = new Vector3(floorAt.x, CourtHeight + 0.005f, floorAt.z);
        }

        static Transform Find(string name)
        {
            foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None)) if (t.name == name) return t;
            return null;
        }
    }
}
