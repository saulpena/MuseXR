using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace MuseXR.Interaction
{
    /// <summary>
    /// Test tooling (Saul, 5 Oct 2026: "go to each of the paintings and make sure they are not clipping any
    /// objects, positioned in weird angles, have the proper aspect ratio, the borders look good... not randomly
    /// floating in the air... not blocking or being blocked"). Added in Play to a scene; for every hung work
    /// (WorldPaintings) and hero painting ("Hero · ...") it measures, then photographs it from the front and
    /// from 50 degrees to the side, in its room. Prints an AUDIT line per work and a VERDICT; it is a harness to
    /// run, not to edit to pass.
    ///
    /// Each number can fail: aspect error (canvas proportions against the image's, crop included), tilt
    /// (the canvas off vertical), the wall behind it (ray into the scan's collider), anything inside the
    /// painting's box or standing between it and its viewing spot (other renderers' bounds), and its height.
    /// </summary>
    public sealed class PaintingAudit : MonoBehaviour
    {
        public const string Folder = "Assets/Screenshots/paintings";
        public string label = "scene";
        /// <summary>Proposal mode: each hung work moved flush onto the wall behind it, or onto an easel when none is near; photographed after.</summary>
        public bool propose;
        public const float WallReach = 2.5f, Flush = 0.03f, EaselShelfY = 0.84f, EaselShelfZ = 0.31f;
        public bool Done { get; private set; }
        public string Report { get; private set; } = "running";

        public const float AspectTolerance = 0.03f, TiltTolerance = 3f, WallFar = 0.35f, ViewDistance = 2.4f;

        IEnumerator Start()
        {
            System.IO.Directory.CreateDirectory(Folder);
            yield return null; yield return null;
            var works = Find();
            var sb = new StringBuilder();
            int faults = 0;
            var probes = new Dictionary<WorldPaintings, MuseXR.Worlds.CaptureProbe>();
            // The visitor's own camera: a second camera does not draw the splat world, and a painting must be judged in its room.
            var cam = Camera.main;
            foreach (var bh in cam.GetComponents<Behaviour>()) if (bh.GetType().Name.Contains("TrackedPose")) bh.enabled = false;
            var rig = FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>();
            if (rig != null) { var cc = rig.GetComponent<CharacterController>(); if (cc != null) cc.enabled = false; foreach (var bh in rig.GetComponentsInChildren<Behaviour>()) if (bh.GetType().Name.Contains("Gravity") || bh.GetType().Name.Contains("Move") || bh.GetType().Name.Contains("Turn")) bh.enabled = false; }
            var hud = new List<GameObject>();
            foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None)) if (t.name == "Torso Panel" || t.name == "Chapter Question") { if (t.gameObject.activeSelf) hud.Add(t.gameObject); t.gameObject.SetActive(false); }
            RenderTexture rt = null;
            int n = 0;
            foreach (var (canvas, owner, kind) in works)
            {
                if (canvas == null) continue;
                n++;
                var r = canvas.GetComponent<Renderer>();
                var notes = new List<string>();
                // Aspect: the canvas against the part of the image it shows.
                var tex = r.sharedMaterial != null ? r.sharedMaterial.mainTexture : null;
                var s = canvas.lossyScale; float canvasAspect = Mathf.Abs(s.x / s.y);
                if (tex != null && tex.height > 0 && kind != "ceiling")
                {
                    var tiling = r.sharedMaterial.HasProperty("_BaseMap") ? r.sharedMaterial.GetTextureScale("_BaseMap") : Vector2.one;
                    float imageAspect = MuseXR.Worlds.PictureAspect.Of(tex) * Mathf.Abs(tiling.x) / Mathf.Abs(tiling.y);
                    float err = Mathf.Abs(canvasAspect / imageAspect - 1f);
                    if (err > AspectTolerance) notes.Add("ASPECT " + (err * 100f).ToString("F0") + "% off (canvas " + canvasAspect.ToString("F2") + ", image " + imageAspect.ToString("F2") + ")");
                }
                // Tilt: a wall painting hangs plumb.
                var normal = -canvas.forward;
                if (kind != "ceiling")
                {
                    float pitch = Vector3.Angle(new Vector3(normal.x, 0f, normal.z), normal);
                    float roll = Vector3.Angle(Vector3.ProjectOnPlane(canvas.up, normal), Vector3.ProjectOnPlane(Vector3.up, normal));
                    if (pitch > TiltTolerance) notes.Add("TILTED " + pitch.ToString("F0") + " deg off vertical");
                    if (roll > TiltTolerance) notes.Add("ROLLED " + roll.ToString("F0") + " deg");
                }
                // The wall behind, from the scan's collider.
                MuseXR.Worlds.CaptureProbe probe = null;
                if (owner != null && !probes.TryGetValue(owner, out probe))
                {
                    MuseXR.Worlds.WorldDefinition world = null;
                    foreach (var w in MuseXR.Worlds.WorldCatalog.Small) if (w.key == owner.worldKey) world = w;
                    var model = owner.colliderModel;
#if UNITY_EDITOR
                    // Rooms that hang by hand-set placements carry no collider; the audit still wants their walls.
                    if (model == null && world != null)
                        model = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Worlds/Colliders/" + MuseXR.Worlds.CaptureProbe.BaseKey(world.key) + "-collider.glb");
#endif
                    probe = world != null && model != null ? MuseXR.Worlds.CaptureProbe.Open(world, new[] { model }) : null;
                    probes[owner] = probe;
                }
                string wall = "no collider";
                if (probe != null && kind != "ceiling")
                {
                    var hit = probe.Ray(canvas.position + normal * 0.3f, -normal, 3f);
                    if (hit.HasValue) { var gap = hit.Value - 0.3f; wall = gap.ToString("F2") + " m"; if (gap > WallFar) notes.Add("FLOATING " + gap.ToString("F2") + " m off the wall"); if (gap < -0.08f) notes.Add("SUNK " + (-gap).ToString("F2") + " m into the wall"); }
                    else { wall = "none within 3 m"; notes.Add("NO WALL behind"); }
                    var f = probe.Floor(canvas.position);
                    if (!float.IsNaN(f)) wall += ", centre " + (canvas.position.y - f).ToString("F2") + " m above floor";
                }
                // Clipping and blocking: other things' bounds in the painting's box, or between it and its viewer.
                var b = r.bounds; b.Expand(0.04f);
                var view = canvas.position + normal * ViewDistance;
                foreach (var o in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                {
                    if (!o.enabled || !o.gameObject.activeInHierarchy || o.transform.IsChildOf(canvas) || canvas.IsChildOf(o.transform)) continue;
                    if (o is ParticleSystemRenderer || o.name.StartsWith("Viewing") || o.GetComponent<GaussianSplatting.Runtime.GaussianSplatRenderer>() != null) continue;
                    if (o.GetComponentInParent<Canvas>() != null && o.GetComponentInParent<Canvas>().transform.IsChildOf(canvas)) continue;
                    var ob = o.bounds;
                    if (ob.size.sqrMagnitude < 1e-6f || ob.size.magnitude > 30f) continue;
                    if (ob.Intersects(b)) { notes.Add("CLIPS " + Path(o.transform)); continue; }
                    if (kind == "ceiling") continue;
                    if (ob.IntersectRay(new Ray(view, (canvas.position - view).normalized), out var d) && d < ViewDistance - 0.1f) notes.Add("BLOCKED BY " + Path(o.transform) + " at " + d.ToString("F1") + " m from the viewer");
                }
                // Photographs: front, then 50 degrees to the side.
                var name = canvas.name.Length > 40 ? canvas.name.Substring(0, 40) : canvas.name;
                var file = label + "-" + n.ToString("00") + "-" + Safe(name);
                var size = Mathf.Max(s.x, s.y);
                var dist = kind == "ceiling" ? 0f : Mathf.Max(ViewDistance, size * 1.6f);
                if (kind == "ceiling") Place(rig, cam, canvas.position - Vector3.up * 2.2f, canvas.position);
                else Place(rig, cam, canvas.position + normal * dist, canvas.position);
                yield return Shoot(cam, rt, file + "-front.png");
                if (kind != "ceiling")
                {
                    var side = Quaternion.AngleAxis(50f, Vector3.up) * new Vector3(normal.x, 0f, normal.z).normalized;
                    Place(rig, cam, canvas.position + side * dist * 0.85f, canvas.position);
                    yield return Shoot(cam, rt, file + "-side.png");
                }
                if (propose && kind == "hung" && probe != null) yield return Propose(canvas, probe, rig, cam, file, sb);
                if (notes.Count > 0) faults++;
                sb.AppendLine("AUDIT " + n.ToString("00") + " " + canvas.name + " [" + kind + "] " + canvas.lossyScale.x.ToString("F2") + "x" + canvas.lossyScale.y.ToString("F2") + " m, wall " + wall + " :: " + (notes.Count == 0 ? "ok" : string.Join("; ", notes)));
            }
            foreach (var p in probes.Values) p?.Dispose();
            foreach (var h in hud) if (h != null) h.SetActive(true);
            Report = "VERDICT: " + (faults == 0 ? "PASS" : "NOT YET") + " (" + n + " works, " + faults + " with findings)\n" + sb;
            Debug.Log("[PaintingAudit] " + label + "\n" + Report);
            Done = true;
        }

        /// <summary>The wall behind, as a point and a normal (three rays), within <see cref="WallReach"/>.</summary>
        static bool Wall(MuseXR.Worlds.CaptureProbe probe, Transform canvas, out Vector3 point, out Vector3 normal)
        {
            point = normal = Vector3.zero;
            var n = -canvas.forward; n.y = 0f; n.Normalize();
            var right = Vector3.Cross(Vector3.up, n);
            Vector3? Hit(Vector3 o) { var d = probe.Ray(o + n * 0.3f, -n, WallReach + 0.3f); return d.HasValue ? o + n * 0.3f - n * d.Value : (Vector3?)null; }
            var c = Hit(canvas.position); var l = Hit(canvas.position - right * 0.4f); var r = Hit(canvas.position + right * 0.4f);
            if (!c.HasValue) return false;
            point = c.Value;
            normal = n;
            if (l.HasValue && r.HasValue)
            {
                var along = r.Value - l.Value; along.y = 0f;
                var wn = Vector3.Cross(along.normalized, Vector3.up);
                if (Vector3.Dot(wn, n) < 0f) wn = -wn;
                if (Vector3.Angle(wn, n) < 35f) normal = wn;   // a far steeper reading is a corner or clutter: keep facing as hung
            }
            return true;
        }

        IEnumerator Propose(Transform canvas, MuseXR.Worlds.CaptureProbe probe, Unity.XR.CoreUtils.XROrigin rig, Camera cam, string file, StringBuilder sb)
        {
            var oldPos = canvas.position; var oldRot = canvas.rotation;
            string what;
            if (Wall(probe, canvas, out var point, out var normal))
            {
                canvas.SetPositionAndRotation(new Vector3(point.x, oldPos.y, point.z) + normal * Flush, Quaternion.LookRotation(-normal, Vector3.up));
                what = "ONTO WALL " + (oldPos - canvas.position).magnitude.ToString("F2") + " m back";
            }
            else
            {
                // An easel under it, the canvas resting on its shelf and its face where the easel's canvas would be.
                var n = -canvas.forward; n.y = 0f; n.Normalize();
                var h = canvas.lossyScale.y;
                var floor = probe.Floor(oldPos); if (float.IsNaN(floor)) floor = oldPos.y - 1.6f;
                var easelAt = new Vector3(oldPos.x, floor, oldPos.z) - n * EaselShelfZ;
                var easel = PropModels.Spawn("easel", canvas.parent, easelAt, Quaternion.LookRotation(n, Vector3.up), new Vector3(0f, 1.8f, 0f));
                if (easel != null) easel.name = "Proposed easel · " + canvas.name;
                canvas.SetPositionAndRotation(new Vector3(oldPos.x, floor + EaselShelfY + h * 0.5f + 0.02f, oldPos.z), Quaternion.LookRotation(-n, Vector3.up));
                what = "ON AN EASEL (no wall within " + WallReach + " m)";
            }
            var nn = -canvas.forward; nn.y = 0f; nn.Normalize();
            var dist = Mathf.Max(ViewDistance, Mathf.Max(canvas.lossyScale.x, canvas.lossyScale.y) * 1.6f);
            Place(rig, cam, canvas.position + nn * dist, canvas.position);
            yield return Shoot(cam, null, file + "-after-front.png");
            Place(rig, cam, canvas.position + Quaternion.AngleAxis(50f, Vector3.up) * nn * dist * 0.85f, canvas.position);
            yield return Shoot(cam, null, file + "-after-side.png");
            var local = canvas.parent != null ? canvas.parent.InverseTransformPoint(canvas.position) : canvas.position;
            var facing = canvas.parent != null ? canvas.parent.InverseTransformDirection(-canvas.forward) : -canvas.forward;
            sb.AppendLine("PROPOSE " + canvas.name + ": " + what + " -> centre " + local.ToString("F2") + " facing " + facing.ToString("F2"));
        }

        static void Place(Unity.XR.CoreUtils.XROrigin rig, Camera cam, Vector3 eye, Vector3 look)
        {
            if (rig != null) rig.MoveCameraToWorldLocation(eye); else cam.transform.position = eye;
            cam.transform.rotation = Quaternion.LookRotation(look - eye, Vector3.up);
        }

        IEnumerator Shoot(Camera cam, RenderTexture rt, string file)
        {
            // A few frames: the splat sort follows the camera late, and a first frame after a move draws no world.
            for (var i = 0; i < 4; i++) yield return null;
            yield return new WaitForEndOfFrame();
            var t = ScreenCapture.CaptureScreenshotAsTexture();
            System.IO.File.WriteAllBytes(Folder + "/" + file, t.EncodeToPNG());
            Destroy(t);
        }

        static List<(Transform canvas, WorldPaintings owner, string kind)> Find()
        {
            var list = new List<(Transform, WorldPaintings, string)>();
            foreach (var wp in FindObjectsByType<WorldPaintings>(FindObjectsSortMode.None))
                foreach (var t in wp.Hung) if (t != null && t.gameObject.activeInHierarchy) list.Add((t, wp, "hung"));
            foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
            {
                if (!t.name.StartsWith("Hero · ") || !t.gameObject.activeInHierarchy) continue;
                if (System.Array.IndexOf(HeroPaintings, t.name.Substring("Hero · ".Length)) < 0) continue;   // statues and glows are not paintings
                // The painted surface: the largest textured renderer under it (statues have no flat textured canvas).
                Renderer best = null;
                foreach (var r in t.GetComponentsInChildren<Renderer>())
                {
                    var m = r.sharedMaterial;
                    if (m == null || m.mainTexture == null || !m.shader.name.StartsWith("Universal Render Pipeline/")) continue;
                    if (best == null || r.bounds.size.sqrMagnitude > best.bounds.size.sqrMagnitude) best = r;
                }
                if (best == null) continue;
                var ceiling = Mathf.Abs(Vector3.Dot(best.transform.forward, Vector3.up)) > 0.7f;
                list.Add((best.transform, FindAnyObjectByType<WorldPaintings>(), ceiling ? "ceiling" : "hero"));
            }
            return list;
        }

        static readonly string[] HeroPaintings = { "The Starry Night", "The Bedroom", "The Great Wave", "Water Lilies", "Mona Lisa" };

        static string Path(Transform t) => (t.parent != null ? t.parent.name + "/" : "") + t.name;
        static string Safe(string s) { foreach (var c in System.IO.Path.GetInvalidFileNameChars()) s = s.Replace(c, '_'); return s.Replace(' ', '_').Replace('·', '-'); }
    }
}
