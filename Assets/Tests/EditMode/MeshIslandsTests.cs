using MuseXR.Interaction;
using NUnit.Framework;
using UnityEngine;

namespace MusePico.Tests
{
    public class MeshIslandsTests
    {
        /// <summary>Two unit quads 3 m apart, each with its corners split as along a UV seam.</summary>
        static Mesh TwoFigures()
        {
            var m = new Mesh();
            Vector3[] Quad(float x) => new[] { new Vector3(x, 0, 0), new Vector3(x + 1, 0, 0), new Vector3(x + 1, 1, 0), new Vector3(x, 1, 0) };
            var a = Quad(0f); var b = Quad(3f);
            // Each quad's two triangles use their own copies of the shared diagonal corners: a seam.
            m.vertices = new[] { a[0], a[1], a[2], a[0], a[2], a[3], b[0], b[1], b[2], b[0], b[2], b[3] };
            m.triangles = new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 };
            return m;
        }

        [Test]
        public void EachSeparateFigureIsOnePart()
        {
            var parts = MeshIslands.Bounds(TwoFigures(), 0.4f, 10);
            Assert.AreEqual(2, parts.Count, "two figures, seams welded");
            foreach (var p in parts) Assert.AreEqual(1f, p.size.x, 1e-4f);
        }

        [Test]
        public void SmallPartsAreLeftOut()
        {
            Assert.AreEqual(0, MeshIslands.Bounds(TwoFigures(), 2f, 10).Count);
            Assert.AreEqual(1, MeshIslands.Bounds(TwoFigures(), 0.4f, 1).Count, "capped at max");
        }

        /// <summary>Two posts 3 m apart standing on one slab, all one joined mesh: two figures, not one.</summary>
        [Test]
        public void FiguresOnAJoinedPlatformAreFoundApart()
        {
            var m = new Mesh();
            var verts = new System.Collections.Generic.List<Vector3>();
            var tris = new System.Collections.Generic.List<int>();
            void Tri(Vector3 a, Vector3 b, Vector3 c) { tris.Add(verts.Count); verts.Add(a); tris.Add(verts.Count); verts.Add(b); tris.Add(verts.Count); verts.Add(c); }
            Tri(new Vector3(-2, 0, -1), new Vector3(2, 0, -1), new Vector3(2, 0, 1));          // the slab
            Tri(new Vector3(-2, 0, -1), new Vector3(2, 0, 1), new Vector3(-2, 0, 1));
            foreach (var x in new[] { -1.5f, 1.5f })                                           // two posts, 2 m tall
            {
                Tri(new Vector3(x, 0, 0), new Vector3(x + 0.05f, 0, 0), new Vector3(x + 0.05f, 2, 0));
                Tri(new Vector3(x, 0, 0), new Vector3(x + 0.05f, 2, 0), new Vector3(x, 2, 0));   // narrower than one grid cell: one figure each
            }
            m.SetVertices(verts); m.SetTriangles(tris, 0); m.RecalculateBounds();
            var figures = MeshIslands.Figures(m, 0.45f, 0.25f, 64, 1, 10);
            Assert.AreEqual(2, figures.Count);
            foreach (var f in figures) Assert.Less(f.size.x, 0.5f, "each post boxed on its own");
        }
    }
}
