using MuseXR.Worlds;
using NUnit.Framework;
using UnityEngine;

namespace MusePico.Tests
{
    public class MirroredMeshTests
    {
        // A floor triangle off to the +X side, wound so its normal points up in Unity.
        static Mesh Floor()
        {
            var m = new Mesh
            {
                vertices = new[] { new Vector3(2, 0, 0), new Vector3(2, 0, 1), new Vector3(3, 0, 0) },
                triangles = new[] { 0, 1, 2 },
            };
            m.RecalculateNormals();
            return m;
        }

        [Test]
        public void TheFloorMovesToTheOtherSideOfX()
        {
            var baked = MirroredMesh.Bake(Floor(), Matrix4x4.identity);
            Assert.Less(baked.bounds.max.x, 0f);
            Assert.AreEqual(-2.5f, baked.bounds.center.x, 1e-4f);
        }

        [Test]
        public void AMirroredFloorStillFacesUp()
        {
            Assert.Greater(Floor().normals[0].y, 0.99f, "fixture");
            var baked = MirroredMesh.Bake(Floor(), Matrix4x4.identity);
            Assert.Greater(baked.normals[0].y, 0.99f,
                "a teleport ray hits the top of the floor; without reversing the winding it faces down");
        }

        [Test]
        public void TheNodeTransformIsAppliedBeforeTheMirror()
        {
            var baked = MirroredMesh.Bake(Floor(), Matrix4x4.Translate(new Vector3(1, 5, 0)));
            Assert.AreEqual(-3.5f, baked.bounds.center.x, 1e-4f);
            Assert.AreEqual(5f, baked.bounds.center.y, 1e-4f);
        }
    }
}
