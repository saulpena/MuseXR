using System.Collections;
using System.Collections.Generic;
using MuseXR.Slots;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

namespace MuseXR.Interaction
{
    /// <summary>
    /// Brings her chapter A to life in the laid-out Palace (Tests/PalaceChapter.unity, built by
    /// ChapterLayout from her diagram A). It finds what the layout placed - "Prop crane", "Prop
    /// turtle", "Interaction Miniature court", "Mark monet / van_gogh / socrates" - and adds:
    ///
    ///   a teleport floor over the whole court (invisible), so the visitor can walk up to everything;
    ///   grip-grabbable miniatures that idle-turn, follow the hand and turn only with the stick;
    ///   the court as her 12 cm slot, with snap, bronze bell, haptics, float-home and the 3 s undo;
    ///   the companions answering in turn from where her diagram stands them, with subtitles;
    ///   once they have spoken, three reason chips over the court; A saves palace{object, yaw, reason, mode};
    ///   then her transition: the moon gate opens with the grotto beyond, and walking through it goes there.
    /// </summary>
    public sealed class PalaceChapterInteractions : MonoBehaviour
    {
        public PalaceChapter Chapter { get; private set; }
        public SlotStation Court { get; private set; }
        public CompanionGroup Companions { get; private set; }
        public MusePico.Dialogue.JourneyRecord Record { get; } = new MusePico.Dialogue.JourneyRecord();

        IEnumerator Start()
        {
            yield return null;   // after the rig and the layout have woken
            var head = Camera.main != null ? Camera.main.transform : null;
            var spawn = head != null ? head.position : Vector3.zero;

            TeleportFloor();

            var crane = Find("Prop crane");
            var turtle = Find("Prop turtle");
            var courtT = Find("Interaction Miniature court");
            if (crane == null || turtle == null || courtT == null)
            {
                Debug.LogError("[Palace] layout objects missing: crane " + (crane != null) + ", turtle " + (turtle != null) + ", court " + (courtT != null));
                yield break;
            }

            RaiseCourt(courtT);

            // The court's slot sits on its surface; the station hangs off the court object.
            var slot = new GameObject("Court Slot").transform;
            slot.SetParent(courtT, false);
            var toViewer = spawn - courtT.position; toViewer.y = 0f; toViewer.Normalize();
            // The slot faces the visitor, so a piece set down facing them reads 0 degrees (facing away it
            // read "Crane · 178°" for a crane nobody had turned).
            slot.SetPositionAndRotation(courtT.position, Quaternion.LookRotation(toViewer, Vector3.up));

            var pieces = new[] { Holdable.Make(crane.gameObject, "Crane"), Holdable.Make(turtle.gameObject, "Turtle") };
            Court = SlotStation.Make(courtT.gameObject, global::MuseXR.Slots.Chapter.Palace, new[] { slot }, pieces);

            // The companions where her diagram stands them; they answer in turn, they do not walk.
            var figures = new Dictionary<string, Transform>();
            foreach (var id in Masters.DefaultTrio)
            {
                var mark = Find("Mark " + id);
                if (mark != null) figures[id] = mark;
            }
            var groupGo = new GameObject("Companions");
            groupGo.transform.SetParent(transform, false);
            Companions = groupGo.AddComponent<CompanionGroup>();
            Companions.FollowVisitor = false;
            Companions.Head = head;
            var order = new List<string>(); foreach (var id in Masters.DefaultTrio) if (figures.ContainsKey(id)) order.Add(id);
            Companions.Set(order, figures);
            var subtitles = System.Type.GetType("MuseXR.UI.SubtitleRig, MuseXR.UI.Interaction");
            if (subtitles != null) groupGo.AddComponent(subtitles);

            // Reason chips centred on the court: a row standing just behind the placed piece, so the
            // choice sits on what was chosen (Saul, headset test: "not centred where I'm placing it").
            // PalaceChapter turns them to face wherever the visitor stands when they appear.
            var chipsAt = courtT.position - toViewer * ChipsBehind + Vector3.up * ChipsUp;
            Chapter = PalaceChapter.Make(courtT.gameObject, Court, null, null, Record, chipsAt,
                                         Quaternion.LookRotation(-toViewer, Vector3.up));
            Chapter.Group = Companions;
            Chapter.Saved += _ => { Debug.Log("[Record] " + Record.SummaryJson()); OpenMoonGate(); };
            ConfirmInput.Pressed += OnPressed;
            _courtTop = courtT.position;
        }

        void OnPressed(string button, string target, bool ok) =>
            Debug.Log("[Palace] " + button + " -> " + target + (ok ? "" : " (nothing to do)"));

        void OnDestroy() => ConfirmInput.Pressed -= OnPressed;

        // ---- the moon gate: her transition to chapter B ----------------------------------------

        [Tooltip("Assets/Prefabs/Portal Door.prefab: the door that opens in the moon gate.")]
        public GameObject doorPrefab;

        [Tooltip("The next chapter's world, seen and walked into through the moon gate (grotto-hall-of-time-500k).")]
        public GaussianSplatting.Runtime.GaussianSplatAsset nextWorldAsset;

        /// <summary>Her transition: "through the moon gate the courtyard vista becomes grotto cliffs".</summary>
        public const string NextWorldKey = "grotto-hall-of-time" + MuseXR.Worlds.WorldCatalog.SmallSuffix;

        /// <summary>How far past the doorway the grotto's spawn lies (JourneyDoors' value).</summary>
        public const float ArrivalPastDoor = 1.2f;

        /// <summary>
        /// The capture's own moon gate, on the floor at the threshold, in the palace's frame (the
        /// world stands at the origin). Measured 3 Oct 2026 by triangulating two Editor captures of
        /// the right-hand wall (eye 1.6 m, from (-2, 0.6) and (-2, 3.0), both facing -X): both put
        /// the threshold 5.5 m deep at (-7.5, 0.0). The layout's "Exit Moon gate" marker, placed from
        /// the schematic, stands 3.5 m away in front of a column - a door there opened in the wrong
        /// place. The collider cannot locate it: above 1.4 m it reports a sloping surface across the
        /// opening.
        /// </summary>
        public static readonly Vector3 MoonGateFloor = new Vector3(-7.5f, 0f, 0f);

        /// <summary>The gate opens out of the hall along -X (the right-hand wall from the entry).</summary>
        public static readonly Vector3 MoonGateOutward = Vector3.left;

        /// <summary>
        /// Its keyhole, metres, from the same captures: a round opening ~3.4 m across centred
        /// ~2.3 m up, over a passage ~2.2 m wide down to the floor. Set a little inside the gold ring
        /// so the capture's own frame surrounds what shows through.
        /// </summary>
        public const float GateRadius = 1.6f, GateCentreHeight = 2.3f, GatePassageWidth = 2.1f;

        /// <summary>The door at the moon gate once the choice is kept, else null.</summary>
        public MuseXR.Worlds.SplatPortalDoor Door { get; private set; }

        Vector3 _courtTop;
        TMPro.TextMeshPro _afterKeep;
        bool _arrived;

        /// <summary>
        /// After A keeps the choice: the portal door stands in her moon-gate exit (the right-hand
        /// doorway in diagram A) with the grotto behind it, placed so walking or teleporting through
        /// lands the visitor at the grotto's spawn. The door rises when the visitor looks toward it
        /// and opens with the grotto showing through (SplatPortalDoor, the journey's door unchanged).
        /// Crossing it destroys the palace; the companions come along.
        /// </summary>
        void OpenMoonGate()
        {
            Transform exit = null;   // the layout's marker: only its parent (the layout root) is used
            foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None)) if (t.name.StartsWith("Exit Moon gate")) exit = t;
            MuseXR.Worlds.WorldDefinition next = null;
            foreach (var w in MuseXR.Worlds.WorldCatalog.Small) if (w.key == NextWorldKey) next = w;
            var here = FindAnyObjectByType<GaussianSplatting.Runtime.GaussianSplatRenderer>();
            if (exit == null || next == null || doorPrefab == null || nextWorldAsset == null || here == null)
            {
                Debug.LogError("[Palace] cannot open the moon gate: exit " + (exit != null) + ", world " + (next != null) +
                               ", door prefab " + (doorPrefab != null) + ", grotto asset " + (nextWorldAsset != null) + ", palace " + (here != null));
                return;
            }

            // In the capture's own moon gate; the door's +Z points out through it, into the grotto.
            var pose = new MuseXR.Worlds.DoorPose { position = MoonGateFloor,
                                                    rotation = Quaternion.LookRotation(MoonGateOutward, Vector3.up) };
            var spawn = next.ScaledSpawn; spawn.y = 0f;
            MuseXR.Worlds.WorldDoorLayout.NextWorldFrame(pose, spawn, next.SpawnRotation, ArrivalPastDoor, 0f, out var framePos, out var frameRot);
            var pivot = new GameObject("Next World (behind the moon gate): " + next.key).transform;
            pivot.SetPositionAndRotation(framePos, frameRot);

            var world = new GameObject("World_" + next.key);
            world.transform.SetParent(pivot, false);
            world.transform.localScale = next.SplatScale;
            var r = world.AddComponent<GaussianSplatting.Runtime.GaussianSplatRenderer>();
            r.m_Asset = nextWorldAsset;
            r.m_ShaderSplats = here.m_ShaderSplats; r.m_ShaderComposite = here.m_ShaderComposite;
            r.m_ShaderDebugPoints = here.m_ShaderDebugPoints; r.m_ShaderDebugBoxes = here.m_ShaderDebugBoxes;
            r.m_CSSplatUtilities = here.m_CSSplatUtilities;
            MuseXR.Worlds.SplatRenderTuning.Apply(r);
            r.enabled = false; r.enabled = true;   // resources are built in OnEnable

            // The companions leave the palace's layout before it is destroyed with the old world.
            foreach (var f in Companions.Figures.Values) f.SetParent(Companions.transform, true);
            var props = new List<GameObject>();
            if (exit.parent != null) props.Add(exit.parent.gameObject);   // court, plinths, pieces, the chips on the court
            foreach (var n in new[] { "Court Table", "Interaction Visuals" }) { var g = GameObject.Find(n); if (g != null) props.Add(g); }

            var go = Instantiate(doorPrefab);
            go.name = "Moon Gate to " + next.displayName;
            go.transform.SetPositionAndRotation(pose.position, pose.rotation);
            Door = go.GetComponent<MuseXR.Worlds.SplatPortalDoor>();
            Door.currentWorld = here;
            Door.nextWorld = r;
            Door.currentWorldProps = props.ToArray();
            Door.triggerDistance = 10f;   // from the court the gate is ~7.5 m away: it opens as the visitor turns to it
            // Her moon gate is round (Q2: "Curate/Palace = moon gate (round mask mesh)"), and the
            // capture already has one: no door leaves, the capture's stone ring is the frame, and the
            // grotto shows only through its keyhole.
            float top = GateCentreHeight + GateRadius;
            Door.apertureSize = new Vector2(2f * GateRadius, top);
            if (Door.aperture != null) Door.aperture.localPosition = new Vector3(0f, top * 0.5f, 0f);
            Door.maskMesh = KeyholeMesh(GateRadius, GateCentreHeight, GatePassageWidth);
            if (Door.doorVisual != null) Door.doorVisual.gameObject.SetActive(false);
            Door.doorVisual = null; Door.leftLeaf = null; Door.rightLeaf = null;
            if (Camera.main != null)
            {
                Door.head = Camera.main.transform;
                Camera.main.farClipPlane = Mathf.Max(Camera.main.farClipPlane, next.cameraFar);
            }
            Door.enabled = true;

            // The chapter is over: the companions walk with the visitor again (§3.4), re-marked off the
            // path at the next teleport or turn. Left on her diagram's V mark, Van Gogh stood in the
            // capture's real gate, between the court and the door (Editor capture, 3 Oct 2026).
            Companions.StopTurns();
            Companions.FollowVisitor = true;

            AfterKeep("Kept.  The moon gate is open, on your right.\nWalk through it.");
            Debug.Log("[Palace] moon gate at " + pose.position.ToString("F2") + " opens onto " + next.displayName);
        }

        /// <summary>
        /// The moon gate's keyhole as a mask mesh in the portal's unit quad (-0.5..0.5 in XY, scaled by
        /// the aperture: width 2r, height centre + r, origin at mid-height): a disc of radius
        /// <paramref name="r"/> centred <paramref name="centre"/> metres up, on a passage
        /// <paramref name="passage"/> wide down to the floor.
        /// </summary>
        public static Mesh KeyholeMesh(float r, float centre, float passage)
        {
            float w = 2f * r, h = centre + r;
            Vector3 U(float x, float y) => new Vector3(x / w, y / h - 0.5f, 0f);   // metres (x from the axis, y from the floor) -> unit quad
            var v = new List<Vector3>(); var tris = new List<int>();
            const int n = 48;
            v.Add(U(0f, centre));
            for (var i = 0; i <= n; i++) { var a = 2f * Mathf.PI * i / n; v.Add(U(Mathf.Cos(a) * r, centre + Mathf.Sin(a) * r)); }
            for (var i = 1; i <= n; i++) { tris.Add(0); tris.Add(i + 1); tris.Add(i); }
            int q = v.Count;
            float hw = Mathf.Min(passage, w) * 0.5f;
            v.Add(U(-hw, 0f)); v.Add(U(hw, 0f)); v.Add(U(hw, centre)); v.Add(U(-hw, centre));
            tris.AddRange(new[] { q, q + 2, q + 1, q, q + 3, q + 2 });
            var m = new Mesh { name = "Moon Gate Keyhole" };
            m.SetVertices(v); m.SetTriangles(tris, 0); m.RecalculateBounds();
            return m;
        }

        /// <summary>True when a point (metres: x from the gate's axis, y up from the floor) is in the keyhole.</summary>
        public static bool InKeyhole(float x, float y, float r, float centre, float passage)
        {
            var dy = y - centre;
            return x * x + dy * dy <= r * r || (Mathf.Abs(x) <= passage * 0.5f && y >= 0f && y <= centre);
        }

        /// <summary>One line over the court once the choice is kept: what to do next.</summary>
        void AfterKeep(string line)
        {
            var eye = Camera.main != null ? Camera.main.transform.position : _courtTop + Vector3.back;
            var at = _courtTop + Vector3.up * 0.75f;
            var away = at - eye; away.y = 0f;
            _afterKeep = new GameObject("After Keep").AddComponent<TMPro.TextMeshPro>();
            _afterKeep.transform.SetParent(transform, false);
            _afterKeep.transform.SetPositionAndRotation(at, Quaternion.LookRotation(away.normalized, Vector3.up));
            _afterKeep.rectTransform.sizeDelta = new Vector2(1.0f, 0.2f);
            _afterKeep.fontSize = 0.36f;   // ~10 mm cap height: read from the court, under a metre away
            _afterKeep.alignment = TMPro.TextAlignmentOptions.Center;
            _afterKeep.color = new Color(1f, 0.95f, 0.85f);
            _afterKeep.outlineWidth = 0.2f;
            _afterKeep.outlineColor = new Color32(40, 28, 16, 255);
            _afterKeep.text = line;
        }

        void Update()
        {
            if (Door == null || _arrived || !Door.HasCrossed) return;
            // Through the gate: the palace and its layout go with the door; the companions stand
            // round the visitor in the grotto.
            _arrived = true;
            if (_afterKeep != null) Destroy(_afterKeep.gameObject);
            Companions.PlaceAll();
            Debug.Log("[Palace] through the moon gate: in the grotto");
        }

        /// <summary>An invisible floor at the layout's ground, teleportable everywhere in the court.</summary>
        void TeleportFloor()
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Teleport Floor";
            floor.transform.SetParent(transform, false);
            floor.transform.position = Vector3.zero;
            floor.transform.localScale = new Vector3(4f, 1f, 4f);   // 40 x 40 m
            floor.GetComponent<Renderer>().enabled = false;          // the world is the splat; this only catches teleports
            floor.SetActive(false);                                  // XRI registers an area once, with its settings
            var area = floor.AddComponent<TeleportationArea>();
            // The rig's teleport rays select ONLY on the "Teleport" interaction layer (bit 31). Measured in
            // Saul's headset test: an area on the default layer is never a valid target, so teleport did
            // nothing at all.
            area.interactionLayers = TeleportLayer;
            area.filterSelectionByHitNormal = true;
            floor.SetActive(true);
        }

        /// <summary>
        /// The chip row's centre above the court's top: just over the confirm strip (which stands
        /// 0.5 m up, ~0.16 m tall), so it reads top to bottom as her 4.3 prompt - options, then A/B,
        /// then the piece. Measured: a row behind the court and lower was hidden behind the strip.
        /// </summary>
        public const float ChipsBehind = 0f, ChipsUp = 0.7f;

        /// <summary>The interaction layer the rig's teleport interactors select on.</summary>
        public const int TeleportLayer = 1 << 31;

        /// <summary>The court's top, at hand height: her rule puts what you handle at 0.8-1.3 m.</summary>
        public const float CourtHeight = 0.85f;

        /// <summary>
        /// Her "miniature court" is a small model of the palace court where the kept piece is set. The
        /// layout marks it on the floor; it stands on a pale-stone table at hand height instead.
        /// </summary>
        void RaiseCourt(Transform court)
        {
            var floorAt = court.position;
            var table = GameObject.CreatePrimitive(PrimitiveType.Cube);
            table.name = "Court Table";
            table.transform.SetParent(transform, false);
            table.transform.position = new Vector3(floorAt.x, CourtHeight * 0.5f, floorAt.z);
            table.transform.localScale = new Vector3(0.7f, CourtHeight, 0.5f);
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", new Color(0.86f, 0.83f, 0.77f));   // her pale-stone interaction zone
            table.GetComponent<Renderer>().sharedMaterial = m;
            court.position = new Vector3(floorAt.x, CourtHeight + 0.005f, floorAt.z);
        }

        static Transform Find(string name)
        {
            foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None)) if (t.name == name) return t;
            return null;
        }
    }
}
