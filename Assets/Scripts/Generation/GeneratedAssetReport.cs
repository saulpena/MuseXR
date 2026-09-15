using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace MusePico.Generation
{
    public enum AssetVerdict
    {
        /// <summary>Within budget. Ship it.</summary>
        Good,
        /// <summary>Usable, but it is spending more than its share of the frame.</summary>
        Heavy,
        /// <summary>Over budget on a standalone headset. Regenerate smaller or keep it off-device.</summary>
        OverBudget,
    }

    /// <summary>
    /// What actually came back, measured — as opposed to what was asked for.
    ///
    /// The request carries a <c>face_limit</c>, but a limit is a ceiling the generator aims at,
    /// not a contract, and it says nothing at all about textures. Texture memory is usually the
    /// real cost: a single 2048² RGBA map is 16 MB uncompressed, and a PBR material has three of
    /// them. Two textured pieces can cost more memory than the entire triangle budget of the
    /// scene they sit in.
    ///
    /// So every generated object is measured on arrival and the numbers are shown in the harness.
    /// "Is Tripo output performant for VR" is then a reading rather than an opinion.
    /// </summary>
    public struct GeneratedAssetReport
    {
        public int Triangles;
        public int Vertices;
        public int Meshes;
        public int Materials;
        public int Textures;
        /// <summary>Uncompressed GPU bytes for every unique texture. This is the number that bites.</summary>
        public long TextureBytes;
        public int LargestTextureSize;
        public long DownloadBytes;
        public float GenerationSeconds;
        public float CreditsSpent;

        public AssetVerdict Verdict => Judge(Triangles, TextureBytes);

        // Thresholds for ONE object on a standalone headset, not for a whole scene. A PICO 4 or
        // Quest 3 scene budget is roughly 300-500k triangles and a few hundred MB of texture
        // across everything, so a single exhibit that eats 25k triangles and 24 MB is already
        // taking a visible slice of it.
        public const int GoodTriangles = 25000;
        public const int HeavyTriangles = 60000;
        public const long GoodTextureBytes = 24L * 1024 * 1024;
        public const long HeavyTextureBytes = 64L * 1024 * 1024;

        /// <summary>Pure, so the thresholds are pinned by tests rather than by eyeballing a build.</summary>
        public static AssetVerdict Judge(int triangles, long textureBytes)
        {
            if (triangles > HeavyTriangles || textureBytes > HeavyTextureBytes) return AssetVerdict.OverBudget;
            if (triangles > GoodTriangles || textureBytes > GoodTextureBytes) return AssetVerdict.Heavy;
            return AssetVerdict.Good;
        }

        /// <summary>Uncompressed GPU bytes for one texture, mip chain included (+1/3).</summary>
        public static long EstimateTextureBytes(int width, int height, bool hasMipmaps, int bytesPerPixel = 4)
        {
            var basis = (long)width * height * bytesPerPixel;
            return hasMipmaps ? basis * 4 / 3 : basis;
        }

        /// <summary>
        /// Walks an imported hierarchy and counts it. Materials and textures are de-duplicated by
        /// instance — a glTF with one material on six primitives costs one material, and counting
        /// it six times would make every result look worse than it is.
        /// </summary>
        public static GeneratedAssetReport Measure(GameObject root)
        {
            var report = new GeneratedAssetReport();
            if (root == null) return report;

            var seenMeshes = new HashSet<int>();
            var seenMaterials = new HashSet<int>();
            var seenTextures = new HashSet<int>();

            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = filter.sharedMesh;
                if (mesh == null || !seenMeshes.Add(mesh.GetInstanceID())) continue;
                report.Meshes++;
                report.Vertices += mesh.vertexCount;
                for (var i = 0; i < mesh.subMeshCount; i++)
                    report.Triangles += (int)(mesh.GetIndexCount(i) / 3);
            }

            foreach (var skinned in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = skinned.sharedMesh;
                if (mesh == null || !seenMeshes.Add(mesh.GetInstanceID())) continue;
                report.Meshes++;
                report.Vertices += mesh.vertexCount;
                for (var i = 0; i < mesh.subMeshCount; i++)
                    report.Triangles += (int)(mesh.GetIndexCount(i) / 3);
            }

            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material == null || !seenMaterials.Add(material.GetInstanceID())) continue;
                    report.Materials++;

                    foreach (var id in material.GetTexturePropertyNameIDs())
                    {
                        var texture = material.GetTexture(id);
                        if (texture == null || !seenTextures.Add(texture.GetInstanceID())) continue;
                        report.Textures++;
                        report.TextureBytes += EstimateTextureBytes(
                            texture.width, texture.height, texture.mipmapCount > 1);
                        report.LargestTextureSize = Mathf.Max(
                            report.LargestTextureSize, Mathf.Max(texture.width, texture.height));
                    }
                }
            }

            return report;
        }

        /// <summary>One line for a headset-readable HUD.</summary>
        public string Summarise()
        {
            var sb = new StringBuilder();
            sb.Append(Triangles.ToString("N0")).Append(" tris · ");
            sb.Append(Materials).Append(Materials == 1 ? " material · " : " materials · ");
            sb.Append(Textures).Append(" tex ");
            if (LargestTextureSize > 0) sb.Append('(').Append(LargestTextureSize).Append("px) ");
            sb.Append((TextureBytes / 1024f / 1024f).ToString("0.#")).Append(" MB");
            if (DownloadBytes > 0) sb.Append(" · ").Append((DownloadBytes / 1024f / 1024f).ToString("0.#")).Append(" MB dl");
            if (GenerationSeconds > 0) sb.Append(" · ").Append(GenerationSeconds.ToString("0")).Append("s");
            if (CreditsSpent > 0) sb.Append(" · ").Append(CreditsSpent.ToString("0.##")).Append(" credits");
            sb.Append(" · ").Append(Verdict);
            return sb.ToString();
        }
    }
}
