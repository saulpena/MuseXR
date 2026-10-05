using MusePico.Dialogue;
using MuseXR.Interaction;
using NUnit.Framework;
using UnityEngine;

namespace MusePico.Tests
{
    /// <summary>Her "Based on" line: each roundtable thread cites one real record, and the label comes from it.</summary>
    public class RoundtableCitationTests
    {
        static readonly string[] Records = { "palace", "vangogh", "monet", "question" };

        static string Payload(string a, string b, string c) =>
            "{\"worldTitle\":\"T\",\"synthesis\":\"S\",\"threads\":[" +
            "{\"speakerId\":\"monet\",\"speaker\":\"Claude Monet\",\"text\":\"x\",\"basedOn\":\"" + a + "\"}," +
            "{\"speakerId\":\"van_gogh\",\"speaker\":\"Vincent van Gogh\",\"text\":\"y\",\"basedOn\":\"" + b + "\"}," +
            "{\"speakerId\":\"socrates\",\"speaker\":\"Socrates\",\"text\":\"z\",\"basedOn\":\"" + c + "\"}]}";

        [Test]
        public void EachThreadCitingADifferentRealRecordIsValid()
        {
            Assert.IsNull(RoundtableClient.DescribeInvalid(Payload("monet", "vangogh", "palace"), 3, Records));
        }

        [Test]
        public void ACitationOfARecordThatDoesNotExistIsRefused()
        {
            StringAssert.Contains("not one of the records", RoundtableClient.DescribeInvalid(Payload("monet", "grotto", "palace"), 3, Records));
        }

        [Test]
        public void TwoThreadsCitingTheSameRecordAreRefusedWhenThereAreEnough()
        {
            StringAssert.Contains("same record", RoundtableClient.DescribeInvalid(Payload("monet", "monet", "palace"), 3, Records));
        }

        [Test]
        public void WithoutRecordsTheOldContractStands()
        {
            Assert.IsNull(RoundtableClient.DescribeInvalid(Payload("", "", ""), 3));
        }

        [Test]
        public void YourWorldsStrokeIsOneAndAHalfTimesAsBroad()
        {
            Assert.AreEqual(StrokeBrush.WidthAt(0.5f) * 1.5f, StrokeBrush.WidthAt(0.5f, 1.5f), 1e-5f);
            var pts = new[] { new Vector3(0, 2, 0), new Vector3(1, 2.5f, 0), new Vector3(2, 2, 0) };
            var ups = StrokeBrush.LevelUps(pts);
            foreach (var u in ups) Assert.AreEqual(0f, u.y, 1e-4f, "level, so it shows its breadth to someone underneath");
        }
    }
}
