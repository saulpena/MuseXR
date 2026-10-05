using UnityEngine;

namespace MuseXR.Interaction
{
    /// <summary>
    /// The generated models that replace the shapes the chapters used to build from cubes and cylinders (Saul,
    /// 4 Oct 2026: "no placeholders or things made with code anymore"). Each is in Resources/Props as a .gltf
    /// with its textures beside it (ASTC on Android; see ModelTextureImport), front +Z, origin at the bottom
    /// centre. Loaded by name, so no scene or prefab has to change to carry them.
    /// </summary>
    public static class PropModels
    {
        public const string Folder = "Props/";

        /// <summary>
        /// <paramref name="name"/> under <paramref name="parent"/>, turned by <paramref name="rotation"/> (world),
        /// scaled into <paramref name="fit"/> (world metres, x/y/z of the turned model; a 0 leaves that axis to
        /// the others), standing with its base centre on <paramref name="floor"/>. <paramref name="uniform"/>
        /// keeps its proportions (the smallest ratio wins); otherwise each axis is fitted on its own.
        /// Null, with an error, when the model is missing: nothing is put up in its place.
        /// </summary>
        public static GameObject Spawn(string name, Transform parent, Vector3 floor, Quaternion rotation, Vector3 fit, bool uniform = true)
        {
            var model = Resources.Load<GameObject>(Folder + name);
            if (model == null) { Debug.LogError("[PropModels] missing Resources/" + Folder + name); return null; }
            var go = Object.Instantiate(model, parent);
            go.name = "Prop · " + name;
            go.transform.SetPositionAndRotation(floor, rotation);
            go.transform.localScale = Vector3.one;
            var size = LocalSize(go);
            var s = Vector3.one;
            if (uniform)
            {
                var k = float.MaxValue;
                if (fit.x > 0f && size.x > 1e-5f) k = Mathf.Min(k, fit.x / size.x);
                if (fit.y > 0f && size.y > 1e-5f) k = Mathf.Min(k, fit.y / size.y);
                if (fit.z > 0f && size.z > 1e-5f) k = Mathf.Min(k, fit.z / size.z);
                if (k < float.MaxValue) s = Vector3.one * k;
            }
            else
            {
                var k = fit.y > 0f && size.y > 1e-5f ? fit.y / size.y : 1f;   // an axis left at 0 follows the height
                s = new Vector3(fit.x > 0f && size.x > 1e-5f ? fit.x / size.x : k, k, fit.z > 0f && size.z > 1e-5f ? fit.z / size.z : k);
            }
            var p = go.transform.parent;
            var ps = p != null ? p.lossyScale : Vector3.one;
            go.transform.localScale = new Vector3(s.x / ps.x, s.y / ps.y, s.z / ps.z);
            Seat(go, floor);
            return go;
        }

        /// <summary>Base centre on <paramref name="floor"/>: the lowest point on it, centred over it.</summary>
        public static void Seat(GameObject go, Vector3 floor)
        {
            var b = Bounds(go);
            go.transform.position += new Vector3(floor.x - b.center.x, floor.y - b.min.y, floor.z - b.center.z);
        }

        /// <summary>
        /// The layout's baked stands ("Plinth crane", "Plinth object"...: a Foot, Body and Cap of cubes, saved
        /// into the frames with no material) shown as the plinth model instead, the same height, its top where
        /// the cap's was so whatever stands on it still stands on it.
        /// </summary>
        public static int ReplacePlinths(Transform root)
        {
            var n = 0;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.StartsWith("Plinth ") || t.Find("Body") == null) continue;
                var cap = t.Find("Cap");
                var top = cap != null ? Bounds(cap.gameObject).max.y : t.position.y + 1f;
                var width = t.Find("Foot") != null ? Bounds(t.Find("Foot").gameObject).size.x : 0.44f;
                foreach (var r in t.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
                var go = Spawn("plinth", t, t.position, t.rotation, new Vector3(width, top - t.position.y, width), uniform: false);
                if (go != null) n++;
            }
            return n;
        }

        /// <summary>How far the easel model's canvas face stands in front of its bounds centre (measured, 5 Oct 2026).</summary>
        public const float EaselCanvasDepth = 0.2f;

        /// <summary>
        /// The layout's baked painting easels ("Easel Water Lilies": three cube legs and a ledge, saved with no
        /// material) shown as the easel model, blank canvas toward the visitor: the painter's easels of the
        /// garden. Their "Work ..." paintings are never shown in the journey (checked 5 Oct: forced on, they
        /// stood 1.8 m wide behind the easel); if they ever are, the easel steps back so its canvas face is 2 cm
        /// behind the painting's plane.
        /// </summary>
        public static int ReplaceEasels(Transform root)
        {
            var n = 0;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.StartsWith("Easel ") || t.Find("Ledge") == null) continue;
                Transform work = null;
                var title = t.name.Substring("Easel ".Length);
                foreach (var w in root.GetComponentsInChildren<Transform>(true)) if (w.name == "Work " + title) { work = w; break; }
                foreach (var r in t.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
                // A painting reads with its +Z away from the viewer; the model's front is its +Z, so it turns to face them.
                var facing = work != null ? work.rotation : t.rotation;
                var away = facing * Vector3.forward; away.y = 0f; away = away.sqrMagnitude > 1e-6f ? away.normalized : Vector3.forward;
                var at = t.position + away * (EaselCanvasDepth + 0.02f);
                if (Spawn("easel", t, at, Quaternion.LookRotation(-away, Vector3.up), new Vector3(0f, 1.8f, 0f)) != null) n++;
            }
            return n;
        }

        static Vector3 LocalSize(GameObject go)
        {
            // Measured in the model's own turned frame, so a fit means the same thing whatever way it faces.
            var t = go.transform;
            var rot = t.rotation; t.rotation = Quaternion.identity;
            var b = Bounds(go);
            t.rotation = rot;
            return b.size;
        }

        public static Bounds Bounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b;
        }
    }
}
