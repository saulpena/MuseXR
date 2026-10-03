using System;
using System.Collections.Generic;
using System.Linq;
using MuseXR.Slots;
using NUnit.Framework;

namespace MusePico.Tests
{
    /// <summary>Her shared slot system (chatplan §3.2) and the hand interactions around it, as logic.</summary>
    public class SlotsTests
    {
        static float[] D(params float[] d) => d;

        // ---- SlotBoard -------------------------------------------------------------------

        [Test]
        public void TheSnapRadiusIsTwelveCentimetres()
        {
            var b = new SlotBoard(1, 1);
            b.Grab(0);
            Assert.AreEqual(-1, b.Move(D(0.121f)), "just outside 12 cm must not align");
            Assert.AreEqual(0, b.Move(D(0.119f)), "just inside 12 cm must align");
            Assert.AreEqual(SlotState.Aligned, b.StateOf(0));
        }

        [Test]
        public void ReleasedThirtyCentimetresAwayItFloatsHomeAndTheSlotStaysEmpty()
        {
            // Her Palace check: "Release 30 cm from the slot and the crane floats back to its plinth."
            var b = new SlotBoard(1, 2);
            var cues = new List<SlotEvent>();
            b.Cue += cues.Add;
            b.Grab(0);
            Assert.AreEqual(-1, b.Release(D(0.30f)));
            Assert.AreEqual(SlotState.Empty, b.StateOf(0));
            Assert.AreEqual(-1, b.PlacedPiece);
            Assert.AreEqual(SlotCue.FloatHome, cues.Last().Cue);
            Assert.AreEqual(0, cues.Last().Piece);
            Assert.AreEqual(ChoiceConfirm.Phase.Open, b.Choice.Current);
        }

        [Test]
        public void ReleasedInsideTheRadiusItIsPlacedAndTheStripAppears()
        {
            var b = new SlotBoard(1, 2);
            var cues = new List<SlotCue>();
            b.Cue += e => cues.Add(e.Cue);
            b.Grab(1);
            b.Move(D(0.05f));
            Assert.AreEqual(0, b.Release(D(0.05f), "Turtle · 35°"));
            Assert.AreEqual(SlotState.Placed, b.StateOf(0));
            Assert.AreEqual(1, b.PlacedPiece);
            Assert.AreEqual(ChoiceConfirm.Phase.Pending, b.Choice.Current);
            Assert.AreEqual("Keep this moment? Turtle · 35°", b.Choice.StripLine);
            CollectionAssert.AreEqual(new[] { SlotCue.Aligned, SlotCue.Placed }, cues);
        }

        [Test]
        public void TheNearestOfTwoSocketsWins()
        {
            // Grotto: one lamp, sockets "detail" and "whole".
            var b = new SlotBoard(2, 1);
            b.Grab(0);
            Assert.AreEqual(1, b.Move(D(0.10f, 0.04f)));
            Assert.AreEqual(SlotState.Empty, b.StateOf(0));
            Assert.AreEqual(SlotState.Aligned, b.StateOf(1));
            Assert.AreEqual(0, b.Move(D(0.02f, 0.08f)), "moving across hands the halo to the other socket");
            Assert.AreEqual(SlotState.Empty, b.StateOf(1));
        }

        [Test]
        public void AlignmentCuesFireOnceOnEntryAndOnceOnExit()
        {
            var b = new SlotBoard(1, 1);
            var cues = new List<SlotCue>();
            b.Cue += e => cues.Add(e.Cue);
            b.Grab(0);
            foreach (var d in new[] { 0.5f, 0.2f, 0.1f, 0.08f, 0.06f, 0.11f, 0.3f, 0.4f })
                b.Move(D(d));
            CollectionAssert.AreEqual(new[] { SlotCue.Aligned, SlotCue.Unaligned }, cues,
                "the light haptic must fire once per entry, not every frame inside the radius");
        }

        [Test]
        public void OneChoicePerChapter_ASecondPieceCannotAlignWhileOneIsPlaced()
        {
            var b = new SlotBoard(1, 2);
            b.Grab(0); b.Release(D(0f), "Crane");
            Assert.IsTrue(b.Grab(1));
            Assert.AreEqual(-1, b.Move(D(0f)));
            Assert.AreEqual(-1, b.Release(D(0f)), "the turtle floats home; the crane stays");
            Assert.AreEqual(0, b.PlacedPiece);
        }

        [Test]
        public void UndoAt2_9SecondsRestoresTheEmptySlot()
        {
            // §3.1: "Placing then pressing B at 2.9 s restores the prior state; at 3.1 s it does not."
            var b = new SlotBoard(1, 1);
            b.Grab(0); b.Release(D(0f), "Crane");
            for (var i = 0; i < 29; i++) b.Tick(0.1f);
            Assert.IsTrue(b.Undo());
            Assert.AreEqual(SlotState.Empty, b.StateOf(0));
            Assert.AreEqual(-1, b.PlacedPiece);
            Assert.AreEqual(ChoiceConfirm.Phase.Open, b.Choice.Current);
        }

        [Test]
        public void UndoAt3_1SecondsDoesNothing()
        {
            var b = new SlotBoard(1, 1);
            b.Grab(0); b.Release(D(0f), "Crane");
            for (var i = 0; i < 31; i++) b.Tick(0.1f);
            Assert.IsFalse(b.Undo());
            Assert.AreEqual(SlotState.Placed, b.StateOf(0));
            Assert.AreEqual(0, b.PlacedPiece);
        }

        [Test]
        public void AfterTheBarRunsOutLiftingItOutStillChangesIt()
        {
            // Her Palace undo: "B within 3s, or lift it back out to reselect".
            var b = new SlotBoard(1, 2);
            b.Grab(0); b.Release(D(0f), "Crane");
            b.Tick(10f);
            Assert.IsTrue(b.Grab(0));
            Assert.AreEqual(SlotState.Empty, b.StateOf(0));
            Assert.AreEqual(ChoiceConfirm.Phase.Open, b.Choice.Current);
            b.Release(D(0.5f));
            Assert.IsTrue(b.Grab(1));
            Assert.AreEqual(0, b.Release(D(0.01f), "Turtle"));
            Assert.AreEqual(1, b.PlacedPiece);
        }

        [Test]
        public void ConfirmLocksThePieceUntilReopened()
        {
            var b = new SlotBoard(1, 1);
            b.Grab(0); b.Release(D(0f), "Crane");
            Assert.IsTrue(b.Confirm());
            Assert.IsFalse(b.Grab(0), "a kept choice cannot be lifted out");
            Assert.IsFalse(b.Undo());
            b.Reopen();
            Assert.IsTrue(b.Grab(0), "the roundtable reopens it");
            Assert.AreEqual(SlotState.Empty, b.StateOf(0));
        }

        [Test]
        public void ConfirmWithNothingPlacedDoesNothing()
        {
            var b = new SlotBoard(1, 1);
            Assert.IsFalse(b.Confirm());
            b.Grab(0);
            Assert.IsFalse(b.Confirm(), "holding is not choosing");
        }

        [Test]
        public void OnlyOnePieceInHandAtATime()
        {
            var b = new SlotBoard(1, 2);
            Assert.IsTrue(b.Grab(0));
            Assert.IsFalse(b.Grab(1));
        }

        // ---- ChoiceConfirm (the stroke and the ring, which have no slot) -------------------

        [Test]
        public void RedoAnyTimeKeepsBWorkingUntilA()
        {
            // Van Gogh "B redraws ... unlimited retakes"; Monet "B turns the ring again".
            var c = new ChoiceConfirm(redoAnyTime: true);
            c.Make("Cobalt · The Bedroom");
            c.Tick(60f);
            Assert.IsTrue(c.Redo());
            Assert.AreEqual(ChoiceConfirm.Phase.Open, c.Current);
            c.Make("Chrome yellow · The Bedroom");
            Assert.IsTrue(c.Confirm());
            Assert.IsFalse(c.Redo());
        }

        [Test]
        public void TheUndoBarDrainsFromFullToEmptyOverThreeSeconds()
        {
            var c = new ChoiceConfirm();
            c.Make("Dusk · Water Lilies");
            Assert.AreEqual(1f, c.UndoFraction, 1e-5);
            c.Tick(1.5f);
            Assert.AreEqual(0.5f, c.UndoFraction, 1e-5);
            c.Tick(5f);
            Assert.AreEqual(0f, c.UndoFraction);
            Assert.IsTrue(c.CanConfirm);
        }

        // ---- SlotLook: shape and text, never colour alone --------------------------------

        [Test]
        public void EveryStateHasItsOwnShapeAndWords()
        {
            var looks = new[] { SlotState.Empty, SlotState.Aligned, SlotState.Placed }.Select(s => SlotLook.For(s, 3f)).ToArray();
            Assert.AreEqual(3, looks.Select(l => l.Shape).Distinct().Count(), "shape alone must tell the states apart");
            Assert.AreEqual(3, looks.Select(l => l.Title).Distinct().Count());
            Assert.AreEqual(3, looks.Select(l => l.Badge).Distinct().Count());
            Assert.AreEqual("Hold Grip, place here", looks[0].Caption);
            Assert.AreEqual("Undo 3s · B", looks[2].Caption);
            Assert.AreEqual(HapticPulse.Light, looks[1].Haptic);
            Assert.AreEqual(HapticPulse.Confirm, looks[2].Haptic);
        }

        [Test]
        public void ThePlacedCaptionCountsDown()
        {
            Assert.AreEqual("Undo 2s · B", SlotLook.For(SlotState.Placed, 1.2f).Caption);
            StringAssert.Contains("lift out", SlotLook.For(SlotState.Placed, 0f).Caption);
        }

        [Test]
        public void EachChapterHasItsOwnSound()
        {
            Assert.AreEqual(ChapterSound.BronzeBell, SlotRules.SoundOf(Chapter.Palace));
            Assert.AreEqual(ChapterSound.StoneChime, SlotRules.SoundOf(Chapter.Grotto));
            Assert.AreEqual(ChapterSound.Wood, SlotRules.SoundOf(Chapter.VanGogh));
            Assert.AreEqual(ChapterSound.Water, SlotRules.SoundOf(Chapter.Monet));
        }

        // ---- FloatHome -------------------------------------------------------------------

        [Test]
        public void FloatHomeStartsAndEndsOnTheLineAndArcsBetween()
        {
            var dist = 0.3f;
            var dur = FloatHome.Duration(dist);
            Assert.AreEqual(0f, FloatHome.Along(0f, dur), 1e-5);
            Assert.AreEqual(1f, FloatHome.Along(dur, dur), 1e-5);
            Assert.AreEqual(0f, FloatHome.Lift(0f, dur, dist), 1e-5);
            Assert.AreEqual(0f, FloatHome.Lift(dur, dur, dist), 1e-5);
            Assert.Greater(FloatHome.Lift(dur * 0.5f, dur, dist), 0.05f, "it must rise over the plinth edge");
            Assert.LessOrEqual(FloatHome.Lift(dur * 0.5f, dur, 10f), FloatHome.MaxLift + 1e-5);
            Assert.Less(FloatHome.Duration(0.1f), FloatHome.Duration(1f));
            Assert.LessOrEqual(FloatHome.Duration(50f), FloatHome.MaxSeconds);
        }

        // ---- StickStepper ----------------------------------------------------------------

        [Test]
        public void OneFlickIsOneFifteenDegreeStep()
        {
            var s = new StickStepper();
            var steps = 0;
            foreach (var x in new[] { 0f, 0.4f, 0.8f, 1f, 1f, 1f, 0.9f, 0.5f, 0.2f, 0f })
                steps += s.Update(x);
            Assert.AreEqual(1, steps, "holding the stick over must not keep turning");
            Assert.AreEqual(15f, s.Degrees);
        }

        [Test]
        public void ThreeFlicksRightAndOneLeftIsThirtyDegrees()
        {
            var s = new StickStepper();
            foreach (var x in new[] { 1f, 0f, 1f, 0f, 1f, 0f, -1f, 0f }) s.Update(x);
            Assert.AreEqual(30f, s.Degrees);
            Assert.AreEqual(345, StickStepper.Display(-15f));
            Assert.AreEqual(35, StickStepper.Display(395f));
        }

        [Test]
        public void ARestingStickOffCentreDoesNothing()
        {
            var s = new StickStepper();
            for (var i = 0; i < 100; i++) Assert.AreEqual(0, s.Update(0.5f));
        }

        // ---- DialDetents -----------------------------------------------------------------

        [Test]
        public void TwistingRightFromAfternoonReachesDusk()
        {
            var d = new DialDetents();
            Assert.AreEqual(1, d.Detent);
            d.Grab(0f);
            var crossed = new List<int>();
            for (var a = 0f; a <= 70f; a += 2f) { var c = d.Turn(a); if (c >= 0) crossed.Add(c); }
            CollectionAssert.AreEqual(new[] { 2 }, crossed, "one tick, at Dusk");
            d.Release();
            Assert.AreEqual(60f, d.Angle);
        }

        [Test]
        public void ATremorOnTheBoundaryDoesNotChatter()
        {
            var d = new DialDetents();
            d.Grab(0f);
            var ticks = 0;
            for (var i = 0; i < 50; i++) ticks += d.Turn(30f + (i % 2 == 0 ? 3f : -3f)) >= 0 ? 1 : 0;
            Assert.AreEqual(0, ticks);
            Assert.AreEqual(1, d.Detent);
        }

        [Test]
        public void TheDialStopsAtTheEndsAndRespondsAtOnceComingBack()
        {
            var d = new DialDetents(2);
            d.Grab(0f);
            for (var a = 0f; a <= 150f; a += 5f) d.Turn(a);
            Assert.AreEqual(DialDetents.AngleOf(2) + DialDetents.Overtravel, d.Angle, 1e-4);
            d.Turn(140f);
            Assert.AreEqual(DialDetents.AngleOf(2) + DialDetents.Overtravel - 10f, d.Angle, 1e-4,
                "no dead zone after pushing past the stop");
        }

        [Test]
        public void TwistAcrossPlusMinus180DoesNotJump()
        {
            var d = new DialDetents();
            d.Grab(170f);
            d.Turn(178f);
            d.Turn(-176f);   // +6 more
            Assert.AreEqual(14f, d.Angle, 1e-3);
        }

        // ---- ChimeSynth: checks that fail if the sounds are wrong or the same -----------

        static float Goertzel(float[] x, int rate, float hz, int from, int count)
        {
            var w = 2.0 * Math.PI * hz / rate;
            var c = 2.0 * Math.Cos(w);
            double s0, s1 = 0, s2 = 0;
            for (var i = from; i < from + count && i < x.Length; i++) { s0 = x[i] + c * s1 - s2; s2 = s1; s1 = s0; }
            return (float)Math.Sqrt(s1 * s1 + s2 * s2 - c * s1 * s2) / count;
        }

        static float Rms(float[] x, float fromShare, float toShare)
        {
            int a = (int)(x.Length * fromShare), b = (int)(x.Length * toShare);
            double sum = 0; for (var i = a; i < b; i++) sum += x[i] * x[i];
            return (float)Math.Sqrt(sum / Math.Max(1, b - a));
        }

        [Test]
        public void EveryChimeIsNormalisedAndFinite()
        {
            foreach (ChapterSound s in Enum.GetValues(typeof(ChapterSound)))
            {
                var x = ChimeSynth.Render(s);
                Assert.AreEqual((int)(ChimeSynth.Seconds(s) * ChimeSynth.SampleRate), x.Length, s.ToString());
                Assert.IsTrue(x.All(v => !float.IsNaN(v) && !float.IsInfinity(v)), s + " has NaN");
                Assert.AreEqual(ChimeSynth.Peak, x.Max(Math.Abs), 1e-4, s.ToString());
            }
        }

        [Test]
        public void EachChimeSoundsAtItsOwnPitch()
        {
            const int rate = ChimeSynth.SampleRate;
            var pitch = new Dictionary<ChapterSound, float>
            {
                { ChapterSound.BronzeBell, ChimeSynth.BellHz },
                { ChapterSound.StoneChime, ChimeSynth.StoneHz },
                { ChapterSound.Wood, ChimeSynth.WoodHz },
            };
            foreach (var kv in pitch)
            {
                var x = ChimeSynth.Render(kv.Key);
                var own = Goertzel(x, rate, kv.Value, 2000, 8000);
                foreach (var other in pitch.Where(o => o.Key != kv.Key))
                    Assert.Greater(own, 3f * Goertzel(x, rate, other.Value, 2000, 8000),
                        kv.Key + " should be strongest at its own pitch, not at " + other.Key + "'s");
            }
        }

        [Test]
        public void TheBellRingsLongestAndTheWoodIsShortest()
        {
            float Tail(ChapterSound s) { var x = ChimeSynth.Render(s); var n = (int)(0.5f * ChimeSynth.SampleRate); double e = 0; for (var i = n; i < Math.Min(x.Length, n + 2000); i++) e += x[i] * x[i]; return (float)e; }
            Assert.Greater(Tail(ChapterSound.BronzeBell), Tail(ChapterSound.StoneChime));
            Assert.Greater(Tail(ChapterSound.StoneChime), Tail(ChapterSound.Wood));
            Assert.Greater(Rms(ChimeSynth.Render(ChapterSound.BronzeBell), 0.5f, 0.75f), 0.01f, "the bell is still sounding at 2 s");
        }

        [Test]
        public void AWaterDropRisesInPitch()
        {
            var x = ChimeSynth.Render(ChapterSound.Water);
            int Crossings(int from, int count) { var c = 0; for (var i = from + 1; i < from + count; i++) if ((x[i - 1] < 0) != (x[i] < 0)) c++; return c; }
            var early = Crossings((int)(0.002f * ChimeSynth.SampleRate), 441);
            var late = Crossings((int)(0.08f * ChimeSynth.SampleRate), 441);
            Assert.Greater(late, early * 1.4f, "Minnaert: a closing bubble's pitch rises");
        }
    }
}
