using System;
using MusePico.Dialogue;
using MuseXR.Slots;
using UnityEngine;

namespace MuseXR.Interaction
{
    /// <summary>
    /// Her chapter B, Grotto · Hall of Time, end to end in a scene (storyboard B, chatplan §2.5):
    ///
    ///   1 Before    the lamp on its brass stand, front-left ("Grip to take the lamp").
    ///   2 During    carried over the near relief, the lamp's light (relief only, never the splat)
    ///               reveals the carving's depth (LampLight).
    ///   3 Feedback  set in a socket with the stone chime: "detail" before the relief keeps it lit;
    ///               "whole" on the rail post rims the distant Buddha in gold. The companions voice
    ///               the two ways of seeing, one at a time (A is "next"); nothing else is on show.
    ///   4 Saved     then "Keep this moment? Lamp · Whole"; A saves grotto{lampSlot, exhibitId}.
    ///               Undo is hers: lift the lamp out and set it in the other socket.
    /// </summary>
    public sealed class GrottoChapter : MonoBehaviour, IConfirmable
    {
        public GrottoFlow Flow { get; } = new GrottoFlow();
        public SlotStation Sockets { get; private set; }
        public CompanionGroup Group { get; private set; }
        public JourneyRecord Record { get; private set; }
        /// <summary>The gold rim round the distant Buddha (additive), shown while the lamp is in "whole".</summary>
        public Renderer BuddhaRim { get; private set; }
        public bool Listening { get; private set; }

        public event Action<GrottoFlow> Saved;

        public const float RimSeconds = 1.2f;
        public static readonly Color RimGold = new Color(1f, 0.74f, 0.32f);

        float _rim;
        bool _wantFocus;

        public static GrottoChapter Make(GameObject host, SlotStation sockets, CompanionGroup group, JourneyRecord record, Renderer buddhaRim)
        {
            var c = host.AddComponent<GrottoChapter>();
            c.Sockets = sockets; c.Group = group; c.Record = record ?? new JourneyRecord(); c.BuddhaRim = buddhaRim;
            if (sockets != null) sockets.Cue += c.OnCue;
            if (buddhaRim != null) buddhaRim.sharedMaterial.SetColor("_BaseColor", Color.black);
            return c;
        }

        void OnCue(SlotStation s, SlotEvent e)
        {
            switch (e.Cue)
            {
                case SlotCue.Placed:
                    if (!Flow.Placed(s.SlotNames[e.Slot])) return;
                    Debug.Log("[Grotto] lamp in " + Flow.LampSlot);
                    Respond();
                    break;
                case SlotCue.Undone:
                case SlotCue.Lifted:
                    StopListening();
                    Flow.Unplaced();
                    Debug.Log("[Grotto] lamp lifted - set it in either socket");
                    break;
            }
        }

        /// <summary>Step 3: the companions voice the two ways of seeing; the strip waits for them.</summary>
        void Respond()
        {
            Listening = true;
            _wantFocus = true;
            if (Sockets != null) Sockets.HideStrip = true;
            if (Group == null || Group.Ids.Count == 0) { AskToKeep(); return; }
            var slot = Flow.LampSlot;
            // Speaking order depends on the socket; Set clears the group's own table, so copy it first.
            var figures = new System.Collections.Generic.Dictionary<string, Transform>();
            foreach (var kv in Group.Figures) figures[kv.Key] = kv.Value;
            var present = new System.Collections.Generic.List<string>();
            foreach (var id in GrottoFlow.Order(slot)) if (figures.ContainsKey(id)) present.Add(id);
            Group.Set(present, figures);
            Group.LineFor = id => GrottoFlow.Line(id, slot);
            Group.TurnsFinished -= OnTurnsFinished;
            Group.TurnsFinished += OnTurnsFinished;
            Group.BeginTurns();
        }

        void OnTurnsFinished() { if (Listening && Flow.Current == GrottoFlow.Phase.Placed) AskToKeep(); }

        void AskToKeep()
        {
            Listening = false;
            if (Sockets != null) Sockets.HideStrip = false;
            _wantFocus = true;
            Debug.Log("[Grotto] A keeps the lamp in " + Flow.LampSlot);
        }

        void StopListening()
        {
            Listening = false;
            if (Sockets != null) Sockets.HideStrip = false;
            Group?.StopTurns();
        }

        // ---- A and B -----------------------------------------------------------------------

        public bool Confirm()
        {
            if (Listening)
            {
                if (Group != null && Group.Turns != null) return Group.Confirm();   // A is "next" while they speak
                ChimePlayer.Play(ChimePlayer.RefuseClip(), transform.position, 0.5f);
                return false;
            }
            if (!Flow.CanSave || Sockets == null || !Sockets.Confirm()) return false;
            Flow.Save();
            Record.SetGrotto(new JourneyRecord.GrottoChoice { LampSlot = Flow.LampSlot, ExhibitId = Flow.ExhibitId });
            ConfirmInput.Drop(this);
            Debug.Log("[Grotto] saved: grotto{lampSlot " + Flow.LampSlot + ", exhibitId " + Flow.ExhibitId + "}");
            Saved?.Invoke(Flow);
            return true;
        }

        public bool Redo() => Sockets != null && Sockets.Redo();

        void Update()
        {
            // Shown while the lamp is in "whole" and the choice is open; after keeping it fades, since
            // splats write no depth and from the arch the ring showed through the alcove wall.
            var on = Flow.Current == GrottoFlow.Phase.Placed && Flow.LampSlot == GrottoFlow.Whole;
            _rim = Mathf.MoveTowards(_rim, on ? 1f : 0f, Time.deltaTime / RimSeconds);
            if (BuddhaRim != null) BuddhaRim.sharedMaterial.SetColor("_BaseColor", RimGold * Mathf.SmoothStep(0f, 1f, _rim));
        }

        void LateUpdate()
        {
            // While a choice is open (speaking or waiting to keep), A comes here first.
            if (!_wantFocus) return;
            _wantFocus = false;
            if (Flow.Current == GrottoFlow.Phase.Placed) ConfirmInput.Take(this);
        }
    }
}
