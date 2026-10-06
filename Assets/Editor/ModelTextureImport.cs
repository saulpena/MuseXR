using UnityEditor;

namespace MuseXR.EditorTools
{
    /// <summary>
    /// Import settings for the generated models' textures, applied on every (re)import so they cannot drift.
    /// The models are unpacked to .gltf with their images beside them (Tools/models/unpack.cjs) precisely so
    /// that these settings apply: inside a .glb, glTFast keeps textures uncompressed, and one 20k-triangle
    /// statue carried 89.5 MB of them (4 Oct 2026). Here: ASTC 6x6 on Android, colour in sRGB, normal and
    /// metal-roughness linear, and a size cap - heroes, doors and the Art props 2048, the Resources props 1024,
    /// the paint pots 512; metal-roughness at half that.
    /// Also the museum's paintings, which shipped as RGBA32 - 17.5 MB apiece, 226 MB for the set (5 Oct 2026):
    /// on Android Unity will not compress a non-power-of-two texture that has mipmaps (measured: 13.5 MB with,
    /// 1.1 MB without). So they arrive already upscaled to a power of two by Tools/artworks/make_pot.py - Lanczos,
    /// because Unity's own stretch blurred them (42-66% of the close-up detail kept, against 95-98%). ToLarger here
    /// is only a guard for a picture added without that step. Their true proportions live in
    /// Resources/PictureAspects.txt (PictureAspectTable), read through MuseXR.Worlds.PictureAspect.
    /// </summary>
    public sealed class ModelTextureImport : AssetPostprocessor
    {
        const string Heroes = "Assets/Resources/Heroes/", Props = "Assets/Resources/Props/",
                     ArtProps = "Assets/Art/Props/", ArtDoors = "Assets/Art/Doors/", Artworks = "Assets/Museum/Artworks/";

        void OnPreprocessTexture()
        {
            var path = assetPath.Replace('\\', '/');
            var ti = (TextureImporter)assetImporter;
            if (path.StartsWith(Artworks))
            {
                ti.npotScale = TextureImporterNPOTScale.ToLarger;
                // The upscale is uneven (843x1046 becomes 1024x2048), so at a distance the GPU picks its mip by the
                // more-stretched axis and blurs the other: measured, aniso 1 kept 48% of the detail, aniso 4 all of it.
                ti.anisoLevel = 4;
                ti.SetPlatformTextureSettings(new TextureImporterPlatformSettings
                {
                    name = "Android", overridden = true, maxTextureSize = 2048,
                    format = TextureImporterFormat.ASTC_6x6, textureCompression = TextureImporterCompression.Compressed,
                });
                return;
            }
            bool hero = path.StartsWith(Heroes), prop = path.StartsWith(Props), art = path.StartsWith(ArtProps) || path.StartsWith(ArtDoors);
            if (!hero && !prop && !art) return;
            var file = System.IO.Path.GetFileNameWithoutExtension(path);
            bool normal = file.EndsWith("_normal"), mr = file.EndsWith("_metalRough"), colour = file.EndsWith("_baseColor");
            if (!normal && !mr && !colour) return;   // the hall's paintings and anything else here keep their own settings

            var size = hero || art ? 2048 : file.StartsWith("pot-") ? 512 : 1024;
            if (mr) size /= 2;
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
