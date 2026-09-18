using System.Collections.Generic;
using System.IO;
using System.Linq;
using MuseXR.Worlds;
using NUnit.Framework;
using UnityEditor;

namespace MusePico.Tests
{
    /// <summary>
    /// Pins the Small world set — Skylar's captures re-exported from Marble at 500k.
    ///
    /// Two failures these exist to catch, both of which otherwise surface only on the headset:
    ///  - an address that no converted asset answers to, which Addressables reports as a load
    ///    failure at runtime and nowhere earlier;
    ///  - a spawn drifting away from the value that 27 commits of playtesting settled on.
    /// </summary>
    public class WorldCatalogTests
    {
        /// <summary>
        /// The six that exist at both resolutions, and the values ported from the full-res
        /// entries. Written out rather than read from WorldCatalog.Skylar so that a change to
        /// EITHER list has to be made deliberately in both places.
        /// </summary>
        static readonly object[] Ported =
        {
            //          base key                                 scale  spawnX  spawnZ  groundY  yaw    far
            new object[]{"van-gogh-inspired-gallery-interior",    1.7f,  1.79f,  0.30f,  0.0f,    0f,    200f},
            new object[]{"elegant-floral-palace-interior",        1.7f,  1.16f, -2.2f,   0.2f,   25f,    200f},
            new object[]{"mexican-courtyard-bedroom-fantasy",     1.7f,  2.21f, -1.23f,  0.1f,    0f,    400f},
            new object[]{"grand-conservatory-with-lush-gardens",  1.7f, -1.6f,  -4.8f,   0.9f,    0f,    400f},
            new object[]{"enchanted-water-garden-sanctuary",      1.7f,  0.8f, -19f,     1.1f,  180f,    400f},
            new object[]{"dreamlike-coastal-villa-gardens",       1.7f, -1.6f,  -2.8f,   5.6f,    0f,    400f},
        };

        static readonly string[] Unmeasured =
        {
            "enchanted-palace-garden",
            "fantasy-realm-of-shimmering-spheres",
        };

        static WorldDefinition Find(string baseKey) =>
            WorldCatalog.Small.FirstOrDefault(w => w.key == baseKey + WorldCatalog.SmallSuffix);

        static string PathOf(string address) =>
            AssetDatabase.FindAssets("t:GaussianSplatAsset")
                .Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == address);

        [Test]
        public void SmallSet_HasEightWorlds()
        {
            Assert.AreEqual(8, WorldCatalog.Small.Count);
        }

        [Test]
        public void EveryKey_CarriesTheSuffix_AndIsUnique()
        {
            // Six of the eight share a capture with a full-res asset, and Addressables keys off
            // the file name — without the suffix those six would collide and one would silently
            // win. This is the guard on that.
            foreach (var w in WorldCatalog.Small)
                StringAssert.EndsWith(WorldCatalog.SmallSuffix, w.key, "world " + w.displayName);

            CollectionAssert.AllItemsAreUnique(WorldCatalog.Small.Select(w => w.key).ToList());
        }

        [Test]
        public void SmallKeys_DoNotCollideWithTheFullResSet()
        {
            var full = new HashSet<string>(WorldCatalog.Skylar.Select(w => w.Address));
            foreach (var w in WorldCatalog.Small)
                Assert.IsFalse(full.Contains(w.Address),
                    "address " + w.Address + " is already taken by the full-res set");
        }

        [Test, TestCaseSource(nameof(Ported))]
        public void PortedWorlds_KeepTheirPlaytestedSpawn(
            string baseKey, float scale, float spawnX, float spawnZ,
            float groundY, float yaw, float far)
        {
            var w = Find(baseKey);
            Assert.IsNotNull(w, baseKey + " is missing from the Small set");
            Assert.IsTrue(w.hasMeasuredSpawn, baseKey + " lost its measured spawn");
            Assert.AreEqual(scale,   w.worldScale, 1e-4f, "worldScale");
            Assert.AreEqual(spawnX,  w.spawn.x,    1e-4f, "spawn.x");
            Assert.AreEqual(spawnZ,  w.spawn.y,    1e-4f, "spawn.z");
            Assert.AreEqual(groundY, w.groundY,    1e-4f, "groundY");
            Assert.AreEqual(yaw,     w.yawDegrees, 1e-4f, "yaw");
            Assert.AreEqual(far,     w.cameraFar,  1e-4f, "cameraFar");
        }

        [Test, TestCaseSource(nameof(Ported))]
        public void PortedWorlds_MatchTheFullResEntryTheyCameFrom(
            string baseKey, float scale, float spawnX, float spawnZ,
            float groundY, float yaw, float far)
        {
            // The 500k bounds sit strictly inside the full-res bounds on all six faces, which is
            // decimation rather than a change of frame — so these numbers must stay identical
            // between the two sets. If someone retunes one, this fails and forces the question.
            var full = WorldCatalog.Skylar.FirstOrDefault(x => x.key == baseKey);
            Assert.IsNotNull(full, baseKey + " is not in the full-res set");
            var small = Find(baseKey);

            Assert.AreEqual(full.worldScale, small.worldScale, 1e-4f, "worldScale");
            Assert.AreEqual(full.spawn.x,    small.spawn.x,    1e-4f, "spawn.x");
            Assert.AreEqual(full.spawn.y,    small.spawn.y,    1e-4f, "spawn.z");
            Assert.AreEqual(full.groundY,    small.groundY,    1e-4f, "groundY");
            Assert.AreEqual(full.yawDegrees, small.yawDegrees, 1e-4f, "yaw");
            Assert.AreEqual(full.cameraFar,  small.cameraFar,  1e-4f, "cameraFar");
        }

        [Test]
        public void TheTwoNewWorlds_AreMarkedUnmeasured()
        {
            // Neither has a spawn that ports: enchanted-palace-garden is not in worlds.js at all,
            // and fantasy-realm's profile there is in metric space, ~11x off this .spz's extent.
            // Claiming a measured spawn would put the visitor somewhere arbitrary with no warning.
            foreach (var baseKey in Unmeasured)
            {
                var w = Find(baseKey);
                Assert.IsNotNull(w, baseKey + " is missing from the Small set");
                Assert.IsFalse(w.hasMeasuredSpawn,
                    baseKey + " claims a measured spawn it does not have");
            }
        }

        [Test]
        public void EveryAddress_ResolvesToAConvertedAsset()
        {
            foreach (var w in WorldCatalog.Small)
                Assert.IsNotNull(PathOf(w.Address),
                    "no converted GaussianSplatAsset named '" + w.Address + "'. Reconvert from " +
                    "Tools/marble/smallworlds; an address with no asset fails only at runtime.");
        }

        [Test]
        public void Get_ReturnsTheRightSet()
        {
            Assert.AreSame(WorldCatalog.Small,   WorldCatalog.Get(WorldSet.Small));
            Assert.AreSame(WorldCatalog.Samples, WorldCatalog.Get(WorldSet.Samples));
            Assert.AreSame(WorldCatalog.Skylar,  WorldCatalog.Get(WorldSet.Skylar));
        }

        [Test]
        public void SpawnsSitInsideTheAssetBounds()
        {
            // A spawn outside the cloud is the "visitor starts in the sky" failure that cost
            // muse-infinity several commits. Bounds come from the asset, so this checks the
            // ported numbers against the 500k re-export rather than against the original — the
            // interiors lost 20-25% of their extent to decimation.
            foreach (var w in WorldCatalog.Small)
            {
                if (!w.hasMeasuredSpawn) continue;

                var path = PathOf(w.Address);
                Assert.IsNotNull(path, "missing asset for " + w.Address);
                var asset = AssetDatabase
                    .LoadAssetAtPath<GaussianSplatting.Runtime.GaussianSplatAsset>(path);
                Assert.IsNotNull(asset, "could not load " + path);

                Assert.IsTrue(w.spawn.x >= asset.boundsMin.x && w.spawn.x <= asset.boundsMax.x,
                    w.Address + ": spawn x " + w.spawn.x + " outside " +
                    asset.boundsMin.x + ".." + asset.boundsMax.x);
                Assert.IsTrue(w.spawn.y >= asset.boundsMin.z && w.spawn.y <= asset.boundsMax.z,
                    w.Address + ": spawn z " + w.spawn.y + " outside " +
                    asset.boundsMin.z + ".." + asset.boundsMax.z);
            }
        }
    }
}
