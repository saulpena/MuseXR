using System;
using System.Collections.Generic;

namespace MuseXR.Slots
{
    /// <summary>
    /// Her chapter B, Grotto · Hall of Time, as one flow (storyboard B, chatplan §2.5):
    ///
    ///   Choosing   the lamp sits on its brass stand ("Grip to take the lamp"); carrying it over the
    ///              near relief reveals its depth.
    ///   Placed     set in the "detail" socket (before the relief) or the "whole" socket (the rail
    ///              post): stone chime; detail lights the relief, whole rims the distant Buddha in
    ///              gold; the companions voice the two ways of seeing. Then "Keep this moment?".
    ///   Saved      A: grotto{lampSlot, exhibitId}.
    ///
    /// Undo is hers: lift the lamp out and set it in the other socket (Placed again, other slot).
    /// Pure: the scene reports placements; this says what is chosen and what the companions say.
    /// </summary>
    public sealed class GrottoFlow
    {
        public enum Phase { Choosing, Placed, Saved }

        public const string Detail = "detail", Whole = "whole";

        /// <summary>
        /// The exhibit the lamp is set against. Her record example is <c>"aic-142512"</c>, the
        /// Gandhara relief (AIC, CC0) that the grotto's niche relief stands in for.
        /// </summary>
        public const string NearReliefExhibit = "aic-142512";

        public Phase Current { get; private set; } = Phase.Choosing;
        /// <summary>"detail" or "whole" once placed, else empty.</summary>
        public string LampSlot { get; private set; } = string.Empty;
        public string ExhibitId => NearReliefExhibit;

        public event Action<Phase> PhaseChanged;

        /// <summary>The lamp is in a socket. <paramref name="slotName"/> is the socket's name ("Detail" / "Whole").</summary>
        public bool Placed(string slotName)
        {
            if (Current == Phase.Saved) return false;
            var s = Normalise(slotName);
            if (s.Length == 0) return false;
            LampSlot = s;
            Go(Phase.Placed);
            return true;
        }

        /// <summary>The lamp lifted out again (or undone).</summary>
        public void Unplaced()
        {
            if (Current == Phase.Saved) return;
            LampSlot = string.Empty;
            Go(Phase.Choosing);
        }

        public bool CanSave => Current == Phase.Placed;

        public bool Save()
        {
            if (!CanSave) return false;
            Go(Phase.Saved);
            return true;
        }

        public bool RimsTheBuddha => Current != Phase.Choosing && LampSlot == Whole;
        public bool LightsTheRelief => Current != Phase.Choosing && LampSlot == Detail;

        static string Normalise(string slotName)
        {
            var s = (slotName ?? string.Empty).Trim().ToLowerInvariant();
            return s == Detail || s == Whole ? s : string.Empty;
        }

        void Go(Phase p) { Current = p; PhaseChanged?.Invoke(p); }

        /// <summary>
        /// Only when there is no live dialogue: what each companion says about the chosen way of seeing. The
        /// "detail" lines are hers, word for word (MUSE-VR-design, the Grotto storyboard); she gives none for
        /// "whole", so those are ours, in the same voices. Live, every companion answers through their own lens.
        /// </summary>
        public static string Line(string master, string lampSlot)
        {
            var detail = lampSlot == Detail;
            switch (master)
            {
                case Masters.VanGogh: return detail ? "Look how close you are leaning. You are looking for the place where the carver's hand pressed hardest, and it is right there in the deepest cut"
                                                    : "Up close it would be stone and toolmarks. From the rail you only see the calm, and you chose the calm";
                case Masters.Monet: return detail ? "The moment the lamp comes close the shadows on this stone move. Flat places now have depth, and when daylight returns it will all fold back in"
                                                  : "From the rail it is one form, a mountain holding the light all day. Up close, the hour would keep changing it";
                case Masters.Socrates: return detail ? "You chose the detail. The Buddha is right behind you. Are you afraid the whole would make you look too small?"
                                                     : "You chose the whole. Standing back, do you see the Buddha, or only how small you are beside it?";
                default: return Masters.Name(master) + " considers the lamp.";
            }
        }

        /// <summary>Who speaks first: the companion of the chosen way of seeing, then the other, then Socrates.</summary>
        public static string[] Order(string lampSlot) =>
            lampSlot == Whole ? new[] { Masters.Monet, Masters.VanGogh, Masters.Socrates }
                              : new[] { Masters.VanGogh, Masters.Monet, Masters.Socrates };

        /// <summary>
        /// The visitor's own companions, whoever they are, in that order where it applies: the close look's
        /// master first for "detail", the whole's for "whole", then the rest as they walk. Only the default three
        /// used to speak, so a chosen Frida or Picasso was silently left out.
        /// </summary>
        public static List<string> Order(string lampSlot, IEnumerable<string> company)
        {
            var present = new List<string>(company);
            var order = new List<string>();
            foreach (var id in Order(lampSlot)) if (present.Contains(id)) order.Add(id);
            foreach (var id in present) if (!order.Contains(id)) order.Add(id);
            return order;
        }
    }
}
