using MuseXR.UI;
using TMPro;
using UnityEngine;

namespace MuseXR.Interaction
{
    /// <summary>
    /// The Palace's hero work from her design (P1, main): "The empty throne at the centre of the hall now holds
    /// an 8-metre gold goddess whose phoenix crown nearly touches the coffered ceiling... Stand at the foot of
    /// the steps and look up". Aim and hold the trigger to replicate a 15 cm gold figurine into the satchel.
    ///
    /// The model is a image-to-3D test from her concept still (img/tripo/goddess-throne.webp), remeshed
    /// to ~30k triangles with 1024 textures. It stands 7 m, not 8: in this capture the coffered ceiling sits
    /// just above a 7 m crown (measured with marker poles and by eye from the steps, 4 Oct 2026).
    /// </summary>
    public sealed class PalaceHeroes : MonoBehaviour
    {
        [Tooltip("Assets/Art/Heroes/goddess-throne.glb - the golden phoenix-crowned goddess on her throne.")]
        public GameObject goddess;

        /// <summary>The capture's throne seat, in the Palace frame: past the steps, before the gold screen.</summary>
        public static readonly Vector3 ThroneAt = new Vector3(0f, 0f, -7.2f);
        /// <summary>The entry the visitor walks in from; she faces it, so "her gaze lands exactly where you are".</summary>
        public static readonly Vector3 EntryAt = new Vector3(0f, 0f, 2.4f);
        public const float GoddessHeight = 7f;
        static readonly Color HallGold = new Color(1f, 0.8f, 0.42f);

        public GameObject Goddess { get; private set; }

        void Start()
        {
            if (goddess == null) { Debug.LogWarning("[PalaceHeroes] no goddess model"); return; }
            var floor = transform.TransformPoint(ThroneAt);
            var entry = transform.TransformPoint(EntryAt);
            var go = Instantiate(goddess, transform);
            go.name = "Hero · golden phoenix-crowned goddess";
            var toEntry = entry - floor; toEntry.y = 0f;
            go.transform.rotation = Quaternion.LookRotation(toEntry.sqrMagnitude > 1e-4f ? toEntry.normalized : Vector3.forward, Vector3.up);
            go.transform.localScale = Vector3.one;
            var b = Bounds(go);
            if (b.size.y > 1e-4f) go.transform.localScale = Vector3.one * (GoddessHeight / b.size.y);
            b = Bounds(go);
            go.transform.position += new Vector3(floor.x - b.center.x, floor.y - b.min.y, floor.z - b.center.z);
            // The generated texture is a pale champagne beside the capture's saturated gilt: warm it to the hall's gold.
            foreach (var r in go.GetComponentsInChildren<Renderer>())
                foreach (var m in r.materials)
                    if (m.HasProperty("baseColorFactor")) m.SetColor("baseColorFactor", HallGold);
                    else if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", HallGold);

            // A solid box: she is on the dais, which is not walkable anyway (her diagram: "non-walkable").
            b = Bounds(go);
            var box = go.AddComponent<BoxCollider>();
            box.center = go.transform.InverseTransformPoint(b.center);
            var s = go.transform.lossyScale;
            box.size = new Vector3(b.size.x / s.x, b.size.y / s.y, b.size.z / s.z);
            Replicable.Make(go, "Golden phoenix-crowned goddess", "palace").replicaName = "A 15 cm gold figurine";
            Label(go, entry, "Golden phoenix-crowned goddess  ·  7 m", "AI original sculpture  ·  aim and hold the trigger to replicate");
            Goddess = go;
        }

        void Label(GameObject hero, Vector3 entry, string title, string sub)
        {
            // On the floor at the foot of the steps, where her script stands the visitor.
            var b = Bounds(hero);
            var toEntry = entry - b.center; toEntry.y = 0f; toEntry.Normalize();
            var at = new Vector3(b.center.x, b.min.y + 1.1f, b.center.z) + toEntry * (b.extents.z + 2.2f);
            var anchor = new GameObject("Label " + title).transform;
            anchor.SetParent(hero.transform, true);
            anchor.SetPositionAndRotation(at, Quaternion.LookRotation(-toEntry, Vector3.up));   // +Z away from the viewer reads
            var c = MuseUi.Canvas(anchor, "Label", 3f, 260f);
            var card = MuseUi.Card(c, MuseTheme.Paper, MuseTheme.OptionRadius, MuseTheme.Line, 1f, padX: 10f, padY: 7f, gap: 2f, name: "Plate");
            card.GetComponent<UnityEngine.UI.VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
            var t = MuseUi.Text(card, title, MuseUi.Face.SansSemi, 11f, MuseTheme.Ink, name: "Title"); t.alignment = TextAlignmentOptions.Center;
            var u = MuseUi.Text(card, sub, MuseUi.Face.Sans, 9f, MuseTheme.Ink3, name: "Sub"); u.alignment = TextAlignmentOptions.Center;
        }

        static Bounds Bounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b;
        }
    }
}
