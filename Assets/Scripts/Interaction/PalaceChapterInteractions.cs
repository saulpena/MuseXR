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
    ///   three reason chips beside the court; A saves palace{object, yaw, reason, mode};
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

            // The court's slot sits on its surface; the station hangs off the court object.
            var slot = new GameObject("Court Slot").transform;
            slot.SetParent(courtT, false);
            var toViewer = spawn - courtT.position; toViewer.y = 0f; toViewer.Normalize();
            slot.SetPositionAndRotation(courtT.position, Quaternion.LookRotation(-toViewer, Vector3.up));

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

            // Reason chips beside the court at hand height, on the side away from Socrates (blind review:
            // on the other side they crossed his body), never between the eye and the court on the floor.
            var right = Vector3.Cross(Vector3.up, -toViewer).normalized;
            var side = figures.TryGetValue(Masters.Socrates, out var soc) && Vector3.Dot(soc.position - courtT.position, right) > 0f ? -1f : 1f;
            var chipsAt = courtT.position + right * (1.0f * side) + toViewer * 0.3f + Vector3.up * 0.95f;
            Chapter = PalaceChapter.Make(courtT.gameObject, Court, null, null, Record, chipsAt,
                                         Quaternion.LookRotation((chipsAt - spawn).WithY0().normalized, Vector3.up));
            Chapter.Group = Companions;

            // The board to the visitor's left of the court, readable from the spawn.
            Board = EventBoard.Make(transform, courtT.position - right * (1.6f * side) + toViewer * 0.6f + Vector3.up * 1.5f, spawn,
                                    "PALACE · what just happened");
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
            floor.AddComponent<TeleportationArea>();
        }

        static Transform Find(string name)
        {
            foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None)) if (t.name == name) return t;
            return null;
        }
    }

    static class VecExt
    {
        public static Vector3 WithY0(this Vector3 v) { v.y = 0f; return v; }
    }
}
