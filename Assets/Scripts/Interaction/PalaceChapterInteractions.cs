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
    ///   three reason chips in a row behind the placed piece; A saves palace{object, yaw, reason, mode};
    ///   an event board so the tester sees every press and save.
    /// </summary>
    public sealed class PalaceChapterInteractions : MonoBehaviour
    {
        public PalaceChapter Chapter { get; private set; }
        public SlotStation Court { get; private set; }
        public CompanionGroup Companions { get; private set; }
        public EventBoard Board { get; private set; }
        public MusePico.Dialogue.JourneyRecord Record { get; } = new MusePico.Dialogue.JourneyRecord();

        IEnumerator Start()
        {
            yield return null;   // after the rig and the layout have woken
            var head = Camera.main != null ? Camera.main.transform : null;
            var spawn = head != null ? head.position : Vector3.zero;

            TeleportFloor();

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

            // The companions where her diagram stands them; they answer in turn, they do not walk.
            var figures = new Dictionary<string, Transform>();
            foreach (var id in Masters.DefaultTrio)
            {
                var mark = Find("Mark " + id);
                if (mark != null) figures[id] = mark;
            }
            var groupGo = new GameObject("Companions");
            groupGo.transform.SetParent(transform, false);
            Companions = groupGo.AddComponent<CompanionGroup>();
            Companions.FollowVisitor = false;
            Companions.Head = head;
            var order = new List<string>(); foreach (var id in Masters.DefaultTrio) if (figures.ContainsKey(id)) order.Add(id);
            Companions.Set(order, figures);
            var subtitles = System.Type.GetType("MuseXR.UI.SubtitleRig, MuseXR.UI.Interaction");
            if (subtitles != null) groupGo.AddComponent(subtitles);

            // Reason chips centred on the court: a row standing just behind the placed piece, so the
            // choice sits on what was chosen (Saul, headset test: "not centred where I'm placing it").
            // PalaceChapter turns them to face wherever the visitor stands when they appear.
            var right = Vector3.Cross(Vector3.up, -toViewer).normalized;
            var side = figures.TryGetValue(Masters.Socrates, out var soc) && Vector3.Dot(soc.position - courtT.position, right) > 0f ? -1f : 1f;
            var chipsAt = courtT.position - toViewer * ChipsBehind + Vector3.up * ChipsUp;
            Chapter = PalaceChapter.Make(courtT.gameObject, Court, null, null, Record, chipsAt,
                                         Quaternion.LookRotation(-toViewer, Vector3.up));
            Chapter.Group = Companions;

            // A small board standing beside the court, on the side away from Socrates: in view from the
            // entry and from the court, below every master's head. (At 80 degrees off to the side it
            // made the visitor look around for it.)
            var boardAt = new Vector3(courtT.position.x, 0f, courtT.position.z) + right * (BoardAside * side)
                          - toViewer * BoardBehind + Vector3.up * 1.05f;
            Board = EventBoard.Make(transform, boardAt, spawn, "PALACE · what just happened");
            Board.transform.localScale = Vector3.one * 0.4f;
            Chapter.Note += Board.Note;
            Chapter.Saved += _ => Board.Note("[Record] " + Record.SummaryJson());
            Court.Cue += (s, e) => { if (e.Cue == SlotCue.Aligned) Board.Note("[Court] aligned - let go to place"); };
            Companions.LineStarted += (id, line) => Board.Note(Masters.Name(id) + ": " + line);
            ConfirmInput.Pressed += OnPressed;
            Board.Note("Grip a miniature (reach, or point and grip). Stick turns it. Bring it to the court.");
        }

        void OnPressed(string button, string target, bool ok)
        {
            if (Board != null) Board.Note("[" + button + "] -> " + target + (ok ? "" : " (nothing to do)"));
        }

        void OnDestroy() => ConfirmInput.Pressed -= OnPressed;

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
        public const float ChipsBehind = 0f, ChipsUp = 0.68f;
        /// <summary>The event board's distance to the side of the court.</summary>
        public const float BoardAside = 1.5f, BoardBehind = 0.6f;

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
