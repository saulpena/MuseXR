using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace MuseXR.EditorTools
{
    /// <summary>
    /// Writes Resources/PictureAspects.txt - each museum picture's true width and height, read from its master in
    /// Tools/artworks/source - which MuseXR.Worlds.PictureAspect reads at runtime, because the copies in
    /// Assets/Museum/Artworks are upscaled to a power of two (Tools/artworks/make_pot.py). Rewritten before every player build so a build cannot carry a stale
    /// table; PictureAspectTests fails in the Editor if the committed copy has drifted.
    /// </summary>
    public sealed class PictureAspectTable : IPreprocessBuildWithReport
    {
        public const string Folder = "Assets/Museum/Artworks", Masters = "Tools/artworks/source", OutputPath = "Assets/Resources/PictureAspects.txt";

        public int callbackOrder => 0;
        public void OnPreprocessBuild(BuildReport report) => Write();

        [MenuItem("MuseXR/Museum/Write Picture Aspects")]
        public static void Write()
        {
            var text = Build();
            if (File.Exists(OutputPath) && File.ReadAllText(OutputPath) == text) return;
            File.WriteAllText(OutputPath, text);
            AssetDatabase.ImportAsset(OutputPath);
        }

        public static string Build()
        {
            var rows = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Folder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var master = Path.Combine(Masters, Path.GetFileName(path));
                if (!File.Exists(master)) throw new BuildFailedException(path + " has no master in " + Masters + " - add it there and run Tools/artworks/make_pot.py");
                var probe = new UnityEngine.Texture2D(2, 2);
                try
                {
                    UnityEngine.ImageConversion.LoadImage(probe, File.ReadAllBytes(master));
                    rows.Add(Path.GetFileNameWithoutExtension(path) + " " + probe.width + " " + probe.height);
                }
                finally { UnityEngine.Object.DestroyImmediate(probe); }
            }
            rows.Sort(System.StringComparer.Ordinal);
            var sb = new StringBuilder("# name width height - source proportions of the museum's pictures (PictureAspectTable)\n");
            foreach (var r in rows) sb.Append(r).Append('\n');
            return sb.ToString();
        }
    }
}
