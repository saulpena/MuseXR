using System.Linq;
using MuseXR.Worlds;
using NUnit.Framework;
using UnityEngine;

namespace MusePico.Tests
{
    /// <summary>
    /// Pins the world doors: the scenes are muse-infinity's, in her order, each with a world that
    /// exists; the doors stand where the visitor can see them square on; and walking through one is
    /// told apart from walking past it.
    /// </summary>
    public class WorldDoorsTests
    {
        [Test]
        public void ScenesAreHersInHerOrderAndEachHasAWorld()
        {
            var scenes = ExhibitionWorlds.Scenes;
            Assert.AreEqual(7, scenes.Count, "her eight scenes plus the dream world, less the two with no 500k export");

            var chapters = scenes.Select(s => int.Parse(s.chapter.Substring(0, 2))).ToArray();
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5, 7, 9 }, chapters, "her numbering, gaps kept");

            var original = WorldCatalog.Get(WorldSet.Original).Select(w => w.key).ToList();
            foreach (var s in scenes)
            {
                CollectionAssert.Contains(original, s.worldKey, s.title + " has no world in the Original set");
                Assert.IsFalse(string.IsNullOrEmpty(s.title));
                StringAssert.StartsWith(s.chapter.Substring(0, 2), s.thumbnail, "thumbnail belongs to another scene");
            }
            CollectionAssert.AllItemsAreUnique(scenes.Select(s => s.worldKey).ToArray());
        }

        [Test]
        public void ThumbnailsAreInTheProject()
        {
            foreach (var s in ExhibitionWorlds.Scenes)
            {
                var path = "Assets/Textures/ExhibitionScenes/" + s.thumbnail + ".png";
                Assert.IsNotNull(UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(path), path);
            }
        }

        [Test]
        public void EachWorldLeadsToTheNextAndTheLastBackToTheFirst()
        {
            int n = ExhibitionWorlds.Scenes.Count;
            for (int i = 0; i < n - 1; i++) Assert.AreEqual(i + 1, ExhibitionWorlds.Next(i));
            Assert.AreEqual(0, ExhibitionWorlds.Next(n - 1));
        }

        [Test]
        public void EveryWorldHasAPlacedDoor()
        {
            foreach (var s in ExhibitionWorlds.Scenes)
            {
                // Far enough to be a place to walk to, near enough to be seen and reached.
                Assert.That(s.doorDistance, Is.InRange(1.5f, 15f), s.title);
                Assert.That(s.doorBearing, Is.InRange(0f, 359.9f), s.title);
                Assert.IsFalse(string.IsNullOrEmpty(s.doorReason), s.title + " has no reason for its door's place");
            }
        }

        [Test]
        public void APlacedDoorFacesTheVisitorSquareOn()
        {
            var centre = new Vector3(1f, 0.3f, -2f);
            var ahead = WorldDoorLayout.Placed(centre, Vector3.forward, 0f, 7f);
            Assert.AreEqual(new Vector3(1f, 0.3f, 5f).ToString("F3"), ahead.position.ToString("F3"));
            Assert.Greater(Vector3.Dot(ahead.rotation * Vector3.forward, Vector3.forward), 0.999f,
                           "+Z away from the visitor: what a Quad and a TMP label need to be readable");

            // Bearings turn clockwise seen from above: 90 is to the visitor's right.
            var right = WorldDoorLayout.Placed(centre, Vector3.forward, 90f, 2f);
            Assert.AreEqual(new Vector3(3f, 0.3f, -2f).ToString("F3"), right.position.ToString("F3"));
            // The facing a spawn has is honoured, not world +Z.
            var turned = WorldDoorLayout.Placed(centre, Vector3.right, 0f, 2f);
            Assert.AreEqual(new Vector3(3f, 0.3f, -2f).ToString("F3"), turned.position.ToString("F3"));
        }

        [Test]
        public void EveryDoorWorldHasACollider()
        {
            foreach (var s in ExhibitionWorlds.Scenes)
            {
                var baseKey = s.worldKey.Substring(0, s.worldKey.Length - WorldCatalog.SmallSuffix.Length);
                var path = "Assets/Worlds/Colliders/" + baseKey + "-collider.glb";
                Assert.IsNotNull(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path), path);
            }
        }

        [Test]
        public void FloorIsSampledUnderBothPostsAndTheMiddle()
        {
            var pose = new DoorPose { position = new Vector3(1f, 0.5f, 2f), rotation = Quaternion.Euler(0f, 90f, 0f) };
            var s = WorldDoorLayout.FloorSamples(pose, 0.6f);
            Assert.AreEqual(3, s.Length);
            // Facing +X, the door's right is -Z: the posts sit either side along Z.
            Assert.AreEqual(new Vector3(1f, 0.5f, 2.6f).ToString("F3"), s[0].ToString("F3"));
            Assert.AreEqual(pose.position, s[1]);
            Assert.AreEqual(new Vector3(1f, 0.5f, 1.4f).ToString("F3"), s[2].ToString("F3"));

            var p = new Vector3(2.5f, 1f, -3f);
            Assert.AreEqual(new Vector3(-2.5f, 1f, -3f), WorldDoorLayout.ToColliderFrame(p));
            Assert.AreEqual(p, WorldDoorLayout.ToColliderFrame(WorldDoorLayout.ToColliderFrame(p)));
        }

        [Test]
        public void OneStrayFloorHitCannotLiftADoor()
        {
            // The water garden's case: two samples on the path, one on the flower arch overhead.
            Assert.AreEqual(0.28f, WorldDoorLayout.Median3(0.28f, 2.2f, 0.25f), 1e-6f);
            Assert.AreEqual(0.25f, WorldDoorLayout.Median3(-1f, 0.25f, 2.2f), 1e-6f);
            Assert.AreEqual(1f, WorldDoorLayout.Median3(1f, 1f, 5f), 1e-6f);
            Assert.AreEqual(0.2f, WorldDoorLayout.Median3(float.NaN, 0.2f, 2.2f), 1e-6f, "one sample over a hole");
            Assert.IsNaN(WorldDoorLayout.Median3(float.NaN, 0.2f, float.NaN), "mostly hole: no floor");
        }

        [Test]
        public void TheVisitorLandsOnTheCaptureFloorByHerRule()
        {
            // Under the spawns, measured: the collider's floor against the playtested groundY, at
            // worldScale 1.7. Her groundAt (WalkGround.PickGroundHeight) is the rule.
            Assert.AreEqual(0.08f, MusePico.Worlds.WalkGround.PickGroundHeight(new[] { 0.08f }, 1.53f, 1.7f), 1e-6f,
                            "conservatory: inside the window, the floor wins");
            Assert.AreEqual(1.78f, MusePico.Worlds.WalkGround.PickGroundHeight(new[] { 1.78f }, 9.52f, 1.7f), 1e-6f,
                            "coastal villa: the garden 7.7 m down is still nearer than mid-air");
            Assert.AreEqual(0.27f, MusePico.Worlds.WalkGround.PickGroundHeight(new[] { 0.27f, 6.0f }, 0.34f, 1.7f), 1e-6f,
                            "a ceiling hit above the window never wins");
        }

        [Test]
        public void ThePaintersArriveBehindAndEitherSide()
        {
            var floor = new Vector3(2f, 0.3f, -1f);
            var left = WorldDoorLayout.ArrivalSlot(floor, 0f, -1f);
            var right = WorldDoorLayout.ArrivalSlot(floor, 0f, 1f);
            Assert.AreEqual(new Vector3(1.1f, 0.3f, -2.3f).ToString("F3"), left.ToString("F3"));
            Assert.AreEqual(new Vector3(2.9f, 0.3f, -2.3f).ToString("F3"), right.ToString("F3"));
            // Facing +X (yaw 90): behind is -X, the visitor's right is -Z.
            var turned = WorldDoorLayout.ArrivalSlot(floor, 90f, 1f);
            Assert.AreEqual(new Vector3(0.7f, 0.3f, -1.9f).ToString("F3"), turned.ToString("F3"));
        }

        [Test]
        public void TheNextWorldWaitsWithItsSpawnJustThroughTheDoor()
        {
            // A door facing +X at the visitor's floor 0.3; the next world's spawn at (5, 1.1, -2) in
            // its own frame, facing yaw 180 there.
            var door = new DoorPose { position = new Vector3(2f, 0.3f, 4f), rotation = Quaternion.Euler(0f, 90f, 0f) };
            var spawn = new Vector3(5f, 1.1f, -2f);
            var spawnRot = Quaternion.Euler(0f, 180f, 0f);
            WorldDoorLayout.NextWorldFrame(door, spawn, spawnRot, 1.2f, 0.3f, out var pos, out var rot);

            var landed = pos + rot * spawn;
            Assert.AreEqual(new Vector3(3.2f, 0.3f, 4f).ToString("F3"), landed.ToString("F3"),
                            "1.2 m through the doorway, on the visitor's floor");
            var facing = rot * spawnRot * Vector3.forward;
            Assert.Greater(Vector3.Dot(facing, door.rotation * Vector3.forward), 0.999f,
                           "arriving faces on through the door, as the spawn was tuned to");
            Assert.AreEqual(0f, Vector3.Angle(rot * Vector3.up, Vector3.up), 1e-3f, "worlds are only ever turned about the vertical");
        }

        [Test]
        public void EveryChapterThatLeadsOnHasADoorSpot()
        {
            var chapters = MusePico.Dialogue.ExhibitionSpine.Chapters;
            for (int i = 0; i < chapters.Count - 1; i++)
            {
                var key = chapters[i].EffectiveWorldKey;
                var spot = WorldDoorSpots.For(key);
                Assert.IsNotNull(spot, chapters[i].Chapter + " " + key + " has no door to the next chapter");
                Assert.That(spot.distance, Is.InRange(1.5f, 15f), key);
                Assert.IsFalse(string.IsNullOrEmpty(spot.reason), key + " has no reason for its door's place");
            }
        }

        [Test]
        public void WalkingThroughCountsAndWalkingPastDoesNot()
        {
            float half = WorldDoor.Width / 2f;
            Assert.IsTrue(WorldDoorLayout.Crossed(new Vector3(0.1f, 1.6f, -0.2f), new Vector3(0.12f, 1.6f, 0.05f), half));
            Assert.IsFalse(WorldDoorLayout.Crossed(new Vector3(1.5f, 1.6f, -0.2f), new Vector3(1.5f, 1.6f, 0.05f), half),
                           "round the side of the frame");
            Assert.IsFalse(WorldDoorLayout.Crossed(new Vector3(0f, 1.6f, 0.2f), new Vector3(0f, 1.6f, -0.1f), half),
                           "backing out the way you came");
            Assert.IsFalse(WorldDoorLayout.Crossed(new Vector3(0f, 1.6f, -1f), new Vector3(0f, 1.6f, -0.5f), half),
                           "approaching, not yet through");
        }
    }
}
