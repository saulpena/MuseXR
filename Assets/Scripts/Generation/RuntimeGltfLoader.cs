using System;
using System.Threading;
using System.Threading.Tasks;
using GLTFast;
using GLTFast.Logging;
using UnityEngine;

namespace MusePico.Generation
{
    /// <summary>
    /// Turns the bytes Tripo hands back into a GameObject, on device, at runtime.
    ///
    /// This is the step that has no Editor equivalent: in the gallery a <c>.glb</c> goes through
    /// glTFast's ScriptedImporter and becomes an asset. Here there is no asset database, so
    /// glTFast's runtime importer parses the buffer and builds meshes and materials directly.
    ///
    /// Three settings are deliberate rather than default:
    ///
    ///   <b>Mip maps on.</b> Without them a textured object shimmers badly under head motion —
    ///   worse in a headset than on a monitor, because the head never holds perfectly still — and
    ///   every sample misses the texture cache. Costs a third more texture memory and is worth it.
    ///
    ///   <b>Textures not readable.</b> A readable texture keeps a second copy in CPU memory for
    ///   nothing; nothing here reads pixels back.
    ///
    ///   <b>Animation off.</b> Tripo's generation endpoints return static meshes — the rig and
    ///   retarget endpoints are separate, paid steps — so an animation pass would find nothing.
    /// </summary>
    public static class RuntimeGltfLoader
    {
        public sealed class Result
        {
            public GameObject Root;
            public GeneratedAssetReport Report;
            public string Error;
            public bool Success => Root != null && string.IsNullOrEmpty(Error);
        }

        public static ImportSettings VrImportSettings()
        {
            return new ImportSettings
            {
                GenerateMipMaps = true,
                TexturesReadable = false,
                AnisotropicFilterLevel = 4,
                AnimationMethod = AnimationMethod.None,
                NodeNameMethod = NameImportMethod.OriginalUnique,
            };
        }

        /// <summary>
        /// Parses <paramref name="glb"/> and instantiates it under <paramref name="parent"/>.
        ///
        /// Never throws for a bad file — a generated model is user-driven input and a malformed
        /// one must not take the app down mid-demo. Failures come back in
        /// <see cref="Result.Error"/>. Cancellation does throw, because that is the caller's own
        /// doing.
        /// </summary>
        public static async Task<Result> LoadAsync(
            byte[] glb, Transform parent, string name, CancellationToken cancellationToken = default)
        {
            var result = new Result();

            if (glb == null || glb.Length == 0)
            {
                result.Error = "The download was empty.";
                return result;
            }

            // A collecting logger keeps glTFast's complaints out of the console and puts them
            // where the harness can show them on a panel the wearer can actually read.
            var logger = new CollectingLogger();
            var import = new GltfImport(logger: logger);

            try
            {
                var loaded = await import.Load(glb, null, VrImportSettings(), cancellationToken);
                if (!loaded)
                {
                    result.Error = Describe(logger, "glTFast could not parse the model.");
                    import.Dispose();
                    return result;
                }

                var root = new GameObject(string.IsNullOrEmpty(name) ? "Generated" : name);
                root.transform.SetParent(parent, false);

                var instantiated = await import.InstantiateMainSceneAsync(root.transform, cancellationToken);
                if (!instantiated)
                {
                    result.Error = Describe(logger, "glTFast parsed the model but could not build it.");
                    UnityEngine.Object.Destroy(root);
                    import.Dispose();
                    return result;
                }

                result.Root = root;
                result.Report = GeneratedAssetReport.Measure(root);
                result.Report.DownloadBytes = glb.Length;
                return result;
            }
            catch (OperationCanceledException)
            {
                import.Dispose();
                throw;
            }
            catch (Exception ex)
            {
                result.Error = ex.GetType().Name + ": " + ex.Message;
                import.Dispose();
                return result;
            }
        }

        static string Describe(CollectingLogger logger, string fallback)
        {
            var items = logger?.Items;
            if (items == null) return fallback;

            foreach (var item in items)
            {
                if (item.Type != LogType.Error) continue;
                var message = item.ToString();
                // The one worth naming: a compressed buffer this project cannot decode. glTFast
                // gates EXT_meshopt_compression and KHR_draco_mesh_compression behind optional
                // packages, neither of which is installed — so never ask Tripo to compress.
                if (message.IndexOf("meshopt", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    message.IndexOf("draco", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return message + " — the model was requested with compression, which this " +
                           "build cannot decode. Leave `compress` unset.";
                }
                return message;
            }

            return fallback;
        }

        /// <summary>
        /// Scales and lifts a freshly imported model so it stands on <paramref name="standOnY"/>
        /// at a sensible height, using the same bounds-driven fit the gallery uses.
        ///
        /// Necessary for the same reason there: a generated mesh has no authored pivot and no
        /// agreed unit, so two models from the same prompt can differ in size by a factor of ten.
        /// </summary>
        public static void FitInPlace(GameObject root, float targetHeight, float standOnY)
        {
            if (root == null) return;

            var bounds = LocalBounds(root);
            var size = bounds.size;
            var tallest = Mathf.Max(size.y, Mathf.Max(size.x, size.z));
            var scale = tallest > 1e-4f ? targetHeight / tallest : 1f;

            root.transform.localScale = Vector3.one * scale;

            var centre = bounds.center * scale;
            var halfHeight = size.y * 0.5f * scale;
            root.transform.localPosition = new Vector3(
                -centre.x, standOnY - (centre.y - halfHeight), -centre.z);
        }

        static Bounds LocalBounds(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            var started = false;
            var result = new Bounds();
            var toRoot = root.transform.worldToLocalMatrix;

            foreach (var renderer in renderers)
            {
                if (renderer is ParticleSystemRenderer) continue;
                var local = renderer.localBounds;
                var matrix = toRoot * renderer.transform.localToWorldMatrix;

                for (var corner = 0; corner < 8; corner++)
                {
                    var point = local.center + Vector3.Scale(local.extents, new Vector3(
                        (corner & 1) == 0 ? -1f : 1f,
                        (corner & 2) == 0 ? -1f : 1f,
                        (corner & 4) == 0 ? -1f : 1f));
                    var inRoot = matrix.MultiplyPoint3x4(point);

                    if (!started) { result = new Bounds(inRoot, Vector3.zero); started = true; }
                    else result.Encapsulate(inRoot);
                }
            }

            return started ? result : new Bounds(Vector3.zero, Vector3.zero);
        }
    }
}
