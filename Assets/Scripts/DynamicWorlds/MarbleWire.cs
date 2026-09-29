using System;
using System.Globalization;
using System.Text.RegularExpressions;
using MusePico.Tripo;
using UnityEngine;

namespace MuseXR.DynamicWorlds
{
    /// <summary>
    /// The World Labs Marble API as data: request bodies in, parsed responses out, no network.
    /// Everything here was checked against real responses on 29 Sep 2026 (Tools/marble/marble.mjs
    /// ran the same two calls), including one place the published spec is wrong — see
    /// <see cref="PanoUrl"/>.
    /// </summary>
    public static class MarbleWire
    {
        public const string BaseUrl = "https://api.worldlabs.ai/marble/v1";
        public const string KeyHeader = "WLT-Api-Key";

        /// <summary>POST /pano:depth_to_rgb. A PNG depth pano needs z_min/z_max to be decoded.</summary>
        public static string DepthToRgbBody(string depthPngBase64, float zMin, float zMax, string prompt) =>
            new JsonBuilder()
                .Add("depth_pano_image", new JsonBuilder()
                    .Add("source", "data_base64")
                    .Add("data_base64", depthPngBase64)
                    .Add("extension", "png"))
                .AddRaw("z_min", Number(zMin))
                .AddRaw("z_max", Number(zMax))
                .Add("text_prompt", prompt)
                .ToString();

        /// <summary>
        /// POST /worlds:generate from a panorama. The model is always sent (the docs disagree on the
        /// default) and the world is always private: visibility cannot be changed after creation.
        /// </summary>
        public static string PanoWorldBody(string panoPngBase64, string model, string displayName) =>
            new JsonBuilder()
                .Add("display_name", displayName.Length > 64 ? displayName.Substring(0, 64) : displayName)
                .Add("model", model)
                .Add("world_prompt", new JsonBuilder()
                    .Add("type", "image")
                    .Add("image_prompt", new JsonBuilder()
                        .Add("source", "data_base64")
                        .Add("data_base64", panoPngBase64)
                        .Add("extension", "png"))
                    .Add("is_pano", true))
                .Add("permission", new JsonBuilder()
                    .Add("public", false)
                    .Add("allow_id_access", false)
                    .AddStringArray("allowed_readers", Array.Empty<string>())
                    .AddStringArray("allowed_writers", Array.Empty<string>()))
                .AddStringArray("tags", new[] { "musexr", "dynamic-world" })
                .ToString();

        static string Number(float v) => v.ToString("R", CultureInfo.InvariantCulture);

        // ------------------------------------------------------------------ responses

        [Serializable] public class Operation
        {
            public string operation_id;
            public bool done;
            public OperationError error;
            public Metadata metadata;
            public World response;
            public Cost cost;
        }
        [Serializable] public class OperationError { public string code; public string message; }
        [Serializable] public class Metadata { public Progress progress; public string world_id; }
        [Serializable] public class Progress { public string status; }
        [Serializable] public class Cost { public float total_credits; }

        [Serializable] public class World
        {
            public string world_id;
            public string id;
            public string model;
            public Assets assets;
        }
        [Serializable] public class Assets { public Imagery imagery; public Splats splats; public Mesh mesh; }
        [Serializable] public class Imagery { public string pano_url; }
        [Serializable] public class Splats { public Semantics semantics_metadata; }
        [Serializable] public class Semantics { public float metric_scale_factor; public float ground_plane_offset; }
        [Serializable] public class Mesh { public string collider_mesh_url; }
        [Serializable] class PanoResult { public string pano_url; }
        [Serializable] class OperationPanoOnly { public PanoResult response; }

        public static Operation ParseOperation(string json) => JsonUtility.FromJson<Operation>(json);

        /// <summary>
        /// Measured 29 Sep 2026: a finished depth_to_rgb operation puts the URL at
        /// <c>response.assets.imagery.pano_url</c> (a World-shaped response with an empty world_id),
        /// NOT at <c>response.pano_url</c> as the spec's PanoDepthToRgbResult says. Both are read.
        /// </summary>
        public static string PanoUrl(string operationJson)
        {
            var op = JsonUtility.FromJson<Operation>(operationJson);
            var fromAssets = op?.response?.assets?.imagery?.pano_url;
            if (!string.IsNullOrEmpty(fromAssets)) return fromAssets;
            var spec = JsonUtility.FromJson<OperationPanoOnly>(operationJson);
            return string.IsNullOrEmpty(spec?.response?.pano_url) ? null : spec.response.pano_url;
        }

        /// <summary>The world an operation made. The schema says world_id; the docs' example says id.</summary>
        public static string WorldId(Operation op)
        {
            if (!string.IsNullOrEmpty(op?.response?.world_id)) return op.response.world_id;
            if (!string.IsNullOrEmpty(op?.response?.id)) return op.response.id;
            return string.IsNullOrEmpty(op?.metadata?.world_id) ? null : op.metadata.world_id;
        }

        /// <summary>
        /// One splat tier's URL out of GET /worlds/{id}. The tiers are keys like "500k", which no
        /// C# field can be named, so JsonUtility cannot reach them.
        /// </summary>
        public static string SpzUrl(string worldJson, string tier = "500k")
        {
            var m = Regex.Match(worldJson, "\"" + Regex.Escape(tier) + "\"\\s*:\\s*\"([^\"]+)\"");
            return m.Success ? Regex.Unescape(m.Groups[1].Value) : null;
        }

        /// <summary>
        /// Metric scale and ground offset, or null. Draft worlds come back without them (measured);
        /// a factor of exactly 1 is World Labs' "could not infer".
        /// </summary>
        public static Semantics ParseSemantics(string worldJson)
        {
            if (!worldJson.Contains("\"metric_scale_factor\"")) return null;
            // JsonUtility builds empty objects for absent fields, so "is it wrapped in world?" is
            // answered by which shape yields a factor, not by a null check.
            var s = JsonUtility.FromJson<WorldEnvelope>(worldJson)?.world?.assets?.splats?.semantics_metadata;
            if (s == null || s.metric_scale_factor <= 0f)
                s = JsonUtility.FromJson<World>(worldJson)?.assets?.splats?.semantics_metadata;
            return s == null || s.metric_scale_factor <= 0f ? null : s;
        }
        [Serializable] class WorldEnvelope { public World world; }
    }
}
