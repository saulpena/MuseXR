using System.Linq;
using MusePico.Dialogue;
using NUnit.Framework;

namespace MusePico.Tests
{
    public class MemoryWorldTests
    {
        [Test]
        public void HerManorIsTheSameCloudEveryTime()
        {
            var a = MemoryWorld.Create();
            var b = MemoryWorld.Create();
            Assert.AreEqual(a.Count, b.Count);
            Assert.AreEqual(a[1234].X, b[1234].X, 1e-6f, "her hash is deterministic");
            Assert.Greater(a.Count, 6000, "floor, facade, arches, colonnades, branches and dust");
        }

        [Test]
        public void TheFacadeHasHerDoorAndWindowsCutOut()
        {
            // Stone points on the facade plane never fall inside the central door or the two windows.
            var facade = MemoryWorld.Create().Where(p => p.Family == "stone" && System.Math.Abs(p.Z - 15.5f) < .1f).ToList();
            Assert.IsNotEmpty(facade);
            Assert.IsFalse(facade.Any(p => System.Math.Abs(p.X) < 1.3f && p.Y < 1.4f && p.Y > -2.3f), "the central door is open");
            Assert.IsFalse(facade.Any(p => System.Math.Abs(p.X - 3.35f) < .68f && p.Y > -.36f && p.Y < 1.46f), "the right window is open");
        }

        [Test]
        public void EachAnswerSettlesIntoItsOwnShape()
        {
            MemoryWorld.FinalTarget("invention", 0, 300, .5f, .5f, 0f, out var x0, out var y0, out _);
            MemoryWorld.FinalTarget("invention", 17, 300, .5f, .5f, 0f, out var x17, out _, out _);
            Assert.Less(x0, -4.5f, "the facade grid starts at the left");
            Assert.Greater(x17, 4.5f, "eighteen columns across");
            MemoryWorld.FinalTarget("perception", 0, 300, 0f, .5f, 0f, out var px, out _, out _);
            MemoryWorld.FinalTarget("perception", 0, 300, 1f, .5f, 0f, out var qx, out _, out _);
            Assert.Greater(qx - px, 10f, "a long horizon band");
        }
    }
}
