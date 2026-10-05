using MuseXR.Worlds;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MusePico.Tests
{
    /// <summary>
    /// The museum's pictures import stretched to a power of two so Android will compress them; every frame, crop and
    /// card still has to take the photograph's own proportions. Each test here fails if that breaks: a missing or
    /// stale table answers with the stretched ratio, not the source one.
    /// </summary>
    public class PictureAspectTests
    {
        const string Folder = "Assets/Museum/Artworks";

        [Test]
        public void EveryPictureKeepsItsMastersProportions()
        {
            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { Folder });
            Assert.Greater(guids.Length, 40, "the museum's pictures were not found");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var master = "Tools/artworks/source/" + System.IO.Path.GetFileName(path);
                Assert.IsTrue(System.IO.File.Exists(master), path + " has no master photograph in Tools/artworks/source");
                var probe = new Texture2D(2, 2);
                probe.LoadImage(System.IO.File.ReadAllBytes(master));
                float expected = probe.width / (float)probe.height;
                Object.DestroyImmediate(probe);
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                Assert.AreEqual(expected, PictureAspect.Of(tex), 1e-4f, path + ": run MuseXR > Museum > Write Picture Aspects");
            }
        }

        [Test]
        public void EveryPictureImportsAsAPowerOfTwoSoAndroidCompressesIt()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Folder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                Assert.IsTrue(Mathf.IsPowerOfTwo(tex.width) && Mathf.IsPowerOfTwo(tex.height),
                              path + " is " + tex.width + "x" + tex.height + ": with mipmaps, Android ships it uncompressed");
                var android = ((TextureImporter)AssetImporter.GetAtPath(path)).GetPlatformTextureSettings("Android");
                Assert.IsTrue(android.overridden && android.format == TextureImporterFormat.ASTC_6x6, path + " is not ASTC on Android");
            }
        }

        [Test]
        public void UnlistedTexturesAnswerFromTheirOwnSize()
        {
            var t = new Texture2D(300, 100) { name = "not-a-museum-picture" };
            Assert.AreEqual(3f, PictureAspect.Of(t), 1e-5f);
            Assert.AreEqual(1.3f, PictureAspect.Of(null, 1.3f));
            Object.DestroyImmediate(t);
        }

        [Test]
        public void TheTableParsesAndSkipsJunk()
        {
            var d = PictureAspect.Parse("# header\naic-1 1679 2048\n\nbroken line\naic-2 0 5\naic-3 843 664\r\n");
            Assert.AreEqual(2, d.Count);
            Assert.AreEqual(1679f / 2048f, d["aic-1"], 1e-6f);
            Assert.AreEqual(843f / 664f, d["aic-3"], 1e-6f);
        }
    }
}
