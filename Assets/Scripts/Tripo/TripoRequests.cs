using System;
using System.Collections.Generic;

namespace MusePico.Tripo
{
    /// <summary>
    /// Fields shared by the three "…-to-model" endpoints. Everything is nullable and everything
    /// null is omitted, so a request carries only what was deliberately chosen.
    /// </summary>
    public abstract class TripoModelRequest
    {
        /// <summary>Defaults to H3.1 rather than being left to the server, so a build is reproducible.</summary>
        public string Model = TripoApi.Models.H31;
        public bool? Texture;
        public bool? Pbr;
        public string TextureQuality;
        public string GeometryQuality;
        public int? FaceLimit;
        public int? ModelSeed;
        public int? TextureSeed;
        public bool? AutoSize;
        public bool? Quad;
        public bool? SmartLowPoly;
        public bool? GenerateParts;
        public bool? ExportUv;
        public string Compress;

        public abstract string Path { get; }

        protected JsonBuilder Common(JsonBuilder json) => json
            .Add("model", Model)
            .Add("texture", Texture)
            .Add("pbr", Pbr)
            .Add("texture_quality", TextureQuality)
            .Add("geometry_quality", GeometryQuality)
            .Add("face_limit", FaceLimit)
            .Add("model_seed", ModelSeed)
            .Add("texture_seed", TextureSeed)
            .Add("auto_size", AutoSize)
            .Add("quad", Quad)
            .Add("smart_low_poly", SmartLowPoly)
            .Add("generate_parts", GenerateParts)
            .Add("export_uv", ExportUv)
            .Add("compress", Compress);

        public abstract string ToJson();

        /// <summary>
        /// Floor price in credits, before add-ons. Texture-free generation is the cheaper tier.
        /// </summary>
        public virtual int EstimatedCredits()
        {
            var withTexture = Texture != false;
            if (Path == TripoApi.Paths.TextToModel) return withTexture ? 20 : 10;
            return withTexture ? 30 : 20;
        }
    }

    public sealed class TextToModelRequest : TripoModelRequest
    {
        public string Prompt;
        public string NegativePrompt;
        public int? ImageSeed;
        public string Style;

        public override string Path => TripoApi.Paths.TextToModel;

        public override string ToJson()
        {
            if (string.IsNullOrWhiteSpace(Prompt)) throw new ArgumentException("Tripo text-to-model needs a prompt.");
            var json = new JsonBuilder().Add("prompt", Prompt);
            Common(json)
                .Add("negative_prompt", NegativePrompt)
                .Add("image_seed", ImageSeed)
                .Add("style", Style);
            return json.ToString();
        }
    }

    public sealed class ImageToModelRequest : TripoModelRequest
    {
        public TripoFileRef File;
        public bool? EnableImageAutofix;
        public string TextureAlignment;
        public string Orientation;
        public string Style;

        public override string Path => TripoApi.Paths.ImageToModel;

        public override string ToJson()
        {
            if (File.IsEmpty) throw new ArgumentException("Tripo image-to-model needs a url, file_token or object.");
            var json = new JsonBuilder().Add("file", File.ToJson());
            Common(json)
                .Add("enable_image_autofix", EnableImageAutofix)
                .Add("texture_alignment", TextureAlignment)
                .Add("orientation", Orientation)
                .Add("style", Style);
            return json.ToString();
        }
    }

    /// <summary>
    /// Four views in [front, left, back, right] order — the order muse-infinity already crops its
    /// turnaround sheets into, under assets/generated/turnarounds/views/&lt;character&gt;/.
    ///
    /// Front is mandatory; the other three may be left empty and are sent as an empty object,
    /// which is how the API is told to skip a view rather than to expect one.
    /// </summary>
    public sealed class MultiviewToModelRequest : TripoModelRequest
    {
        public const int Front = 0, Left = 1, Back = 2, Right = 3;

        public readonly TripoFileRef[] Files = new TripoFileRef[4];
        /// <summary>Alternative to <see cref="Files"/>: reuse a prior image-to-multiview task.</summary>
        public string OriginalTaskId;
        public string TextureAlignment;
        public string Orientation;

        public override string Path => TripoApi.Paths.MultiviewToModel;

        public override string ToJson()
        {
            var hasFiles = false;
            foreach (var f in Files) if (!f.IsEmpty) { hasFiles = true; break; }

            if (!hasFiles && string.IsNullOrEmpty(OriginalTaskId))
                throw new ArgumentException("Tripo multiview needs files ([front, left, back, right]) or an original_task_id.");
            if (hasFiles && Files[Front].IsEmpty)
                throw new ArgumentException("Tripo multiview cannot omit the front view.");

            var json = new JsonBuilder();
            if (hasFiles)
            {
                var views = new List<JsonBuilder>(4);
                foreach (var f in Files) views.Add(f.IsEmpty ? new JsonBuilder() : f.ToJson());
                json.AddObjectArray("files", views);
            }
            else
            {
                json.Add("original_task_id", OriginalTaskId);
            }

            Common(json)
                .Add("texture_alignment", TextureAlignment)
                .Add("orientation", Orientation);
            return json.ToString();
        }
    }

    /// <summary>
    /// POST /v3/animations/rig-check — the only free call Tripo publishes, and therefore the right
    /// way to prove a new key works without spending anything.
    /// </summary>
    public sealed class RigCheckRequest
    {
        /// <summary>A prior task id, a file token, or a model URL.</summary>
        public string Input;

        public string Path => TripoApi.Paths.RigCheck;

        public string ToJson()
        {
            if (string.IsNullOrWhiteSpace(Input)) throw new ArgumentException("Rig check needs an input model.");
            return new JsonBuilder().Add("input", Input).ToString();
        }
    }

    public sealed class RigRequest
    {
        public string Input;
        public string Model = TripoApi.RigModels.V25;
        public string RigType = TripoApi.RigType.Biped;
        /// <summary>Mixamo naming is the one Unity's humanoid avatar mapper recognises.</summary>
        public string Spec = TripoApi.RigSpec.Mixamo;
        public string OutFormat = "glb";

        public string Path => TripoApi.Paths.Rig;

        public string ToJson()
        {
            if (string.IsNullOrWhiteSpace(Input)) throw new ArgumentException("Rig needs an input model.");
            return new JsonBuilder()
                .Add("input", Input)
                .Add("model", Model)
                .Add("rig_type", RigType)
                .Add("spec", Spec)
                .Add("out_format", OutFormat)
                .ToString();
        }
    }

    public sealed class RetargetRequest
    {
        public string Input;
        public readonly List<string> Animations = new List<string>();
        public string OutFormat = "glb";
        public bool? BakeAnimation;
        public bool? ExportWithGeometry;
        public bool? AnimateInPlace;

        public string Path => TripoApi.Paths.Retarget;

        public string ToJson()
        {
            if (string.IsNullOrWhiteSpace(Input)) throw new ArgumentException("Retarget needs a rigged model.");
            if (Animations.Count == 0) throw new ArgumentException("Retarget needs at least one animation preset.");
            if (Animations.Count > TripoApi.Animations.MaxPerRetarget)
                throw new ArgumentException("Retarget accepts at most " + TripoApi.Animations.MaxPerRetarget + " animations per call.");
            return new JsonBuilder()
                .Add("input", Input)
                .AddStringArray("animations", Animations)
                .Add("out_format", OutFormat)
                .Add("bake_animation", BakeAnimation)
                .Add("export_with_geometry", ExportWithGeometry)
                .Add("animate_in_place", AnimateInPlace)
                .ToString();
        }
    }

    /// <summary>
    /// POST /v3/mesh/decimate. Relevant because every Tripo mesh in muse-infinity is ~2M triangles
    /// — generous for a render farm, unusable in a headset. Server-side decimation costs credits;
    /// Tools/tripo/decimate.mjs does the same thing locally for free, so this exists for
    /// completeness rather than as the default path.
    /// </summary>
    public sealed class DecimateRequest
    {
        public string Input;
        public int? FaceLimit;
        public bool? Quad;
        public bool? Bake;

        public string Path => TripoApi.Paths.MeshDecimate;

        public string ToJson()
        {
            if (string.IsNullOrWhiteSpace(Input)) throw new ArgumentException("Decimate needs an input model.");
            return new JsonBuilder()
                .Add("input", Input)
                .Add("face_limit", FaceLimit)
                .Add("quad", Quad)
                .Add("bake", Bake)
                .ToString();
        }
    }

    /// <summary>
    /// POST /v3/models/convert. GLB is what glTFast reads and is already the default output, so
    /// this is only needed for FBX (quad meshes) or USDZ.
    /// </summary>
    public sealed class ConvertRequest
    {
        public string Input;
        public string Format = "GLB";
        public int? FaceLimit;
        public int? TextureSize;
        public string TextureFormat;
        public bool? Quad;
        public bool? PivotToCenterBottom;
        public bool? WithAnimation;

        public string Path => TripoApi.Paths.Convert;

        public string ToJson()
        {
            if (string.IsNullOrWhiteSpace(Input)) throw new ArgumentException("Convert needs an input model.");
            return new JsonBuilder()
                .Add("input", Input)
                .Add("format", Format)
                .Add("face_limit", FaceLimit)
                .Add("texture_size", TextureSize)
                .Add("texture_format", TextureFormat)
                .Add("quad", Quad)
                .Add("pivot_to_center_bottom", PivotToCenterBottom)
                .Add("with_animation", WithAnimation)
                .ToString();
        }
    }
}
