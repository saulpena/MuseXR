using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace MusePico.Worlds
{
    /// <summary>One frame of a walk, as recorded by <see cref="WalkHarness"/>.</summary>
    public struct WalkSample
    {
        public float time;
        public Vector3 position;
        public bool grounded;
    }

    /// <summary>The result of pushing the stick one way until it stops.</summary>
    public class WalkLeg
    {
        public string name;
        public Vector2 stick;
        public Vector3 start;
        public Vector3 end;
        public float seconds;
        public float floorMinY;
        public float floorMaxY;
        public int frames;
        public int ungroundedFrames;
        /// <summary>What the CharacterController was touching when it stopped: "marble collider",
        /// "fallback box", or "nothing".</summary>
        public string stoppedBy = "nothing";

        public float HorizontalDistance =>
            Vector2.Distance(new Vector2(start.x, start.z), new Vector2(end.x, end.z));

        public float FloorDrift => floorMaxY - floorMinY;
    }

    /// <summary>
    /// The verdict half of the walkability test: pure arithmetic over recorded samples, with no
    /// Unity scene, no physics and no frames. <see cref="WalkHarness"/> does the driving; this
    /// decides what the numbers mean, so the rules are unit-testable in EditMode and the same
    /// thresholds apply to every world.
    ///
    /// The thresholds are deliberately stated here rather than inline in a test, because every
    /// new Marble world is judged against them and they are the thing to argue about when one
    /// fails.
    /// </summary>
    public static class WalkAudit
    {
        /// <summary>How far outside the configured walk box counts as having escaped. The
        /// CharacterController's own radius and skin width let it rest slightly proud of a
        /// boundary collider, so exact containment is the wrong test.</summary>
        public const float EscapeMargin = 0.35f;

        /// <summary>Below this much under the floor, the visitor did not step down — they fell.
        /// Generous because Marble interiors are genuinely uneven.</summary>
        public const float FallBelowFloor = 2.0f;

        /// <summary>Floor movement over a leg beyond this is worth NOTING, not failing. Measured
        /// 16 Sep 2026 on both worlds: the visitor climbs plinths and benches, ending 1.1-2.0 m up.
        /// It reproduces through the real locomotion stack, so it is genuine behaviour rather than
        /// a probe artifact — but it is a comfort problem, not a containment break, and failing on
        /// it would fail every Marble world we have.</summary>
        public const float NotableFloorDrift = 1.0f;

        /// <summary>Ending this far above the floor means the leg finished standing on furniture
        /// rather than on the ground. Also a note, not a failure.</summary>
        public const float StrandedAboveFloor = 0.8f;

        /// <summary>True when the position is outside the walk box by more than the margin.</summary>
        public static bool Escaped(Vector3 position, Vector4 bounds, float margin = EscapeMargin)
        {
            return position.x < bounds.x - margin || position.x > bounds.y + margin ||
                   position.z < bounds.z - margin || position.z > bounds.w + margin;
        }

        /// <summary>True when the visitor ended up far enough below the floor to have fallen
        /// through it rather than stepped down.</summary>
        public static bool Fell(float y, float floorY, float maxDrop = FallBelowFloor)
        {
            return y < floorY - maxDrop;
        }

        /// <summary>
        /// The things that FAIL a world: it did not contain the visitor, or it did not hold them up.
        /// Deliberately only these two.
        ///
        /// CORRECTION (16 Sep 2026): the first version of this also failed on floor drift and on
        /// <c>CharacterController.isGrounded</c> being false for most frames. Running it against
        /// two worlds showed both rules were wrong. <c>isGrounded</c> read false for ~95% of frames
        /// on scenes where the visitor demonstrably never left the floor (end Y within 0.2 m of it
        /// throughout) — it is only true when the controller moved down into something during the
        /// last Move, so at ~166 fps with tiny per-frame gravity steps it is almost never set, and
        /// sampling it from Update races the locomotion stack anyway. Y position is the honest fall
        /// test and <see cref="Fell"/> already does it. Drift turned out to be real furniture
        /// climbing present in every world we have, which belongs in <see cref="Notes"/>.
        /// </summary>
        public static List<string> Problems(WalkLeg leg, Vector4 bounds, float floorY)
        {
            var found = new List<string>();
            if (leg == null) { found.Add("leg was not run"); return found; }

            if (Escaped(leg.end, bounds, EscapeMargin))
                found.Add($"escaped the walk box: ended at ({leg.end.x:F2}, {leg.end.z:F2}), box is " +
                          $"x {bounds.x:F2}..{bounds.y:F2}  z {bounds.z:F2}..{bounds.w:F2}");

            if (Fell(leg.floorMinY, floorY))
                found.Add($"fell through the floor: reached y={leg.floorMinY:F2} against floor y={floorY:F2}");

            return found;
        }

        /// <summary>
        /// Things worth knowing that do not fail the world. These are the comparable numbers
        /// between worlds — a world that collects no notes is comfortable, one that collects
        /// several is walkable but scrappy.
        /// </summary>
        public static List<string> Notes(WalkLeg leg, float floorY)
        {
            var notes = new List<string>();
            if (leg == null) return notes;

            if (leg.FloorDrift > NotableFloorDrift)
                notes.Add($"climbed {leg.FloorDrift:F2} m during the leg (plinth, bench or stepped collider)");

            if (leg.end.y > floorY + StrandedAboveFloor)
                notes.Add($"ended {leg.end.y - floorY:F2} m above the floor — standing on furniture, not the ground");

            if (leg.stoppedBy == "nothing")
                notes.Add($"never met an obstacle: walked {leg.HorizontalDistance:F2} m and the leg simply ran out " +
                          "of time, so this direction's boundary was not actually tested");

            return notes;
        }

        /// <summary>A human-readable table of every leg, for the test log and the console.</summary>
        public static string Report(string worldName, IReadOnlyList<WalkLeg> legs, Vector4 bounds, float floorY)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[WalkAudit] {worldName}");
            sb.AppendLine($"  walk box: x {bounds.x:F2}..{bounds.y:F2}   z {bounds.z:F2}..{bounds.w:F2}   floorY {floorY:F2}");
            sb.AppendLine("  leg                    travelled   end position            climb   stopped by");
            foreach (var leg in legs)
            {
                if (leg == null) continue;
                sb.AppendLine($"  {leg.name,-20} {leg.HorizontalDistance,8:F2} m   " +
                              $"({leg.end.x,6:F2},{leg.end.y,5:F2},{leg.end.z,7:F2})   " +
                              $"{leg.FloorDrift,5:F2}   {leg.stoppedBy}");
            }
            foreach (var leg in legs)
            {
                if (leg == null) continue;
                foreach (var note in Notes(leg, floorY))
                    sb.AppendLine($"  note  {leg.name}: {note}");
            }
            return sb.ToString();
        }
    }
}
