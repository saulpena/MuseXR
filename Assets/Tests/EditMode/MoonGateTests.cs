using MuseXR.Interaction;
using MuseXR.Worlds;
using NUnit.Framework;
using UnityEngine;

namespace MusePico.Tests
{
    /// <summary>The Palace's moon gate: the keyhole the grotto shows through (her chapter A transition).</summary>
    public class MoonGateTests
    {
        const float R = MoonGate.Radius;
        const float C = MoonGate.CentreHeight;
        const float P = MoonGate.PassageWidth;

        [Test]
        public void TheKeyholeIsTheRoundOpeningAndThePassageBelowIt()
        {
            Assert.IsTrue(MoonGate.InKeyhole(0f, C, R, C, P), "the centre of the round opening");
            Assert.IsTrue(MoonGate.InKeyhole(0f, 0.05f, R, C, P), "the threshold: walk through at floor level");
            Assert.IsTrue(MoonGate.InKeyhole(0f, 1.6f, R, C, P), "eye height on the axis");
            Assert.IsFalse(MoonGate.InKeyhole(R * 0.95f, 0.1f, R, C, P), "the wall beside the passage, at the floor");
            Assert.IsFalse(MoonGate.InKeyhole(R * 0.95f, C + R * 0.95f, R, C, P), "the wall in the corner above the circle");
        }

        [Test]
        public void TheMaskMeshFillsTheUnitQuadsWidthAndHeightAndNoMore()
        {
            var m = MoonGate.KeyholeMesh(R, C, P);
            var b = m.bounds;
            Assert.AreEqual(-0.5f, b.min.x, 1e-3f); Assert.AreEqual(0.5f, b.max.x, 1e-3f);
            Assert.AreEqual(-0.5f, b.min.y, 1e-3f, "down to the floor");
            Assert.AreEqual(0.5f, b.max.y, 1e-3f, "up to the top of the round opening");
            Assert.Greater(m.triangles.Length / 3, 40);
            Object.DestroyImmediate(m);
        }

        [Test]
        public void EveryMaskVertexLiesOnTheKeyholeInMetres()
        {
            var m = MoonGate.KeyholeMesh(R, C, P);
            float w = 2f * R, h = C + R;
            foreach (var v in m.vertices)
            {
                float x = v.x * w, y = (v.y + 0.5f) * h;   // back to metres from the axis and the floor
                Assert.IsTrue(MoonGate.InKeyhole(x * 0.999f, Mathf.Lerp(y, C, 0.001f), R, C, P),
                              "vertex at " + x.ToString("F2") + ", " + y.ToString("F2") + " m");
            }
            Object.DestroyImmediate(m);
        }

        [Test]
        public void TheGateIsOnTheRightHandWallAndOpensOutOfTheHall()
        {
            // Diagram A: the moon-gate exit is in the right-hand wall as the visitor enters facing the
            // throne (-Z), whose right is -X in this world.
            Assert.Less(PalaceChapterInteractions.MoonGateFloor.x, -5f);
        }
    }
}
