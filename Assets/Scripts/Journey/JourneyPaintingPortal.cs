using System.Collections;
using GaussianSplatting.Runtime;
using MusePico.Dialogue;
using MusePico.Worlds;
using MuseXR.Worlds;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MusePico.Journey
{
    /// <summary>
    /// A painting in the gallery that is a door into its own world (<see cref="PaintingWorlds"/>),
    /// both ways. Separate from the chapter doors: a door leads on, a painting is a detour.
    ///
    /// <b>Making it a door.</b> In a headset, touch the centre of the canvas with a controller and
    /// hold it there for two seconds: a gold ring fills on the canvas, and when it closes the
    /// painting grows to walk-in size, standing on the floor, and opens. Becoming large does not open
    /// it on its own (Saul, 1 Oct 2026). In the Editor, P near it does the same at once. Its world is
    /// loaded behind it, and follows it if it is carried.
    ///
    /// <b>Two-way, and it stays.</b> Near it, on either side, the canvas opens onto the other world;
    /// step away and it heals shut. Leaning in to look only shows the other world round your head:
    /// you count as inside once you are well past the canvas
    /// (<see cref="PaintingPortalRules.InsideAfter"/>). Inside, the painting stands behind you, shut,
    /// still showing its picture; walk back up to it and it opens onto the gallery, and you walk out
    /// where you went in. Both worlds stay loaded the whole time, nobody is moved, nothing fades.
    ///
    /// The rendering is the splat portal the doors use (<see cref="SplatPortal"/>): the world the eye
    /// is in draws everywhere except the opening, the other world only through it.
    /// </summary>
    public sealed class JourneyPaintingPortal : MonoBehaviour
    {
        public MuseumJourneyRunner runner;
        public WorldCycler worlds;
        public CaptureFloor floor;
        [Tooltip("The doors share the cycler's one world-beside slot: they are held off while a painting is a door.")]
        public JourneyDoors doors;

        [Header("Touch to open")]
        [Tooltip("How long a controller must stay on the centre of the canvas, seconds.")]
        public float holdSeconds = 2f;
        [Tooltip("How far from the centre still counts as the centre, metres.")]
        public float touchRadius = 0.2f;
        [Tooltip("How far from the paint the tip may be, metres.")]
        public float touchDepth = 0.12f;
        [Tooltip("A tip that slips off for less than this does not reset the hold, seconds.")]
        public float touchGrace = 0.15f;

        [Tooltip("The canvas height it must have to open, metres, and roughly upright.")]
        public float openAtHeight = 1.8f;
        [Range(0f, 90f)] public float maxTiltDegrees = 25f;
        [Tooltip("Editor P: the canvas height it grows to, metres.")]
        public float growToHeight = 2.1f;
        [Tooltip("Editor P: only within this distance of the painting, metres.")]
        public float growWithin = 10f;

        [Header("Two-way door")]
        [Tooltip("It opens while the eye is this close to the canvas plane, on either side, metres.")]
        public float openWithin = 3.5f;
        [Tooltip("...and no further than this past either edge, metres.")]
        public float openSideways = 1.5f;
        [Tooltip("How far past the canvas the eye must go to count as walked through (either way), metres.")]
        public float throughBand = 0.8f;
        public float openSeconds = 1.5f;
        public float closeSeconds = 1f;
        [Tooltip("How far past the canvas the world's spawn stands, metres.")]
        public float behind = 0.4f;
        public float followSharpness = 6f;

        /// <summary>Idle: a painting. Loading / Ready: a door with its world loaded. Dormant: still
        /// a door (large, framed) but its world let go, so the chapter's own door can come back.</summary>
        enum State { Idle, Loading, Ready, Dormant }

        [Tooltip("Outside, shut, and this far from the painting: its world is let go and the chapter door returns, metres.")]
        public float releaseBeyond = 6f;
        [Tooltip("A dormant painting reloads its world when the visitor comes this close, metres.")]
        public float reloadWithin = 4.5f;
        State _state;
        GameObject _painting;          // the hung work: a Quad with its frame as children
        Renderer _front;
        Renderer _back;                // the picture again, on the canvas back: the shut door seen from inside
        GameObject _ring;              // the frame as an open ring: its boards are solid behind the canvas
        string _id;
        string _forKey;
        WorldDefinition _next;
        Transform _pivot;              // follows the painting
        Transform _frame;              // under the pivot: puts the world's spawn at the pivot
        CaptureProbe _probe;
        GameObject _bounds;
        GaussianSplatRenderer _gallery, _world;
        int _worldSortNth = 4;
        Material _mask;
        Mesh _maskMesh;
        float _tear;
        bool _inside;
        bool _placed;
        Coroutine _setUp;
        float _held;                   // seconds the centre has been touched, non-stop
        float _offFor;
        LineRenderer _ringTrack, _ringFill;
        Transform _indicator;
        BoxCollider _ground;           // inside: a patch of the capture's real ground under the visitor

        static readonly int DoorHalf = Shader.PropertyToID("_DoorHalf");
        static readonly int MaskHalf = Shader.PropertyToID("_MaskHalf");
        static readonly int DoorOn = Shader.PropertyToID("_DoorOn");
        static readonly int Seep = Shader.PropertyToID("_Seep");
        static readonly int SeepRadius = Shader.PropertyToID("_SeepRadius");
        static readonly int ClipSeepToDoor = Shader.PropertyToID("_ClipSeepToDoor");

        /// <summary>The visitor is inside the painting's world.</summary>
        public bool Inside => _inside;

        void Update()
        {
            if (runner == null || worlds == null || runner.Journey == null) return;
            bool inGallery = runner.Journey.Current == Stage.WorldExploration;
            var current = worlds.Current;

            if (_state != State.Idle)
            {
                if (!inGallery || current == null || current.key != _forKey || _painting == null) { Abandon(); return; }

                // Only one world can wait beside the current one, and the chapter's door needs it
                // too (Saul, 1 Oct 2026: after the painting there was no way on). Walked away from
                // the shut painting: let its world go so the door comes back. Walked back: reload.
                if (_state == State.Ready && !_inside && _tear <= 0f && !Within(_painting, releaseBeyond)) Release();
                else if (_state == State.Dormant && Within(_painting, reloadWithin))
                    _setUp = StartCoroutine(SetUp(_painting, _id, current));
                return;
            }

            if (!inGallery || current == null || runner.wall == null) { ShowHold(null, 0f); return; }
            var painting = FindPainting(out var id);
            if (painting == null) { ShowHold(null, 0f); return; }

            // Editor: P does what the two-second touch does.
            bool activate = Keyboard.current != null && Keyboard.current.pKey.wasPressedThisFrame && Within(painting, growWithin);

            if (Touching(painting)) { _held += Time.deltaTime; _offFor = 0f; }
            else if (_held > 0f && (_offFor += Time.deltaTime) > touchGrace) _held = 0f;
            ShowHold(painting, _held / Mathf.Max(1e-3f, holdSeconds));
            if (_held >= holdSeconds) activate = true;

            if (!activate) return;
            _held = 0f;
            ShowHold(null, 0f);
            Grow(painting);
            _setUp = StartCoroutine(SetUp(painting, id, current));
        }

        bool Within(GameObject painting, float metres)
        {
            var eye = Eye();
            if (eye == null) return false;
            var flat = painting.transform.position - eye.position; flat.y = 0f;
            return flat.magnitude <= metres;
        }

        /// <summary>Either controller's poke tip is on the centre of the canvas.</summary>
        bool Touching(GameObject painting)
        {
            if (worlds.xrOrigin == null) return false;
            var t = painting.transform;
            var half = new Vector2(Mathf.Abs(t.lossyScale.x), Mathf.Abs(t.lossyScale.y)) * 0.5f;
            var toCanvas = Matrix4x4.TRS(t.position, t.rotation, Vector3.one).inverse;
            foreach (var poke in worlds.xrOrigin.GetComponentsInChildren<UnityEngine.XR.Interaction.Toolkit.Interactors.XRPokeInteractor>())
            {
                if (!poke.isActiveAndEnabled) continue;
                var tip = poke.attachTransform != null ? poke.attachTransform.position : poke.transform.position;
                if (PaintingPortalRules.TouchesCentre(toCanvas.MultiplyPoint3x4(tip), half, touchRadius, touchDepth)) return true;
            }
            return false;
        }

        /// <summary>
        /// The hold indicator: a faint ring on the centre of the canvas with a gold arc that fills
        /// clockwise over the two seconds. Hidden when nothing is touching.
        /// </summary>
        void ShowHold(GameObject painting, float progress)
        {
            if (painting == null || progress <= 0f)
            {
                if (_indicator != null) _indicator.gameObject.SetActive(false);
                return;
            }
            if (_indicator == null)
            {
                _indicator = new GameObject("Painting Hold Indicator").transform;
                _ringTrack = Ring(_indicator, "Track", new Color(1f, 1f, 1f, 0.35f), 0.008f);
                _ringFill = Ring(_indicator, "Fill", new Color(0.788f, 0.667f, 0.447f, 1f), 0.016f);
            }
            _indicator.gameObject.SetActive(true);
            var t = painting.transform;
            // A hair in front of the picture, facing out of it; a fixed size whatever the canvas.
            _indicator.SetPositionAndRotation(t.position - t.forward * 0.02f, t.rotation);
            const float radius = 0.12f;
            Arc(_ringTrack, radius, 1f);
            Arc(_ringFill, radius, Mathf.Clamp01(progress));
        }

        static LineRenderer Ring(Transform parent, string name, Color colour, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.widthMultiplier = width;
            line.numCapVertices = 2;
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = 3100;
            m.SetColor("_BaseColor", colour);
            line.sharedMaterial = m;
            return line;
        }

        static void Arc(LineRenderer line, float radius, float fraction)
        {
            int n = Mathf.Max(2, Mathf.CeilToInt(64 * fraction) + 1);
            line.positionCount = n;
            for (int i = 0; i < n; i++)
            {
                // Clockwise from twelve o'clock, as seen from in front of the picture (-Z).
                float a = Mathf.PI * 0.5f + 2f * Mathf.PI * fraction * i / (n - 1);
                line.SetPosition(i, new Vector3(-Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f));
            }
        }

        void LateUpdate()
        {
            if (_state == State.Ready && _painting != null) Drive(Time.deltaTime);
            KeepOnTheGround();
        }

        /// <summary>
        /// Inside, the visitor stands on the painting world's real ground, the surface its collider
        /// reports (her groundAt), not on the flat physics floor laid at the arrival spot's height. The
        /// painters walk on that collider; on the flat floor the visitor stood a metre and more below
        /// them wherever the bank rose. A small box follows under the visitor at the ground's height;
        /// the flat floor stays as the net where the collider has nothing.
        /// </summary>
        void KeepOnTheGround()
        {
            bool on = _state == State.Ready && _inside && _probe != null;
            if (!on) { if (_ground != null) _ground.enabled = false; return; }
            var eye = Eye();
            if (eye == null) return;
            float y = _probe.Floor(eye.position);
            if (float.IsNaN(y)) return;                    // keep the last patch: no hole under the visitor
            if (_ground == null)
            {
                _ground = new GameObject("Painting Ground (physics only)").AddComponent<BoxCollider>();
                _ground.size = new Vector3(1.6f, 0.2f, 1.6f);
            }
            _ground.enabled = true;
            _ground.transform.SetPositionAndRotation(new Vector3(eye.position.x, y - 0.1f, eye.position.z), Quaternion.identity);
        }

        /// <summary>The hung work that has a world inside it, or null.</summary>
        GameObject FindPainting(out string id)
        {
            foreach (Transform c in runner.wall)
                if (PaintingWorlds.For(c.name) != null) { id = c.name; return c.gameObject; }
            id = null;
            return null;
        }

        /// <summary>Grow it to walk-in size, standing on the floor (left as it is if already that large).</summary>
        void Grow(GameObject painting)
        {
            var t = painting.transform;
            float height = Mathf.Abs(t.lossyScale.y);
            if (height < 1e-3f || height >= growToHeight) return;
            t.localScale *= growToHeight / height;
            var p = t.position;
            t.position = new Vector3(p.x, worlds.FloorY + 0.02f + growToHeight * 0.5f, p.z);
        }

        IEnumerator SetUp(GameObject painting, string id, WorldDefinition here)
        {
            _state = State.Loading;
            _painting = painting;
            _id = id;
            _forKey = here.key;

            // Let a waiting door drop its world first: it does so in its own Update, and its
            // CancelBeside would otherwise cancel the load started below.
            if (doors != null) doors.Hold = true;
            for (int i = 0; i < 30 && worlds.Beside != null; i++) yield return null;
            yield return null;
            if (worlds.Beside != null) { Debug.LogWarning("[PaintingPortal] a door still holds its world"); Abandon(); yield break; }

            var key = PaintingWorlds.For(id);
            _next = null;
            foreach (var w in WorldCatalog.Small) if (w.key == key) _next = w;
            if (_next == null) { Debug.LogWarning($"[PaintingPortal] no world '{key}'"); Abandon(); yield break; }

            // The pivot follows the painting; the frame under it puts the world's spawn at the pivot,
            // facing along it, so walking through lands where its spawn would.
            _pivot = new GameObject("Painting World (behind the canvas): " + _next.key).transform;
            _frame = new GameObject("Spawn Frame").transform;
            _frame.SetParent(_pivot, false);
            _probe = floor != null ? floor.Open(_next, _frame) : null;
            float nextFloor = _next.groundY * _next.worldScale;
            if (_probe != null)
            {
                float y = _probe.Floor(_next.ScaledSpawn);   // frame still at identity
                if (!float.IsNaN(y)) nextFloor = y;
            }
            var spawn = _next.ScaledSpawn; spawn.y = nextFloor;
            var back = Quaternion.Inverse(_next.SpawnRotation);
            _frame.localRotation = back;
            _frame.localPosition = -(back * spawn);
            _placed = false;
            Follow(force: true);

            GaussianSplatRenderer next = null;
            yield return worlds.LoadBeside(_next, _frame, r => next = r);
            if (next == null || _painting == null || worlds.Current == null || worlds.Current.key != _forKey) { Abandon(); yield break; }

            _bounds = PhysicsBounds.BuildFromSplatBounds("World Bounds (physics only)",
                next.asset.boundsMin, next.asset.boundsMax, _next.worldScale, nextFloor,
                worlds.boundsInset, worlds.wallHeight);
            _bounds.transform.SetParent(_frame, false);
            SetWalls(_bounds, false);     // until the visitor is in it: they could stand across the gallery

            _gallery = worlds.CurrentRenderer;
            _world = next;
            _worldSortNth = _world.m_SortNthFrame;
            _world.m_DrawLimit = 0;       // loaded, not drawn, until the canvas opens

            var door = doors != null && doors.doorPrefab != null ? doors.doorPrefab.GetComponent<SplatPortalDoor>() : null;
            _mask = door != null && door.maskMaterial != null ? new Material(door.maskMaterial) { name = "Painting Mask (runtime)" } : null;
            _maskMesh = door != null ? door.maskMesh : null;

            _front = _painting.GetComponent<Renderer>();
            if (_back == null) _back = MakeBack(_painting, _front);
            if (_ring == null) _ring = MakeRing(_painting);
            LetTheVisitorThrough();

            var eye = Eye();
            var cam = eye != null ? eye.GetComponent<Camera>() : null;
            if (cam != null) cam.farClipPlane = Mathf.Max(cam.farClipPlane, _next.cameraFar);

            _tear = 0f;
            _inside = false;
            _state = State.Ready;
            _setUp = null;
            Debug.Log($"[PaintingPortal] {runner.TitleOf(_id)} is a door onto {_next.displayName}");
        }

        void Drive(float dt)
        {
            var eye = Eye();
            if (eye == null) return;
            var canvas = _painting.transform;
            var size = new Vector2(Mathf.Abs(canvas.lossyScale.x), Mathf.Abs(canvas.lossyScale.y));
            var half = size * 0.5f;
            var canvasToWorld = Matrix4x4.TRS(canvas.position, canvas.rotation, Vector3.one);
            var e = canvasToWorld.inverse.MultiplyPoint3x4(eye.position);
            bool eyeInside = e.z > 0f;

            // Which world the visitor is in: only once they are well past the canvas.
            bool was = _inside;
            _inside = PaintingPortalRules.InsideAfter(_inside, e.z, throughBand);
            if (_inside != was) SideChanged();

            // Open while near on either side, held roughly upright and large; heal otherwise.
            bool large = size.y >= openAtHeight && PaintingPortalRules.TiltDegrees(canvas.up) <= maxTiltDegrees;
            bool near = large && PaintingPortalRules.NearTwoWay(e, half, openWithin, openSideways);
            _tear = Mathf.MoveTowards(_tear, near ? 1f : 0f, dt / Mathf.Max(1e-3f, near ? openSeconds : closeSeconds));

            // The world follows the painting while it is carried in the gallery; never while the
            // visitor is in it, or its ground would move under them.
            if (!_inside && e.z < -0.3f) Follow(force: false);

            // The opening sits a centimetre on the eye's side of the paint, so the canvas surface the
            // eye faces lies beyond it and is erased where the opening is.
            var doorToWorld = Matrix4x4.TRS(canvas.position + canvas.forward * (eyeInside ? 0.01f : -0.01f),
                                            canvas.rotation, Vector3.one);
            bool showing = _tear > 0.001f;
            var here = eyeInside ? _world : _gallery;
            var there = eyeInside ? _gallery : _world;

            // Leaning into the plane: the erase cannot follow the eye into it, so the paint would
            // fill the view for a frame.
            bool inPlane = PortalGeometry.InCrossingZone(e, half, 0.3f);
            if (_front != null) _front.enabled = !(showing && inPlane);
            if (_back != null) _back.enabled = !showing;

            if (showing && here != null && there != null)
            {
                SplatPortal.MaskMaterial = _mask;
                SplatPortal.MaskMesh = _maskMesh;
                SplatPortal.DoorToWorld = doorToWorld;
                SplatPortal.WorldToDoor = doorToWorld.inverse;
                SplatPortal.HalfSize = half;
                SplatPortal.MaskHalfSize = half;
                SplatPortal.CullMargin = 0.5f;
                // From the gallery the picture is erased in the opening; from inside, the gallery's
                // own meshes are what the opening shows, so nothing is erased.
                SplatPortal.EraseMeshes = !eyeInside;
                here.m_PortalRole = SplatPortalRole.Outside;
                here.m_DrawLimit = -1;
                there.m_PortalRole = SplatPortalRole.Through;
                there.m_DrawLimit = -1;
                _world.m_SortNthFrame = eyeInside ? _worldSortNth : 1;   // it may move with the painting
                if (_mask != null)
                {
                    _mask.SetVector(DoorHalf, half);
                    _mask.SetVector(MaskHalf, half);
                    _mask.SetFloat(DoorOn, 0f);
                    _mask.SetFloat(ClipSeepToDoor, 1f);
                    _mask.SetFloat(SeepRadius, half.magnitude * 1.15f);
                    _mask.SetFloat(Seep, PortalSequence.Ease(_tear));
                }
                SplatPortal.Active = true;
            }
            else
            {
                SplatPortal.Active = false;
                SplatPortal.EraseMeshes = false;
                if (_gallery != null) { _gallery.m_PortalRole = SplatPortalRole.None; _gallery.m_DrawLimit = _inside ? 0 : -1; }
                if (_world != null) { _world.m_PortalRole = SplatPortalRole.None; _world.m_DrawLimit = _inside ? -1 : 0; _world.m_SortNthFrame = _worldSortNth; }
            }
        }

        /// <summary>Walked in or out: swap which world's walls stand, and tell the journey.</summary>
        void SideChanged()
        {
            SetWalls(_bounds, _inside);
            SetWalls(worlds.CurrentBounds, !_inside);
            runner.SetInsidePainting(_inside, _id);
            // The painters come with the visitor: their floor is rebuilt in the world now entered.
            if (floor != null) floor.ArriveIn(_inside ? _probe : null);
            Debug.Log("[PaintingPortal] " + (_inside ? "walked into " + _next.displayName : "walked back out to the gallery"));
        }

        void Follow(bool force)
        {
            if (_pivot == null || _painting == null) return;
            var canvas = _painting.transform;
            if (!PaintingPortalRules.Heading(canvas.forward, out _)) return;   // lying flat: keep the last pose
            PaintingPortalRules.FollowPose(canvas.position, canvas.forward, behind, worlds.FloorY, out var pos, out var yaw);
            var rot = Quaternion.Euler(0f, yaw, 0f);
            if (force || !_placed) { _pivot.SetPositionAndRotation(pos, rot); _placed = true; return; }
            float k = 1f - Mathf.Exp(-followSharpness * Time.deltaTime);
            _pivot.SetPositionAndRotation(Vector3.Lerp(_pivot.position, pos, k), Quaternion.Slerp(_pivot.rotation, rot, k));
        }

        /// <summary>
        /// The canvas back: the same picture, turned to read correctly from inside. A Quad shows one
        /// side only, so without it the shut door would be an empty frame seen from the brook.
        /// </summary>
        static Renderer MakeBack(GameObject painting, Renderer front)
        {
            if (front == null) return null;
            var back = GameObject.CreatePrimitive(PrimitiveType.Quad);
            back.name = "Canvas Back";
            Destroy(back.GetComponent<Collider>());
            back.transform.SetParent(painting.transform, false);
            back.transform.localPosition = new Vector3(0f, 0f, 0.002f);
            back.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            var r = back.GetComponent<Renderer>();
            r.sharedMaterial = front.sharedMaterial;
            return r;
        }

        /// <summary>The gallery frame's boards: solid, and behind the canvas, so from inside they filled the opening.</summary>
        static readonly string[] FrameBoards = { "Frame (walnut)", "Mat", "Fillet (gold)" };

        /// <summary>
        /// Swap the frame's solid boards for an open ring of the same walnut: from the gallery the
        /// opening erases what lies behind it, but from inside nothing does, and the boards stood
        /// across the doorway as a brown wall.
        /// </summary>
        static GameObject MakeRing(GameObject painting)
        {
            var t = painting.transform;
            Material walnut = null;
            foreach (var name in FrameBoards)
            {
                var board = t.Find(name);
                if (board == null) continue;
                if (walnut == null && name == "Frame (walnut)") walnut = board.GetComponent<Renderer>().sharedMaterial;
                board.gameObject.SetActive(false);
            }
            var ring = new GameObject("Door Ring");
            ring.transform.SetParent(t, false);
            float w = Mathf.Abs(t.lossyScale.x), h = Mathf.Abs(t.lossyScale.y);
            const float bar = 0.12f, depth = 0.08f;
            // In canvas-local units (the canvas is a 1 x 1 quad scaled to w x h).
            float bx = bar / w, by = bar / h;
            Bar(ring.transform, walnut, new Vector3(0f, 0.5f + by * 0.5f, 0f), new Vector3(1f + 2f * bx, by, depth));
            Bar(ring.transform, walnut, new Vector3(0f, -0.5f - by * 0.5f, 0f), new Vector3(1f + 2f * bx, by, depth));
            Bar(ring.transform, walnut, new Vector3(-0.5f - bx * 0.5f, 0f, 0f), new Vector3(bx, 1f, depth));
            Bar(ring.transform, walnut, new Vector3(0.5f + bx * 0.5f, 0f, 0f), new Vector3(bx, 1f, depth));
            return ring;
        }

        static void Bar(Transform ring, Material material, Vector3 localPosition, Vector3 localScale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Bar";
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(ring, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = localScale;
            if (material != null) go.GetComponent<Renderer>().sharedMaterial = material;
        }

        /// <summary>The painting's colliders are for grabbing; the body walks through them.</summary>
        void LetTheVisitorThrough()
        {
            var body = worlds.xrOrigin != null ? worlds.xrOrigin.GetComponent<CharacterController>() : null;
            if (body == null || _painting == null) return;
            foreach (var c in _painting.GetComponentsInChildren<Collider>(true))
                Physics.IgnoreCollision(body, c, true);
        }

        Transform Eye() => worlds.xrOrigin != null ? worlds.xrOrigin.Camera.transform
                         : Camera.main != null ? Camera.main.transform : null;

        /// <summary>
        /// Let the painting's world go but keep the painting a door: large, framed, its back showing.
        /// The chapter door may now take the world-beside slot; coming back within
        /// <see cref="reloadWithin"/> loads the painting's world again.
        /// </summary>
        void Release()
        {
            SplatPortal.Active = false;
            SplatPortal.EraseMeshes = false;
            if (_gallery != null) { _gallery.m_PortalRole = SplatPortalRole.None; _gallery.m_DrawLimit = -1; }
            worlds.CancelBeside();
            _probe?.Dispose();
            _probe = null;
            if (_bounds != null) Destroy(_bounds);
            _bounds = null;
            if (_pivot != null) Destroy(_pivot.gameObject);
            _pivot = null; _frame = null; _world = null; _gallery = null;
            if (_mask != null) Destroy(_mask);
            _mask = null;
            if (_front != null) _front.enabled = true;
            if (_back != null) _back.enabled = true;
            if (doors != null) doors.Hold = false;
            _state = State.Dormant;
            Debug.Log("[PaintingPortal] let the painting's world go; the chapter door can return");
        }

        void Abandon()
        {
            if (_setUp != null) StopCoroutine(_setUp);
            _setUp = null;
            SplatPortal.Active = false;
            SplatPortal.EraseMeshes = false;
            if (_gallery != null) { _gallery.m_PortalRole = SplatPortalRole.None; _gallery.m_DrawLimit = -1; }
            if (_inside) { _inside = false; runner.SetInsidePainting(false, _id); }
            SetWalls(worlds.CurrentBounds, true);
            worlds.CancelBeside();
            _probe?.Dispose();
            _probe = null;
            if (_bounds != null) Destroy(_bounds);
            _bounds = null;
            if (_pivot != null) Destroy(_pivot.gameObject);
            _pivot = null; _frame = null; _world = null; _gallery = null;
            if (_back != null) Destroy(_back.gameObject);
            _back = null;
            if (_ring != null) Destroy(_ring);
            _ring = null;
            if (_ground != null) Destroy(_ground.gameObject);
            _ground = null;
            if (_painting != null)
                foreach (var name in FrameBoards) { var b = _painting.transform.Find(name); if (b != null) b.gameObject.SetActive(true); }
            if (_front != null) _front.enabled = true;
            _front = null;
            if (_mask != null) Destroy(_mask);
            _mask = null;
            if (doors != null) doors.Hold = false;
            _state = State.Idle;
            _painting = null;
            _held = 0f;
            ShowHold(null, 0f);
        }

        static void SetWalls(GameObject bounds, bool on)
        {
            if (bounds == null) return;
            foreach (Transform c in bounds.transform)
                if (c.name.StartsWith("Wall")) c.gameObject.SetActive(on);
        }

        void OnDisable() { if (_state != State.Idle) Abandon(); }
    }
}
