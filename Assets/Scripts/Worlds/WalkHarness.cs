using System.Collections;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

namespace MusePico.Worlds
{
    /// <summary>
    /// Walks a world the way a visitor does, and records what happened.
    ///
    /// <b>Why this exists.</b> The first walk check on the Sunlit gallery fired 400
    /// <c>CharacterController.Move</c> calls inside one frame. That found the floor and the walls,
    /// but it is not locomotion: no frame advances between calls, so gravity never integrates,
    /// slope limits and step offset are evaluated against a stale ground state, and the visitor
    /// climbed a 1.17 m plinth that a thumbstick would not have climbed. The result could not be
    /// trusted either way, which makes it useless as a regression test.
    ///
    /// <b>What it does instead.</b> It pushes the stick. XRI's move providers read through an
    /// <see cref="XRInputValueReader{TValue}"/>, which has a
    /// <see cref="XRInputValueReader.InputSourceMode.ManualValue"/> mode intended for exactly
    /// this. Setting that drives the REAL chain — LocomotionMediator, XRBodyTransformer,
    /// DynamicMoveProvider, GravityProvider, CharacterController — one frame at a time, at the
    /// project's own move speed. The only thing swapped out is the hardware thumbstick, so what
    /// passes here is what a visitor gets, and it can be re-run unchanged against every new world.
    ///
    /// Teleportation is disabled for the duration: a teleport ray that happens to hit a wall can
    /// move the visitor in one frame and would mask exactly the containment failure being tested.
    /// The previous state is restored afterwards.
    /// </summary>
    public static class WalkHarness
    {
        /// <summary>Everything the harness needs from a scene, resolved once.</summary>
        public class Rig
        {
            public XROrigin origin;
            public CharacterController character;
            public ContinuousMoveProvider move;
            public WalkRig walkRig;
            public bool IsComplete => origin != null && character != null && move != null;

            public string Describe()
            {
                return $"origin={(origin != null ? origin.name : "MISSING")} " +
                       $"character={(character != null ? "yes" : "MISSING")} " +
                       $"moveProvider={(move != null ? move.GetType().Name : "MISSING")} " +
                       $"walkRig={(walkRig != null ? "yes" : "none")}";
            }
        }

        /// <summary>Find the rig in whatever scene is loaded.</summary>
        public static Rig Resolve()
        {
            var rig = new Rig();
            rig.origin = Object.FindAnyObjectByType<XROrigin>();
            rig.character = rig.origin != null ? rig.origin.GetComponent<CharacterController>() : null;
            rig.move = Object.FindAnyObjectByType<ContinuousMoveProvider>();
            rig.walkRig = Object.FindAnyObjectByType<WalkRig>();
            return rig;
        }

        /// <summary>
        /// Turn teleportation off and hand back a token that turns it on again. Returns the
        /// components that were actually disabled so the caller can report the count.
        /// </summary>
        public static List<Behaviour> DisableTeleportation()
        {
            var disabled = new List<Behaviour>();
            foreach (var provider in Object.FindObjectsByType<TeleportationProvider>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!provider.enabled) continue;
                provider.enabled = false;
                disabled.Add(provider);
            }
            return disabled;
        }

        public static void Restore(List<Behaviour> disabled)
        {
            if (disabled == null) return;
            foreach (var b in disabled)
                if (b != null) b.enabled = true;
        }

        /// <summary>
        /// Hold the stick at <paramref name="stick"/> for <paramref name="seconds"/>, sampling
        /// every frame. (0,1) is forward, (1,0) is right — the same convention the thumbstick uses.
        /// </summary>
        /// <param name="result">Filled in as the leg runs; read it after the coroutine finishes.</param>
        public static IEnumerator Walk(Rig rig, string legName, Vector2 stick, float seconds, WalkLeg result,
                                       List<WalkSample> samples = null)
        {
            result.name = legName;
            result.stick = stick;
            result.seconds = seconds;
            result.start = rig.origin.transform.position;
            result.floorMinY = float.MaxValue;
            result.floorMaxY = float.MinValue;

            var reader = rig.move.leftHandMoveInput;
            var previousMode = reader.inputSourceMode;
            var previousValue = reader.manualValue;

            reader.inputSourceMode = XRInputValueReader.InputSourceMode.ManualValue;
            reader.manualValue = stick;

            float elapsed = 0f;
            while (elapsed < seconds)
            {
                yield return null;
                elapsed += Time.deltaTime;

                var p = rig.origin.transform.position;
                result.frames++;
                if (!rig.character.isGrounded) result.ungroundedFrames++;
                if (p.y < result.floorMinY) result.floorMinY = p.y;
                if (p.y > result.floorMaxY) result.floorMaxY = p.y;

                if (samples != null)
                    samples.Add(new WalkSample { time = elapsed, position = p, grounded = rig.character.isGrounded });
            }

            // Let go of the stick before measuring, so the last frame is not mid-push.
            reader.manualValue = Vector2.zero;
            yield return null;

            result.end = rig.origin.transform.position;
            result.stoppedBy = WhatIsTouching(rig);

            reader.inputSourceMode = previousMode;
            reader.manualValue = previousValue;
        }

        /// <summary>Teleport the rig back to a known pose between legs. A CharacterController
        /// caches its own position, so it has to be toggled around the write.</summary>
        public static void Reseat(Rig rig, Vector3 position, float yawDegrees)
        {
            bool had = rig.character != null && rig.character.enabled;
            if (had) rig.character.enabled = false;
            rig.origin.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yawDegrees, 0f));
            if (had) rig.character.enabled = true;
            Physics.SyncTransforms();
        }

        /// <summary>
        /// Name what is up against the visitor, so a report says whether the world stopped them or
        /// the invisible fallback box did. That distinction is the whole point: a world held only
        /// by the box has no real walls there.
        /// </summary>
        public static string WhatIsTouching(Rig rig)
        {
            var centre = rig.origin.transform.position + new Vector3(0f, 0.8f, 0f);
            var hits = Physics.OverlapSphere(centre, rig.character.radius + 0.35f);
            bool box = false, marble = false;
            foreach (var h in hits)
            {
                var root = h.transform.root.name;
                if (root.StartsWith("World Bounds")) box = true;
                else if (root.StartsWith("Collider_")) marble = true;
            }
            if (box && marble) return "both";
            if (box) return "fallback box";
            if (marble) return "marble collider";
            return "nothing";
        }
    }
}
