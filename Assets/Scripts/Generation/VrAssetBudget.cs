using System;
using MusePico.Tripo;

namespace MusePico.Generation
{
    /// <summary>
    /// What to ask Tripo for so the result is usable in a headset, and what to refuse.
    ///
    /// This exists because the defaults are wrong for VR in a way that is invisible until the
    /// frame rate collapses: the five Tripo meshes already in this project are ~1.96M triangles
    /// each. A standalone headset has a whole-scene budget of a few hundred thousand. Generating
    /// at the default and fixing it afterwards is not an option at runtime — there is no
    /// decimation step on device.
    ///
    /// So the budget is applied to the REQUEST. Everything here is arithmetic and constants, no
    /// Unity types, so the mapping is pinned by tests rather than discovered on a device.
    /// </summary>
    public enum VrBudgetTier
    {
        /// <summary>Many on screen at once — crowd figures, scattered props.</summary>
        Background,
        /// <summary>The default. One or a few pieces the visitor walks up to.</summary>
        Exhibit,
        /// <summary>One hero piece, nothing else competing. Slowest and dearest.</summary>
        Hero,
    }

    public readonly struct VrAssetBudget
    {
        public readonly VrBudgetTier Tier;
        /// <summary>Triangle ceiling requested via <c>face_limit</c>.</summary>
        public readonly int FaceLimit;
        public readonly bool Texture;
        public readonly bool Pbr;
        public readonly string TextureQuality;
        /// <summary>Tripo model id. P1 is the low-poly line and the only sane default here.</summary>
        public readonly string Model;

        VrAssetBudget(VrBudgetTier tier, int faceLimit, bool texture, bool pbr, string textureQuality, string model)
        {
            Tier = tier;
            FaceLimit = faceLimit;
            Texture = texture;
            Pbr = pbr;
            TextureQuality = textureQuality;
            Model = model;
        }

        /// <summary>
        /// P1's own documented ceiling. Asking for more is not a stricter budget that happens to
        /// be ignored — it is an invalid request, so it is clamped rather than sent.
        /// </summary>
        public const int P1MaxFaceLimit = 20000;

        /// <summary>Below this a mesh stops reading as an object at all. Tripo's own floor is 48.</summary>
        public const int MinFaceLimit = 250;

        public static VrAssetBudget For(VrBudgetTier tier)
        {
            switch (tier)
            {
                case VrBudgetTier.Background:
                    // No PBR: a background piece does not earn three texture fetches per pixel,
                    // and the extra maps are the larger part of its memory.
                    return new VrAssetBudget(tier, 4000, true, false, TripoApi.TextureQuality.Standard, TripoApi.Models.P1);

                case VrBudgetTier.Hero:
                    return new VrAssetBudget(tier, P1MaxFaceLimit, true, true, TripoApi.TextureQuality.Detailed, TripoApi.Models.P1);

                case VrBudgetTier.Exhibit:
                default:
                    return new VrAssetBudget(tier, 10000, true, true, TripoApi.TextureQuality.Standard, TripoApi.Models.P1);
            }
        }

        /// <summary>Same tier, a different triangle ceiling. Clamped to what P1 will accept.</summary>
        public VrAssetBudget WithFaceLimit(int faceLimit) =>
            new VrAssetBudget(Tier, Clamp(faceLimit), Texture, Pbr, TextureQuality, Model);

        public VrAssetBudget WithTexture(bool texture) =>
            new VrAssetBudget(Tier, FaceLimit, texture, texture && Pbr, TextureQuality, Model);

        public static int Clamp(int faceLimit) =>
            faceLimit < MinFaceLimit ? MinFaceLimit :
            faceLimit > P1MaxFaceLimit ? P1MaxFaceLimit : faceLimit;

        /// <summary>
        /// Stamps the budget onto a request, and — just as importantly — turns OFF three options
        /// that each produce a file this project cannot load or use:
        ///
        ///   <c>compress: "geometry"</c> emits EXT_meshopt_compression. glTFast decodes that only
        ///     when the meshoptimizer decompress package is installed, and it is not. The import
        ///     fails rather than degrading.
        ///   <c>quad: true</c> forces FBX output. glTFast reads glTF, not FBX, so the download is
        ///     simply not openable.
        ///   <c>generate_parts</c> is incompatible with texture and pbr, so it would silently
        ///     strip the very thing that makes a generated piece look like anything.
        /// </summary>
        public void ApplyTo(TripoModelRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            request.Model = Model;
            request.FaceLimit = Clamp(FaceLimit);
            request.Texture = Texture;
            request.Pbr = Texture && Pbr;
            request.TextureQuality = Texture ? TextureQuality : null;

            request.Compress = null;
            request.Quad = false;
            request.GenerateParts = false;
            request.SmartLowPoly = false;   // P1 already produces low-poly topology; this only adds cost.
        }

        /// <summary>
        /// Seconds this is likely to take, from Tripo's published P1 figures: about 10 s for the
        /// base mesh and about 60 s once textured. A range, not a promise — queue depth is not
        /// visible until the task reports <c>queuing_num</c>.
        /// </summary>
        public (int fast, int slow) EstimatedSeconds =>
            Texture ? (45, 90) : (8, 20);

        /// <summary>Floor price in credits. 1 credit = $0.01.</summary>
        public int EstimatedCredits(bool fromImage) =>
            (fromImage ? 20 : 10) + (Texture ? 10 : 0);
    }
}
