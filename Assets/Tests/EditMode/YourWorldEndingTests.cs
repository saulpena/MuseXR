using System.Collections.Generic;
using MusePico.Dialogue;
using MuseXR.Interaction;
using NUnit.Framework;
using UnityEngine;

namespace MusePico.Tests
{
    /// <summary>Your world reads the journey record in her words (MUSE-VR-design 4.5).</summary>
    public class YourWorldEndingTests
    {
        [Test]
        public void HerMementoLinesComeFromTheRecordInWalkingOrder()
        {
            var r = new JourneyRecord();
            r.SetMonet(new JourneyRecord.MonetChoice { Preset = "dusk", ArtworkId = "aic-16568", Reason = "Water Lilies" });
            r.SetPalace(new JourneyRecord.PalaceChoice { Object = "turtle", YawDeg = 90f, Reason = "Because it is slow, but it keeps going" });
            r.SetGrotto(new JourneyRecord.GrottoChoice { LampSlot = "detail", ExhibitId = "aic-142512" });
            r.SetVanGogh("#2F4F8F", "aic-28560", new List<float[]> { new[] { 0f, 0f, 0f }, new[] { 0f, 1f, 0f } });
            var lines = YourWorldEnding.ChoiceLines(r, id => id == "aic-28560" ? "The Bedroom" : id);
            Assert.AreEqual(4, lines.Count);
            Assert.AreEqual("Turtle · facing east · “Because it is slow, but it keeps going”", lines[0].line);
            Assert.AreEqual("Lamp on the detail", lines[1].line);
            Assert.AreEqual("Cobalt stroke · The Bedroom", lines[2].line);
            Assert.AreEqual("Dusk · Water Lilies", lines[3].line);
        }

        [Test]
        public void ASkippedChapterLeavesNoLine()
        {
            var r = new JourneyRecord();
            r.SetGrotto(new JourneyRecord.GrottoChoice { LampSlot = "whole" });
            var lines = YourWorldEnding.ChoiceLines(r);
            Assert.AreEqual(1, lines.Count);
            Assert.AreEqual("grotto", lines[0].chapter);
            Assert.IsTrue(YourWorldEnding.NothingRecorded(new JourneyRecord()));
            Assert.IsFalse(YourWorldEnding.NothingRecorded(r));
        }

        [Test]
        public void CopiesLineBothSidesOfTheCorridor()
        {
            Assert.Less(YourWorldEnding.CopySlot(0).x, 0f);
            Assert.Greater(YourWorldEnding.CopySlot(1).x, 0f);
            Assert.Greater(YourWorldEnding.CopySlot(2).z, YourWorldEnding.CopySlot(1).z);
            Assert.AreEqual("north-west", YourWorldEnding.Direction(-40f));
            Assert.AreEqual("Your", YourWorldEnding.PotName("#ff00ff"));
        }
    }
}
