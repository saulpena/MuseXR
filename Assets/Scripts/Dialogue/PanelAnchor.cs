using UnityEngine;

namespace MusePico.Dialogue
{
    /// <summary>
    /// Where the reading panel sits, and when it is allowed to move.
    ///
    /// <b>Not head-locked, on purpose.</b> A panel welded to the head is the classic way to make
    /// someone ill in VR: it never settles, so the eyes never get a stable thing to read. This is
    /// body-locked instead — it stays put while the visitor looks around, and only swings round
    /// once they have genuinely turned to face somewhere else.
    ///
    /// <b>Horizontal only.</b> Pitch is ignored entirely and the panel's height is a constant above
    /// the floor, so looking up or down never drags it. That is a stated requirement, not a
    /// simplification: vertical follow is the part that reads as nausea.
    ///
    /// Pure arithmetic so the hysteresis can be argued about in EditMode rather than by putting a
    /// headset on. Same split as <see cref="GalleryWall"/>.
    /// </summary>
    public static class PanelAnchor
    {
        /// <summary>
        /// Metres in front of the visitor.
        ///
        /// Far enough that the tallest stage fits in view: stage 02 offers seven masters and an
        /// action, which is about 2.4 m of panel, and at this distance a 60-degree camera sees
        /// roughly 3.2 m of height. Closer than this and the bottom row falls off the screen.
        /// </summary>
        public const float Distance = 2.8f;

        /// <summary>Metres above the floor — a constant, which is what makes it not bob.</summary>
        public const float Height = 1.45f;

        /// <summary>
        /// Turn further than this from the panel and it starts to follow. Wide enough that ordinary
        /// looking around — reading the room, glancing at a companion — never disturbs it.
        /// </summary>
        public const float ReleaseDegrees = 35f;

        /// <summary>
        /// Once following, it stops again inside this. Much smaller than
        /// <see cref="ReleaseDegrees"/>: that gap IS the hysteresis, and without it the panel
        /// chatters in and out of motion at exactly the angle a visitor tends to hold.
        /// </summary>
        public const float SettleDegrees = 4f;

        /// <summary>How fast it swings round. Slow enough to read as deliberate, not a snap.</summary>
        public const float DegreesPerSecond = 160f;

        /// <summary>
        /// Walk further than this from where the panel is anchored and it comes with you.
        ///
        /// <b>This is the half that was missing, and it is what made the panel shake.</b> Yaw was
        /// damped from the start, but the ground position was read from the head EVERY FRAME — so
        /// the millimetre jitter of inside-out tracking, and every sway of a person standing still,
        /// translated the panel directly. In an Editor screenshot the camera is perfectly still and
        /// it is invisible; in a headset the head never stops moving and it is the first thing you
        /// see. A panel is a thing in the room, so it must hold still while you shift your weight.
        /// </summary>
        public const float ReleaseMetres = 0.55f;

        /// <summary>Once moving, it parks again inside this. The gap to
        /// <see cref="ReleaseMetres"/> is the hysteresis, exactly as with the yaw.</summary>
        public const float SettleMetres = 0.04f;

        /// <summary>How fast it slides after you. A little under walking pace.</summary>
        public const float MetresPerSecond = 1.8f;

        /// <summary>
        /// Height changes smaller than this are ignored.
        ///
        /// <c>floorY</c> comes from the rig, and a <c>CharacterController</c> resting on a collider
        /// micro-bounces by a millimetre or two forever. Feeding that straight into the panel's
        /// height is a second, vertical shake with a completely different cause from the first.
        /// A staircase step is far larger than this, so riding one still works.
        /// </summary>
        public const float FloorStep = 0.09f;

        /// <summary>
        /// The panel's new yaw, and whether it is still moving.
        ///
        /// <paramref name="following"/> is the caller's own latch: false means parked, true means
        /// mid-swing. Pass back what this returns.
        /// </summary>
        public static float Follow(
            float panelYaw, float headYaw, bool following, float deltaTime, out bool stillFollowing)
        {
            var offset = Mathf.DeltaAngle(panelYaw, headYaw);
            var away = Mathf.Abs(offset);

            if (!following)
            {
                if (away <= ReleaseDegrees) { stillFollowing = false; return panelYaw; }
                following = true;               // the visitor has turned away: start moving
            }

            if (away <= SettleDegrees) { stillFollowing = false; return headYaw; }

            var step = DegreesPerSecond * Mathf.Max(deltaTime, 0f);
            stillFollowing = true;
            return step >= away ? headYaw : panelYaw + Mathf.Sign(offset) * step;
        }

        /// <summary>
        /// The panel's anchored ground position, and whether it is still sliding.
        ///
        /// The exact shape of <see cref="Follow"/>, in metres on the XZ plane instead of degrees:
        /// parked until the visitor has genuinely walked away, then it slides after them and parks
        /// again. <paramref name="following"/> is the caller's latch; pass back what this returns.
        /// </summary>
        public static Vector2 FollowGround(
            Vector2 anchor, Vector2 head, bool following, float deltaTime, out bool stillFollowing)
        {
            var offset = head - anchor;
            var away = offset.magnitude;

            if (!following)
            {
                if (away <= ReleaseMetres) { stillFollowing = false; return anchor; }
                following = true;               // the visitor has walked off: start moving
            }

            if (away <= SettleMetres) { stillFollowing = false; return head; }

            var step = MetresPerSecond * Mathf.Max(deltaTime, 0f);
            stillFollowing = true;
            return step >= away ? head : anchor + offset / away * step;
        }

        /// <summary>
        /// The floor height the panel should use: the held one, unless the real floor has moved by
        /// more than <see cref="FloorStep"/>. A deadband rather than a ramp, because a floor change
        /// that matters is a step and should be taken at once.
        /// </summary>
        public static float FollowFloor(float held, float measured) =>
            Mathf.Abs(measured - held) > FloorStep ? measured : held;

        /// <summary>
        /// Where the panel hangs: in front of the visitor on the ground plane, at a fixed height.
        ///
        /// <paramref name="floorY"/> is the world height the visitor is standing on, so the panel
        /// rides a staircase without ever tracking the head's own bobbing.
        /// </summary>
        public static Vector3 Position(Vector3 headPosition, float panelYaw, float floorY)
        {
            var forward = Quaternion.Euler(0f, panelYaw, 0f) * Vector3.forward;
            var flat = new Vector3(headPosition.x, 0f, headPosition.z) + forward * Distance;
            return new Vector3(flat.x, floorY + Height, flat.z);
        }

        /// <summary>
        /// The panel's own rotation. A flat thing is readable when its +Z points AWAY from the
        /// viewer — the rule CLAUDE.md records for TMP and for Quads alike — and the panel's +Z
        /// already points away down <paramref name="panelYaw"/>, so this is that yaw unchanged.
        /// Applying a further 180 here is what mirrored the runtime panel once before.
        /// </summary>
        public static Quaternion Rotation(float panelYaw) => Quaternion.Euler(0f, panelYaw, 0f);
    }
}
