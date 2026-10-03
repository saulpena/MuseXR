using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MuseXR.Interaction;
using MuseXR.Slots;
using MuseXR.Worlds;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using Pointer = MuseXR.Interaction.Pointer;
using UnityEngine.TestTools;
using UnityEngine.XR.Interaction.Toolkit.Inputs;

namespace MusePico.Tests
{
    /// <summary>
    /// The hand interactions driven frame by frame with scripted hands: no headset, no scene. Each test
    /// builds what it needs and tears it down. Her checks are quoted where she gives one.
    /// </summary>
    public class InteractionPlayTests
    {
        readonly List<GameObject> _made = new List<GameObject>();

        GameObject Make(string name, PrimitiveType? type = null)
        {
            var go = type.HasValue ? GameObject.CreatePrimitive(type.Value) : new GameObject(name);
            go.name = name;
            _made.Add(go);
            return go;
        }

        (ScriptedHandSource source, GripHand grip, Pointer pointer, Transform aim) Hand(Hand side = MuseXR.Interaction.Hand.Right)
        {
            var go = Make("Hand " + side);
            var source = new ScriptedHandSource(side, go.transform);
            var grip = GripHand.Attach(go, source);
            var pointer = Pointer.Attach(go, source, grip);
            return (source, grip, pointer, go.transform);
        }

        Transform Head(Vector3 at, float yaw = 0f)
        {
            var h = Make("Head").transform;
            h.SetPositionAndRotation(at, Quaternion.Euler(0f, yaw, 0f));
            return h;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _made) if (go != null) Object.Destroy(go);
            _made.Clear();
            LocomotionLock.Release(MuseXR.Interaction.Hand.Left);
            LocomotionLock.Release(MuseXR.Interaction.Hand.Right);
        }

        static IEnumerator Frames(int n) { for (var i = 0; i < n; i++) yield return null; }

        // ---- slots ----------------------------------------------------------------------

        (SlotStation station, Holdable a, Holdable b, Transform slot) Palace()
        {
            var host = Make("Palace");
            var slot = Make("Slot").transform;
            slot.position = new Vector3(0f, 0.8f, 1f);
            var a = Make("Crane", PrimitiveType.Cube); a.transform.position = new Vector3(-0.5f, 0.9f, 1f); a.transform.localScale = Vector3.one * 0.2f;
            var b = Make("Turtle", PrimitiveType.Cube); b.transform.position = new Vector3(0.5f, 0.9f, 1f); b.transform.localScale = Vector3.one * 0.2f;
            var ha = Holdable.Make(a, "Crane", idleSpin: false);
            var hb = Holdable.Make(b, "Turtle", idleSpin: false);
            Physics.SyncTransforms();
            return (SlotStation.Make(host, Chapter.Palace, new[] { slot }, new[] { ha, hb }), ha, hb, slot);
        }

        [UnityTest]
        public IEnumerator GripTakesThePieceTheStickTurnsItAndItSeatsLevelOnTheSlot()
        {
            var (station, crane, _, slot) = Palace();
            var h = Hand();
            h.aim.SetPositionAndRotation(crane.transform.position, Quaternion.identity);
            h.source.Grip = true;
            yield return Frames(2);
            Assert.AreEqual(Holdable.Mode.Held, crane.State, "grip took it by hand");

            h.source.Stick = new Vector2(1f, 0f); yield return Frames(2);
            h.source.Stick = Vector2.zero; yield return Frames(2);
            h.source.Stick = new Vector2(1f, 0f); yield return Frames(2);
            h.source.Stick = Vector2.zero; yield return Frames(2);
            Assert.AreEqual(30f, crane.StickDegrees, "two flicks, 15° each");

            h.aim.position += (slot.position + Vector3.up * 0.05f) - crane.BasePoint;
            yield return Frames(2);
            Assert.AreEqual(SlotState.Aligned, station.Board.StateOf(0));
            var buzzesBefore = h.source.Buzzes;

            h.source.Grip = false;
            yield return new WaitForSeconds(FloatHome.SnapSeconds + 0.1f);
            Assert.AreEqual(Holdable.Mode.Seated, crane.State);
            Assert.Less(Vector3.Distance(crane.BasePoint, slot.position), 0.002f, "base on the slot");
            Assert.Greater(crane.transform.up.y, 0.9999f, "level");
            Assert.AreEqual(30, station.YawOf(crane), "seated at the turned yaw");
            Assert.Greater(h.source.Buzzes, buzzesBefore, "the confirm haptic fired");
            Assert.AreEqual("chime-BronzeBell", ChimePlayer.LastPlayed);
            Assert.AreEqual("Keep this moment? Crane · 30°", station.Board.Choice.StripLine);

            Assert.IsTrue(ConfirmInput.PressA());
            Assert.IsTrue(station.Board.Choice.IsConfirmed);
            h.aim.position = crane.transform.position; h.source.Grip = true;
            yield return Frames(2);
            Assert.AreNotEqual(Holdable.Mode.Held, crane.State, "a kept piece cannot be lifted out");
        }

        [UnityTest]
        public IEnumerator ReleasedThirtyCentimetresAwayItFloatsHomeAndNeverFalls()
        {
            // Her check (2.4): "Release 30 cm from the slot and the crane floats back to its plinth."
            var (_, crane, _, slot) = Palace();
            var home = crane.transform.position;
            var h = Hand();
            h.aim.position = crane.transform.position; h.source.Grip = true;
            yield return Frames(2);
            h.aim.position += (slot.position + new Vector3(0f, 0.1f, -0.28f)) - crane.BasePoint;
            yield return Frames(2);
            h.source.Grip = false;
            var lowest = float.MaxValue;
            var t = 0f;
            while (t < FloatHome.MaxSeconds + 0.3f) { lowest = Mathf.Min(lowest, crane.transform.position.y); t += Time.deltaTime; yield return null; }
            Assert.AreEqual(Holdable.Mode.Resting, crane.State);
            Assert.Less(Vector3.Distance(crane.transform.position, home), 0.001f, "back on its plinth");
            Assert.GreaterOrEqual(lowest, home.y - 0.001f, "it never dropped below its plinth on the way");
        }

        [UnityTest]
        public IEnumerator BInsideTheBarSendsItHomeAndAfterTheBarDoesNot()
        {
            var (station, crane, _, slot) = Palace();
            var h = Hand();
            h.aim.position = crane.transform.position; h.source.Grip = true; yield return Frames(2);
            h.aim.position += slot.position - crane.BasePoint; yield return Frames(2);
            h.source.Grip = false; yield return Frames(3);
            Assert.AreEqual(SlotState.Placed, station.Board.StateOf(0));
            Assert.IsTrue(ConfirmInput.PressB(), "B inside 3 s");
            Assert.AreEqual(SlotState.Empty, station.Board.StateOf(0));
            yield return new WaitForSeconds(FloatHome.MaxSeconds);

            h.aim.position = crane.transform.position; h.source.Grip = true; yield return Frames(2);
            h.aim.position += slot.position - crane.BasePoint; yield return Frames(2);
            h.source.Grip = false;
            yield return new WaitForSeconds(SlotRules.UndoSeconds + 0.2f);
            Assert.IsFalse(ConfirmInput.PressB(), "B after the bar is refused");
            Assert.AreEqual(SlotState.Placed, station.Board.StateOf(0));
        }

        [UnityTest]
        public IEnumerator ARayGrabFromTwoMetresBringsThePieceToTheHand()
        {
            var (_, crane, _, _) = Palace();
            var h = Hand();
            var from = crane.transform.position + new Vector3(0f, 0.3f, -2f);
            h.aim.SetPositionAndRotation(from, Quaternion.LookRotation(crane.transform.position - from));
            h.source.Grip = true;
            yield return Frames(2);
            Assert.IsTrue(h.grip.HeldByRay);
            yield return new WaitForSeconds(Holdable.ReelSeconds + 0.1f);
            Assert.Less(Vector3.Distance(crane.transform.position, h.aim.position), 0.3f, "it came to the hand");
        }

        [UnityTest]
        public IEnumerator HoldingSomethingLocksOnlyThatHandsLocomotion()
        {
            var asset = ScriptableObject.CreateInstance<InputActionAsset>();
            var left = asset.AddActionMap("XRI Left Locomotion");
            var right = asset.AddActionMap("XRI Right Locomotion");
            var lt = left.AddAction("Teleport Mode"); var rt = right.AddAction("Teleport Mode");
            var rs = right.AddAction("Snap Turn");
            asset.Enable();
            var mgr = Make("Rig").AddComponent<InputActionManager>();
            mgr.actionAssets = new List<InputActionAsset> { asset };

            var (_, crane, _, _) = Palace();
            var h = Hand();
            h.aim.position = crane.transform.position; h.source.Grip = true;
            yield return Frames(2);
            Assert.IsFalse(rt.enabled, "the holding hand's stick does not teleport");
            Assert.IsFalse(rs.enabled, "nor snap-turn");
            Assert.IsTrue(lt.enabled, "the other hand still can");
            h.source.Grip = false;
            yield return Frames(2);
            Assert.IsTrue(rt.enabled && rs.enabled, "released, the stick moves the visitor again");
            Object.Destroy(asset);
        }

        // ---- pointing -------------------------------------------------------------------

        [UnityTest]
        public IEnumerator TheTriggerRayHoversThenSelectsOncePerPress()
        {
            var target = Make("Target", PrimitiveType.Cube);
            target.transform.position = new Vector3(0f, 1.5f, 3f);
            var p = Pointable.Make(target, "t");
            Physics.SyncTransforms();
            var hovered = 0; var selected = 0;
            p.Hovering += _ => hovered++;
            p.Selected += (_, __) => selected++;
            var h = Hand();
            h.aim.SetPositionAndRotation(new Vector3(0f, 1.5f, 0f), Quaternion.identity);
            yield return Frames(2);
            Assert.AreEqual(1, hovered);
            Assert.IsTrue(p.Hovered);
            Assert.Greater(h.source.Buzzes, 0, "a tick in the hand on hover");
            h.source.Trigger = true; yield return Frames(5);
            h.source.Trigger = false; yield return Frames(2);
            h.source.Trigger = true; yield return Frames(2);
            Assert.AreEqual(2, selected, "two presses, two selections, however long held");
            h.aim.rotation = Quaternion.Euler(0f, 90f, 0f); yield return Frames(2);
            Assert.IsFalse(p.Hovered);
        }

        // ---- Company --------------------------------------------------------------------

        (CompanyStage stage, Dictionary<string, Transform> standees, Transform head) Company()
        {
            var head = Head(new Vector3(0f, 1.6f, 0f));
            var standees = new Dictionary<string, Transform>();
            for (var i = 0; i < Masters.Row.Count; i++)
            {
                var s = Make("Standee " + Masters.Row[i], PrimitiveType.Cube).transform;
                s.position = new Vector3((i - 2.5f) * 0.85f, 0.9f, 3.2f);
                s.localScale = new Vector3(0.6f, 1.8f, 0.05f);
                standees[Masters.Row[i]] = s;
            }
            Physics.SyncTransforms();
            var stage = CompanyStage.Make(Make("Company"), standees);
            stage.Group.Head = head;
            return (stage, standees, head);
        }

        IEnumerator Point(ScriptedHandSource source, Transform aim, Transform target)
        {
            aim.SetPositionAndRotation(new Vector3(0f, 1.2f, 0f), Quaternion.LookRotation(target.position - new Vector3(0f, 1.2f, 0f)));
            yield return Frames(2);
            source.Trigger = true; yield return Frames(2);
            source.Trigger = false; yield return Frames(1);
        }

        [UnityTest]
        public IEnumerator TheFourthStandeeIsRefusedWithAShakeABuzzAndASound()
        {
            var (stage, standees, _) = Company();
            var h = Hand();
            foreach (var id in new[] { Masters.Socrates, Masters.Frida, Masters.Monet }) yield return Point(h.source, h.aim, standees[id]);
            CollectionAssert.AreEquivalent(new[] { Masters.Socrates, Masters.Frida, Masters.Monet }, stage.Invitation.Chosen);
            var row = standees[Masters.Hilma].position;
            yield return Point(h.source, h.aim, standees[Masters.Hilma]);
            Assert.IsFalse(stage.Invitation.IsChosen(Masters.Hilma));
            Assert.AreEqual(0.8f, h.source.LastBuzzAmplitude, 1e-4, "a hard buzz");
            Assert.AreEqual("refuse", ChimePlayer.LastPlayed, "and a knock");
            var moved = 0f;
            for (var i = 0; i < 6; i++) { moved = Mathf.Max(moved, Vector3.Distance(standees[Masters.Hilma].position, row)); yield return null; }
            Assert.Greater(moved, 0.005f, "it visibly shook");
        }

        [UnityTest]
        public IEnumerator TheChosenStepToMarksWithinSixtyDegreesAndAnswerInHerOrder()
        {
            var (stage, standees, head) = Company();
            stage.Preselect(new[] { Masters.Socrates, Masters.VanGogh, Masters.Monet });
            var lines = new List<string>();
            stage.Group.LineStarted += (id, _) => lines.Add(id);
            List<string> done = null;
            stage.Completed += ids => done = ids.ToList();

            Assert.IsTrue(ConfirmInput.PressA());
            yield return new WaitForSeconds(CompanyStage.StepSeconds + 0.2f);
            Assert.AreEqual(CompanyStage.Phase.Answering, stage.Current);

            foreach (var id in stage.Invitation.Chosen)
            {
                var to = standees[id].position - head.position; to.y = 0f;
                var d = to.magnitude;
                var bearing = Vector3.SignedAngle(Vector3.forward, to, Vector3.up);
                Assert.That(d, Is.InRange(CompanionMarks.MinDistance - 0.01f, CompanionMarks.MaxDistance + 0.01f), id + " distance");
                Assert.LessOrEqual(Mathf.Abs(bearing), CompanionMarks.MaxBearing + 0.5f, id + " bearing");
            }

            for (var i = 0; i < 400 && done == null; i++) { ConfirmInput.PressA(); yield return null; }
            CollectionAssert.AreEqual(new[] { Masters.Monet, Masters.VanGogh, Masters.Socrates }, done, "companions[] in her order");
            CollectionAssert.AreEqual(new[] { Masters.Monet, Masters.VanGogh, Masters.Socrates }, lines.Distinct().ToArray());
        }

        [UnityTest]
        public IEnumerator AfterATeleportEveryCompanionIsOnANewMarkNextFrame()
        {
            var (stage, _, head) = Company();
            stage.Preselect(Masters.DefaultTrio);
            ConfirmInput.PressA();
            yield return new WaitForSeconds(CompanyStage.StepSeconds + 0.2f);
            head.position += new Vector3(4f, 0f, 2f);
            head.rotation = Quaternion.Euler(0f, 90f, 0f);
            yield return Frames(2);
            foreach (var kv in stage.Group.Figures)
            {
                var to = kv.Value.position - head.position; to.y = 0f;
                Assert.That(to.magnitude, Is.InRange(CompanionMarks.MinDistance - 0.01f, CompanionMarks.MaxDistance + 0.01f), kv.Key);
                Assert.LessOrEqual(Vector3.Angle(head.forward, to), CompanionMarks.MaxBearing + 0.5f, kv.Key + " is in front");
            }
        }

        // ---- artwork, cards, plinth, dial ------------------------------------------------

        [UnityTest]
        public IEnumerator PointingAtAWorkForPointFourSecondsAsksForItsCardAndFourSecondsOfGazeMakesItSeen()
        {
            var frame = Make("Frame", PrimitiveType.Quad);
            Object.DestroyImmediate(frame.GetComponent<Collider>());
            frame.transform.position = new Vector3(0f, 1.5f, 4f);
            var mark = Make("Mark").transform; mark.position = new Vector3(0f, 0f, 2f);
            var w = ArtworkWatcher.Make(frame, "aic-16568", mark);
            var head = Head(new Vector3(0f, 1.6f, -2f), 180f);   // looking away, far from the mark
            w.Head = head;
            Physics.SyncTransforms();
            var cards = 0; var seen = 0;
            w.CardWanted += _ => cards++;
            w.Seen += (_, __) => seen++;
            var h = Hand();
            h.aim.SetPositionAndRotation(new Vector3(0f, 1.5f, -2f), Quaternion.identity);
            yield return new WaitForSeconds(0.25f);
            Assert.AreEqual(0, cards, "not yet at 0.25 s");
            yield return new WaitForSeconds(0.3f);
            Assert.AreEqual(1, cards, "card after the 0.4 s dwell");
            Assert.AreEqual(0, seen, "pointing is not looking");
            head.rotation = Quaternion.identity;
            yield return new WaitForSeconds(ArtworkAttention.SeenSeconds + 0.3f);
            Assert.AreEqual(1, seen);
            Assert.GreaterOrEqual(w.GazeSeconds, ArtworkAttention.SeenSeconds);
        }

        [UnityTest]
        public IEnumerator PickingACardFlipsItAndPickingTheOtherFlipsItBack()
        {
            var host = Make("Cards");
            var a = Make("Card A", PrimitiveType.Quad).transform; a.position = new Vector3(-0.3f, 1.2f, 1.5f);
            var b = Make("Card B", PrimitiveType.Quad).transform; b.position = new Vector3(0.3f, 1.2f, 1.5f);
            Physics.SyncTransforms();
            var st = CardChoiceStation.Make(host, new[] { a, b }, new[] { "Crane", "Turtle" });
            var h = Hand();
            yield return Point(h.source, h.aim, a);
            yield return new WaitForSeconds(CardChoiceStation.FlipSeconds + 0.1f);
            Assert.AreEqual(0, st.FaceUp);
            Assert.AreEqual(180f, Quaternion.Angle(Quaternion.identity, a.localRotation), 0.5f, "card A turned over");
            yield return Point(h.source, h.aim, b);
            yield return new WaitForSeconds(CardChoiceStation.FlipSeconds + 0.1f);
            Assert.AreEqual(0f, Quaternion.Angle(Quaternion.identity, a.localRotation), 0.5f, "card A back again");
            Assert.AreEqual(180f, Quaternion.Angle(Quaternion.identity, b.localRotation), 0.5f);
            Assert.IsTrue(ConfirmInput.PressA());
            Assert.AreEqual("Turtle", st.Logic.Choice.Summary);
        }

        [UnityTest]
        public IEnumerator APlinthChimesOnceAsTheVisitorApproaches()
        {
            var plinth = Make("Plinth", PrimitiveType.Cube);
            plinth.transform.position = new Vector3(0f, 0.45f, 3f);
            var c = ApproachChime.Make(plinth, ChapterSound.StoneChime);
            var head = Head(new Vector3(0f, 1.6f, 0f));
            c.Head = head;
            yield return Frames(3);
            Assert.AreEqual(0, c.Played);
            head.position = new Vector3(0f, 1.6f, 2f);
            yield return Frames(3);
            Assert.AreEqual(1, c.Played);
            Assert.AreEqual("chime-StoneChime", ChimePlayer.LastPlayed);
            yield return Frames(10);
            Assert.AreEqual(1, c.Played, "once per approach");
        }

        [UnityTest]
        public IEnumerator TwistingTheDialToDuskClicksOnceAndStartsTheEase()
        {
            var driver = Make("Driver").AddComponent<TimeRingDriver>();
            driver.keyboard = false;
            var dialGo = Make("Dial");
            dialGo.transform.position = new Vector3(0f, 1.1f, 0.6f);
            var box = dialGo.AddComponent<BoxCollider>(); box.size = new Vector3(0.4f, 0.4f, 0.08f); box.isTrigger = true;
            var ring = Make("Ring").transform; ring.SetParent(dialGo.transform, false);
            Physics.SyncTransforms();
            var dial = TimeRingDial.Make(dialGo, ring, driver);
            var clicks = new List<TimeOfDay>();
            dial.Clicked += clicks.Add;
            var h = Hand();
            var basis = Quaternion.LookRotation(dialGo.transform.forward, dialGo.transform.up);
            h.aim.SetPositionAndRotation(dialGo.transform.position + new Vector3(0f, 0.15f, -0.03f), basis);
            h.source.Grip = true;
            yield return Frames(2);
            for (var a = 0f; a <= 64f; a += 4f)
            {
                h.aim.rotation = Quaternion.AngleAxis(-a, dialGo.transform.forward) * basis;   // clockwise as the visitor sees it
                yield return null;
            }
            CollectionAssert.AreEqual(new[] { TimeOfDay.Dusk }, clicks);
            Assert.AreEqual(TimeOfDay.Dusk, driver.Ring.Chosen);
            Assert.IsTrue(driver.Ring.IsEasing, "the 4 s ease has begun");
            h.source.Grip = false;
            yield return Frames(2);
            Assert.AreEqual(DialDetents.AngleOf(2), dial.Detents.Angle, "settles on Dusk");
        }

        // ---- the lamp lights only the relief --------------------------------------------

        [UnityTest]
        public IEnumerator TheLampLightsTheReliefAndNothingElseInRange()
        {
            // Her check (2.5), adapted: the relief's lit pixels change, and a control block well inside
            // the light's range does not change at all.
            var relief = Make("Relief", PrimitiveType.Quad); relief.transform.position = new Vector3(-0.4f, 1.3f, 2f);
            var control = Make("Control", PrimitiveType.Quad); control.transform.position = new Vector3(0.4f, 1.3f, 2f);
            var lit = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            relief.GetComponent<Renderer>().sharedMaterial = lit;
            control.GetComponent<Renderer>().sharedMaterial = lit;
            LampLight.MarkRelief(relief.GetComponent<Renderer>());
            var lamp = Make("Lamp");
            lamp.transform.position = new Vector3(0f, 1.3f, 1.6f);
            var light = LampLight.Make(lamp, Vector3.zero).Light;

            var cam = Make("Probe").AddComponent<Camera>();
            cam.transform.position = new Vector3(0f, 1.3f, 0.5f);
            var rt = new RenderTexture(256, 128, 24);
            cam.targetTexture = rt;
            Texture2D Grab()
            {
                cam.Render(); RenderTexture.active = rt;
                var t = new Texture2D(256, 128, TextureFormat.RGB24, false);
                t.ReadPixels(new Rect(0, 0, 256, 128), 0, 0); t.Apply(); RenderTexture.active = null;
                return t;
            }
            yield return null;
            light.enabled = true; var on = Grab();
            light.enabled = false; var off = Grab();
            float Delta(Renderer r)
            {
                var b = r.bounds;
                var lo = cam.WorldToViewportPoint(b.min); var hi = cam.WorldToViewportPoint(b.max);
                float sum = 0; int n = 0;
                for (var y = (int)(Mathf.Min(lo.y, hi.y) * 128) + 2; y < (int)(Mathf.Max(lo.y, hi.y) * 128) - 2; y++)
                for (var x = (int)(Mathf.Min(lo.x, hi.x) * 256) + 2; x < (int)(Mathf.Max(lo.x, hi.x) * 256) - 2; x++)
                {
                    var a = on.GetPixel(x, y); var c = off.GetPixel(x, y);
                    sum += Mathf.Abs(a.r - c.r) + Mathf.Abs(a.g - c.g) + Mathf.Abs(a.b - c.b); n++;
                }
                return n > 0 ? sum / n * 255f / 3f : -1f;
            }
            var reliefDelta = Delta(relief.GetComponent<Renderer>());
            var controlDelta = Delta(control.GetComponent<Renderer>());
            rt.Release();
            Assert.Greater(reliefDelta, 1f, "the lamp lights the relief");
            Assert.AreEqual(0f, controlDelta, 1e-4, "and not the block beside it, " + Vector3.Distance(lamp.transform.position, control.transform.position).ToString("F2") + " m away");
        }
    }
}
