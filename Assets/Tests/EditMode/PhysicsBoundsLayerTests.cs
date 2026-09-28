using MusePico.Worlds;
using NUnit.Framework;
using UnityEngine;

namespace MusePico.Tests
{
    public class PhysicsBoundsLayerTests
    {
        [Test]
        public void TheWallLayerExistsInTheProject()
        {
            Assert.GreaterOrEqual(PhysicsBounds.Layer, 0,
                "without the layer the invisible walls fall back to Default and block every pointing ray");
        }

        [Test]
        public void WallsAreOnTheirOwnLayerAndTheFloorIsNot()
        {
            var root = PhysicsBounds.Build("test bounds", -2f, 2f, -2f, 2f, 0f);
            try
            {
                var wallLayer = PhysicsBounds.Layer;
                foreach (Transform child in root.transform)
                {
                    if (child.name == "Floor") Assert.AreEqual(0, child.gameObject.layer, "floor stays Default");
                    else Assert.AreEqual(wallLayer, child.gameObject.layer, child.name);
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void ARayFromInsideReachesPastTheWallWhenItSkipsTheWallLayer()
        {
            // The fault this layer fixes: a painting 0.3 m behind the walk box's wall.
            var root = PhysicsBounds.Build("test bounds", -2f, 2f, -2f, 2f, 0f);
            var painting = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                painting.transform.position = new Vector3(2.3f + 0.5f, 1.5f, 0f);   // just beyond Wall +X's inner face
                painting.transform.localScale = new Vector3(0.02f, 1f, 1f);
                Physics.SyncTransforms();

                var pointing = ~(1 << PhysicsBounds.Layer);
                Assert.IsTrue(Physics.Raycast(new Vector3(0f, 1.5f, 0f), Vector3.right, out var hit, 10f, pointing));
                Assert.AreSame(painting, hit.collider.gameObject);

                Assert.IsTrue(Physics.Raycast(new Vector3(0f, 1.5f, 0f), Vector3.right, out var blocked, 10f, ~0));
                Assert.AreEqual("Wall +X", blocked.collider.name, "a mask that includes the layer (teleport) still stops at the wall");
            }
            finally
            {
                Object.DestroyImmediate(painting);
                Object.DestroyImmediate(root);
            }
        }
    }
}
