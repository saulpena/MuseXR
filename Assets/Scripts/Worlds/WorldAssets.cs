using GaussianSplatting.Runtime;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace MusePico.Worlds
{
    /// <summary>
    /// The one way a Marble world gets into memory, and the one way it is let go.
    ///
    /// <b>Why Addressables everywhere, including single-world test scenes.</b> A direct
    /// <c>GaussianSplatAsset</c> reference puts the world in the APK and keeps it resident for as
    /// long as the scene is. The museum walks through nine or ten worlds and Skylar's eight alone
    /// are ~1.37 GB converted, so the shipping app cannot hold them that way — which means the
    /// real app loads by address whatever a test scene does. Two mechanisms was the actual bug
    /// source: a test scene on direct references never exercises the catalog, so the first time
    /// Addressables can fail is on the headset. There is also a silent size trap — an asset that
    /// is both directly referenced by a built scene AND marked Addressable is built into the APK
    /// <i>and</i> the bundle.
    ///
    /// The usual objection, "someone will forget the content build", does not apply here:
    /// <see cref="MuseXR.EditorTools.XRBuild"/> runs <c>BuildContent()</c> on every player build
    /// before <c>BuildPlayer</c>.
    /// </summary>
    public static class WorldAssets
    {
        /// <summary>Begin loading the world at <paramref name="address"/>. Yield on the returned
        /// handle, then check <see cref="AsyncOperationHandle.Status"/> before using Result.</summary>
        public static AsyncOperationHandle<GaussianSplatAsset> LoadAsync(string address)
        {
            return Addressables.LoadAssetAsync<GaussianSplatAsset>(address);
        }

        /// <summary>Release a handle if it is valid, and invalidate it. Safe to call twice.</summary>
        public static void Release(ref AsyncOperationHandle<GaussianSplatAsset> handle)
        {
            if (!handle.IsValid()) return;
            Addressables.Release(handle);
            handle = default;
        }

        /// <summary>
        /// What to print when a load fails. Names the most common cause rather than the exception,
        /// because the exception says "no location" and the cause is almost always that the player
        /// shipped without an Addressables content build, or that the world was converted but
        /// never marked (a converted asset is inert until it has an address).
        /// </summary>
        public static string DescribeFailure(string address, AsyncOperationHandle<GaussianSplatAsset> handle)
        {
            string inner = handle.OperationException != null ? handle.OperationException.Message : "no exception";
            return $"[WorldAssets] could not load world \"{address}\": {inner}. " +
                   "Has the Addressables content been built for this player, and has the world been " +
                   "marked addressable (MuseXR > Addressables > Mark Worlds Addressable)? " +
                   "A converted GaussianSplatAsset has no address until it is marked.";
        }

        /// <summary>True when the handle finished and produced an asset.</summary>
        public static bool Succeeded(AsyncOperationHandle<GaussianSplatAsset> handle)
        {
            return handle.IsValid() && handle.Status == AsyncOperationStatus.Succeeded && handle.Result != null;
        }
    }
}
