using System;
using System.Collections.Generic;
using MusePico.Dialogue;
using MuseXR.UI;
using MuseXR.Worlds;
using TMPro;
using UnityEngine;

namespace MusePico.Journey
{
    /// <summary>
    /// Lays one of Skylar's chapters out in its world from her top-down diagram
    /// (<see cref="ChapterDiagrams"/>), calibrated against the world's collider as her plan asks:
    ///
    ///   - the entry is her "Entry view": a floor point and a facing (up her page);
    ///   - diagram units become metres separately across and along (her diagrams are schematic);
    ///   - works and exits are snapped to the real wall on the side she draws them, works at her
    ///     1.5 m frame-centre height, her artwork card 0.3 m right of the frame at the same depth;
    ///   - companion marks get the masters themselves, facing the entry;
    ///   - the interaction point gets her rose ring and its label.
    ///
    /// Everything it builds is a child of this object, so a chapter is one root to show or hide.
    /// </summary>
    public sealed class ChapterLayout : MonoBehaviour
    {
        [Serializable]
        public struct Calibration
        {
            public Vector3 entry;          // world floor point of her Entry view
            public float yaw;              // world yaw the entry faces (up her page)
            public float across, along;    // metres per diagram unit
            /// <summary>Mirror her diagram left-right. For her own captures, whose rooms open on the
            /// other side from where her schematic draws them: the world stays as she knows it.</summary>
            public bool flipX;
        }

        /// <summary>A placement her schematic cannot reach in this world: a point in the entry's frame
        /// (x right, z ahead, metres) and the wall to snap to from there.</summary>
        public struct Override
        {
            public float x, z; public WallSide wall;
            /// <summary>Place exactly here, facing <see cref="faceYaw"/> (degrees from the entry's facing:
            /// into the wall for a work, out of the door for an exit) - for parts of a capture its
            /// collider does not cover, placed by eye from renders.</summary>
            public bool fixedPlace; public float faceYaw;
            /// <summary>Stand the work on a wooden easel rather than a wall: her diagrams draw some
            /// works across the path, facing the visitor, where there is no wall.</summary>
            public bool stand;
            /// <summary>Frame centre above the floor; 0 means her 1.5 m.</summary>
            public float height;
            /// <summary>Fit the canvas inside this box (metres) instead of the gallery's default size -
            /// for hanging a work inside a frame the capture already has.</summary>
            public Vector2 fit;
            /// <summary>No gold frame of ours: the capture's own frame surrounds the work.</summary>
            public bool noFrame;
            /// <summary>Stand the canvas's lower edge this far above the floor (overrides height), so a work
            /// of any aspect sits at the foot of a tall frame like a picture in a deep mount.</summary>
            public float bottom;
            /// <summary>A linen mount of this size behind the canvas, filling a capture frame's opening
            /// so none of the capture's own blurred canvas shows round a narrower work.</summary>
            public Vector2 mount;
        }

        public const float FrameCentreHeight = 1.5f;   // her rule
        public const float CardOffset = 0.3f;          // her rule: 0.3 m right of the frame
        public const float ViewingDistance = 2.0f;
        public const float NarrowWork = 1.0f;          // her rule: a work narrower than this has its card below it
        public const float CardGap = 0.08f;
        public const float CaptureFrameBorder = 0.14f; // a capture's gold moulding outside the mount
        public const float GroundTolerance = 0.6f;
        public const float FigureLift = 0.03f;     // how far a floor may sit from the entry's level     // her viewing mark is 1.8-2.5 m from a work

        public ChapterDiagrams.Diagram Diagram { get; private set; }
        public Calibration Cal { get; private set; }

        /// <summary>Where each diagram item ended up, by id (after snapping), for checks and the journey.</summary>
        public readonly Dictionary<string, Pose> Placed = new Dictionary<string, Pose>();

        /// <summary>Each work's card by artwork id, hidden until <see cref="ShowCard"/> (her "appears on point").</summary>
        public readonly Dictionary<string, GameObject> Cards = new Dictionary<string, GameObject>();

        /// <summary>Build with every card showing - for review renders only.</summary>
        [NonSerialized] public bool ShowCards;

        public void ShowCard(string artworkId, bool show)
        {
            if (Cards.TryGetValue(artworkId, out var c) && c != null) c.SetActive(show);
        }

        Func<Vector3, Vector3, float, float?> _ray;   // world origin, direction, max -> distance

        /// <summary>
        /// Build. <paramref name="ray"/> casts against the world's collider (CaptureProbe.Ray);
        /// <paramref name="image"/> resolves an artwork id to its picture; <paramref name="info"/> to its card;
        /// <paramref name="master"/> gives a master's prefab; <paramref name="exitModel"/> the door.
        /// </summary>
        public void Build(ChapterDiagrams.Diagram diagram, Calibration cal,
                          Func<Vector3, Vector3, float, float?> ray,
                          Func<string, Texture2D> image, Func<string, ArtworkInfo> info,
                          Func<string, GameObject> master, GameObject exitModel, float exitHeightOffset = 0f,
                          IReadOnlyDictionary<string, Override> overrides = null)
        {
            Diagram = diagram; Cal = cal; _ray = ray;
            var rot = Quaternion.Euler(0f, cal.yaw, 0f);
            var fwd = rot * Vector3.forward; var right = rot * Vector3.right;

            foreach (var item in diagram.Items)
            {
                var (lx, lz) = ChapterDiagrams.ToLocal(diagram, item, cal.across, cal.along);
                var wallSide = cal.flipX ? Flip(item.Wall) : item.Wall;
                if (cal.flipX) lx = -lx;
                if (overrides != null && overrides.TryGetValue(item.Id, out var o)) { lx = o.x; lz = o.z; if (o.wall != WallSide.None) wallSide = o.wall; }
                var p = Ground(cal.entry + right * lx + fwd * lz);
                Vector3? fixedDir = null;
                Override fo = default;
                if (overrides != null && overrides.TryGetValue(item.Id, out fo) && fo.fixedPlace)
                    fixedDir = Quaternion.Euler(0f, cal.yaw + fo.faceYaw, 0f) * Vector3.forward;
                switch (item.Kind)
                {
                    case DiagramKind.Work:
                        Work(item, p, fixedDir ?? WallDir(wallSide, right, fwd), image, info, fixedDir.HasValue,
                             fo.height > 0f ? fo.height : FrameCentreHeight, fo.fit, fo.noFrame, fo.bottom, fo.mount);
                        if (fixedDir.HasValue && fo.stand) Easel(item, p, fixedDir.Value);
                        break;
                    case DiagramKind.Mark: Mark(item, p, master); break;
                    case DiagramKind.Interaction: Interaction(item, p); break;
                    case DiagramKind.Exit: Exit(item, p, fixedDir.HasValue ? -fixedDir.Value : WallDir(wallSide, right, fwd), exitModel, exitHeightOffset, fixedDir.HasValue); break;
                    default: Placed[item.Id] = new Pose(p, rot); break;
                }
            }
        }

        static WallSide Flip(WallSide w) => w == WallSide.Left ? WallSide.Right : w == WallSide.Right ? WallSide.Left : w;

        static Vector3 WallDir(WallSide side, Vector3 right, Vector3 fwd) => side switch
        {
            WallSide.Left => -right, WallSide.Right => right, WallSide.Ahead => fwd, WallSide.Behind => -fwd, _ => right,
        };

        /// <summary>p moved onto the capture's floor beneath it (a garden path falls away from its
        /// entry); unchanged when the collider has no floor there.</summary>
        Vector3 Ground(Vector3 p)
        {
            if (_ray == null) return p;
            const float up = 1.6f;
            var d = _ray(p + Vector3.up * up, Vector3.down, up + 2.5f);
            if (!d.HasValue || d.Value <= 0.3f) return p;
            float y = p.y + up - d.Value;
            // A bench, a sill or a ledge is not the floor: only a surface near the entry's own level counts.
            return Mathf.Abs(y - Cal.entry.y) <= GroundTolerance ? new Vector3(p.x, y, p.z) : p;
        }

        /// <summary>The wall's distance from p along dir at frame height, or null when there is none within reach.</summary>
        float? WallFrom(Vector3 p, Vector3 dir, float reach = 12f)
        {
            if (_ray == null) return null;
            var d = _ray(p + Vector3.up * FrameCentreHeight, dir, reach);
            // Junk geometry right at a point reads 0.1-0.2 m; a wall is further than that.
            return d.HasValue && d.Value > 0.25f ? d : null;
        }

        /// <summary>The wall's inward direction (horizontal), from three hits spread along it; null when
        /// the wall is not continuous enough there to say.</summary>
        Vector3? WallNormalInto(Vector3 p, Vector3 dir)
        {
            var side = Vector3.Cross(Vector3.up, dir).normalized;
            var o = p + Vector3.up * FrameCentreHeight;
            float? a = _ray?.Invoke(o - side * 0.35f, dir, 12f), b = _ray?.Invoke(o + side * 0.35f, dir, 12f);
            if (!a.HasValue || !b.HasValue || a.Value < 0.25f || b.Value < 0.25f) return null;
            var pa = o - side * 0.35f + dir * a.Value; var pb = o + side * 0.35f + dir * b.Value;
            var along = pb - pa; along.y = 0f;
            if (along.magnitude > 2.5f) return null;   // a corner or an opening, not one wall
            var n = Vector3.Cross(Vector3.up, along.normalized);
            return Vector3.Dot(n, dir) < 0f ? -n : n;
        }

        void Work(DiagramItem item, Vector3 p, Vector3 wallDir, Func<string, Texture2D> image, Func<string, ArtworkInfo> info, bool exact = false,
                  float centreHeight = FrameCentreHeight, Vector2 fit = default, bool noFrame = false, float bottom = 0f,
                  Vector2 mount = default)
        {
            var d = exact ? null : WallFrom(p, wallDir);
            var onWall = (d.HasValue ? p + wallDir * (d.Value - 0.06f) : p) + Vector3.up * centreHeight;
            // Square to the wall itself, not to the direction it was looked for in: a probe that
            // meets a wall at 40 degrees would otherwise hang the work edge-on to it.
            var into = exact ? wallDir : WallNormalInto(p, wallDir) ?? wallDir;
            var faces = Quaternion.LookRotation(into, Vector3.up);   // +Z into the wall: reads from the room

            var tex = image?.Invoke(item.Id);
            float aspect = tex != null ? tex.width / (float)tex.height : 1.25f;
            var size = WebGalleryLayout.CanvasSize(aspect);
            if (fit.x > 0f && fit.y > 0f) size = aspect >= fit.x / fit.y ? new Vector2(fit.x, fit.x / aspect) : new Vector2(fit.y * aspect, fit.y);
            if (bottom > 0f) onWall += Vector3.up * (bottom + size.y / 2f - centreHeight);

            var work = new GameObject("Work " + item.Label);
            work.transform.SetParent(transform, false);
            work.transform.SetPositionAndRotation(onWall, faces);
            var canvas = GameObject.CreatePrimitive(PrimitiveType.Quad);
            canvas.name = "Canvas"; Kill(canvas.GetComponent<Collider>());
            canvas.transform.SetParent(work.transform, false);
            canvas.transform.localScale = new Vector3(size.x, size.y, 1f);
            var shader = Shader.Find("MuseXR/Artwork");
            var m = new Material(shader != null ? shader : Shader.Find("Universal Render Pipeline/Unlit"));
            if (tex != null) m.SetTexture("_BaseMap", tex);
            canvas.GetComponent<Renderer>().sharedMaterial = m;
            if (!noFrame) Frame(work.transform, size);
            if (mount.x > 0f && mount.y > 0f)
            {
                var board = GameObject.CreatePrimitive(PrimitiveType.Quad);
                board.name = "Mount"; Kill(board.GetComponent<Collider>());
                board.transform.SetParent(work.transform, false);
                board.transform.localPosition = new Vector3(0f, 0f, 0.012f);   // just behind the canvas
                board.transform.localScale = new Vector3(mount.x, mount.y, 1f);
                var lm = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                lm.SetColor("_BaseColor", new Color(0.9f, 0.86f, 0.78f));   // her paper, warmed
                board.GetComponent<Renderer>().sharedMaterial = lm;
            }
            var box = work.AddComponent<BoxCollider>(); box.size = new Vector3(size.x, size.y, 0.06f);   // pointable
            Placed[item.Id] = new Pose(onWall, faces);

            // Her 4.1 card: 0.3 m to the right of the frame as the visitor faces it, same depth.
            var a = info?.Invoke(item.Id);
            if (a != null)
            {
                var viewerRight = faces * Vector3.right;   // +Z points away from the viewer, so +X is their right
                var anchor = new GameObject("Card " + item.Label).transform;
                anchor.SetParent(work.transform, false);
                float cardW = 440f * PanelScale.MetresPerPixel(ViewingDistance);
                anchor.position = onWall + viewerRight * (size.x / 2f + CardOffset + cardW / 2f) - wallDir * 0.02f;
                anchor.rotation = faces;
                MuseScreens.ArtworkCard(anchor, a, ViewingDistance);
                // Her rule: below the frame, not beside it, when the work is under 1 m wide.
                if (size.x < NarrowWork)
                {
                    Canvas.ForceUpdateCanvases();
                    float lo = float.MaxValue, hi = float.MinValue; var c = new Vector3[4];
                    foreach (var rt in anchor.GetComponentsInChildren<RectTransform>())
                    {
                        if (rt.GetComponent<Canvas>() != null) continue;   // the canvas root's rect is not what it draws
                        rt.GetWorldCorners(c);
                        foreach (var v in c) { lo = Mathf.Min(lo, v.y); hi = Mathf.Max(hi, v.y); }
                    }
                    float cardH = hi > lo ? hi - lo : 0.3f;
                    // Clear what surrounds the canvas too: a mount, and the capture's own frame moulding round it.
                    float below = Mathf.Max(size.y, mount.y) / 2f + (mount.y > 0f ? CaptureFrameBorder : 0f);
                    anchor.position = onWall - Vector3.up * (below + CardGap + cardH / 2f) - wallDir * 0.02f;
                }
                // Her 4.1 card "appears on point": built, then hidden until a pointer asks for it.
                anchor.gameObject.SetActive(ShowCards);
                Cards[item.Id] = anchor.gameObject;
            }
        }

        void Easel(DiagramItem item, Vector3 floor, Vector3 into)
        {
            // Three legs of an artist's easel: two in front splayed out, one behind leaning in.
            var wood = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            wood.SetColor("_BaseColor", new Color(0.42f, 0.3f, 0.2f)); wood.SetFloat("_Smoothness", 0.25f);
            var root = new GameObject("Easel " + item.Label).transform; root.SetParent(transform, false);
            root.SetPositionAndRotation(floor, Quaternion.LookRotation(into, Vector3.up));
            void Leg(Vector3 foot, Vector3 top)
            {
                var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
                b.name = "Leg"; Kill(b.GetComponent<Collider>());
                b.transform.SetParent(root, false);
                var a = root.TransformPoint(foot); var t = root.TransformPoint(top);
                b.transform.position = (a + t) / 2f;
                b.transform.rotation = Quaternion.FromToRotation(Vector3.up, (t - a).normalized);
                b.transform.localScale = new Vector3(0.035f, (t - a).magnitude, 0.035f);
                b.GetComponent<Renderer>().sharedMaterial = wood;
            }
            float top = FrameCentreHeight + 0.75f;
            // +Z is behind the canvas (it faces back along -Z at the visitor), so every leg is too.
            Leg(new Vector3(-0.42f, 0f, 0.12f), new Vector3(-0.08f, top, 0.1f));
            Leg(new Vector3(0.42f, 0f, 0.12f), new Vector3(0.08f, top, 0.1f));
            Leg(new Vector3(0f, 0f, 0.8f), new Vector3(0f, top - 0.1f, 0.12f));
            // The ledge the canvas sits on.
            var ledge = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ledge.name = "Ledge"; Kill(ledge.GetComponent<Collider>());
            ledge.transform.SetParent(root, false);
            ledge.transform.localPosition = new Vector3(0f, FrameCentreHeight - 0.55f, -0.04f);
            ledge.transform.localScale = new Vector3(1.0f, 0.03f, 0.08f);
            ledge.GetComponent<Renderer>().sharedMaterial = wood;
        }

        static void Frame(Transform work, Vector2 size)
        {
            const float w = 0.06f, depth = 0.05f;
            var gold = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            gold.SetColor("_BaseColor", new Color(0.72f, 0.58f, 0.3f)); gold.SetFloat("_Metallic", 0.7f); gold.SetFloat("_Smoothness", 0.45f);
            void Bar(Vector3 pos, Vector3 scale)
            {
                var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
                b.name = "Frame"; Kill(b.GetComponent<Collider>());
                b.transform.SetParent(work, false); b.transform.localPosition = pos; b.transform.localScale = scale;
                b.GetComponent<Renderer>().sharedMaterial = gold;
            }
            Bar(new Vector3(0, size.y / 2 + w / 2, 0.01f), new Vector3(size.x + 2 * w, w, depth));
            Bar(new Vector3(0, -size.y / 2 - w / 2, 0.01f), new Vector3(size.x + 2 * w, w, depth));
            Bar(new Vector3(size.x / 2 + w / 2, 0, 0.01f), new Vector3(w, size.y, depth));
            Bar(new Vector3(-size.x / 2 - w / 2, 0, 0.01f), new Vector3(w, size.y, depth));
        }

        void Mark(DiagramItem item, Vector3 p, Func<string, GameObject> master)
        {
            var toEntry = Cal.entry - p; toEntry.y = 0f;
            var facing = toEntry.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(toEntry, Vector3.up) : Quaternion.identity;
            var prefab = master?.Invoke(item.Id);
            GameObject go;
            if (prefab != null) { go = Instantiate(prefab, transform); go.name = "Mark " + item.Id; }
            else go = new GameObject("Mark " + item.Id);
            go.transform.SetParent(transform, true);
            // A few centimetres up: floor splats composite over anything at y 0 and haze a figure's feet.
            go.transform.SetPositionAndRotation(p + Vector3.up * FigureLift, facing);
            Placed[item.Id] = new Pose(p, facing);
        }

        void Interaction(DiagramItem item, Vector3 p)
        {
            // Her diagram's mark: a rose ring, a centre dot and a soft halo, flat on the floor. Real
            // meshes rather than UI sprites, so it survives a saved scene and reads at a low angle.
            var root = new GameObject("Interaction " + item.Label).transform;
            root.SetParent(transform, false);
            root.position = p + Vector3.up * 0.05f;   // clear of floor splats that would band across it
            Disc(root, "Halo", 0f, 0.46f, new Color(MuseTheme.Rose.r, MuseTheme.Rose.g, MuseTheme.Rose.b, 0.16f), 0f);
            Disc(root, "Ring", 0.3f, 0.36f, MuseTheme.Rose, 0.004f);
            Disc(root, "Dot", 0f, 0.07f, MuseTheme.Rose, 0.004f);
            Placed[item.Id] = new Pose(p, Quaternion.Euler(0f, Cal.yaw, 0f));
        }

        /// <summary>Hang a work exactly here, framed, its face toward -<paramref name="into"/> - for
        /// works that float free (Your world's "works you stayed with").</summary>
        public void HangFree(string id, string label, Vector3 floorPoint, Vector3 into, float centreHeight,
                             Func<string, Texture2D> image, Func<string, ArtworkInfo> info, float width = 0f)
        {
            var item = new DiagramItem(id, DiagramKind.Work, 0f, 0f, label);
            Work(item, floorPoint, into, image, info, true, centreHeight, width > 0f ? new Vector2(width, width * 1.5f) : default);
        }

        /// <summary>A display stand: a body, a gold cap and a gold foot. Returns the cap's top centre,
        /// where a piece stands.</summary>
        public Vector3 Plinth(string name, Vector3 floor, Quaternion facing, Color body, float height = 1.0f, float width = 0.34f)
        {
            var root = new GameObject("Plinth " + name).transform; root.SetParent(transform, false);
            root.SetPositionAndRotation(floor, facing);
            var lacquer = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            lacquer.SetColor("_BaseColor", body); lacquer.SetFloat("_Smoothness", 0.55f);
            var gold = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            gold.SetColor("_BaseColor", new Color(0.78f, 0.6f, 0.28f)); gold.SetFloat("_Metallic", 0.8f); gold.SetFloat("_Smoothness", 0.5f);
            void Block(string n, float y0, float y1, float w, Material m)
            {
                var b = GameObject.CreatePrimitive(PrimitiveType.Cube); b.name = n;
                Kill(b.GetComponent<Collider>());
                b.transform.SetParent(root, false);
                b.transform.localPosition = new Vector3(0f, (y0 + y1) / 2f, 0f);
                b.transform.localScale = new Vector3(w, y1 - y0, w);
                b.GetComponent<Renderer>().sharedMaterial = m;
            }
            Block("Foot", 0f, 0.06f, width + 0.08f, gold);
            Block("Body", 0.06f, height - 0.05f, width, lacquer);
            Block("Cap", height - 0.05f, height, width + 0.06f, gold);
            return floor + Vector3.up * height;
        }

        /// <summary>Destroy that also works when a chapter is laid out in the Editor.</summary>
        static void Kill(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }

        static void Disc(Transform parent, string name, float inner, float outer, Color colour, float lift)
        {
            const int n = 64;
            var v = new List<Vector3>(); var t = new List<int>();
            for (int i = 0; i <= n; i++)
            {
                float a = i * Mathf.PI * 2f / n, c = Mathf.Cos(a), s = Mathf.Sin(a);
                v.Add(new Vector3(c * inner, lift, s * inner)); v.Add(new Vector3(c * outer, lift, s * outer));
                if (i < n) { int k = i * 2; t.AddRange(new[] { k, k + 2, k + 1, k + 1, k + 2, k + 3 }); }
            }
            var mesh = new Mesh { name = name }; mesh.SetVertices(v); mesh.SetTriangles(t, 0); mesh.RecalculateBounds();
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            m.SetColor("_BaseColor", colour); m.SetFloat("_Cull", 0f);   // either winding reads from above
            if (colour.a < 1f)
            {
                m.SetFloat("_Surface", 1f); m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha); m.SetFloat("_ZWrite", 0f);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.renderQueue = 3000;
            }
            go.AddComponent<MeshRenderer>().sharedMaterial = m;
        }

        void Exit(DiagramItem item, Vector3 p, Vector3 wallDir, GameObject model, float heightOffset, bool exact = false)
        {
            var d = exact ? null : WallFrom(p, wallDir, 20f);
            var at = d.HasValue ? p + wallDir * (d.Value - 0.1f) : p;
            var faces = Quaternion.LookRotation(-wallDir, Vector3.up);   // the door's front (+Z) faces into the room
            Transform go;
            if (model != null) { go = Instantiate(model, transform).transform; go.name = "Exit " + item.Label; }
            else go = new GameObject("Exit " + item.Label).transform;
            go.SetParent(transform, true);
            go.SetPositionAndRotation(at + Vector3.up * heightOffset, faces);
            Placed[item.Id] = new Pose(at, faces);
        }
    }
}
