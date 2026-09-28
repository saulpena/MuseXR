using System.IO;
using System.Linq;
using MuseXR.Worlds;
using NUnit.Framework;
using UnityEditor;

namespace MusePico.Tests
{
    /// <summary>
    /// Pins the quality benchmark's list of versions. What these catch: two versions under one
    /// key (the second would silently show the first), a version standing somewhere other than its
    /// environment's spawn (the comparison would then be of two views, not two files), and a
    /// converted bench asset that no catalog entry names (built into the APK, never shown).
    /// </summary>
    public class QualityBenchCatalogTests
    {
        [Test]
        public void Keys_AreUniqueAndPrefixed()
        {
            var keys = QualityBenchCatalog.All.Select(v => v.key).ToList();
            Assert.That(keys, Is.Unique);
            foreach (var k in keys) StringAssert.StartsWith(QualityBenchCatalog.Prefix, k);
            foreach (var v in QualityBenchCatalog.All) Assert.AreEqual(v.key, v.world.key, v.Label);
        }

        [Test]
        public void Labels_AreUnique_AndSayWhereTheSplatsCameFrom()
        {
            Assert.That(QualityBenchCatalog.All.Select(v => v.Label), Is.Unique);
            foreach (var v in QualityBenchCatalog.All)
            {
                Assert.IsNotEmpty(v.environment, v.key);
                Assert.IsNotEmpty(v.version, v.key);
                Assert.IsNotEmpty(v.provenance, v.key);
            }
        }

        [Test]
        public void EveryVersion_StandsAtItsEnvironmentsShippedSpawn()
        {
            var bases = new[]
            {
                ("TEMPLE HALL", "empty-chinese-imperial-temple-hall"),
                ("CONSERVATORY", "grand-conservatory-garden-path"),
            };
            foreach (var (env, baseKey) in bases)
            {
                var shipped = WorldCatalog.Small.Single(w => w.key == baseKey + WorldCatalog.SmallSuffix);
                var versions = QualityBenchCatalog.All.Where(v => v.environment == env).ToList();
                Assert.That(versions, Is.Not.Empty, env);
                foreach (var v in versions)
                {
                    Assert.AreEqual(shipped.worldScale, v.world.worldScale, v.Label);
                    Assert.AreEqual(shipped.spawn, v.world.spawn, v.Label);
                    Assert.AreEqual(shipped.groundY, v.world.groundY, v.Label);
                    Assert.AreEqual(shipped.yawDegrees, v.world.yawDegrees, v.Label);
                    Assert.AreEqual(shipped.cameraFar, v.world.cameraFar, v.Label);
                }
            }
        }

        [Test]
        public void EnvironmentSwitch_LandsOnTheFirstVersionOfTheNextEnvironment_AndWraps()
        {
            var all = QualityBenchCatalog.All;
            var envs = QualityBenchCatalog.Environments;
            Assert.GreaterOrEqual(envs.Count, 2);

            int i = 0;
            for (int n = 0; n < envs.Count; n++)
            {
                Assert.AreEqual(envs[n], all[i].environment);
                i = QualityBenchCatalog.NextEnvironmentStart(all, i);
            }
            Assert.AreEqual(0, i, "after the last environment it should wrap to the first");
            Assert.AreEqual("1 / " + all.Count(v => v.environment == envs[0]),
                            QualityBenchCatalog.PositionInEnvironment(all, 0));
        }

        [Test]
        public void ReferenceOnlyVersions_LiveInTheReferenceFolder_AndOnlyThey()
        {
            // The folder is what keeps a version out of the headset build; the flag is what the
            // catalog says. If they disagree, either a crashing version ships or a good one is lost.
            foreach (var v in QualityBenchCatalog.All)
            {
                var path = AssetDatabase.FindAssets("t:GaussianSplatAsset", new[] { QualityBenchCatalog.Folder })
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == v.key);
                if (path == null) continue;   // not converted yet
                bool inReference = path.StartsWith(QualityBenchCatalog.ReferenceFolder + "/");
                Assert.AreEqual(v.referenceOnly, inReference, $"{v.Label} is at {path}");
            }
        }

        [Test]
        public void EveryConvertedBenchAsset_IsNamedByTheCatalog()
        {
            if (!AssetDatabase.IsValidFolder(QualityBenchCatalog.Folder)) Assert.Pass("no bench assets converted yet");
            var keys = QualityBenchCatalog.All.Select(v => v.key).ToHashSet();
            foreach (var guid in AssetDatabase.FindAssets("t:GaussianSplatAsset", new[] { QualityBenchCatalog.Folder }))
            {
                var name = Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(guid));
                Assert.IsTrue(keys.Contains(name), $"{name} is converted but no QualityBenchCatalog entry shows it");
            }
        }
    }
}
