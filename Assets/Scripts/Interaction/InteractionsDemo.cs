using MuseXR.Slots;
using MuseXR.Worlds;
using UnityEngine;

namespace MuseXR.Interaction
{
    /// <summary>
    /// Builds the interactions test scene (Assets/Scenes/Tests/Interactions.unity) around the spawn,
    /// laid out from her storyboards, every station within ±60° of the spawn's forward:
    ///
    ///   left   Palace: crane and turtle on stone plinths either side of a low court; pick one up,
    ///          turn it with the stick, place it in the court (bronze bell).
    ///   ahead  Grotto: a lamp on a brass stand front-left, a relief panel, a "detail" socket before
    ///          the relief and a "whole" socket on a rail post (stone chime). The lamp lights only
    ///          the relief: the grey block beside it is the control and must never brighten.
    ///   right  Monet: the time ring on a pedestal, gripped and turned through three detents.
    ///
    /// The crane, turtle and lamp are primitive stand-ins until their models exist. Slot rings, cards
    /// and the confirm strip belong to the UI layer; the thin circles here only show the 12 cm snap
    /// radius so a capture can be read.
    /// </summary>
    public sealed class InteractionsDemo : MonoBehaviour
    {
        public SlotStation Palace { get; private set; }
        public SlotStation Grotto { get; private set; }
        public TimeRingDial Dial { get; private set; }
        public TimeRingDriver Driver { get; private set; }
        public Renderer Relief { get; private set; }
        public Renderer Control { get; private set; }
        public Holdable Lamp { get; private set; }
        public CompanyStage Company { get; private set; }
        public ArtworkWatcher Artwork { get; private set; }
        public CardChoiceStation Cards { get; private set; }
        public ApproachChime Plinth { get; private set; }
        public HeightCalibrator Calibrator { get; private set; }

        [Tooltip("Portraits for the six standees, in her row order: Monet, Van Gogh, Socrates, Frida, Hilma, Morisot.")]
        public Texture2D[] portraits = new Texture2D[0];
        [Tooltip("The work hung in the gallery zone (Water Lilies).")]
        public Texture2D artwork;

        void Start() => Build();

        Material _stone, _bronze, _jade, _brass, _wood, _glass;

        void Build()
        {
            _stone = Lit(new Color(0.78f, 0.75f, 0.7f));
            _bronze = Lit(new Color(0.55f, 0.38f, 0.2f), 0.6f, 0.55f);
            _jade = Lit(new Color(0.42f, 0.6f, 0.5f), 0f, 0.7f);
            _brass = Lit(new Color(0.78f, 0.6f, 0.3f), 0.8f, 0.6f);
            _wood = Lit(new Color(0.42f, 0.3f, 0.2f));
            _glass = Unlit(new Color(1f, 0.85f, 0.55f));

            var o = transform.position;
            var f = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            Vector3 At(float bearing, float distance) => o + Quaternion.Euler(0f, bearing, 0f) * (f * Vector3.forward) * distance;
            Quaternion Facing(Vector3 p) { var d = p - o; d.y = 0f; return Quaternion.LookRotation(d.normalized, Vector3.up); }

            BuildPalace(At(-38f, 1.45f), Facing(At(-38f, 1.45f)));
            BuildGrotto(At(2f, 2.1f), Facing(At(2f, 2.1f)));
            BuildMonet(At(42f, 1.35f), Facing(At(42f, 1.35f)));

            // Turn round: the Company row stands behind the spawn.
            BuildCompany(o + (f * Vector3.back) * 3.2f, Facing(o + (f * Vector3.back) * 3.2f));
            // Turn left: a hung work with its viewing mark, the Palace card fallback, a Your-world plinth.
            BuildGallery(o, f * Quaternion.Euler(0f, -90f, 0f));

            Calibrator = gameObject.AddComponent<HeightCalibrator>();
        }

        // ---- Palace ---------------------------------------------------------------------

        void BuildPalace(Vector3 centre, Quaternion awayFromViewer)
        {
            var root = Group("Palace", centre, awayFromViewer);
            var right = awayFromViewer * Vector3.right;

            // The court: a low pale-stone table, its slot at the centre of the top.
            var court = Box(root, "Court", centre + Vector3.up * 0.39f, awayFromViewer, new Vector3(0.5f, 0.78f, 0.4f), _stone);
            var slot = Point(root, "Court Slot", centre + Vector3.up * 0.78f, awayFromViewer);
            SnapCircle(slot);

            var cranePlinth = centre - right * 0.62f + awayFromViewer * Vector3.forward * 0.1f;
            var turtlePlinth = centre + right * 0.62f + awayFromViewer * Vector3.forward * 0.1f;
            Box(root, "Crane Plinth", cranePlinth + Vector3.up * 0.42f, awayFromViewer, new Vector3(0.28f, 0.84f, 0.28f), _stone);
            Box(root, "Turtle Plinth", turtlePlinth + Vector3.up * 0.42f, awayFromViewer, new Vector3(0.28f, 0.84f, 0.28f), _stone);

            var crane = Crane(root, cranePlinth + Vector3.up * 0.84f, awayFromViewer);
            var turtle = Turtle(root, turtlePlinth + Vector3.up * 0.84f, awayFromViewer);
            var pieces = new[] { Holdable.Make(crane, "Crane"), Holdable.Make(turtle, "Turtle") };

            Palace = SlotStation.Make(root.gameObject, Chapter.Palace, new[] { slot }, pieces);
            Palace.Cue += (s, e) => Debug.Log("[Palace] " + e + "  strip: " + s.Board.Choice.StripLine);
            Palace.Confirmed += (s, piece, at, yaw) => Debug.Log("[Palace] kept " + s.Pieces[piece].Id + " at " + yaw + "°");
        }

        GameObject Crane(Transform parent, Vector3 baseAt, Quaternion rot)
        {
            var c = new GameObject("Crane");
            c.transform.SetParent(parent, false);
            c.transform.SetPositionAndRotation(baseAt, rot);
            Part(c, PrimitiveType.Cylinder, "Base", new Vector3(0f, 0.01f, 0f), Vector3.zero, new Vector3(0.12f, 0.01f, 0.12f), _bronze);
            Part(c, PrimitiveType.Capsule, "Leg", new Vector3(0f, 0.06f, 0f), Vector3.zero, new Vector3(0.012f, 0.05f, 0.012f), _bronze);
            Part(c, PrimitiveType.Sphere, "Body", new Vector3(0f, 0.13f, 0f), Vector3.zero, new Vector3(0.07f, 0.06f, 0.12f), _bronze);
            Part(c, PrimitiveType.Capsule, "Neck", new Vector3(0f, 0.2f, 0.05f), new Vector3(25f, 0f, 0f), new Vector3(0.015f, 0.06f, 0.015f), _bronze);
            Part(c, PrimitiveType.Sphere, "Head", new Vector3(0f, 0.26f, 0.08f), Vector3.zero, new Vector3(0.025f, 0.025f, 0.03f), _bronze);
            Part(c, PrimitiveType.Capsule, "Beak", new Vector3(0f, 0.255f, 0.115f), new Vector3(90f, 0f, 0f), new Vector3(0.008f, 0.025f, 0.008f), _bronze);
            return c;
        }

        GameObject Turtle(Transform parent, Vector3 baseAt, Quaternion rot)
        {
            var t = new GameObject("Turtle");
            t.transform.SetParent(parent, false);
            t.transform.SetPositionAndRotation(baseAt, rot);
            Part(t, PrimitiveType.Sphere, "Shell", new Vector3(0f, 0.04f, 0f), Vector3.zero, new Vector3(0.15f, 0.08f, 0.18f), _jade);
            Part(t, PrimitiveType.Sphere, "Head", new Vector3(0f, 0.03f, 0.11f), Vector3.zero, new Vector3(0.04f, 0.035f, 0.05f), _jade);
            foreach (var (x, z) in new[] { (-0.06f, 0.06f), (0.06f, 0.06f), (-0.06f, -0.06f), (0.06f, -0.06f) })
                Part(t, PrimitiveType.Sphere, "Foot", new Vector3(x, 0.012f, z), Vector3.zero, new Vector3(0.035f, 0.024f, 0.035f), _jade);
            return t;
        }

        // ---- Grotto ---------------------------------------------------------------------

        void BuildGrotto(Vector3 centre, Quaternion awayFromViewer)
        {
            var root = Group("Grotto", centre, awayFromViewer);
            var fwd = awayFromViewer * Vector3.forward;
            var right = awayFromViewer * Vector3.right;

            // The relief: a carved panel standing at the back. Lit by the room and by the lamp.
            var relief = new GameObject("Relief");
            relief.transform.SetParent(root, false);
            relief.transform.SetPositionAndRotation(centre + fwd * 0.35f + Vector3.up * 1.3f, awayFromViewer);
            relief.AddComponent<MeshFilter>().sharedMesh = ReliefMesh(1.1f, 0.8f, 72, 52);
            Relief = relief.AddComponent<MeshRenderer>();
            Relief.sharedMaterial = Lit(new Color(0.55f, 0.5f, 0.43f), 0f, 0.25f);   // grotto stone; paler clips under daylight
            LampLight.MarkRelief(Relief);

            // The control: same stone, no relief layer. The lamp must never reach it.
            Control = Box(root, "Control Block", centre + fwd * 0.35f + right * 0.85f + Vector3.up * 1.1f, awayFromViewer,
                          new Vector3(0.35f, 0.35f, 0.1f), Lit(new Color(0.55f, 0.5f, 0.43f), 0f, 0.25f)).GetComponent<Renderer>();

            // "detail" before the wall relief; "whole" on the central rail post.
            var detailPost = centre + fwd * 0.05f - right * 0.2f;
            Box(root, "Detail Post", detailPost + Vector3.up * 0.5f, awayFromViewer, new Vector3(0.12f, 1f, 0.12f), _stone);
            var detail = Point(root, "Socket Detail", detailPost + Vector3.up * 1.0f, awayFromViewer);
            SnapCircle(detail);

            var railPost = centre - fwd * 0.2f + right * 0.45f;
            Box(root, "Rail Post", railPost + Vector3.up * 0.525f, awayFromViewer, new Vector3(0.12f, 1.05f, 0.12f), _stone);
            Box(root, "Rail", railPost + Vector3.up * 0.98f + right * 0.4f, awayFromViewer, new Vector3(0.8f, 0.05f, 0.05f), _stone);
            var whole = Point(root, "Socket Whole", railPost + Vector3.up * 1.05f, awayFromViewer);
            SnapCircle(whole);

            // The brass stand, front-left, the lamp on it at reachable height (seated too).
            var stand = centre - fwd * 0.45f - right * 0.6f;
            Box(root, "Brass Stand", stand + Vector3.up * 0.45f, awayFromViewer, new Vector3(0.05f, 0.9f, 0.05f), _brass);
            Box(root, "Brass Tray", stand + Vector3.up * 0.905f, awayFromViewer, new Vector3(0.18f, 0.01f, 0.18f), _brass);
            var lamp = LampModel(root, stand + Vector3.up * 0.91f, awayFromViewer);
            LampLight.Make(lamp, new Vector3(0f, 0.11f, 0f));
            Lamp = Holdable.Make(lamp, "Lamp", idleSpin: false);

            Grotto = SlotStation.Make(root.gameObject, Chapter.Grotto, new[] { detail, whole }, new[] { Lamp },
                                      new[] { "Detail", "Whole" });
            Grotto.Cue += (s, e) => Debug.Log("[Grotto] " + e + "  strip: " + s.Board.Choice.StripLine);
        }

        GameObject LampModel(Transform parent, Vector3 baseAt, Quaternion rot)
        {
            var l = new GameObject("Lamp");
            l.transform.SetParent(parent, false);
            l.transform.SetPositionAndRotation(baseAt, rot);
            Part(l, PrimitiveType.Cylinder, "Foot", new Vector3(0f, 0.01f, 0f), Vector3.zero, new Vector3(0.09f, 0.01f, 0.09f), _brass);
            Part(l, PrimitiveType.Cylinder, "Stem", new Vector3(0f, 0.05f, 0f), Vector3.zero, new Vector3(0.025f, 0.035f, 0.025f), _brass);
            Part(l, PrimitiveType.Sphere, "Glass", new Vector3(0f, 0.11f, 0f), Vector3.zero, new Vector3(0.07f, 0.08f, 0.07f), _glass);
            Part(l, PrimitiveType.Cylinder, "Cap", new Vector3(0f, 0.16f, 0f), Vector3.zero, new Vector3(0.05f, 0.008f, 0.05f), _brass);
            return l;
        }

        /// <summary>A carved panel: lotus roundels and a border in shallow relief, so a moving lamp shows depth.</summary>
        static Mesh ReliefMesh(float width, float height, int nx, int ny)
        {
            float H(float u, float v)
            {
                float h = 0f;
                // three roundels across, petals round each
                for (var k = 0; k < 3; k++)
                {
                    float cx = (k + 0.5f) / 3f, cy = 0.5f;
                    float dx = (u - cx) * width, dy = (v - cy) * height;
                    float r = Mathf.Sqrt(dx * dx + dy * dy), a = Mathf.Atan2(dy, dx);
                    float petal = 0.13f + 0.03f * Mathf.Cos(8f * a);
                    h += 0.03f * Mathf.Clamp01(1f - Mathf.Abs(r - petal) / 0.025f);
                    h += 0.035f * Mathf.Clamp01(1f - r / 0.06f);
                }
                float edge = Mathf.Min(Mathf.Min(u, 1f - u) * width, Mathf.Min(v, 1f - v) * height);
                h += 0.025f * Mathf.Clamp01(1f - Mathf.Abs(edge - 0.05f) / 0.02f);
                return h;
            }
            var verts = new Vector3[(nx + 1) * (ny + 1)];
            var uvs = new Vector2[verts.Length];
            for (var y = 0; y <= ny; y++)
            for (var x = 0; x <= nx; x++)
            {
                float u = x / (float)nx, v = y / (float)ny;
                // relief stands out toward the viewer, which is -Z here (+Z points away)
                verts[y * (nx + 1) + x] = new Vector3((u - 0.5f) * width, (v - 0.5f) * height, -H(u, v));
                uvs[y * (nx + 1) + x] = new Vector2(u, v);
            }
            var tris = new int[nx * ny * 6];
            var t = 0;
            for (var y = 0; y < ny; y++)
            for (var x = 0; x < nx; x++)
            {
                int i = y * (nx + 1) + x;
                tris[t++] = i; tris[t++] = i + nx + 1; tris[t++] = i + 1;
                tris[t++] = i + 1; tris[t++] = i + nx + 1; tris[t++] = i + nx + 2;
            }
            var m = new Mesh { name = "relief", vertices = verts, uv = uvs, triangles = tris };
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        // ---- Monet ----------------------------------------------------------------------

        void BuildMonet(Vector3 centre, Quaternion awayFromViewer)
        {
            var root = Group("Monet", centre, awayFromViewer);
            Box(root, "Pedestal", centre + Vector3.up * 0.45f, awayFromViewer, new Vector3(0.3f, 0.9f, 0.3f), _stone);

            // The dial stands on the pedestal, its face to the visitor, tilted back so it reads seated too.
            var dial = new GameObject("Time Ring Dial");
            dial.transform.SetParent(root, false);
            dial.transform.SetPositionAndRotation(centre + Vector3.up * 1.1f, awayFromViewer * Quaternion.Euler(-20f, 0f, 0f));
            var ring = new GameObject("Ring");
            ring.transform.SetParent(dial.transform, false);
            ring.AddComponent<MeshFilter>().sharedMesh = Torus(0.15f, 0.018f, 48, 12);
            ring.AddComponent<MeshRenderer>().sharedMaterial = _brass;
            // A knob at the top of the ring, so its turn can be seen from any distance.
            Part(ring, PrimitiveType.Sphere, "Knob", new Vector3(0f, 0.15f, -0.01f), Vector3.zero, Vector3.one * 0.045f, _bronze);
            // Three detent notches on the fixed backing: left, top, right.
            for (var k = 0; k < DialDetents.Count; k++)
            {
                var a = DialDetents.AngleOf(k) * Mathf.Deg2Rad;
                Part(dial, PrimitiveType.Cube, "Detent " + k, new Vector3(Mathf.Sin(a) * 0.2f, Mathf.Cos(a) * 0.2f, 0f),
                     new Vector3(0f, 0f, -DialDetents.AngleOf(k)), new Vector3(0.012f, 0.03f, 0.012f), _wood);
            }
            var box = dial.AddComponent<BoxCollider>();
            box.size = new Vector3(0.42f, 0.42f, 0.08f);
            box.isTrigger = true;

            Driver = new GameObject("Time Ring Driver").AddComponent<TimeRingDriver>();
            Driver.transform.SetParent(root, false);
            Dial = TimeRingDial.Make(dial, ring.transform, Driver);
            Dial.Clicked += t => Debug.Log("[Monet] ring -> " + t);
        }

        static Mesh Torus(float radius, float tube, int seg, int sides)
        {
            var verts = new Vector3[(seg + 1) * (sides + 1)];
            var tris = new int[seg * sides * 6];
            for (var i = 0; i <= seg; i++)
            for (var j = 0; j <= sides; j++)
            {
                float a = i / (float)seg * Mathf.PI * 2f, b = j / (float)sides * Mathf.PI * 2f;
                var c = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                verts[i * (sides + 1) + j] = c * (radius + tube * Mathf.Cos(b)) + Vector3.forward * tube * Mathf.Sin(b);
            }
            var t = 0;
            for (var i = 0; i < seg; i++)
            for (var j = 0; j < sides; j++)
            {
                int p = i * (sides + 1) + j, q = (i + 1) * (sides + 1) + j;
                tris[t++] = p; tris[t++] = q; tris[t++] = p + 1;
                tris[t++] = p + 1; tris[t++] = q; tris[t++] = q + 1;
            }
            var m = new Mesh { name = "ring", vertices = verts, triangles = tris };
            m.RecalculateNormals();
            return m;
        }

        // ---- Company: six standees in a row ----------------------------------------------

        void BuildCompany(Vector3 centre, Quaternion awayFromViewer)
        {
            var root = Group("Company", centre, awayFromViewer);
            var right = awayFromViewer * Vector3.right;
            var standees = new System.Collections.Generic.Dictionary<string, Transform>();
            for (var i = 0; i < Masters.Row.Count; i++)
            {
                var id = Masters.Row[i];
                var at = centre + right * ((i - 2.5f) * 0.85f);
                var s = new GameObject("Standee " + id).transform;
                s.SetParent(root, true);
                s.SetPositionAndRotation(at, awayFromViewer);
                // A board on a foot: her standee fallback, so a master never vanishes.
                var board = GameObject.CreatePrimitive(PrimitiveType.Cube);
                board.name = "Board";
                Object.DestroyImmediate(board.GetComponent<Collider>());
                board.transform.SetParent(s, false);
                board.transform.localPosition = new Vector3(0f, 0.95f, 0.02f);
                board.transform.localScale = new Vector3(0.6f, 1.7f, 0.03f);
                board.GetComponent<Renderer>().sharedMaterial = _stone;
                Part(s.gameObject, PrimitiveType.Cylinder, "Foot", new Vector3(0f, 0.02f, 0f), Vector3.zero, new Vector3(0.4f, 0.02f, 0.4f), _wood);
                var face = GameObject.CreatePrimitive(PrimitiveType.Quad);
                face.name = "Portrait";
                Object.DestroyImmediate(face.GetComponent<Collider>());
                face.transform.SetParent(s, false);
                face.transform.localPosition = new Vector3(0f, 1.42f, 0f);   // in front of the board (+Z is away)
                face.transform.localScale = new Vector3(0.5f, 0.62f, 1f);
                var m = Unlit(Color.white);
                if (i < portraits.Length && portraits[i] != null) m.SetTexture("_BaseMap", portraits[i]);
                face.GetComponent<Renderer>().sharedMaterial = m;
                standees[id] = s;
            }
            Company = CompanyStage.Make(root.gameObject, standees);
            Company.Question = "What is worth keeping?";
            Company.Toggled += (id, r) => Debug.Log("[Company] " + id + " -> " + r + "  chosen: " + string.Join(", ", Company.Invitation.Chosen));
            Company.Group.LineStarted += (id, line) => Debug.Log("[Company] " + id + " says: " + line);
            Company.Completed += ids => Debug.Log("[Company] companions[] = " + string.Join(", ", ids));
        }

        // ---- Gallery: an artwork, the card fallback, a Your-world plinth ------------------

        void BuildGallery(Vector3 origin, Quaternion facing)
        {
            var fwd = facing * Vector3.forward;
            var right = facing * Vector3.right;
            var root = Group("Gallery", origin + fwd * 2f, facing);

            // The work, frame centre at 1.5 m (her rule), and its viewing mark 2.0 m in front.
            var wallAt = origin + fwd * 4.2f;
            var frame = GameObject.CreatePrimitive(PrimitiveType.Quad);
            frame.name = "Artwork Water Lilies";
            Object.DestroyImmediate(frame.GetComponent<Collider>());
            frame.transform.SetParent(root, true);
            frame.transform.SetPositionAndRotation(wallAt + Vector3.up * 1.5f, facing);
            frame.transform.localScale = new Vector3(1.0f, 0.95f, 1f);
            var am = Unlit(Color.white);
            if (artwork != null) am.SetTexture("_BaseMap", artwork);
            frame.GetComponent<Renderer>().sharedMaterial = am;
            Box(root, "Wall", wallAt + fwd * 0.05f + Vector3.up * 1.4f, facing, new Vector3(3f, 2.8f, 0.08f), _stone);
            var mark = Point(root, "Viewing Mark", wallAt - fwd * 2.0f, facing);
            SnapCircle(mark);
            Artwork = ArtworkWatcher.Make(frame, "aic-16568", mark);
            Artwork.CardWanted += w => Debug.Log("[Gallery] card wanted for " + w.ArtworkId);
            Artwork.Seen += (w, sec) => Debug.Log("[Gallery] seen " + w.ArtworkId + " after " + sec.ToString("F1") + " s");

            // The card fallback: two exhibit cards on a lectern, right of the path.
            var lectern = origin + fwd * 1.6f + right * 1.1f;
            Box(root, "Lectern", lectern + Vector3.up * 0.5f, facing, new Vector3(0.7f, 1.0f, 0.3f), _stone);
            var cards = new Transform[2];
            var names = new[] { "Crane", "Turtle" };
            for (var i = 0; i < 2; i++)
            {
                var c = new GameObject("Card " + names[i]).transform;
                c.SetParent(root, true);
                c.SetPositionAndRotation(lectern + Vector3.up * 1.25f + right * ((i - 0.5f) * 0.36f), facing);
                var back = GameObject.CreatePrimitive(PrimitiveType.Quad);
                back.name = "Back";
                Object.DestroyImmediate(back.GetComponent<Collider>());
                back.transform.SetParent(c, false);
                back.transform.localScale = new Vector3(0.3f, 0.42f, 1f);
                back.GetComponent<Renderer>().sharedMaterial = Unlit(new Color(0.55f, 0.18f, 0.16f));   // red lacquer
                var front = GameObject.CreatePrimitive(PrimitiveType.Quad);
                front.name = "Front";
                Object.DestroyImmediate(front.GetComponent<Collider>());
                front.transform.SetParent(c, false);
                front.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                front.transform.localPosition = new Vector3(0f, 0f, 0.002f);
                front.transform.localScale = new Vector3(0.3f, 0.42f, 1f);
                front.GetComponent<Renderer>().sharedMaterial = Unlit(new Color(0.95f, 0.92f, 0.85f));
                var label = new GameObject("Name").AddComponent<TMPro.TextMeshPro>();
                label.transform.SetParent(c, false);
                label.transform.localPosition = new Vector3(0f, 0f, 0.004f);
                label.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                label.rectTransform.sizeDelta = new Vector2(0.28f, 0.1f);
                label.fontSize = 0.6f;
                label.alignment = TMPro.TextAlignmentOptions.Center;
                label.color = new Color(0.2f, 0.16f, 0.12f);
                label.text = names[i];
                cards[i] = c;
            }
            Cards = CardChoiceStation.Make(root.gameObject, cards, names);
            Cards.Kept += (st, id) => Debug.Log("[Gallery] card kept: " + id);

            // A Your-world plinth that rings the bronze bell as you approach.
            var plinth = Box(root, "Your-world Plinth", origin + fwd * 1.4f - right * 1.4f + Vector3.up * 0.45f, facing,
                             new Vector3(0.4f, 0.9f, 0.4f), _stone);
            Plinth = ApproachChime.Make(plinth, ChapterSound.BronzeBell);
            Plinth.Rang += c => Debug.Log("[Gallery] plinth chimed " + c.Sound);
        }

        void Update()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.hKey.wasPressedThisFrame && Calibrator != null)
                Debug.Log("[Calibration] offset " + Calibrator.Calibrate().ToString("F2") + " m");
        }

        // ---- helpers --------------------------------------------------------------------

        Transform Group(string name, Vector3 at, Quaternion rot)
        {
            var g = new GameObject(name).transform;
            g.SetParent(transform, true);
            g.SetPositionAndRotation(at, rot);
            return g;
        }

        static Transform Point(Transform parent, string name, Vector3 at, Quaternion rot)
        {
            var p = new GameObject(name).transform;
            p.SetParent(parent, true);
            p.SetPositionAndRotation(at, rot);
            return p;
        }

        static GameObject Box(Transform parent, string name, Vector3 at, Quaternion rot, Vector3 size, Material m)
        {
            var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
            b.name = name;
            b.transform.SetParent(parent, true);
            b.transform.SetPositionAndRotation(at, rot);
            b.transform.localScale = size;
            b.GetComponent<Renderer>().sharedMaterial = m;
            return b;
        }

        static void Part(GameObject parent, PrimitiveType type, string name, Vector3 local, Vector3 euler, Vector3 scale, Material m)
        {
            var p = GameObject.CreatePrimitive(type);
            p.name = name;
            // Immediate: Holdable.Make runs this frame and must not see a collider that is about to vanish.
            Object.DestroyImmediate(p.GetComponent<Collider>());
            p.transform.SetParent(parent.transform, false);
            p.transform.localPosition = local;
            p.transform.localRotation = Quaternion.Euler(euler);
            p.transform.localScale = scale;
            p.GetComponent<Renderer>().sharedMaterial = m;
        }

        /// <summary>The snap radius drawn flat around a slot: debug only, never her slot ring.</summary>
        static void SnapCircle(Transform slot)
        {
            var lr = slot.gameObject.AddComponent<LineRenderer>();
            const int n = 48;
            lr.positionCount = n; lr.loop = true; lr.useWorldSpace = false;
            lr.widthMultiplier = 0.003f;
            lr.sharedMaterial = Unlit(new Color(0.35f, 0.33f, 0.3f));
            for (var i = 0; i < n; i++)
            {
                var a = i / (float)n * Mathf.PI * 2f;
                lr.SetPosition(i, new Vector3(Mathf.Cos(a), 0.002f, Mathf.Sin(a)) * SlotRules.SnapRadius + Vector3.up * 0.002f);
            }
        }

        static Material Lit(Color c, float metallic = 0f, float smoothness = 0.35f)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smoothness);
            return m;
        }

        static Material Unlit(Color c)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            m.SetColor("_BaseColor", c);
            return m;
        }
    }
}
