using MusePico.Grab;
using NUnit.Framework;
using UnityEngine;

namespace MusePico.Tests
{
    public class GrabGestureTests
    {
        [Test]
        public void AQuickStillPressIsATap()
        {
            Assert.AreEqual(GrabRelease.Tap, GrabGesture.Classify(0.15f, 0.005f, 1f, 1));
        }

        [Test]
        public void HoldingPastTheTapWindowIsAGrab()
        {
            Assert.AreEqual(GrabRelease.Moved, GrabGesture.Classify(GrabGesture.TapSeconds + 0.01f, 0f, 0f, 1));
        }

        [Test]
        public void CarryingTheHandIsAGrabEvenWhenQuick()
        {
            Assert.AreEqual(GrabRelease.Moved, GrabGesture.Classify(0.2f, GrabGesture.TapTravel + 0.01f, 0f, 1));
        }

        [Test]
        public void TurningTheWristIsAGrabEvenWhenQuick()
        {
            // At the end of a far ray a turn is a large move, so it must count on its own.
            Assert.AreEqual(GrabRelease.Moved, GrabGesture.Classify(0.2f, 0f, GrabGesture.TapTurn + 1f, 1));
        }

        [Test]
        public void TwoHandsIsAlwaysAGrab()
        {
            Assert.AreEqual(GrabRelease.Moved, GrabGesture.Classify(0.1f, 0f, 0f, 2));
        }

        [Test]
        public void ANegativeDurationIsNotATap()
        {
            // A lost press time must not turn into a phantom click.
            Assert.AreEqual(GrabRelease.Moved, GrabGesture.Classify(-1f, 0f, 0f, 1));
        }

        [Test]
        public void ScaleLimitsAllowBothShrinkingAndGrowing()
        {
            Assert.Less(Grabbable.MinScaleRatio, 1f);
            Assert.Greater(Grabbable.MaxScaleRatio, 1f);
        }

        [Test]
        public void MakeConfiguresATwoHandedKinematicGrab()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            try
            {
                var collider = go.GetComponent<Collider>();
                var painting = Grabbable.Make(go, null, GrabReach.AtRayEnd, collider);
                var grab = painting.Interactable;
                var transformer = go.GetComponent<UnityEngine.XR.Interaction.Toolkit.Transformers.XRGeneralGrabTransformer>();
                var body = go.GetComponent<Rigidbody>();

                Assert.AreEqual(UnityEngine.XR.Interaction.Toolkit.Interactables.InteractableSelectMode.Multiple, grab.selectMode);
                Assert.IsTrue(transformer.allowTwoHandedScaling);
                // TwoHandedAverage snaps 90 degrees on the second grab; FirstHandOnly cannot scale.
                Assert.AreEqual(UnityEngine.XR.Interaction.Toolkit.Transformers.XRGeneralGrabTransformer.TwoHandedRotationMode.FirstHandDirectedTowardsSecondHand,
                                transformer.allowTwoHandedRotation);
                Assert.IsFalse(transformer.allowOneHandedScaling, "thumbsticks are turn and move");
                Assert.IsTrue(transformer.clampScaling);
                Assert.IsTrue(body.isKinematic);
                Assert.IsFalse(body.useGravity);
                Assert.IsFalse(grab.throwOnDetach);
                Assert.IsFalse(grab.unparentTransformOnGrab, "works must stay the wall's children");
                Assert.AreEqual(UnityEngine.XR.Interaction.Toolkit.Attachment.InteractableFarAttachMode.Far, grab.farAttachMode);
                Assert.AreEqual(1, grab.colliders.Count);
                Assert.AreSame(go.GetComponent<BoxCollider>(), grab.colliders[0], "the quad's mesh collider is swapped for a box");
                Assert.IsTrue(go.activeSelf, "Make must leave the object as active as it found it");
                Assert.AreSame(painting, Grabbable.Make(go), "making twice must not stack components");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void AQuadGetsACollider_ClosestPointCanAnswer()
        {
            // XRI places a pulled or reached-into grip with Collider.ClosestPoint. A Quad's own
            // MeshCollider is non-convex and answers with the query point, so the painting never
            // came to hand. The swapped collider must put a far point back on the canvas.
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            try
            {
                var grab = Grabbable.Make(go, null, GrabReach.PullToHand, go.GetComponent<Collider>()).Interactable;
                Assert.AreEqual(1, grab.colliders.Count);
                Assert.IsInstanceOf<BoxCollider>(grab.colliders[0]);
                Assert.IsNull(go.GetComponent<MeshCollider>(), "the unusable collider must be gone, not left beside it");
                Physics.SyncTransforms();
                var far = new Vector3(0f, 0f, -3f);
                var onCanvas = grab.colliders[0].ClosestPoint(far);
                Assert.Less(Mathf.Abs(onCanvas.z), 0.02f, "closest point must be on the canvas, not the hand 3 m away");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void PullToHandBringsItInAndAtRayEndLeavesItThere()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                var grabbable = Grabbable.Make(go, null, GrabReach.PullToHand, go.GetComponent<Collider>());
                var grab = grabbable.Interactable;
                // It takes hold at the ray end and is REELED in while held (Update), not flown in:
                // the 0.15 s ease read as the object rushing at the visitor's face.
                Assert.AreEqual(UnityEngine.XR.Interaction.Toolkit.Attachment.InteractableFarAttachMode.Far, grab.farAttachMode);
                Assert.AreEqual(0f, grab.attachEaseInTime);
                Assert.AreEqual(GrabReach.PullToHand, grabbable.Reach);
                var transformer = go.GetComponent<UnityEngine.XR.Interaction.Toolkit.Transformers.XRGeneralGrabTransformer>();
                Assert.AreEqual(Grabbable.CloseScaleMultiplier, transformer.scaleMultiplier,
                                "held close, size must follow the hands; XRI's 0.25 needed a 1.74x spread for x1.13");

                Grabbable.Make(go, null, GrabReach.AtRayEnd);   // re-making re-configures, it does not stack
                Assert.AreEqual(UnityEngine.XR.Interaction.Toolkit.Attachment.InteractableFarAttachMode.Far, grab.farAttachMode);
                Assert.AreEqual(Grabbable.FarScaleMultiplier, transformer.scaleMultiplier);
                Assert.AreEqual(GrabReach.AtRayEnd, grabbable.Reach);
                Assert.AreEqual(1, go.GetComponents<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>().Length);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void AReelIsSteadyAndStopsAtTheHand()
        {
            // 4 m at 1 m/s in 50 ms frames: 0.05 m a frame, whatever the distance — steady, not eased.
            Assert.AreEqual(3.95f, GrabGesture.ReelStep(4f, 1f, 0.05f), 1e-5f);
            Assert.AreEqual(0.95f, GrabGesture.ReelStep(1f, 1f, 0.05f), 1e-5f);
            Assert.AreEqual(0f, GrabGesture.ReelStep(0.02f, 1f, 0.05f), "never overshoots past the hand");
            Assert.AreEqual(2f, GrabGesture.ReelStep(2f, 1f, -0.1f), "a bad frame moves nothing");
            Assert.AreEqual(2f, GrabGesture.ReelStep(2f, 0f, 0.1f));
        }

        [Test]
        public void APaintingCanGrowTallEnoughToWalkInto()
        {
            // The lower-row canvases are 0.85 m tall; a person is ~1.8 m; stepping inside wants 4 m+.
            Assert.GreaterOrEqual(0.85f * Grabbable.MaxScaleRatio, 4f);
        }
    }
}
