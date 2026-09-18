using UnityEngine;

namespace MusePico.Worlds
{
    /// <summary>
    /// Where the masters should be standing, given where the visitor is and which way they face.
    ///
    /// Ported from <c>muse-infinity/lib/museum3d.js:updateCompanions</c>. Pure arithmetic — no
    /// scene, no physics, no frames — so the formation can be argued about in EditMode instead of
    /// by putting a headset on.
    ///
    /// <b>The original mechanism, unchanged.</b> Each master owns a fixed slot in the visitor's
    /// own frame of reference, <c>{back, side}</c> in metres, rebuilt into world space every frame
    /// from the visitor's position and yaw, then damped toward rather than snapped to. The damping
    /// IS the effect: snapped, they are welded to your face like a HUD; damped, they trail and
    /// catch up, which reads as walking with you.
    /// </summary>
    public static class CompanionFormation
    {
        /// <summary>
        /// The three slots from the original, in metres. NEGATIVE back means AHEAD of the visitor.
        ///
        /// These numbers are playtested, not invented: the original's comment records them being
        /// pulled out from 1.6 m to 2.7-3.4 m after acceptance feedback #11, because at point
        /// blank the figures filled the frame and made every world feel cramped.
        ///
        /// The third slot stands dead centre. In a browser viewport that reads as a group portrait;
        /// in a headset, down a 52 m gallery, it stands a man in the middle of the view. Kept as
        /// the faithful default and exposed on the component so it can be judged on the device.
        /// </summary>
        public static readonly Vector2[] DefaultSlots =
        {
            new Vector2(-2.7f, -1.6f),   // (back, side)
            new Vector2(-2.7f, 1.6f),
            new Vector2(-3.4f, 0.0f),
        };

        /// <summary>The original's per-frame damping constant, at its ~60 fps.</summary>
        public const float ReferenceDampingPerFrame = 0.08f;

        /// <summary>No master may stand more than this above the visitor's own floor. From the
        /// original: a mezzanine collider fragment under one slot sat inside the ground-accept
        /// window and kept lifting a companion into the air.</summary>
        public const float MaxAboveVisitor = 0.35f;

        /// <summary>Keep this far off the walk-box edge, so a wide side slot cannot shove a
        /// master into a wall at a corridor edge.</summary>
        public const float BoundsInset = 0.35f;

        /// <summary>
        /// The world-space XZ a slot maps to.
        ///
        /// Same shape as the original — <c>t = anchor - fwd*back + rgt*side</c>, so a negative
        /// <c>back</c> puts a master ahead — but with UNITY'S basis, not three.js's.
        ///
        /// <b>This is the one line the port got wrong first time.</b> The original computes
        /// <c>fwd = (sin y, -cos y)</c>; Unity's <c>Quaternion.Euler(0,y,0) * Vector3.forward</c>
        /// is <c>(sin y, +cos y)</c>. The two conventions differ by 180 degrees, so transcribing
        /// the JS literally put all three masters directly BEHIND the visitor — spawned, footed,
        /// following correctly, and permanently out of shot. Unity's basis:
        /// <c>fwd = (sin y, cos y)</c>, <c>rgt = (cos y, -sin y)</c>.
        /// </summary>
        public static Vector2 SlotTarget(Vector3 anchor, float yawRadians, Vector2 slot)
        {
            float fwdX = Mathf.Sin(yawRadians), fwdZ = Mathf.Cos(yawRadians);
            float rgtX = Mathf.Cos(yawRadians), rgtZ = -Mathf.Sin(yawRadians);
            float back = slot.x, side = slot.y;
            return new Vector2(anchor.x - fwdX * back + rgtX * side,
                               anchor.z - fwdZ * back + rgtZ * side);
        }

        /// <summary>
        /// The original's <c>p += (t - p) * 0.08</c> made frame-rate independent.
        ///
        /// Taking 8% of the gap per frame is only 8% at the frame rate it was written for. A Quest
        /// at 72 Hz and an editor at 160 would otherwise give visibly different follow distances,
        /// and follow distance is the whole feel of this.
        /// </summary>
        public static float DampFactor(float perFrameAt60, float deltaTime)
        {
            if (deltaTime <= 0f) return 0f;
            float retained = Mathf.Clamp01(1f - perFrameAt60);
            return 1f - Mathf.Pow(retained, deltaTime * 60f);
        }

        /// <summary>Hold a slot inside the walk box so nobody is pushed through a wall.</summary>
        public static Vector2 ClampToBounds(Vector2 target, Vector4 bounds, float inset = BoundsInset)
        {
            // A box narrower than twice the inset would invert; clamp to its centre instead.
            float minX = bounds.x + inset, maxX = bounds.y - inset;
            float minZ = bounds.z + inset, maxZ = bounds.w - inset;
            if (minX > maxX) minX = maxX = (bounds.x + bounds.y) * 0.5f;
            if (minZ > maxZ) minZ = maxZ = (bounds.z + bounds.w) * 0.5f;
            return new Vector2(Mathf.Clamp(target.x, minX, maxX), Mathf.Clamp(target.y, minZ, maxZ));
        }

        /// <summary>
        /// How close a master may ever get to the visitor, metres.
        ///
        /// Measured 16 Sep 2026: walking to the end wall of the Sunlit gallery put the visitor at
        /// z = -28.61 while the slots wanted z = -32, outside the walk box. The bounds clamp pinned
        /// all three to the box edge and Socrates ended up 0.68 m away — standing inside the
        /// visitor, his chest filling the headset. The clamp is right; what was missing is that it
        /// must never win against personal space.
        /// </summary>
        public const float PersonalSpace = 1.2f;

        /// <summary>
        /// Push a target out to at least <paramref name="minDistance"/> from the visitor.
        ///
        /// Applied AFTER the bounds clamp, and deliberately allowed to put a master slightly
        /// outside the walk box: a companion standing 30 cm past a boundary line is invisible,
        /// because that box is a walking guide and not geometry. A companion standing inside the
        /// visitor is the single most noticeable thing in the scene.
        /// </summary>
        public static Vector2 PushOutOfPersonalSpace(Vector2 target, Vector3 anchor, Vector2 slot,
                                                     float minDistance = PersonalSpace)
        {
            var away = new Vector2(target.x - anchor.x, target.y - anchor.z);
            float d = away.magnitude;
            if (d >= minDistance) return target;

            if (d < 1e-4f)
            {
                // Exactly on top of the visitor: no direction to push along, so use the slot's own
                // sideways sign to keep the party spread out rather than stacking them.
                float sign = slot.y >= 0f ? 1f : -1f;
                away = new Vector2(sign, 0f);
            }
            else away /= d;

            return new Vector2(anchor.x + away.x * minDistance, anchor.z + away.y * minDistance);
        }

        /// <summary>How close two masters may stand to each other, metres. Two 0.5 m-wide figures
        /// need roughly this much before they read as separate people rather than one blob.</summary>
        public const float CompanionSeparation = 0.85f;

        /// <summary>
        /// Push a set of targets apart until no two are closer than
        /// <paramref name="minSeparation"/>, then re-apply personal space so the relaxation cannot
        /// shove one master into the visitor.
        ///
        /// Relaxation rather than a single pass: with three slots, separating A from B can push B
        /// into C, so it needs a few iterations to settle. Deterministic and cheap at this count.
        /// </summary>
        public static void Separate(Vector2[] targets, Vector3 anchor, Vector2[] slots,
                                    float minSeparation = CompanionSeparation,
                                    float personalSpace = PersonalSpace,
                                    int iterations = 4)
        {
            if (targets == null || targets.Length < 2) return;

            for (int pass = 0; pass < iterations; pass++)
            {
                for (int i = 0; i < targets.Length; i++)
                {
                    for (int j = i + 1; j < targets.Length; j++)
                    {
                        var delta = targets[j] - targets[i];
                        float d = delta.magnitude;
                        if (d >= minSeparation) continue;

                        Vector2 dir;
                        if (d < 1e-4f)
                        {
                            // Exactly coincident: separate along the line between their slots so
                            // the result is stable rather than arbitrary.
                            var sd = (slots != null && slots.Length == targets.Length)
                                ? new Vector2(slots[j].y - slots[i].y, slots[j].x - slots[i].x)
                                : Vector2.zero;
                            dir = sd.sqrMagnitude > 1e-6f ? sd.normalized : new Vector2(1f, 0f);
                        }
                        else dir = delta / d;

                        float push = (minSeparation - d) * 0.5f;
                        targets[i] -= dir * push;
                        targets[j] += dir * push;
                    }
                }
            }

            if (slots != null && slots.Length == targets.Length)
                for (int i = 0; i < targets.Length; i++)
                    targets[i] = PushOutOfPersonalSpace(targets[i], anchor, slots[i], personalSpace);
        }

        /// <summary>Degrees about Y that turns <paramref name="from"/> to face
        /// <paramref name="to"/> — the original's <c>atan2(cam.x - a.x, cam.z - a.z)</c>, so the
        /// faces stay readable whatever the visitor does.</summary>
        public static float FacingYawDegrees(Vector3 from, Vector3 to)
        {
            return Mathf.Atan2(to.x - from.x, to.z - from.z) * Mathf.Rad2Deg;
        }

        /// <summary>Apply the "never above the visitor" rule to a sampled ground height.</summary>
        public static float ClampGroundToVisitor(float sampledGroundY, float visitorGroundY,
                                                 float maxAbove = MaxAboveVisitor)
        {
            return sampledGroundY > visitorGroundY + maxAbove ? visitorGroundY + maxAbove : sampledGroundY;
        }

        /// <summary>
        /// Step the body yaw toward the head yaw, the shortest way round.
        ///
        /// <b>This is the one deliberate departure from the original</b>, and it exists because the
        /// original cannot be ported literally. In the browser <c>this.yaw</c> is a mouse-drag
        /// value and WASD walks along it, so camera yaw and body facing are the same number. In a
        /// headset they are not: driving the formation from the head means three people slide
        /// across your view every time you glance at a painting. Damping the body yaw slowly means
        /// a glance moves nobody and a sustained turn brings them round.
        /// </summary>
        public static float StepBodyYaw(float bodyYawDegrees, float headYawDegrees,
                                        float ratePerSecond, float deltaTime)
        {
            float delta = Mathf.DeltaAngle(bodyYawDegrees, headYawDegrees);
            float t = deltaTime <= 0f ? 0f : 1f - Mathf.Exp(-Mathf.Max(0f, ratePerSecond) * deltaTime);
            return bodyYawDegrees + delta * t;
        }

        /// <summary>Uniform scale that brings a model of <paramref name="nativeHeight"/> to human
        /// height. Tripo meshes have no agreed unit, so the bounding box is the only honest
        /// source of scale — same rule the gallery uses.</summary>
        public static float HeightNormalisingScale(float nativeHeight, float humanHeight = 1.75f)
        {
            if (nativeHeight <= 0.0001f || !float.IsFinite(nativeHeight)) return 1f;
            return humanHeight / nativeHeight;
        }
    }
}
