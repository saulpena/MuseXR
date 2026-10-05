using UnityEditor;

namespace MuseXR.EditorTools
{
    /// <summary>
    /// Import settings for the generated models' textures, applied on every (re)import so they cannot drift.
    /// The models are unpacked to .gltf with their images beside them (Tools/models/unpack.cjs) precisely so
    /// that these settings apply: inside a .glb, glTFast keeps textures uncompressed, and one 20k-triangle
    /// statue carried 89.5 MB of them (4 Oct 2026). Here: ASTC 6x6 on Android, colour in sRGB, normal and
    /// metal-roughness linear, and a size cap - heroes 2048, props 1024, the paint pots 512; metal-roughness
    /// at half that.
    /// </summary>
    public sealed class ModelTextureImport : AssetPostprocessor
    {
        const string Heroes = "Assets/Resources/Heroes/", Props = "Assets/Resources/Props/";

        void OnPreprocessTexture()
        {
            var path = assetPath.Replace('\\', '/');
            bool hero = path.StartsWith(Heroes), prop = path.StartsWith(Props);
            if (!hero && !prop) return;
            var file = System.IO.Path.GetFileNameWithoutExtension(path);
            bool normal = file.EndsWith("_normal"), mr = file.EndsWith("_metalRough"), colour = file.EndsWith("_baseColor");
            if (!normal && !mr && !colour) return;   // the hall's paintings and anything else here keep their own settings

            var size = hero ? 2048 : file.StartsWith("pot-") ? 512 : 1024;
            if (mr) size /= 2;
            var ti = (TextureImporter)assetImporter;
            // Not NormalMap: glTFast's shader unpacks the plain RGB normal itself.
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = colour;
            ti.mipmapEnabled = true;
            ti.maxTextureSize = size;
            ti.SetPlatformTextureSettings(new TextureImporterPlatformSettings
            {
                name = "Android", overridden = true, maxTextureSize = size,
                format = TextureImporterFormat.ASTC_6x6, textureCompression = TextureImporterCompression.Compressed,
            });
        }
    }
}
