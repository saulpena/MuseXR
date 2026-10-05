using System.Collections.Generic;
using MusePico.Dialogue;
using MuseXR.Slots;
using MuseXR.UI;
using TMPro;
using UnityEngine;

namespace MuseXR.Interaction
{
    /// <summary>
    /// Her Your world (MUSE-VR-design, 2 Oct 2026, plan E and 4.5): the pre-made corridor, filled at
    /// runtime with what this visitor carried - the copies in their satchel, each scaled up to 3 m along
    /// the sides; between them what they left in each chapter (the Palace object at the yaw they set, the
    /// Grotto lamp on the socket they chose, their stroke at 1.5x arching overhead, the works they stayed
    /// with in floating gold frames); at the end the answer stone engraved with their answer, the world's
    /// title above it and the memento card 1.4 m over it. Passing each piece replays its chapter's chime.
    ///
    /// Reads the layout her diagram already placed ("Chapter YourWorld": "Plinth object", "Plinth lamp",
    /// "Stroke x1.5", "Work ...", "Answer stone", "Memento anchor"). A skipped chapter leaves no empty
    /// plinth. Opened on its own, with nothing recorded, the layout keeps her sample pieces.
    /// </summary>
    public sealed class YourWorldEnding : MonoBehaviour
    {
        [Tooltip("Assets/Art/Props/crane.glb, turtle.glb and lamp.glb - the Palace and Grotto pieces.")]
        public GameObject craneModel, turtleModel, lampModel;
        [Tooltip("Assets/Museum/artworks.json, for the works stayed with.")]
        public TextAsset artworksJson;
        [Tooltip("Images of the works a visitor may have stayed with, named by id.")]
        public Texture2D[] artworkImages;

        public const float CopyHeight = 3f;
        public const float ChimeRange = 1.6f;
        /// <summary>A plate's centre over the top of the piece it names (the lamp stood in front of its plate at 1.25 m).</summary>
        public const float PlateClearance = 0.3f;
        /// <summary>How far a plinth's plate stands out from the corridor's centre line, past its plinth.</summary>
        public const float PlateOutward = 0.3f;
        /// <summary>The stroke's width over the corridor: a brush stroke seen from 3 m, not a wire.</summary>
        public const float StrokeWidth = 0.1f;

        readonly List<(Vector3 at, AudioClip chime, bool played)> _chimes = new List<(Vector3, AudioClip, bool)>();
        AudioSource _audio;
        CanvasGroup _memento;
        float _waited;
        Transform _palacePiece;

        // ---- pure: what the record says, in her words ---------------------------------------------

        /// <summary>A compass word for a yaw in degrees (0 north, 90 east).</summary>
        public static string Direction(float yawDeg)
        {
            var names = new[] { "north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west" };
            var i = Mathf.RoundToInt(Mathf.Repeat(yawDeg, 360f) / 45f) % 8;
            return names[i];
        }

        /// <summary>Her three pots by colour; any other colour is just "Your".</summary>
        public static string PotName(string hex)
        {
            if (string.IsNullOrEmpty(hex) || !ColorUtility.TryParseHtmlString(hex.StartsWith("#") ? hex : "#" + hex, out var c)) return "Your";
            var pots = new (string name, Color c)[] { ("Cobalt", new Color32(0x2f, 0x4f, 0x8f, 0xff)), ("Chrome yellow", new Color32(0xe3, 0xb3, 0x3a, 0xff)), ("Cypress green", new Color32(0x3f, 0x5f, 0x2f, 0xff)) };
            string best = "Your"; float d = 0.12f;
            foreach (var p in pots) { var e = Mathf.Abs(c.r - p.c.r) + Mathf.Abs(c.g - p.c.g) + Mathf.Abs(c.b - p.c.b); if (e < d) { d = e; best = p.name; } }
            return best;
        }

        static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        /// <summary>
        /// Her memento's four choices, one line per chapter the visitor did, in walking order:
        /// "Turtle · facing east · “Because it is slow...”", "Lamp on the detail", "Cobalt stroke · The
        /// Bedroom", "Dusk · Water Lilies". A chapter not done gives no line.
        /// </summary>
        public static List<(string chapter, string line)> ChoiceLines(JourneyRecord r, System.Func<string, string> titleOf = null)
        {
            var list = new List<(string, string)>();
            if (r == null) return list;
            string Title(string id) => titleOf != null && !string.IsNullOrEmpty(id) ? (titleOf(id) ?? id) : id;
            if (r.Palace != null && !string.IsNullOrEmpty(r.Palace.Object))
                list.Add(("palace", Cap(r.Palace.Object) + " · facing " + Direction(r.Palace.YawDeg) +
                                    (string.IsNullOrWhiteSpace(r.Palace.Reason) ? "" : " · “" + r.Palace.Reason.Trim() + "”")));
            if (r.Grotto != null && !string.IsNullOrEmpty(r.Grotto.LampSlot))
                list.Add(("grotto", "Lamp on the " + (r.Grotto.LampSlot == "detail" ? "detail" : "whole")));
            if (r.VanGogh != null && (r.VanGogh.Points.Count > 0 || !string.IsNullOrEmpty(r.VanGogh.Color)))
                list.Add(("vangogh", PotName(r.VanGogh.Color) + " stroke" + (string.IsNullOrEmpty(r.VanGogh.ArtworkId) ? "" : " · " + Title(r.VanGogh.ArtworkId))));
            if (r.Monet != null && !string.IsNullOrEmpty(r.Monet.Preset))
                list.Add(("monet", Cap(r.Monet.Preset) + " · " + (string.IsNullOrWhiteSpace(r.Monet.Reason) ? Title(r.Monet.ArtworkId) : r.Monet.Reason.Trim())));
            return list;
        }

        /// <summary>Nothing recorded: the scene was opened on its own, not walked into.</summary>
        public static bool NothingRecorded(JourneyRecord r) =>
            r == null || (r.Palace == null && r.Grotto == null && r.VanGogh == null && r.Monet == null &&
                          string.IsNullOrWhiteSpace(r.FinalAnswer.Final) && string.IsNullOrWhiteSpace(r.Question));

        /// <summary>
        /// Where the n-th satchel copy stands: alternating left (-x) and right (+x) of the corridor,
        /// 0.9 m apart down it, from 1.2 m in - so they line both sides, the visitor's choices between.
        /// </summary>
        public static Vector3 CopySlot(int n) => new Vector3(n % 2 == 0 ? -1.9f : 1.9f, 0f, 1.2f + 0.9f * n);

        // ---- the scene -------------------------------------------------------------------------------

        void Start()
        {
            var rec = JourneyMemory.Record;
            var satchel = FindAnyObjectByType<Satchel>();
            var items = satchel != null ? satchel.Items : null;
            bool sample = NothingRecorded(rec) && (items == null || items.Count == 0);
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false; _audio.spatialBlend = 0f; _audio.volume = 0.45f;

            var titles = Titles();
            string TitleOf(string id) => titles.TryGetValue(id ?? "", out var t) ? t : id;

            // The layout's two stands, baked as bare cubes: the lacquer-and-gilt plinth model instead.
            if (transform.parent != null) PropModels.ReplacePlinths(transform.parent);
            else foreach (var r in gameObject.scene.GetRootGameObjects()) PropModels.ReplacePlinths(r.transform);   // a test scene: no frame
            PalacePiece(rec, sample);
            GrottoPiece(rec, sample);
            StrokePiece(rec, sample);
            WorksPieces(rec, sample, TitleOf);
            if (items != null) for (var i = 0; i < items.Count; i++) Copy(items[i], i);
            var stone = Find("Answer stone");
            var anchor = Find("Memento anchor");
            if (stone != null) AnswerStone(stone, rec, sample);
            if (anchor != null) Memento(anchor, rec, sample, TitleOf);
            Debug.Log("[YourWorld] " + (sample ? "her sample pieces (nothing recorded)" :
                      ChoiceLines(rec, TitleOf).Count + " choices, " + (items != null ? items.Count : 0) + " copies, answer: " + rec.FinalAnswer.Final));
        }

        void Update()
        {
            if (_memento != null && _memento.alpha < 1f && QuestionGone())
                _memento.alpha = Mathf.MoveTowards(_memento.alpha, 1f, Time.deltaTime / 0.8f);
            var eye = Camera.main != null ? Camera.main.transform.position : (Vector3?)null;
            if (eye == null) return;
            for (var i = 0; i < _chimes.Count; i++)
            {
                var c = _chimes[i];
                var d = c.at - eye.Value; d.y = 0f;
                if (c.played || d.magnitude > ChimeRange) continue;
                _audio.PlayOneShot(c.chime);
                _chimes[i] = (c.at, c.chime, true);
            }
        }

        Transform Find(string name)
        {
            var root = transform.parent != null ? transform.parent : transform;
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            foreach (var r in gameObject.scene.GetRootGameObjects())
                foreach (var t in r.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        void PalacePiece(JourneyRecord rec, bool sample)
        {
            var plinth = Find("Plinth object"); var shown = Find("crane");
            if (rec.Palace == null || string.IsNullOrEmpty(rec.Palace.Object))
            {
                if (!sample) { Hide(plinth); Hide(shown); }
                return;
            }
            var model = rec.Palace.Object.ToLowerInvariant().Contains("turtle") ? turtleModel : craneModel;
            if (shown != null && model != null)
            {
                var piece = Instantiate(model, shown.parent);
                _palacePiece = piece.transform;
                piece.name = rec.Palace.Object;
                piece.transform.SetPositionAndRotation(shown.position, Quaternion.Euler(0f, rec.Palace.YawDeg, 0f));
                piece.transform.localScale = shown.localScale;
                Hide(shown);
            }
            if (plinth != null)
            {
                Plate(Above(plinth, _palacePiece != null ? _palacePiece : shown), Cap(rec.Palace.Object) + "  ·  the Palace",
                      string.IsNullOrWhiteSpace(rec.Palace.Reason) ? "facing " + Direction(rec.Palace.YawDeg) : "“" + rec.Palace.Reason.Trim() + "”");
                _chimes.Add((plinth.position, ChapterChimes.Clip("palace"), false));   // a bronze bell
            }
        }

        void GrottoPiece(JourneyRecord rec, bool sample)
        {
            var plinth = Find("Plinth lamp"); var lamp = Find("lamp");
            if (rec.Grotto == null || string.IsNullOrEmpty(rec.Grotto.LampSlot))
            {
                if (!sample) { Hide(plinth); Hide(lamp); }
                return;
            }
            if (lamp != null)
            {
                var glow = new GameObject("Lamp glow").AddComponent<Light>();
                glow.transform.SetParent(lamp, false); glow.transform.localPosition = new Vector3(0f, 0.25f, 0f);
                glow.type = LightType.Point; glow.range = 2.5f; glow.intensity = 2.2f; glow.color = new Color(1f, 0.78f, 0.45f);
            }
            if (plinth != null)
            {
                Plate(Above(plinth, lamp), "The lamp  ·  the Grotto", "set on the " + (rec.Grotto.LampSlot == "detail" ? "detail" : "whole"));
                _chimes.Add((plinth.position, ChapterChimes.Clip("grotto"), false));   // a stone chime
            }
        }

        void StrokePiece(JourneyRecord rec, bool sample)
        {
            var strokeT = Find("Stroke x1.5");
            var line = strokeT != null ? strokeT.GetComponent<LineRenderer>() : null;
            if (line == null) return;
            if (rec.VanGogh == null || rec.VanGogh.Points.Count < 2)
            {
                if (!sample) line.enabled = false;
                return;
            }
            // Her "stroke at 1.5x arching over the corridor": the drawn shape, 1.5x, its lowest point at
            // 2.4 m and its middle over the corridor's centre line, so it is overhead and never in the way.
            var pts = rec.VanGogh.Points;
            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue); var max = -min;
            foreach (var p in pts) { var v = new Vector3(p[0], p[1], p[2]); min = Vector3.Min(min, v); max = Vector3.Max(max, v); }
            var centre = (min + max) * 0.5f;
            var at = new Vector3(0f, 2.4f + (max.y - min.y) * 0.75f, 3.2f);
            line.useWorldSpace = false;
            line.positionCount = pts.Count;
            for (var i = 0; i < pts.Count; i++)
                line.SetPosition(i, at + (new Vector3(pts[i][0], pts[i][1], pts[i][2]) - centre) * 1.5f);
            if (ColorUtility.TryParseHtmlString(rec.VanGogh.Color, out var c)) { line.startColor = c; line.endColor = c; if (line.sharedMaterial != null) line.material.color = c; }
            // A brush stroke, not a wire: at 6 cm wide with square ends it read as a stray line in the air
            // (live run, 4 Oct). Her stroke is the visitor's own work, so it is labelled like the others.
            line.widthMultiplier = StrokeWidth; line.numCapVertices = 6; line.numCornerVertices = 4;
            line.enabled = true;
            // Its plate just under the stroke's lower end, so it hangs from the thing it names.
            var a0 = at + (new Vector3(pts[0][0], pts[0][1], pts[0][2]) - centre) * 1.5f;
            var a1 = at + (new Vector3(pts[pts.Count - 1][0], pts[pts.Count - 1][1], pts[pts.Count - 1][2]) - centre) * 1.5f;
            var end = a0.y <= a1.y ? a0 : a1;
            Plate(strokeT.TransformPoint(end + Vector3.down * 0.35f), "Your stroke  ·  the Van Gogh studio",
                  PotName(rec.VanGogh.Color) + ", 1.5x, over the corridor");
            _chimes.Add((strokeT.TransformPoint(at), ChapterChimes.Clip("vangogh"), false));   // wood
        }

        void WorksPieces(JourneyRecord rec, bool sample, System.Func<string, string> titleOf)
        {
            // The works stayed with longest (gaze >= 4 s), else the two the chapters were linked to.
            var ids = new List<string>();
            foreach (var s in rec.Seen()) if (ids.Count < 2) ids.Add(s.id);
            if (ids.Count < 2 && rec.Monet != null && !string.IsNullOrEmpty(rec.Monet.ArtworkId) && !ids.Contains(rec.Monet.ArtworkId)) ids.Add(rec.Monet.ArtworkId);
            if (ids.Count < 2 && rec.VanGogh != null && !string.IsNullOrEmpty(rec.VanGogh.ArtworkId) && !ids.Contains(rec.VanGogh.ArtworkId)) ids.Add(rec.VanGogh.ArtworkId);
            var frames = new[] { Find("Work Water Lilies"), Find("Work The Bedroom") };
            for (var i = 0; i < frames.Length; i++)
            {
                var f = frames[i];
                if (f == null) continue;
                if (i >= ids.Count) { if (!sample) Hide(f); continue; }
                var canvas = f.Find("Canvas");
                var tex = Image(ids[i]);
                if (canvas != null && tex != null)
                {
                    var r = canvas.GetComponent<Renderer>();
                    r.material.SetTexture("_BaseMap", tex);
                    var h = canvas.localScale.y; canvas.localScale = new Vector3(h * tex.width / (float)tex.height, h, 1f);
                }
                f.name = "Work " + titleOf(ids[i]);
                _chimes.Add((f.position, ChapterChimes.Clip("monet"), false));   // water
            }
        }

        void Copy(Satchel.Item item, int n)
        {
            if (item.Copy == null) return;
            var copy = Instantiate(item.Copy, transform);
            copy.name = "Your copy · " + item.Label;
            copy.SetActive(true);   // the satchel keeps its copies off while it is closed
            foreach (var b in copy.GetComponentsInChildren<Behaviour>(true)) if (!(b is Light)) b.enabled = false;   // no satchel spin, no pointing
            copy.transform.localRotation = Quaternion.Euler(0f, n % 2 == 0 ? 90f : -90f, 0f);   // facing across the corridor
            copy.transform.localScale = item.Copy.transform.lossyScale;   // its own proportions, then up to 3 m
            copy.transform.localPosition = Vector3.zero;
            var b0 = Bounds(copy);
            if (b0.size.y > 1e-4f) copy.transform.localScale *= CopyHeight / b0.size.y;
            var slot = transform.TransformPoint(CopySlot(n));
            var b1 = Bounds(copy);
            copy.transform.position += new Vector3(slot.x - b1.center.x, slot.y - b1.min.y + 0.05f, slot.z - b1.center.z);
            Plate(slot + Vector3.up * 1.0f + (n % 2 == 0 ? Vector3.right : Vector3.left) * 0.75f, item.Label,
                  string.IsNullOrEmpty(item.Chapter) ? "" : "replicated in " + Where(item.Chapter));
        }

        static string Where(string chapter) => chapter switch
        {
            "gate" or "gate-venus" => "the conservatory gate", "palace" => "the Palace", "grotto" => "the Grotto",
            "vangogh" => "the Van Gogh studio", "monet" => "the Monet garden", _ => chapter,
        };

        void AnswerStone(Transform stone, JourneyRecord rec, bool sample)
        {
            // No final answer (the round table was skipped): the question they carried in, never a blank stone.
            var answer = !string.IsNullOrWhiteSpace(rec.FinalAnswer.Final) ? rec.FinalAnswer.Final.Trim()
                       : !string.IsNullOrWhiteSpace(rec.Question) ? rec.Question.Trim()
                       : sample ? "A life not wasted is one where I know why I keep walking" : null;
            if (answer == null) return;
            // Engraved on the stone's face toward the visitor (-Z: the stone faces the spawn).
            var face = new GameObject("Engraving").AddComponent<TextMeshPro>();
            face.transform.SetParent(stone, false);
            face.transform.localPosition = new Vector3(0f, 0f, -0.51f);
            face.transform.localRotation = Quaternion.identity;
            // In metres: undo the stone's own scale. (TMP's world size is ~0.1 m of line per fontSize unit:
            // at a tenth of this scale the first engraving came out 3 cm tall.)
            face.transform.localScale = new Vector3(1f / stone.localScale.x, 1f / stone.localScale.y, 1f / stone.localScale.z);
            face.rectTransform.sizeDelta = new Vector2(stone.lossyScale.x * 0.88f, stone.lossyScale.y * 0.6f);
            face.text = "“" + answer + "”";
            face.fontSize = 0.9f; face.enableAutoSizing = true; face.fontSizeMin = 0.3f; face.fontSizeMax = 0.85f;   // read from 2 m: 0.6 came out ~2 cm and the review could not read it
            face.alignment = TextAlignmentOptions.Center; face.color = new Color32(0x2a, 0x22, 0x1a, 0xff); face.fontStyle = FontStyles.Bold;
            var fonts = MuseFonts.Get(); if (fonts != null && fonts.display != null) face.font = fonts.display;
            _chimes.Add((stone.position, Chime(523f, 3.5f, 1.8f, 0.5f), false));
        }

        void Memento(Transform anchor, JourneyRecord rec, bool sample, System.Func<string, string> titleOf)
        {
            // Her memento card (4.5), floating 1.4 m over the stone, toward the visitor.
            var title = !string.IsNullOrWhiteSpace(rec.WorldTitle) ? rec.WorldTitle.Trim() : sample ? "The Garden of the Slow Walker" : null;
            var question = !string.IsNullOrWhiteSpace(rec.Question) ? rec.Question.Trim() : sample ? "What makes a life not wasted?" : null;
            var answer = !string.IsNullOrWhiteSpace(rec.FinalAnswer.Final) ? rec.FinalAnswer.Final.Trim() : sample ? "A life not wasted is one where I know why I keep walking" : null;
            var lines = ChoiceLines(rec, titleOf);
            if (sample && lines.Count == 0)
            {
                lines.Add(("palace", "Turtle · facing east · “Because it is slow, but it keeps going”"));
                lines.Add(("grotto", "Lamp on the detail")); lines.Add(("vangogh", "Cobalt stroke · The Bedroom")); lines.Add(("monet", "Dusk · Water Lilies"));
            }
            var companions = new List<string>();
            foreach (var id in rec.Companions.Count > 0 ? rec.Companions : Masters.Company) companions.Add(ShortName(id));

            var card = new GameObject("Memento").transform;
            card.SetParent(anchor, false);
            card.localPosition = Vector3.zero; card.localRotation = Quaternion.identity;   // the anchor faces the spawn: +Z away from the visitor reads
            var c = MuseUi.Canvas(card, "Memento", 3.5f, 420f);
            // Shown once the chapter's question has gone: from the arrival the question hangs 3.2 m ahead
            // and the memento 6 m ahead at nearly the same height, so both up at once overlapped (live run, 4 Oct).
            _memento = c.gameObject.AddComponent<CanvasGroup>(); _memento.alpha = 0f;
            var glass = MuseUi.Card(c, MuseTheme.Paper, MuseTheme.OptionRadius, MuseTheme.Line, 1f, padX: 22f, padY: 18f, gap: 6f, name: "Card");
            MuseUi.Text(glass, "MUSE∞  ·  Memento" + (sample ? "  ·  sample" : ""), MuseUi.Face.Sans, 9f, MuseTheme.GoldInk, 0.24f, true, name: "Eyebrow");
            // No title when the roundtable failed: her rule, keep the generic ending rather than pretend.
            var t = MuseUi.Text(glass, title ?? "Your world", MuseUi.Face.Serif, 24f, MuseTheme.Ink, name: "Title");
            var fonts = MuseFonts.Get(); if (fonts != null && fonts.display != null) t.font = fonts.display;
            if (question != null) MuseUi.Text(glass, "“" + question + "”", MuseUi.Face.Serif, 13f, MuseTheme.Ink2, name: "Question");
            var letters = "ABCD";
            for (var i = 0; i < lines.Count && i < 4; i++)
                MuseUi.Text(glass, letters[i] + "   " + lines[i].line, MuseUi.Face.Sans, 11.5f, MuseTheme.Ink, name: "Choice " + letters[i]);
            if (answer != null) MuseUi.Text(glass, "“" + answer + "”", MuseUi.Face.Serif, 14f, MuseTheme.Ink, name: "Answer");
            MuseUi.Text(glass, "With " + string.Join(" · ", companions) + " (AI interpretations)", MuseUi.Face.Sans, 9.5f, MuseTheme.Ink3, name: "Company");
            if (title == null) MuseUi.Text(glass, "The roundtable did not answer: a generic ending, not a generated one.", MuseUi.Face.Sans, 9f, MuseTheme.Ink3, name: "Notice");
        }

        static string ShortName(string id) => id switch
        {
            Masters.Monet => "Monet", Masters.VanGogh => "Van Gogh", Masters.Socrates => "Socrates", Masters.Frida => "Frida",
            Masters.Picasso => "Picasso", Masters.Hilma => "Hilma", Masters.Morisot => "Morisot", _ => Masters.Name(id),
        };

        void Plate(Vector3 at, string title, string sub)
        {
            var eye = Camera.main != null ? Camera.main.transform.position : transform.position;
            var anchor = new GameObject("Plate " + title).transform;
            anchor.SetParent(transform, true);
            var away = at - eye; away.y = 0f;
            anchor.SetPositionAndRotation(at, Quaternion.LookRotation(away.sqrMagnitude > 1e-4f ? away.normalized : Vector3.forward, Vector3.up));
            var c = MuseUi.Canvas(anchor, "Plate", 2f, 220f);
            var card = MuseUi.Card(c, MuseTheme.Paper, MuseTheme.OptionRadius, MuseTheme.Line, 1f, padX: 10f, padY: 7f, gap: 2f, name: "Plate");
            card.GetComponent<UnityEngine.UI.VerticalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;
            var a = MuseUi.Text(card, title, MuseUi.Face.SansSemi, 11f, MuseTheme.Ink, name: "Title"); a.alignment = TextAlignmentOptions.Center;
            if (!string.IsNullOrEmpty(sub)) { var b = MuseUi.Text(card, sub, MuseUi.Face.Sans, 9.5f, MuseTheme.Ink3, name: "Sub"); b.alignment = TextAlignmentOptions.Center; }
        }

        /// <summary>A plate's place over a piece on its plinth: clear of the piece's top, so the piece never stands in front of its own words.</summary>
        static Vector3 Above(Transform plinth, Transform piece)
        {
            var top = plinth.position.y + 1.0f;
            if (piece != null) { var b = Bounds(piece.gameObject); if (b.size.sqrMagnitude > 0f) top = Mathf.Max(top, b.max.y); }
            // Out toward its wall: straight above, the lamp's plate ran into the memento's edge from the arrival (review, 4 Oct).
            var x = plinth.position.x + Mathf.Sign(plinth.position.x == 0f ? 1f : plinth.position.x) * PlateOutward;
            return new Vector3(x, top + PlateClearance, plinth.position.z);
        }

        bool QuestionGone()
        {
            _waited += Time.deltaTime;   // never held for good: a question that never shows must not keep the memento away
            if (_waited > ChapterQuestion.ShowSeconds + ChapterQuestion.FadeSeconds + 4f) return true;
            // Any question still up (a chapter's frame is gone by the time its visitor is here, so this is Your world's).
            foreach (var q in FindObjectsByType<ChapterQuestion>(FindObjectsSortMode.None)) if (!q.Done) return false;
            return true;
        }

        static void Hide(Transform t) { if (t != null) t.gameObject.SetActive(false); }

        static Bounds Bounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b;
        }

        Dictionary<string, string> Titles()
        {
            var d = new Dictionary<string, string>();
            if (artworksJson == null) return d;
            foreach (var w in ArtworkCatalog.All(ArtworkCatalog.Parse(artworksJson.text))) d[w.id] = w.title;
            return d;
        }

        Texture2D Image(string id)
        {
            if (artworkImages == null) return null;
            foreach (var t in artworkImages) if (t != null && t.name == id) return t;
            return null;
        }

        /// <summary>Her chapter chimes, synthesised: a decaying tone with one overtone.</summary>
        public static AudioClip Chime(float hz, float decay, float seconds, float overtone)
        {
            const int rate = 44100;
            var n = (int)(rate * seconds);
            var data = new float[n];
            for (var i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float env = Mathf.Exp(-decay * t) * Mathf.Clamp01(t * 300f);
                data[i] = env * (0.6f * Mathf.Sin(2f * Mathf.PI * hz * t) + overtone * 0.4f * Mathf.Sin(2f * Mathf.PI * hz * 2.76f * t));
            }
            var clip = AudioClip.Create("chime-" + hz, n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
