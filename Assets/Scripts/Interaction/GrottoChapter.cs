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
        /// <summary>The rim's level while the lamp is held aligned over "whole"; the sockets are built detail then whole.</summary>
        public const float PreviewLevel = 0.4f;
        public const int WholeSlot = 1;
        public static readonly Color RimGold = new Color(1f, 0.74f, 0.32f);

        float _rim;
        bool _wantFocus;

        public static GrottoChapter Make(GameObject host, SlotStation sockets, CompanionGroup group, JourneyRecord record, Renderer buddhaRim)
        {
            var c = host.AddComponent<GrottoChapter>();
            c.Sockets = sockets; c.Group = group; c.Record = record ?? new JourneyRecord(); c.BuddhaRim = buddhaRim;
            if (sockets != null) { sockets.Cue += c.OnCue; sockets.Confirmed += (_, piece, slot, yaw) => c.Keep(); }
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

        int _asked;

        /// <summary>
        /// Step 3: the companions voice the two ways of seeing - live, each through their own lens, answering this
        /// visitor's choice (Saul, 5 Oct: genuine responses, no placeholders); her lines only when there is no live
        /// dialogue. Every companion the visitor brought speaks, not only the default three. The strip waits.
        /// </summary>
        async void Respond()
        {
            Listening = true;
            _wantFocus = true;
            if (Sockets != null) Sockets.HideStrip = true;
            if (Group == null || Group.Ids.Count == 0) { AskToKeep(); return; }
            var slot = Flow.LampSlot;
            var token = ++_asked;
            // Speaking order depends on the socket; Set clears the group's own table, so copy it first.
            var figures = new System.Collections.Generic.Dictionary<string, Transform>();
            foreach (var kv in Group.Figures) figures[kv.Key] = kv.Value;
            var present = GrottoFlow.Order(slot, figures.Keys);
            DialogueContext.Set(slot == GrottoFlow.Whole ? "You set the lamp to see the whole" : "You set the lamp to see the detail");
            System.Collections.Generic.Dictionary<string, string> live = null;
            try { live = await MasterInsights.Ensure().AskMasters(ReactionQuestion(slot), present, "the lamp set for the " + slot, "a lamp in a cave temple: a stone relief close by, a giant cliff Buddha far off"); }
            catch (Exception ex) { Debug.LogWarning("[Grotto] live reactions: " + ex.Message); }
            if (this == null || token != _asked || Group == null || Flow.LampSlot != slot || Flow.Current != GrottoFlow.Phase.Placed) return;
            Group.Set(present, figures);
            Group.LineFor = id => live != null && live.TryGetValue(id, out var l) ? l : GrottoFlow.Line(id, slot);
            Group.TurnsFinished -= OnTurnsFinished;
            Group.TurnsFinished += OnTurnsFinished;
            MasterVoice.Follow(Group);   // voiced, each line as its turn starts (silent before, live run 4 Oct)
            Group.BeginTurns();
        }

        /// <summary>What the masters are asked: the room's question, the visitor's way of seeing, and what they came in with.</summary>
        static string ReactionQuestion(string slot)
        {
            var asked = JourneyMemory.Record != null ? JourneyMemory.Record.Question : "";
            var way = slot == GrottoFlow.Whole
                ? "they set the lamp on the railing post, to look at the whole: the giant Buddha in the cliff, far off"
                : "they set the lamp before the carved relief, to look at the detail: the stone close up, the carver's cuts";
            return "In the Grotto, the Hall of Time, the room asks: 'Facing things that outlast me, how do I see myself?' "
                 + "Given a lamp, " + way + ". "
                 + (string.IsNullOrWhiteSpace(asked) ? "" : "They came into the museum asking: \"" + asked.Trim() + "\". ")
                 + "Respond to that choice, to them, in one or two short sentences, under 35 words. No numbers, dates or catalogue details.";
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
            if (!Flow.CanSave || Sockets == null) return false;
            return Sockets.Confirm();   // the station's Confirmed event keeps it (Keep)
        }

        /// <summary>
        /// The choice is kept, by whichever route confirmed it: A with this chapter in focus, A with the
        /// station in focus, or the strip's Confirm pressed with the pointer. (Saul's headset run reached
        /// "A keeps the lamp" and never saved - so the arch never rose - because only the first route
        /// saved the chapter.)
        /// </summary>
        void Keep()
        {
            if (Flow.Current != GrottoFlow.Phase.Placed) return;
            StopListening();
            Flow.Save();
            Record.SetGrotto(new JourneyRecord.GrottoChoice { LampSlot = Flow.LampSlot, ExhibitId = Flow.ExhibitId });
            ConfirmInput.Drop(this);
            Debug.Log("[Grotto] saved: grotto{lampSlot " + Flow.LampSlot + ", exhibitId " + Flow.ExhibitId + "}");
            Saved?.Invoke(Flow);
        }

        public bool Redo() => Sockets != null && Sockets.Redo();

        void Update()
        {
            // Shown while the lamp is in "whole" and the choice is open; after keeping it fades, since
            // splats write no depth and from the arch the ring showed through the alcove wall.
            var on = Flow.Current == GrottoFlow.Phase.Placed && Flow.LampSlot == GrottoFlow.Whole;
            // Held aligned over "whole" (before letting go), a faint preview, as the detail side shows the lamp's light
            // on the stele while it is held (Saul, 5 Oct: the two sides should behave alike).
            var preview = !on && Flow.Current != GrottoFlow.Phase.Saved && Sockets != null && Sockets.Board != null && Sockets.Board.AlignedSlot == WholeSlot;
            _rim = Mathf.MoveTowards(_rim, on ? 1f : preview ? PreviewLevel : 0f, Time.deltaTime / RimSeconds);
            if (BuddhaRim != null) BuddhaRim.sharedMaterial.SetColor("_BaseColor", RimGold * Mathf.SmoothStep(0f, 1f, _rim));
        }

        void LateUpdate()
        {
            // No soft-lock: while the lamp waits to be kept and nothing else holds A, A is ours (as in the Palace).
            if (!Listening && Flow.Current == GrottoFlow.Phase.Placed && ConfirmInput.Focus == null) ConfirmInput.Take(this);
            // While a choice is open (speaking or waiting to keep), A comes here first.
            if (!_wantFocus) return;
            _wantFocus = false;
            if (Flow.Current == GrottoFlow.Phase.Placed) ConfirmInput.Take(this);
        }
    }
}
