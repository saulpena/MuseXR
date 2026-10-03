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
        }

        public const float FrameCentreHeight = 1.5f;   // her rule
        public const float CardOffset = 0.3f;          // her rule: 0.3 m right of the frame
        public const float ViewingDistance = 2.0f;     // her viewing mark is 1.8-2.5 m from a work

        public ChapterDiagrams.Diagram Diagram { get; private set; }
        public Calibration Cal { get; private set; }

        /// <summary>Where each diagram item ended up, by id (after snapping), for checks and the journey.</summary>
        public readonly Dictionary<string, Pose> Placed = new Dictionary<string, Pose>();

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
                var p = cal.entry + right * lx + fwd * lz;
                Vector3? fixedDir = null;
                if (overrides != null && overrides.TryGetValue(item.Id, out var fo) && fo.fixedPlace)
                    fixedDir = Quaternion.Euler(0f, cal.yaw + fo.faceYaw, 0f) * Vector3.forward;
                switch (item.Kind)
                {
                    case DiagramKind.Work: Work(item, p, fixedDir ?? WallDir(wallSide, right, fwd), image, info, fixedDir.HasValue); break;
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

        void Work(DiagramItem item, Vector3 p, Vector3 wallDir, Func<string, Texture2D> image, Func<string, ArtworkInfo> info, bool exact = false)
        {
            var d = exact ? null : WallFrom(p, wallDir);
            var onWall = (d.HasValue ? p + wallDir * (d.Value - 0.06f) : p) + Vector3.up * FrameCentreHeight;
            // Square to the wall itself, not to the direction it was looked for in: a probe that
            // meets a wall at 40 degrees would otherwise hang the work edge-on to it.
            var into = exact ? wallDir : WallNormalInto(p, wallDir) ?? wallDir;
            var faces = Quaternion.LookRotation(into, Vector3.up);   // +Z into the wall: reads from the room

            var tex = image?.Invoke(item.Id);
            float aspect = tex != null ? tex.width / (float)tex.height : 1.25f;
            var size = WebGalleryLayout.CanvasSize(aspect);

            var work = new GameObject("Work " + item.Label);
            work.transform.SetParent(transform, false);
            work.transform.SetPositionAndRotation(onWall, faces);
            var canvas = GameObject.CreatePrimitive(PrimitiveType.Quad);
            canvas.name = "Canvas"; Destroy(canvas.GetComponent<Collider>());
            canvas.transform.SetParent(work.transform, false);
            canvas.transform.localScale = new Vector3(size.x, size.y, 1f);
            var shader = Shader.Find("MuseXR/Artwork");
            var m = new Material(shader != null ? shader : Shader.Find("Universal Render Pipeline/Unlit"));
            if (tex != null) m.SetTexture("_BaseMap", tex);
            canvas.GetComponent<Renderer>().sharedMaterial = m;
            Frame(work.transform, size);
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
            }
        }

        static void Frame(Transform work, Vector2 size)
        {
            const float w = 0.06f, depth = 0.05f;
            var gold = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            gold.SetColor("_BaseColor", new Color(0.72f, 0.58f, 0.3f)); gold.SetFloat("_Metallic", 0.7f); gold.SetFloat("_Smoothness", 0.45f);
            void Bar(Vector3 pos, Vector3 scale)
            {
                var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
                b.name = "Frame"; Destroy(b.GetComponent<Collider>());
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
            go.transform.SetPositionAndRotation(p, facing);
            Placed[item.Id] = new Pose(p, facing);
        }

        void Interaction(DiagramItem item, Vector3 p)
        {
            var root = new GameObject("Interaction " + item.Label).transform;
            root.SetParent(transform, false);
            root.SetPositionAndRotation(p + Vector3.up * 0.01f, Quaternion.Euler(90f, 0f, 0f));
            var c = MuseUi.Canvas(root, "Ring", ViewingDistance, 62f);
            c.localScale = Vector3.one * (0.7f / 62f);
            var row = MuseUi.Row(c, 0f, TextAnchor.MiddleCenter);
            MuseUi.Ring(row, UiSprites.Ring(0.12f), MuseTheme.Rose, 62f);
            Placed[item.Id] = new Pose(p, Quaternion.Euler(0f, Cal.yaw, 0f));
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
