using System.Collections;
using GaussianSplatting.Runtime;
using MusePico.Dialogue;
using MusePico.Worlds;
using MuseXR.Worlds;
using UnityEngine;

namespace MusePico.Journey
{
    /// <summary>
    /// Stage 04 walked through doors instead of her ← / → navigator. In every chapter world the
    /// next chapter's world is loaded behind a Buddha door (<see cref="SplatPortalDoor"/>, the
    /// SplatPortal door unchanged): it rises out of the floor when the visitor looks toward its
    /// place, opens with the next world showing through it, and the visitor walks through into it.
    /// No fade, no cut. The door shuts behind them and the old world is gone.
    ///
    /// <b>Where the door stands</b> is the world's <see cref="WorldDoorSpots"/> entry, relative to
    /// its spawn, on the floor its collider reports (<see cref="CaptureFloor"/>).
    ///
    /// <b>Where the next world stands.</b> Each world is a capture in its own frame, so the next one
    /// is put down, through a pivot, so that its spawn lies just past the doorway, facing through
    /// it, at the visitor's floor height: walking through the door lands the visitor where its
    /// playtested spawn would have. Its walking floor is built in the same frame so the visitor can
    /// step across onto it. Once the door has shut, the new world and the visitor are moved back
    /// to the origin together, in one frame, with no visible change (everything the journey knows
    /// about a world assumes it stands at the origin), and the WorldCycler adopts it.
    /// </summary>
    public sealed class JourneyDoors : MonoBehaviour
    {
        public MuseumJourneyRunner runner;
        public WorldCycler worlds;
        public CaptureFloor floor;

        [Tooltip("Assets/Prefabs/Portal Door.prefab: the SplatPortal door, disabled until its worlds are set.")]
        public GameObject doorPrefab;

        [Tooltip("How far past the doorway the next world's spawn lies, metres.")]
        public float arrivalPastDoor = 1.2f;

        enum State { Idle, Loading, Waiting, Crossed }
        State _state;
        string _forKey;          // the world the door stands in
        string _noDoorFor;       // a world already found to have no door (logged once)
        SplatPortalDoor _door;
        Transform _pivot;
        CaptureProbe _nextProbe;
        GameObject _nextBounds;
        float _nextFloor;
        WorldDefinition _nextWorld;
        Coroutine _setUp;

        /// <summary>
        /// Set while a painting is opening (JourneyPaintingPortal): the cycler holds one world beside
        /// the current one, so a door waiting with its world loaded is dropped, and comes back after.
        /// </summary>
        [System.NonSerialized] public bool Hold;

        /// <summary>The door standing in the current world, or null.</summary>
        public SplatPortalDoor Door => _door;

        void Update()
        {
            if (runner == null || worlds == null || runner.Journey == null) return;
            var current = worlds.Current;
            bool inGallery = runner.Journey.Current == Stage.WorldExploration;
            if (Hold && _state != State.Crossed)
            {
                if (_state != State.Idle) Abandon();
                return;
            }

            switch (_state)
            {
                case State.Loading:
                case State.Waiting:
                    // The world under the door changed (Back, a reset) or the visitor left the gallery.
                    if (!inGallery || current == null || current.key != _forKey) { Abandon(); return; }
                    if (_state == State.Waiting && _door != null && _door.Phase >= PortalPhase.Closing) Crossed();
                    return;

                case State.Crossed:
                    if (_door == null || _door.Phase == PortalPhase.Done) Arrive();
                    return;
            }

            if (!inGallery || current == null || worlds.Beside != null) return;
            var spine = runner.Journey.Spine;
            if (spine.InFinalWorld || spine.IsLast) return;
            var key = spine.Current.EffectiveWorldKey + WorldCatalog.SmallSuffix;
            if (current.key != key || key == _noDoorFor) return;   // the chapter's world is not up yet
            _setUp = StartCoroutine(SetUp(current, spine.Index));
        }

        IEnumerator SetUp(WorldDefinition here, int chapterIndex)
        {
            _state = State.Loading;
            _forKey = here.key;

            var spot = WorldDoorSpots.For(here.key);
            var nextKey = ExhibitionSpine.Chapters[chapterIndex + 1].EffectiveWorldKey + WorldCatalog.SmallSuffix;
            _nextWorld = Find(nextKey);
            if (spot == null || _nextWorld == null)
            {
                Debug.LogWarning($"[JourneyDoors] no door in {here.key}: " + (spot == null ? "no door spot placed" : $"no world '{nextKey}'"));
                _noDoorFor = here.key;
                _state = State.Idle;
                yield break;
            }

            // The door, in the current world (at the origin), relative to its spawn.
            var spawn = here.ScaledSpawn; spawn.y = worlds.FloorY;
            // Spots were chosen as bearings from the yaw as entered, before her worlds were turned to
            // face the way they do in her app; measuring from that keeps every door where it was put.
            var pose = WorldDoorLayout.Placed(spawn, here.EnteredRotation * Vector3.forward, spot.bearing, spot.distance);
            pose.position.y = Seat(pose, spot.onCollider);

            // The next world's frame: its spawn just through the doorway, facing through it, at the
            // visitor's floor height so the step across is level.
            _pivot = new GameObject("Next World (behind the door): " + _nextWorld.key).transform;
            _nextProbe = floor != null ? floor.Open(_nextWorld, _pivot) : null;
            _nextFloor = _nextWorld.groundY * _nextWorld.worldScale;
            if (_nextProbe != null)
            {
                float y = _nextProbe.Floor(_nextWorld.ScaledSpawn);   // pivot still at identity
                if (!float.IsNaN(y)) _nextFloor = y;
            }
            var nextSpawn = _nextWorld.ScaledSpawn; nextSpawn.y = _nextFloor;
            WorldDoorLayout.NextWorldFrame(pose, nextSpawn, _nextWorld.SpawnRotation, arrivalPastDoor, worlds.FloorY,
                                           out var framePos, out var frameRot);
            _pivot.SetPositionAndRotation(framePos, frameRot);

            GaussianSplatRenderer next = null;
            yield return worlds.LoadBeside(_nextWorld, _pivot, r => next = r);
            if (next == null || worlds.Current == null || worlds.Current.key != _forKey) { Abandon(); yield break; }

            // Its walking floor, in its frame. The walls stay off until the visitor is in it: they
            // are laid round the next capture and could stand across this world's doorway.
            _nextBounds = PhysicsBounds.BuildFromSplatBounds("World Bounds (physics only)",
                next.asset.boundsMin, next.asset.boundsMax, _nextWorld.worldScale, _nextFloor,
                worlds.boundsInset, worlds.wallHeight);
            _nextBounds.transform.SetParent(_pivot, false);
            SetWalls(_nextBounds, false);

            var go = Instantiate(doorPrefab);
            go.name = "Portal Door → " + _nextWorld.displayName;
            go.transform.SetPositionAndRotation(pose.position, pose.rotation);
            _door = go.GetComponent<SplatPortalDoor>();
            _door.currentWorld = worlds.CurrentRenderer;
            _door.nextWorld = next;
            _door.currentWorldProps = new GameObject[0];
            if (worlds.xrOrigin != null) _door.head = worlds.xrOrigin.Camera.transform;
            _door.enabled = true;

            // The next world is seen through the door from here, so the far plane has to reach its
            // horizon too: through a door from the temple hall (far 250 m) the peach plaza's sky
            // (far 1000 m) was cut off and showed black. The cycler sets the new world's own far
            // plane when it adopts it.
            var eye = worlds.xrOrigin != null ? worlds.xrOrigin.Camera : null;
            if (eye != null) eye.farClipPlane = Mathf.Max(eye.farClipPlane, _nextWorld.cameraFar);

            _state = State.Waiting;
            _setUp = null;
            Debug.Log($"[JourneyDoors] door in {here.displayName} to {_nextWorld.displayName} at {pose.position:F2} ({spot.reason})");
        }

        /// <summary>The visitor has stepped through: they are in the next chapter.</summary>
        void Crossed()
        {
            _state = State.Crossed;
            if (runner.wall != null) runner.wall.gameObject.SetActive(false);   // the old room's pictures
            runner.AdvanceByDoor();
        }

        /// <summary>
        /// The door has shut and the old world is gone. Move the new world and the visitor back to the
        /// origin by the same rigid transform, in one frame, and hand the world to the cycler.
        /// </summary>
        void Arrive()
        {
            var back = Quaternion.Inverse(_pivot.rotation);
            var from = _pivot.position;

            var rig = worlds.xrOrigin != null ? worlds.xrOrigin.transform : null;
            var body = rig != null ? rig.GetComponent<CharacterController>() : null;
            bool had = body != null && body.enabled;
            if (had) body.enabled = false;
            if (rig != null) rig.SetPositionAndRotation(back * (rig.position - from), back * rig.rotation);
            _pivot.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            Physics.SyncTransforms();
            if (had) body.enabled = true;

            SetWalls(_nextBounds, true);
            if (floor != null) floor.Adopt(_nextProbe);
            _nextProbe = null;
            if (_door != null) Destroy(_door.gameObject);
            _door = null;

            worlds.AdoptBeside(_nextBounds, _nextFloor);   // WorldChanged: the wall, the painters, walking
            _nextBounds = null;
            if (_pivot != null) Destroy(_pivot.gameObject);
            _pivot = null;
            if (runner.wall != null) runner.wall.gameObject.SetActive(true);
            _state = State.Idle;
        }

        /// <summary>Drop a door that has not been walked through, and the world behind it.</summary>
        void Abandon()
        {
            if (_setUp != null) StopCoroutine(_setUp);
            _setUp = null;
            if (_door != null)
            {
                if (_door.currentWorld != null) _door.currentWorld.m_PortalRole = SplatPortalRole.None;
                Destroy(_door.gameObject);
            }
            _door = null;
            worlds.CancelBeside();
            _nextProbe?.Dispose();
            _nextProbe = null;
            if (_nextBounds != null) Destroy(_nextBounds);
            _nextBounds = null;
            if (_pivot != null) Destroy(_pivot.gameObject);
            _pivot = null;
            _state = State.Idle;
        }

        float Seat(DoorPose pose, bool onCollider)
        {
            var probe = floor != null ? floor.Current : null;
            if (!onCollider || probe == null) return worlds.FloorY;
            var s = WorldDoorLayout.FloorSamples(pose, 1.2f);
            float y = WorldDoorLayout.Median3(probe.Floor(s[0]), probe.Floor(s[1]), probe.Floor(s[2]));
            return float.IsNaN(y) ? worlds.FloorY : y;
        }

        static void SetWalls(GameObject bounds, bool on)
        {
            if (bounds == null) return;
            foreach (Transform c in bounds.transform)
                if (c.name.StartsWith("Wall")) c.gameObject.SetActive(on);
        }

        static WorldDefinition Find(string key)
        {
            foreach (var w in WorldCatalog.Small) if (w.key == key) return w;
            return null;
        }

        void OnDisable() { if (_state != State.Idle && _state != State.Crossed) Abandon(); }
    }
}
