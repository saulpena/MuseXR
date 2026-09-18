using System.Collections;
using System.Collections.Generic;
using MusePico.Worlds;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace MusePico.Tests
{
    /// <summary>
    /// The walkability regression: load a world scene, push the thumbstick in six directions
    /// through the real locomotion stack, and assert the visitor stays on the floor and inside
    /// the world.
    ///
    /// <b>This is the test every new Marble world has to pass.</b> Adding one is a single
    /// <c>TestCase</c> line — the thresholds, the driving and the reporting are shared, so two
    /// worlds are judged the same way and a regression in <c>WalkRig</c> shows up on all of them
    /// at once.
    ///
    /// It runs in PlayMode rather than EditMode on purpose: gravity, CharacterController collision
    /// and step offset only mean anything across real frames. The judgement half is pure and lives
    /// in <see cref="WalkAudit"/>, which has its own EditMode tests — so what needs a running app
    /// is only the driving, which is the split the project asks for.
    /// </summary>
    [TestFixture]
    public class WalkabilityTests
    {
        /// <summary>Long enough to cross any of these worlds end to end at the rig's move speed
        /// and be stopped by whatever is going to stop us. 12 s was not: the Sunlit gallery's
        /// forward leg walked 30.01 m in a 52 m hall and the leg expired mid-floor, so that
        /// direction's boundary went untested and the run still read green. A leg that ends with
        /// stoppedBy = "nothing" is now called out in the report for exactly this reason.</summary>
        const float LegSeconds = 26f;

        static readonly (string name, Vector2 stick)[] Legs =
        {
            ("forward",        new Vector2(0f, 1f)),
            ("back",           new Vector2(0f, -1f)),
            ("right",          new Vector2(1f, 0f)),
            ("left",           new Vector2(-1f, 0f)),
            ("diagonal fwd-R", new Vector2(0.707f, 0.707f)),
            ("diagonal back-L", new Vector2(-0.707f, -0.707f)),
        };

        [UnityTest]
        [TestCase("Assets/Scenes/Tests/SunlitGallery.unity", ExpectedResult = null)]
        [TestCase("Assets/Scenes/WalkTest.unity", ExpectedResult = null)]
        public IEnumerator World_is_walkable_and_contained(string scenePath)
        {
            yield return LoadScene(scenePath);

            var rig = WalkHarness.Resolve();
            Assert.IsTrue(rig.IsComplete, $"scene {scenePath} is missing part of the rig: {rig.Describe()}");
            Assert.IsNotNull(rig.walkRig, $"scene {scenePath} has no WalkRig");

            // The world loads through Addressables now, so the collider does not exist for the
            // first few frames. Without this wait the first leg walks through an empty scene and
            // falls for ever, which would read as a collider failure.
            yield return WaitForReady(rig.walkRig, scenePath);

            var disabledTeleport = WalkHarness.DisableTeleportation();
            Debug.Log($"[WalkabilityTests] teleportation disabled on {disabledTeleport.Count} provider(s) " +
                      "so a teleport ray cannot move the visitor past a wall mid-test.");

            var bounds = rig.walkRig.bounds * rig.walkRig.worldScale;
            float floorY = rig.walkRig.groundY * rig.walkRig.worldScale;
            var spawn = new Vector3(rig.walkRig.spawn.x * rig.walkRig.worldScale,
                                    rig.origin.transform.position.y,
                                    rig.walkRig.spawn.y * rig.walkRig.worldScale);

            var results = new List<WalkLeg>();
            try
            {
                foreach (var (name, stick) in Legs)
                {
                    WalkHarness.Reseat(rig, spawn, rig.walkRig.yawDegrees);
                    yield return null;

                    var leg = new WalkLeg();
                    yield return WalkHarness.Walk(rig, name, stick, LegSeconds, leg);
                    results.Add(leg);
                }
            }
            finally
            {
                WalkHarness.Restore(disabledTeleport);
            }

            Debug.Log(WalkAudit.Report(scenePath, results, bounds, floorY));

            var failures = new List<string>();
            foreach (var leg in results)
                foreach (var problem in WalkAudit.Problems(leg, bounds, floorY))
                    failures.Add($"{leg.name}: {problem}");

            Assert.IsEmpty(failures,
                $"{scenePath} failed walkability:\n  " + string.Join("\n  ", failures) +
                "\n" + WalkAudit.Report(scenePath, results, bounds, floorY));
        }

        static IEnumerator LoadScene(string scenePath)
        {
#if UNITY_EDITOR
            // EditorSceneManager rather than SceneManager: these scenes are not all in Build
            // Settings (each ships as its own single-scene APK), so a runtime load by name fails.
            // This is why the suite is Editor-run — it is a development gate, not a device test.
            EditorSceneManager.LoadSceneInPlayMode(
                scenePath, new UnityEngine.SceneManagement.LoadSceneParameters(
                    UnityEngine.SceneManagement.LoadSceneMode.Single));
            yield return null;
            yield return null;
#else
            Assert.Ignore("WalkabilityTests loads scenes by path and only runs in the Editor.");
            yield break;
#endif
        }

        static IEnumerator WaitForReady(WalkRig walkRig, string scenePath)
        {
            const float timeout = 30f;
            float waited = 0f;
            while (!walkRig.Ready && waited < timeout)
            {
                waited += Time.deltaTime;
                yield return null;
            }
            Assert.IsTrue(walkRig.Ready,
                $"{scenePath}: WalkRig never became ready within {timeout:F0}s. The world is loaded " +
                "through Addressables — has the content been built (MuseXR > Addressables > Build " +
                "Content) and the world marked (Mark Worlds Addressable)?");
            // One more frame so the CharacterController has settled onto the floor.
            yield return null;
        }
    }
}
