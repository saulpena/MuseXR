using System.Collections.Generic;

namespace MusePico.Tripo
{
    /// <summary>
    /// Every constant the Tripo REST surface defines, in one place.
    ///
    /// This targets the **v3** API. The Node backend in muse-infinity
    /// (<c>services/tripoApi.js</c>) targets v2 — <c>https://api.tripo3d.ai/v2/openapi</c>, one
    /// <c>POST /task</c> endpoint discriminated by a <c>type</c> field. Tripo stops maintaining v2
    /// on 1 Oct 2026 and stops accepting v2 requests on 1 Nov 2026, so porting that client
    /// verbatim would have bought a integration with weeks to live. v3 splits the single task
    /// endpoint into one path per capability and renames the output fields.
    /// </summary>
    public static class TripoApi
    {
        /// <summary>Global (non-mainland-China) service root. No trailing slash.</summary>
        public const string GlobalBaseUrl = "https://openapi.tripo3d.ai/v3";

        /// <summary>Mainland-China service root. Accounts are region-bound; the key decides.</summary>
        public const string ChinaBaseUrl = "https://openapi.tripo3d.com/v3";

        /// <summary>Environment variable the key is read from. Never stored in the project.</summary>
        public const string ApiKeyEnvironmentVariable = "TRIPO_API_KEY";

        public static class Paths
        {
            public const string TextToModel = "/generation/text-to-model";
            public const string ImageToModel = "/generation/image-to-model";
            public const string MultiviewToModel = "/generation/multiview-to-model";
            public const string Texture = "/models/texture";
            public const string Convert = "/models/convert";
            public const string MeshDecimate = "/mesh/decimate";
            public const string MeshSegment = "/mesh/segment";
            public const string RigCheck = "/animations/rig-check";
            public const string Rig = "/animations/rig";
            public const string Retarget = "/animations/retarget";
            public const string Tasks = "/tasks";
            public const string TaskList = "/tasks/list";
            public const string Files = "/files";
            public const string Balance = "/account/balance";
        }

        /// <summary>
        /// Values for the <c>model</c> field. Kept as strings, not an enum: Tripo ships new dated
        /// versions regularly and an enum would reject a newer one the account can already use.
        /// </summary>
        public static class Models
        {
            /// <summary>H3.1 — latest, highest fidelity. The default here.</summary>
            public const string H31 = "v3.1-20260211";
            /// <summary>H3.0 — previous stable.</summary>
            public const string H30 = "v3.0-20250812";
            /// <summary>H2.5 — legacy; what muse-infinity's v2 client defaulted rigging to.</summary>
            public const string H25 = "v2.5-20250123";
            public const string H20 = "v2.0-20240919";
            /// <summary>P1 — tuned for low-poly game assets. The interesting one for XR.</summary>
            public const string P1 = "P1-20260311";
            public const string TurboV1 = "Turbo-v1.0-20250506";

            public static readonly string[] All = { H31, H30, H25, H20, P1, TurboV1 };
        }

        /// <summary>Rig model versions for <see cref="Paths.Rig"/>.</summary>
        public static class RigModels
        {
            /// <summary>Biped only. The API default when <c>model</c> is omitted.</summary>
            public const string V1 = "v1.0-20240301";
            /// <summary>Adds the non-humanoid skeletons.</summary>
            public const string V25 = "v2.5-20260210";
        }

        public static class TextureQuality
        {
            public const string Standard = "standard";
            public const string Detailed = "detailed";
            public const string Extreme = "extreme";
        }

        public static class GeometryQuality
        {
            public const string Standard = "standard";
            public const string Detailed = "detailed";
        }

        public static class TextureAlignment
        {
            public const string OriginalImage = "original_image";
            public const string Geometry = "geometry";
        }

        public static class Orientation
        {
            public const string Default = "default";
            public const string AlignImage = "align_image";
        }

        public static class RigType
        {
            public const string Biped = "biped";
            public const string Quadruped = "quadruped";
            public const string Hexapod = "hexapod";
            public const string Octopod = "octopod";
            public const string Avian = "avian";
            public const string Serpentine = "serpentine";
            public const string Aquatic = "aquatic";
            public const string Others = "others";
        }

        /// <summary>Bone naming convention. <c>mixamo</c> is the one Unity's humanoid rig maps cleanly.</summary>
        public static class RigSpec
        {
            public const string Tripo = "tripo";
            public const string Mixamo = "mixamo";
        }

        public static class Animations
        {
            public const string Idle = "preset:idle";
            public const string Walk = "preset:walk";
            public const string Run = "preset:run";
            public const string Dive = "preset:dive";
            public const string Climb = "preset:climb";
            public const string Jump = "preset:jump";
            public const string Slash = "preset:slash";
            public const string Shoot = "preset:shoot";
            public const string Hurt = "preset:hurt";
            public const string Fall = "preset:fall";
            public const string Turn = "preset:turn";
            public const string QuadrupedWalk = "preset:quadruped:walk";
            public const string HexapodWalk = "preset:hexapod:walk";
            public const string OctopodWalk = "preset:octopod:walk";
            public const string SerpentineMarch = "preset:serpentine:march";
            public const string AquaticMarch = "preset:aquatic:march";

            /// <summary>At most five presets may be combined in one retarget call.</summary>
            public const int MaxPerRetarget = 5;
        }

        /// <summary>
        /// Published credit costs, for showing a price before spending anything.
        /// 1 credit = $0.01 USD. These are the documented base rates; add-ons
        /// (detailed texture, quad, parts) cost extra, so treat a number here as a floor.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, int> BaseCreditCost = new Dictionary<string, int>
        {
            { Paths.TextToModel, 20 },        // 10 with texture:false
            { Paths.ImageToModel, 30 },       // 20 with texture:false
            { Paths.MultiviewToModel, 30 },   // 20 with texture:false
            { Paths.Convert, 10 },
            { Paths.MeshDecimate, 10 },
            { Paths.Rig, 25 },
            { Paths.Retarget, 10 },           // per animation
            { Paths.RigCheck, 0 },            // the only free operation
        };

        /// <summary>Dollar cost of a credit. Used only to print an estimate.</summary>
        public const double UsdPerCredit = 0.01;
    }
}
