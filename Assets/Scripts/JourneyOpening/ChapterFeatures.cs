using System.Collections;
using System.Collections.Generic;
using GaussianSplatting.Runtime;
using MusePico.Dialogue;
using MusePico.Tripo;
using MuseXR.Interaction;
using MuseXR.Slots;
using MuseXR.UI;
using MuseXR.Worlds;
using TMPro;
using UnityEngine;
using InputAction = UnityEngine.InputSystem.InputAction;
using InputActionType = UnityEngine.InputSystem.InputActionType;

namespace MuseXR.Journey
{
    /// <summary>
    /// The Van Gogh and Monet chapters' own features from her updated design (MUSE-VR-design, 2 Oct 2026),
    /// added to GateWorld's chapter frames at runtime, so no scene file is edited (the frames are shared
    /// with the musexr-b agent's work). Each frame gets a child "Chapter Features ..." - the "Chapter "
    /// prefix means ChapterLink shows it through the gate before arrival, like the layout.
    /// Everything is built in the frame's own space: a chapter is laid out at the origin, as in its scene.
    /// </summary>
    public sealed class ChapterFeatures : MonoBehaviour
    {
        void Start()
        {
            Attach<VanGoghFeatures>("Van Gogh Frame", "Chapter Features VanGogh");
            Attach<MonetFeatures>("Monet Frame", "Chapter Features Monet");
        }

        static void Attach<T>(string frameName, string childName) where T : Component
        {
            var frame = FindRoot(frameName);
            if (frame == null) { Debug.LogWarning("[Chapters] no " + frameName); return; }
            if (frame.Find(childName) != null) return;
            var go = new GameObject(childName);
            go.transform.SetParent(frame, false);
            go.AddComponent<T>();
        }

        internal static Transform FindRoot(string name)
        {
            foreach (var r in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                if (r.name == name) return r.transform;
            return null;
        }

        internal static Transform FindDeep(Transform root, string startsWith)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name.StartsWith(startsWith)) return t;
            return null;
        }

        // ---- shared builders -----------------------------------------------------------------------------

        internal static Material Unlit(Color c, Texture2D tex = null, bool twoSided = false)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            m.SetColor("_BaseColor", c);
            if (tex != null) m.SetTexture("_BaseMap", tex);
            if (twoSided) m.SetFloat("_Cull", 0f);
            return m;
        }

        internal static Material Lit(Color c, float metal = 0f, float smooth = 0.4f)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", c); m.SetFloat("_Metallic", metal); m.SetFloat("_Smoothness", smooth);
            return m;
        }

        internal static Material Glow(Color c)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 2f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_ZWrite", 0f); m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            m.SetFloat("_Cull", 0f);
            return m;
        }

        /// <summary>A Quad at a local pose: a Quad shows its face along its -Z, so +Z points away from the viewer.</summary>
        internal static Transform Quad(Transform parent, string name, Vector3 pos, Quaternion rot, Vector2 size, Material m, bool collider = false)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = name;
            if (!collider) Destroy(q.GetComponent<Collider>());
            q.transform.SetParent(parent, false);
            q.transform.localPosition = pos; q.transform.localRotation = rot;
            q.transform.localScale = new Vector3(size.x, size.y, 1f);
            q.GetComponent<Renderer>().sharedMaterial = m;
            return q.transform;
        }

        internal static Transform Part(Transform parent, PrimitiveType type, string name, Vector3 pos, Vector3 scale, Material m, Quaternion? rot = null)
        {
            var g = GameObject.CreatePrimitive(type);
            g.name = name; Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos; g.transform.localScale = scale;
            if (rot.HasValue) g.transform.localRotation = rot.Value;
            g.GetComponent<Renderer>().sharedMaterial = m;
            return g.transform;
        }

        /// <summary>Her museum label: dark ink on a pale card, facing along <paramref name="rot"/>.</summary>
        internal static TextMeshPro Label(Transform parent, Vector3 pos, Quaternion rot, string text, float width, float size = 0.9f)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos; go.transform.localRotation = rot;
            Quad(go.transform, "Card", new Vector3(0f, 0f, 0.005f), Quaternion.identity, new Vector2(width + 0.12f, size * 0.45f), Unlit(new Color(0.96f, 0.94f, 0.9f)));
            var t = go.AddComponent<TextMeshPro>();
            t.text = text; t.fontSize = size; t.alignment = TextAlignmentOptions.Center;
            t.color = new Color(0.2f, 0.17f, 0.14f);
            t.rectTransform.sizeDelta = new Vector2(width, size * 0.42f);
            return t;
        }

        /// <summary>
        /// The chapter's three companions as a crowd, standing on its layout's marks. The journey's chosen
        /// company speaks on those marks in order (the Palace's rule); figures follow the marks' models.
        /// </summary>
        internal static CompanionGroup Crowd(Transform layout, Transform parent)
        {
            var figures = new Dictionary<string, Transform>();
            var order = new List<string>();
            var company = Masters.Company;
            for (var i = 0; i < Masters.DefaultTrio.Count && i < company.Count; i++)
            {
                var mark = FindDeep(layout, "Mark " + Masters.DefaultTrio[i]);
                if (mark == null) continue;
                figures[company[i]] = mark; order.Add(company[i]);
            }
            if (order.Count == 0) return null;
            var go = new GameObject("Companions");
            go.transform.SetParent(parent, false);
            var g = go.AddComponent<CompanionGroup>();
            g.FollowVisitor = false;
            g.Crowd = true;
            g.Head = Camera.main != null ? Camera.main.transform : null;
            g.Set(order, figures);
            go.AddComponent<SubtitleRig>().Group = g;
            return g;
        }

        /// <summary>Her per-chapter confirm sound (bronze bell, stone chime, wood, water), played where it happens.</summary>
        internal static void Chime(ChapterSound sound, Vector3 at)
        {
            var data = ChimeSynth.Render(sound);
            var clip = AudioClip.Create("chime-" + sound, data.Length, 1, ChimeSynth.SampleRate, false);
            clip.SetData(data, 0);
            AudioSource.PlayClipAtPoint(clip, at, 0.9f);
        }

        /// <summary>The voice of the scene's MuseumDialogue, if any: speaks a line in a master's voice.</summary>
        internal static void Voice(MonoBehaviour host, string masterId, string line)
        {
            var dialogue = FindAnyObjectByType<MuseumDialogue>();
            if (dialogue != null && dialogue.HasVoice) host.StartCoroutine(Play(dialogue, masterId, line));
        }

        static IEnumerator Play(MuseumDialogue dialogue, string masterId, string line)
        {
            var roster = masterId == "frida_kahlo" ? "frida" : masterId == "hilma_af_klint" ? "hilma" : masterId == "berthe_morisot" ? "morisot" : masterId;
            var task = dialogue.VoiceAsync(roster, line);
            for (float t = 0f; !task.IsCompleted && t < 10f; t += Time.deltaTime) yield return null;
            var clip = task.IsCompleted && !task.IsFaulted ? task.Result : null;
            if (clip == null) yield break;
            var cam = Camera.main;
            if (cam == null) yield break;
            AudioSource.PlayClipAtPoint(clip, cam.transform.position + cam.transform.right * 0.6f, 0.9f);
        }
    }

    // =====================================================================================================
    // Van Gogh · Studio of the Burning Sky
    // =====================================================================================================

    /// <summary>
    /// Her chapter C hero works and its one action:
    ///   The Starry Night across the corridor ceiling, drifting slowly ("Motion is an interpretation");
    ///   The Bedroom enlarged on the corridor's end wall;
    ///   the easel: touch a pot for a colour, hold the trigger and paint one stroke in the air, A keeps it
    ///   (B redraws); kept, it grows up to the ceiling and curves to the side door as a ribbon of light.
    /// Saved as vangogh{color, artworkId, points} in the shared journey record.
    /// </summary>
    public sealed class VanGoghFeatures : MonoBehaviour, IConfirmable
    {
        // Corridor in the chapter's space (probe captures, 4 Oct): the walk runs along -Z from the origin,
        // the hung works on the +x wall, a gold-draped ceiling about 3.5 m up, the side door at (3.1, -5.3).
        public static readonly Vector3 CeilingCentre = new Vector3(0.7f, 3.25f, -7.5f);
        public static readonly Vector2 CeilingSize = new Vector2(20f, 6.5f);   // along the corridor, across it
        public static readonly Vector3 BedroomAt = new Vector3(0.6f, 0f, -17.5f);
        // Her 6 m does not fit under the 3.25 m ceiling: 3.6 m wide keeps the whole picture on the end wall.
        public const float BedroomWidth = 3.6f, ReliefDepth = 0.2f;

        // Her three replies (the repo's artworkChoices: perception, emotion, invention) and who answers each.
        static readonly string[] Replies =
        {
            "It changes how my eyes work. I will see differently when I leave.",
            "It makes me feel something I don't have words for yet.",
            "It shows me a world that never existed\u2014until someone made it.",
        };
        static readonly string[] ReplyLines =
        {
            "Then keep looking after you leave. The light will go on changing what you saw here.",
            "Good. Do not tame it. A feeling with no name is the most honest visitor you will ever receive.",
            "Then ask who made it, and what it cost them to make what had never been.",
        };
        static readonly string[] ReplyBy = { Masters.Monet, Masters.VanGogh, Masters.Socrates };
        static readonly Dictionary<string, string> WorkIds = new Dictionary<string, string>
        {
            { "Bedroom", "aic-28560" }, { "Self-Portrait", "aic-80607" }, { "Poet's Garden", "aic-14586" }, { "Peasant Woman", "aic-28862" },
        };

        static readonly (string name, Color colour)[] Pots =
        {
            ("Cobalt", new Color32(0x2f, 0x4f, 0x8f, 0xff)),
            ("Chrome yellow", new Color32(0xe3, 0xb3, 0x3a, 0xff)),
            ("Cypress green", new Color32(0x3f, 0x5f, 0x2f, 0xff)),
        };

        Transform _layout, _sky, _easel, _strokeRoot;
        Material _skyMat;
        int _colour = -1;
        readonly List<Renderer> _potRenderers = new List<Renderer>();
        LineRenderer _stroke;
        readonly List<Vector3> _points = new List<Vector3>();
        bool _drawing, _awaitingKeep, _kept;
        // Her rule: the easel lights once a painting has been looked at and the companions heard.
        bool _unlocked, _repliesShown, _groupHooked;
        string _artworkId = "aic-28560";
        GameObject _replies;
        Renderer _easelCanvas;
        TextMeshPro _prompt;
        CompanionGroup _group;
        Vector3 _exit = new Vector3(3.1f, 0f, -5.3f);

        void Start()
        {
            _layout = transform.parent.Find("Chapter VanGogh") ?? transform.parent;
            var exit = ChapterFeatures.FindDeep(_layout, "Exit");
            if (exit != null) _exit = transform.parent.InverseTransformPoint(exit.position);
            BuildSky();
            BuildBedroom();
            BuildEasel();
        }

        /// <summary>The frame stands at the origin once the visitor has arrived (ChapterLink); before that
        /// it is only seen through the gate, and the companions are still walking in the previous chapter.</summary>
        bool Arrived => transform.parent.position.sqrMagnitude < 0.01f && transform.parent.rotation == Quaternion.identity;

        void BuildSky()
        {
            var tex = Resources.Load<Texture2D>("Heroes/starry-night");
            if (tex == null) return;
            _skyMat = ChapterFeatures.Unlit(Color.white, tex, twoSided: true);
            // The quad's +Z points up (its face looks down at the visitor) and its X runs along the corridor
            // (the chapter's Z), so the painting's width lies along her 20 m ceiling.
            var rot = Quaternion.LookRotation(Vector3.up, Vector3.right);
            _sky = ChapterFeatures.Quad(transform, "Hero · The Starry Night", CeilingCentre, rot, CeilingSize, _skyMat);
            // Show 85% of the width (room to drift) and the matching band of the sky, from the top.
            var aspect = tex.width / (float)tex.height;                       // ~1.24
            const float across = 0.85f;
            var band = Mathf.Clamp01(aspect * across * CeilingSize.y / CeilingSize.x);
            _skyMat.SetTextureScale("_BaseMap", new Vector2(across, band));
            _skyMat.SetTextureOffset("_BaseMap", new Vector2(0f, 1f - band - 0.04f));
            var box = _sky.gameObject.AddComponent<BoxCollider>(); box.size = new Vector3(1f, 1f, 0.02f);
            var r = Replicable.Make(_sky.gameObject, "The Starry Night", "vangogh", StarDisc(tex));
            r.replicaName = "A small disc of slowly turning stars";
            CompassTarget.Add(_sky.gameObject, 26, "The Starry Night", "Look up · hold the trigger to replicate");
            ChapterFeatures.Label(transform, new Vector3(-1.9f, 1.55f, -2.2f), Quaternion.LookRotation(Vector3.left),
                "<b>The Starry Night</b>  ·  Vincent van Gogh  ·  1889  ·  MoMA\n<size=70%>Across a 20 m ceiling  ·  motion is an interpretation, original 74 × 92 cm</size>", 2.2f, 0.7f);
        }

        void Update()
        {
            // "The swirling sky drifts slowly overhead."
            if (_skyMat != null)
            {
                var o = _skyMat.GetTextureOffset("_BaseMap");
                var scale = _skyMat.GetTextureScale("_BaseMap");
                o.x = (1f - scale.x) * (0.5f + 0.5f * Mathf.Sin(Time.time * 0.03f));
                _skyMat.SetTextureOffset("_BaseMap", o);
            }
            if (_group == null && _layout != null && Arrived) _group = ChapterFeatures.Crowd(_layout, transform);
            if (_group != null && !_groupHooked) { _group.TurnsFinished += OnTurnsFinished; _groupHooked = true; }
            if (_easel == null || _kept || !_unlocked) return;
            UpdatePots();
            UpdateDrawing();
        }

        /// <summary>The replica: a small disc of the sky that turns.</summary>
        GameObject StarDisc(Texture2D tex)
        {
            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = "Star disc";
            Destroy(disc.GetComponent<Collider>());
            disc.transform.SetParent(transform, false);
            disc.transform.localScale = new Vector3(0.2f, 0.01f, 0.2f);
            disc.GetComponent<Renderer>().sharedMaterial = ChapterFeatures.Unlit(Color.white, tex, true);
            disc.SetActive(false);
            return disc;
        }

        /// <summary>
        /// Her "The Bedroom · 6 m impasto relief": the end wall, the paint standing off it. The depth comes from
        /// the picture's own light and dark (her doc says so, and so does the label), not from the original.
        /// </summary>
        void BuildBedroom()
        {
            var tex = Resources.Load<Texture2D>("Heroes/bedroom");
            if (tex == null) return;
            var h = BedroomWidth * tex.height / tex.width;
            var root = new GameObject("Hero · The Bedroom").transform;
            root.SetParent(transform, false);
            root.localPosition = BedroomAt;
            root.localRotation = Quaternion.LookRotation(Vector3.back);   // faces the visitor walking down -Z: +Z away
            var relief = new GameObject("Relief").transform;
            relief.SetParent(root, false);
            relief.localPosition = new Vector3(0f, 0.15f + h / 2f, 0f);
            relief.gameObject.AddComponent<MeshFilter>().sharedMesh = ReliefFrom(tex, BedroomWidth, h, ReliefDepth);
            var m = ChapterFeatures.Lit(Color.white, 0f, 0.35f);
            m.SetTexture("_BaseMap", tex);
            relief.gameObject.AddComponent<MeshRenderer>().sharedMaterial = m;
            var lamp = new GameObject("Raking light").AddComponent<Light>();   // a light from the side brings the ridges out
            lamp.transform.SetParent(root, false); lamp.transform.localPosition = new Vector3(-BedroomWidth * 0.7f, h + 0.3f, -1.2f);
            lamp.type = LightType.Point; lamp.range = 7f; lamp.intensity = 2.2f; lamp.color = new Color(1f, 0.92f, 0.8f);
            ChapterFeatures.Label(root, new Vector3(0f, 0.08f, -0.25f), Quaternion.identity,
                "<b>The Bedroom</b>  ·  Vincent van Gogh  ·  1889  ·  Art Institute of Chicago\n<size=70%>Relief derived from the picture's light and dark, not the original's paint</size>", BedroomWidth, 0.6f);
        }

        /// <summary>A grid the size of the picture whose vertices stand out toward the viewer by its brightness.</summary>
        static Mesh ReliefFrom(Texture2D tex, float width, float height, float depth)
        {
            const int nx = 120;
            var ny = Mathf.Max(8, Mathf.RoundToInt(nx * height / width));
            // Read it small through a RenderTexture: no need for the image to be CPU-readable, and the blit blurs it.
            var rt = RenderTexture.GetTemporary(nx + 1, ny + 1, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(tex, rt);
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var small = new Texture2D(nx + 1, ny + 1, TextureFormat.RGBA32, false);
            small.ReadPixels(new Rect(0, 0, nx + 1, ny + 1), 0, 0); small.Apply();
            RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
            var px = small.GetPixels();
            Destroy(small);
            var verts = new Vector3[(nx + 1) * (ny + 1)]; var uvs = new Vector2[verts.Length];
            for (var y = 0; y <= ny; y++)
            for (var x = 0; x <= nx; x++)
            {
                var c = px[y * (nx + 1) + x];
                var lum = 0.3f * c.r + 0.59f * c.g + 0.11f * c.b;
                float u = x / (float)nx, v = y / (float)ny;
                var edge = Mathf.Clamp01(Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v)) * 30f);   // flat at the rim
                verts[y * (nx + 1) + x] = new Vector3((u - 0.5f) * width, (v - 0.5f) * height, -lum * depth * edge);   // toward the viewer: -Z
                uvs[y * (nx + 1) + x] = new Vector2(u, v);
            }
            var tris = new int[nx * ny * 6]; var t = 0;
            for (var y = 0; y < ny; y++)
            for (var x = 0; x < nx; x++)
            {
                int a = y * (nx + 1) + x, b = a + 1, c = a + nx + 1, d = c + 1;
                tris[t++] = a; tris[t++] = c; tris[t++] = b; tris[t++] = b; tris[t++] = c; tris[t++] = d;
            }
            var mesh = new Mesh { name = "Bedroom relief", vertices = verts, uv = uvs, triangles = tris };
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        // ---- the easel and the stroke ----------------------------------------------------------------------

        void BuildEasel()
        {
            var mark = ChapterFeatures.FindDeep(_layout, "Interaction Easel");
            var at = mark != null ? transform.parent.InverseTransformPoint(mark.position) : new Vector3(-0.6f, 0.1f, -4.2f);
            _easel = new GameObject("Easel · colour · stroke").transform;
            _easel.SetParent(transform, false);
            _easel.localPosition = new Vector3(at.x, 0f, at.z);
            var toWalk = new Vector3(0.7f, 0f, at.z + 2.2f) - _easel.localPosition; toWalk.y = 0f;
            _easel.localRotation = Quaternion.LookRotation(-toWalk.normalized);   // +Z away from someone on the walk

            var wood = ChapterFeatures.Lit(new Color(0.42f, 0.3f, 0.2f), 0f, 0.25f);
            ChapterFeatures.Part(_easel, PrimitiveType.Cube, "Leg", new Vector3(-0.3f, 0.85f, 0f), new Vector3(0.04f, 1.7f, 0.04f), wood, Quaternion.Euler(0f, 0f, -6f));
            ChapterFeatures.Part(_easel, PrimitiveType.Cube, "Leg", new Vector3(0.3f, 0.85f, 0f), new Vector3(0.04f, 1.7f, 0.04f), wood, Quaternion.Euler(0f, 0f, 6f));
            ChapterFeatures.Part(_easel, PrimitiveType.Cube, "Leg", new Vector3(0f, 0.8f, 0.3f), new Vector3(0.04f, 1.65f, 0.04f), wood, Quaternion.Euler(14f, 0f, 0f));
            _easelCanvas = ChapterFeatures.Part(_easel, PrimitiveType.Cube, "Canvas", new Vector3(0f, 1.25f, -0.03f), new Vector3(0.7f, 0.55f, 0.02f), ChapterFeatures.Lit(new Color(0.42f, 0.4f, 0.37f))).GetComponent<Renderer>();
            ChapterFeatures.Part(_easel, PrimitiveType.Cube, "Shelf", new Vector3(0f, 0.95f, -0.2f), new Vector3(0.75f, 0.03f, 0.18f), wood);

            // Three pots on the shelf: touch one with the right hand, or point and pull the trigger.
            for (var i = 0; i < Pots.Length; i++)
            {
                var pot = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                pot.name = "Pot " + Pots[i].name;
                pot.transform.SetParent(_easel, false);
                pot.transform.localPosition = new Vector3(-0.24f + i * 0.24f, 1.01f, -0.2f);
                pot.transform.localScale = new Vector3(0.09f, 0.05f, 0.09f);
                Destroy(pot.GetComponent<Collider>());
                var r = pot.GetComponent<Renderer>(); r.sharedMaterial = ChapterFeatures.Lit(Pots[i].colour, 0f, 0.6f);
                _potRenderers.Add(r);
                var box = pot.AddComponent<BoxCollider>(); box.size = new Vector3(1.6f, 3f, 1.6f); box.isTrigger = true;
                var index = i;
                Pointable.Make(pot, "pot " + Pots[i].name).Selected += (_, __) => PickColour(index);
            }

            var promptAt = new GameObject("Prompt").transform;
            promptAt.SetParent(_easel, false);
            promptAt.localPosition = new Vector3(0f, 2.05f, -0.05f);
            _prompt = promptAt.gameObject.AddComponent<TextMeshPro>();
            _prompt.fontSize = 0.9f; _prompt.alignment = TextAlignmentOptions.Center; _prompt.color = new Color(0.98f, 0.95f, 0.86f);
            _prompt.rectTransform.sizeDelta = new Vector2(1.8f, 0.4f);
            _prompt.text = "Look at a painting and hear the companions first";
            _prompt.color = new Color(0.8f, 0.78f, 0.72f);
            // Until then the compass leads to the paintings (each hung work is already a target).

            _strokeRoot = new GameObject("Stroke").transform;
            _strokeRoot.SetParent(transform, false);
        }

        /// <summary>A round of the companions has finished: if it was about a painting the visitor stands at,
        /// her question follows - "What is this painting to you?" with three replies.</summary>
        void OnTurnsFinished()
        {
            if (_repliesShown || _kept) return;
            var cam = Camera.main;
            if (cam == null) return;
            Transform nearest = null; var best = 4.5f;
            foreach (var t in transform.parent.GetComponentsInChildren<Transform>())
            {
                if (!t.name.StartsWith("Work ")) continue;
                var d = Vector3.Distance(cam.transform.position, t.position);
                if (d < best) { best = d; nearest = t; }
            }
            if (nearest != null) ShowReplies(nearest);
        }

        void ShowReplies(Transform work)
        {
            _repliesShown = true;
            var title = work.name.Substring(5);
            foreach (var kv in WorkIds) if (title.Contains(kv.Key)) _artworkId = kv.Value;
            var cam = Camera.main.transform;
            var toEye = cam.position - work.position; toEye.y = 0f; toEye.Normalize();
            _replies = new GameObject("What is this painting to you").gameObject;
            _replies.transform.SetParent(transform, true);
            var at = work.position + toEye * 1.1f; at.y = transform.parent.position.y + 1.45f;
            _replies.transform.SetPositionAndRotation(at, Quaternion.LookRotation(-toEye, Vector3.up));
            var q = _replies.AddComponent<TextMeshPro>();
            q.text = "What is this painting to you?"; q.fontSize = 0.75f; q.alignment = TextAlignmentOptions.Center;
            q.color = new Color(0.98f, 0.95f, 0.88f); q.rectTransform.sizeDelta = new Vector2(2f, 0.25f);
            for (var i = 0; i < Replies.Length; i++)
            {
                var chip = new GameObject("Reply " + (i + 1)).transform;
                chip.SetParent(_replies.transform, false);
                chip.localPosition = new Vector3(0f, -0.24f - i * 0.2f, 0f);
                ChapterFeatures.Quad(chip, "Back", new Vector3(0f, 0f, 0.004f), Quaternion.identity, new Vector2(1.7f, 0.17f), ChapterFeatures.Unlit(new Color(0.08f, 0.07f, 0.09f)));
                var t = chip.gameObject.AddComponent<TextMeshPro>();
                t.text = "0" + (i + 1) + "   " + Replies[i]; t.fontSize = 0.42f; t.alignment = TextAlignmentOptions.MidlineLeft;
                t.color = new Color(0.95f, 0.92f, 0.86f); t.rectTransform.sizeDelta = new Vector2(1.6f, 0.16f);
                var box = chip.gameObject.AddComponent<BoxCollider>(); box.size = new Vector3(1.7f, 0.17f, 0.04f); box.isTrigger = true;
                var index = i;
                Pointable.Make(chip.gameObject, "reply " + i).Selected += (_, __) => Reply(index);
            }
        }

        void Reply(int i)
        {
            if (_unlocked) return;
            if (_replies != null) Destroy(_replies);
            var who = System.Linq.Enumerable.Contains(Masters.Company, ReplyBy[i]) ? ReplyBy[i] : (Masters.Company.Count > 0 ? Masters.Company[0] : ReplyBy[i]);
            if (_group != null) _group.Say(who, ReplyLines[i]);
            ChapterFeatures.Voice(this, who, ReplyLines[i]);
            Unlock();
        }

        /// <summary>The easel lights: three pots of paint, and the compass now leads to it.</summary>
        void Unlock()
        {
            _unlocked = true;
            if (_easelCanvas != null) _easelCanvas.sharedMaterial = ChapterFeatures.Lit(new Color(0.93f, 0.9f, 0.84f));
            _prompt.text = "The easel is lit. Touch a pot to pick a colour";
            _prompt.color = new Color(0.98f, 0.95f, 0.86f);
            var light = new GameObject("Easel light").AddComponent<Light>();
            light.transform.SetParent(_easel, false); light.transform.localPosition = new Vector3(0f, 1.9f, -0.6f);
            light.type = LightType.Point; light.range = 2.5f; light.intensity = 1.6f; light.color = new Color(1f, 0.85f, 0.6f);
            CompassTarget.Add(_easel.gameObject, 25, "The easel", "Pick a colour, paint one stroke");
        }

        void PickColour(int index)
        {
            if (_kept || _awaitingKeep || !_unlocked) return;
            _colour = index;
            for (var i = 0; i < _potRenderers.Count; i++)
                _potRenderers[i].transform.localScale = i == index ? new Vector3(0.11f, 0.07f, 0.11f) : new Vector3(0.09f, 0.05f, 0.09f);
            _prompt.text = Pots[index].name + ".  Hold the trigger and paint one stroke in the air";
            var ct = _easel.GetComponent<CompassTarget>(); if (ct != null) ct.MarkDone();
        }

        Transform RightAim(out IHandSource source)
        {
            source = null;
            foreach (var p in Pointer.All)
                if (p.Source != null && p.Source.Hand == Hand.Right && p.Source.Aim != null) { source = p.Source; return p.Source.Aim; }
            return null;
        }

        void UpdatePots()
        {
            if (_awaitingKeep) return;
            var aim = RightAim(out _);
            if (aim == null) return;
            for (var i = 0; i < _potRenderers.Count; i++)
                if (Vector3.Distance(aim.position, _potRenderers[i].transform.position) < 0.08f && _colour != i) PickColour(i);
        }

        /// <summary>The brush tip: on a headset the controller itself; at a desk, 1.2 m out along the mouse ray.</summary>
        static Vector3 Tip(Transform aim) =>
            UnityEngine.XR.XRSettings.isDeviceActive ? aim.position + aim.forward * 0.08f : aim.position + aim.forward * 1.2f;

        void UpdateDrawing()
        {
            if (_colour < 0 || _awaitingKeep) return;
            var aim = RightAim(out var source);
            if (aim == null) return;
            var near = Vector3.Distance(new Vector3(Camera.main.transform.position.x, 0f, Camera.main.transform.position.z),
                                        new Vector3(_easel.position.x, 0f, _easel.position.z)) < 3.5f;
            if (source.Trigger && near && !_drawing) BeginStroke();
            if (_drawing)
            {
                if (source.Trigger)
                {
                    var p = Tip(aim);
                    if (_points.Count == 0 || Vector3.Distance(p, _points[_points.Count - 1]) > 0.015f)
                    {
                        if (_points.Count < JourneyRecord.MaxStrokePoints) _points.Add(p);
                        _stroke.positionCount = _points.Count; _stroke.SetPositions(_points.ToArray());
                    }
                }
                else EndStroke();
            }
        }

        void BeginStroke()
        {
            _drawing = true;
            _points.Clear();
            if (_stroke != null) Destroy(_stroke.gameObject);
            var go = new GameObject("Stroke line");
            go.transform.SetParent(_strokeRoot, false);
            _stroke = go.AddComponent<LineRenderer>();
            _stroke.useWorldSpace = true;
            _stroke.widthCurve = new AnimationCurve(new Keyframe(0f, 0.035f), new Keyframe(1f, 0.018f));
            _stroke.numCapVertices = 6; _stroke.numCornerVertices = 4;
            _stroke.material = ChapterFeatures.Unlit(Pots[_colour].colour);
        }

        void EndStroke()
        {
            _drawing = false;
            if (_points.Count < 4) { _prompt.text = "A longer stroke - hold the trigger and draw"; return; }
            _awaitingKeep = true;
            _prompt.text = "A  Keep this stroke      B  Redraw";
            ConfirmInput.Take(this);
        }

        public bool Confirm()
        {
            if (!_awaitingKeep) return false;
            _awaitingKeep = false; _kept = true;
            ConfirmInput.Drop(this);
            var local = new List<float[]>();
            foreach (var p in _points) { var l = transform.parent.InverseTransformPoint(p); local.Add(new[] { l.x, l.y, l.z }); }
            JourneyMemory.Record.SetVanGogh("#" + ColorUtility.ToHtmlStringRGB(Pots[_colour].colour), _artworkId, local);
            ChapterFeatures.Chime(ChapterSound.Wood, _easel.position + Vector3.up);
            JourneyMemory.Record.MarkChapterDone(VrStage.VanGogh);
            _prompt.text = "Saved  ·  " + Pots[_colour].name + " stroke  ·  linked to " + ArtworkTitle(_artworkId);
            StartCoroutine(GrowToDoor());
            var vg = Masters.VanGogh;
            var line = "You pressed hardest at the very start and lighter as you went. Whatever is on your mind was heaviest at the beginning";
            if (_group != null) _group.Say(vg, line);
            ChapterFeatures.Voice(this, vg, line);
            return true;
        }

        public bool Redo()
        {
            if (!_awaitingKeep) return false;
            _awaitingKeep = false;
            ConfirmInput.Drop(this);
            if (_stroke != null) Destroy(_stroke.gameObject);
            _points.Clear();
            _prompt.text = Pots[_colour].name + ".  Hold the trigger and paint one stroke in the air";
            return true;
        }

        static string ArtworkTitle(string id)
        {
            foreach (var kv in WorkIds) if (kv.Value == id) return kv.Key == "Bedroom" ? "The Bedroom" : kv.Key == "Poet's Garden" ? "The Poet's Garden" : kv.Key;
            return "The Bedroom";
        }

        /// <summary>Her answer: the stroke keeps growing up to the ceiling and curves to the side door, a ribbon of light.</summary>
        IEnumerator GrowToDoor()
        {
            var start = _points[_points.Count - 1];
            var top = new Vector3(start.x, transform.parent.TransformPoint(CeilingCentre).y - 0.25f, start.z);
            var door = transform.parent.TransformPoint(_exit + Vector3.up * 2.2f);
            var tail = new List<Vector3>();
            const int n = 48;
            for (var i = 1; i <= n; i++)
            {
                float t = i / (float)n;
                // A quadratic bend: up to the ceiling, then across to the doorway.
                var a = Vector3.Lerp(start, top, t); var b = Vector3.Lerp(top, door, t);
                tail.Add(Vector3.Lerp(a, b, t));
            }
            var glow = ChapterFeatures.Glow(Pots[_colour].colour * 1.6f + new Color(0.25f, 0.2f, 0.1f));
            var ribbon = new GameObject("Ribbon of light").AddComponent<LineRenderer>();
            ribbon.transform.SetParent(_strokeRoot, false);
            ribbon.useWorldSpace = true; ribbon.material = glow;
            ribbon.widthCurve = new AnimationCurve(new Keyframe(0f, 0.03f), new Keyframe(1f, 0.12f));
            ribbon.numCapVertices = 4;
            var pts = new List<Vector3> { start };
            for (float t = 0f; t < 2.5f; t += Time.deltaTime)
            {
                var k = Mathf.Clamp(Mathf.RoundToInt(t / 2.5f * n), 1, n);
                while (pts.Count - 1 < k) pts.Add(tail[pts.Count - 1]);
                ribbon.positionCount = pts.Count; ribbon.SetPositions(pts.ToArray());
                yield return null;
            }
            ribbon.positionCount = n + 1; pts.Clear(); pts.Add(start); pts.AddRange(tail); ribbon.SetPositions(pts.ToArray());
            _prompt.text = "Your stroke leads to the side door";
        }
    }

    // =====================================================================================================
    // Monet · Garden of Water and Light, and the roundtable
    // =====================================================================================================

    /// <summary>
    /// Her chapter D: the Great Wave rising out of the lily pond and Water Lilies standing on the water;
    /// the time ring on its pedestal (grip and turn: Mist, Afternoon, Dusk; the garden shifts over 4 s),
    /// then "which painting did you stop for?"; and the roundtable in the rotunda at the garden's end:
    /// the companions each recall one of the visitor's records, then a draft answer to keep, rewrite
    /// through Socrates' question, or say again.
    /// </summary>
    public sealed class MonetFeatures : MonoBehaviour, IConfirmable
    {
        // The garden in the chapter's space (probe captures, 4 Oct): the lily pond runs along -Z on the low-x
        // side of the curved stone path; the rotunda ("Form my answer") stands near the start at (6.8, -3).
        public static readonly Vector3 WaveAt = new Vector3(0.2f, -0.35f, -11f);
        public const float WaveHeight = 4.6f;
        public static readonly Vector3 LiliesAt = new Vector3(0.6f, 0f, -21f);
        public const float LiliesWidth = 4.4f;

        static readonly string[] Works = { "Water Lilies", "Arrival of the Normandy Train, Gare Saint-Lazare", "Stacks of Wheat (End of Summer)", "Cliff Walk at Pourville" };
        static readonly string[] WorkIds = { "aic-16568", "aic-16571", "aic-64818", "aic-14620" };

        Transform _layout, _rotunda;
        TimeRingDriver _driver;
        TimeRingDial _dial;
        TimeOfDay _time = TimeOfDay.Afternoon;
        bool _turned, _picked, _tableStarted, _tableDone;
        GameObject _chips;
        TextMeshPro _tableSign;
        CompanionGroup _group;
        Vector3 _rotundaLocal = new Vector3(6.8f, 0f, -3f);
        string _draft = "", _rewrite = "";
        int _answerStage;   // 0 none, 1 draft shown (A keep · X rewrite · Y say), 2 rewrite shown (A use · B back)
        float _undoUntil;   // her 3 s undo after the painting is chosen
        int _pickedWork = -1;
        bool _rewriting;
        InputAction _x, _y;

        void Start()
        {
            _layout = transform.parent.Find("Chapter Monet") ?? transform.parent;
            var exit = ChapterFeatures.FindDeep(_layout, "Exit Choose");
            if (exit != null) _rotundaLocal = transform.parent.InverseTransformPoint(exit.position);
            BuildWave();
            BuildLilies();
            BuildTimeRing();
            BuildRotunda();
            _x = new InputAction("monet-x", InputActionType.Button); UnityEngine.InputSystem.InputActionSetupExtensions.AddBinding(_x, "<XRController>{LeftHand}/primaryButton"); UnityEngine.InputSystem.InputActionSetupExtensions.AddBinding(_x, "<Keyboard>/x"); _x.Enable();
            _y = new InputAction("monet-y", InputActionType.Button); UnityEngine.InputSystem.InputActionSetupExtensions.AddBinding(_y, "<XRController>{LeftHand}/secondaryButton"); UnityEngine.InputSystem.InputActionSetupExtensions.AddBinding(_y, "<Keyboard>/y"); _y.Enable();
        }

        void OnDestroy() { _x?.Dispose(); _y?.Dispose(); }

        bool Arrived => transform.parent.position.sqrMagnitude < 0.01f && transform.parent.rotation == Quaternion.identity;

        void BuildWave()
        {
            var tex = Resources.Load<Texture2D>("Heroes/great-wave");
            if (tex == null) return;
            var w = WaveHeight * tex.width / tex.height;
            var root = new GameObject("Hero · The Great Wave").transform;
            root.SetParent(transform, false);
            root.localPosition = WaveAt;
            root.localRotation = Quaternion.LookRotation(Vector3.left);   // faces the path on the +x side: +Z away from it
            var canvas = ChapterFeatures.Quad(root, "Wave", new Vector3(0f, WaveHeight / 2f, 0f), Quaternion.identity, new Vector2(w, WaveHeight), ChapterFeatures.Unlit(Color.white, tex, true), collider: false);
            var box = canvas.gameObject.AddComponent<BoxCollider>(); box.size = new Vector3(1f, 1f, 0.05f);
            var r = Replicable.Make(canvas.gameObject, "The Great Wave", "monet", SmallWave(tex));
            r.replicaName = "A small wave";
            CompassTarget.Add(canvas.gameObject, 22, "The Great Wave", "Hold the trigger to replicate");
            ChapterFeatures.Label(transform, new Vector3(3.2f, 1.3f, -11f), Quaternion.LookRotation(Vector3.left),
                "<b>The Great Wave off Kanagawa</b>  ·  Hokusai  ·  c. 1830–32  ·  The Met\n<size=70%>Rising from the pond, enlarged about 15×  ·  original 26 × 38 cm</size>", 2.4f, 0.65f);
        }

        GameObject SmallWave(Texture2D tex)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = "Small wave";
            Destroy(q.GetComponent<Collider>());
            q.transform.SetParent(transform, false);
            q.transform.localScale = new Vector3(0.14f, 0.1f, 1f);
            q.GetComponent<Renderer>().sharedMaterial = ChapterFeatures.Unlit(Color.white, tex, true);
            q.SetActive(false);
            return q;
        }

        void BuildLilies()
        {
            var tex = Resources.Load<Texture2D>("Heroes/water-lilies");
            if (tex == null) return;
            var h = LiliesWidth * tex.height / tex.width;
            var root = new GameObject("Hero · Water Lilies").transform;
            root.SetParent(transform, false);
            root.localPosition = LiliesAt;
            root.localRotation = Quaternion.LookRotation(Vector3.back);   // read by a visitor coming down the path from +Z
            ChapterFeatures.Quad(root, "Canvas", new Vector3(0f, 0.15f + h / 2f, 0f), Quaternion.identity, new Vector2(LiliesWidth, h), ChapterFeatures.Unlit(Color.white, tex, true));
            ChapterFeatures.Label(transform, LiliesAt + new Vector3(2.6f, 1.3f, 0f), Quaternion.LookRotation(Vector3.back),
                "<b>Water Lilies</b>  ·  Claude Monet  ·  1906  ·  Art Institute of Chicago\n<size=70%>Standing on the water, enlarged only, nothing changed</size>", 2.2f, 0.6f);
        }

        // ---- the time ring ---------------------------------------------------------------------------------

        void BuildTimeRing()
        {
            var mark = ChapterFeatures.FindDeep(_layout, "Interaction Time-ring");
            var at = mark != null ? transform.parent.InverseTransformPoint(mark.position) : new Vector3(5.2f, 0f, -15f);
            var root = new GameObject("Time ring").transform;
            root.SetParent(transform, false);
            root.localPosition = new Vector3(at.x, 0f, at.z);
            root.localRotation = Quaternion.LookRotation(Vector3.back);   // +Z away from a visitor coming down the path toward -Z
            var stone = ChapterFeatures.Lit(new Color(0.88f, 0.86f, 0.82f), 0f, 0.3f);
            var brass = ChapterFeatures.Lit(new Color(0.83f, 0.66f, 0.32f), 0.85f, 0.65f);
            ChapterFeatures.Part(root, PrimitiveType.Cylinder, "Pedestal", new Vector3(0f, 0.45f, 0f), new Vector3(0.36f, 0.45f, 0.36f), stone);

            var dial = new GameObject("Time Ring Dial");
            dial.transform.SetParent(root, false);
            dial.transform.localPosition = new Vector3(0f, 1.12f, 0f);
            dial.transform.localRotation = Quaternion.Euler(-20f, 0f, 0f);
            var ring = new GameObject("Ring");
            ring.transform.SetParent(dial.transform, false);
            ring.AddComponent<MeshFilter>().sharedMesh = Torus(0.16f, 0.018f);
            ring.AddComponent<MeshRenderer>().sharedMaterial = brass;
            ChapterFeatures.Part(ring.transform, PrimitiveType.Sphere, "Knob", new Vector3(0f, 0.16f, -0.01f), Vector3.one * 0.045f, brass);
            string[] names = { "Mist", "Afternoon", "Dusk" };
            for (var k = 0; k < DialDetents.Count; k++)
            {
                var a = DialDetents.AngleOf(k) * Mathf.Deg2Rad;
                var lab = new GameObject("Mark " + names[k]);
                lab.transform.SetParent(dial.transform, false);
                lab.transform.localPosition = new Vector3(Mathf.Sin(a) * 0.25f, Mathf.Cos(a) * 0.25f, -0.01f);
                var t = lab.AddComponent<TextMeshPro>();
                t.text = names[k]; t.fontSize = 0.45f; t.alignment = TextAlignmentOptions.Center; t.color = new Color(0.3f, 0.24f, 0.16f);
                t.rectTransform.sizeDelta = new Vector2(0.3f, 0.08f);
            }
            var box = dial.AddComponent<BoxCollider>(); box.size = new Vector3(0.45f, 0.45f, 0.1f); box.isTrigger = true;
            ChapterFeatures.Label(root, new Vector3(0f, 1.62f, 0f), Quaternion.identity, "Grip the time ring and turn  ·  Mist  ·  Afternoon  ·  Dusk", 1.4f, 0.55f);

            _driver = new GameObject("Time Ring Driver").AddComponent<TimeRingDriver>();
            _driver.transform.SetParent(root, false);
            _dial = TimeRingDial.Make(dial, ring.transform, _driver);
            _dial.Clicked += OnTime;
            CompassTarget.Add(dial, 21, "The time ring", "Grip it and turn");
        }

        void OnTime(TimeOfDay t)
        {
            _time = t;
            // The world the visitor stands in is the gate's copy now inside this frame: drive that one.
            var worlds = new List<GaussianSplatRenderer>();
            foreach (var r in transform.parent.GetComponentsInChildren<GaussianSplatRenderer>()) worlds.Add(r);
            _driver.worlds = worlds.ToArray();
            var arts = new List<Renderer>();
            foreach (var tr in transform.parent.GetComponentsInChildren<Transform>()) if (tr.name.StartsWith("Work ")) arts.AddRange(tr.GetComponentsInChildren<Renderer>());
            _driver.artworks = arts.ToArray();
            _driver.Choose(t);
            var target = _dial.GetComponent<CompassTarget>(); if (target != null) target.MarkDone();
            if (!_turned)
            {
                _turned = true;
                var line = "The pink on the water has only just come out. In a quarter of an hour it will sink to violet and the edges of the lilies will be gone";
                if (_group != null) _group.Say(Masters.Monet, line);
                ChapterFeatures.Voice(this, Masters.Monet, line);
                StartCoroutine(ShowChips());
            }
        }

        /// <summary>"At this moment, which painting did you stop for?" - her four Monets as chips over the pedestal.</summary>
        IEnumerator ShowChips()
        {
            yield return new WaitForSeconds(2f);
            var root = _dial.transform.parent;
            _chips = new GameObject("Which painting").gameObject;
            _chips.transform.SetParent(root, false);
            _chips.transform.localPosition = new Vector3(0f, 2.15f, 0.1f);
            var q = _chips.AddComponent<TextMeshPro>();
            q.text = "At this moment, which painting did you stop for?"; q.fontSize = 0.8f; q.alignment = TextAlignmentOptions.Center;
            q.color = new Color(0.98f, 0.95f, 0.88f); q.rectTransform.sizeDelta = new Vector2(2.4f, 0.3f);
            for (var i = 0; i < Works.Length; i++)
            {
                var chip = new GameObject("Chip " + Works[i]).transform;
                chip.SetParent(_chips.transform, false);
                chip.localPosition = new Vector3((i - 1.5f) * 0.78f, -0.32f, 0f);
                ChapterFeatures.Quad(chip, "Back", new Vector3(0f, 0f, 0.004f), Quaternion.identity, new Vector2(0.72f, 0.22f), ChapterFeatures.Unlit(new Color(0.08f, 0.07f, 0.09f, 1f)));
                var t = chip.gameObject.AddComponent<TextMeshPro>();
                t.text = Works[i]; t.fontSize = 0.42f; t.alignment = TextAlignmentOptions.Center; t.color = new Color(0.95f, 0.92f, 0.86f);
                t.rectTransform.sizeDelta = new Vector2(0.66f, 0.2f); t.enableWordWrapping = true;
                var box = chip.gameObject.AddComponent<BoxCollider>(); box.size = new Vector3(0.72f, 0.22f, 0.04f); box.isTrigger = true;
                var index = i;
                Pointable.Make(chip.gameObject, "monet work " + i).Selected += (_, __) => PickWork(index);
            }
        }

        void PickWork(int i)
        {
            if (_picked) return;
            _picked = true; _pickedWork = i;
            JourneyMemory.Record.SetMonet(new JourneyRecord.MonetChoice { Preset = _time.ToString().ToLowerInvariant(), ArtworkId = WorkIds[i], Reason = Works[i] });
            if (_chips != null) _chips.SetActive(false);
            ChapterFeatures.Chime(ChapterSound.Water, _dial.transform.position);
            // Her undo: 3 s to take it back with B; A keeps it at once.
            _undoUntil = Time.time + 3f;
            var panel = TorsoPanel.Get();
            if (panel != null) panel.ShowLine(null, "Saved", _time + "  ·  " + Works[i], "B undo  ·  3 s", "Monet garden");
            ConfirmInput.Take(this);
        }

        /// <summary>The undo window has closed (or A kept it): the choice stands and the rotunda calls.</summary>
        void KeepWork()
        {
            _undoUntil = 0f;
            ConfirmInput.Drop(this);
            JourneyMemory.Record.MarkChapterDone(VrStage.Monet);
            if (_chips != null) Destroy(_chips);
            Note("Saved  ·  " + _time + "  ·  " + Works[_pickedWork] + "\nEnd of the garden  ·  Form my answer");
            if (_tableSign != null) _tableSign.text = "Form my answer";
            var ct = _rotunda.GetComponent<CompassTarget>(); if (ct == null) CompassTarget.Add(_rotunda.gameObject, 28, "Form my answer", "The rotunda · they are waiting");
        }

        void UndoWork()
        {
            _undoUntil = 0f; _picked = false; _pickedWork = -1;
            ConfirmInput.Drop(this);
            if (_chips != null) _chips.SetActive(true);
            var panel = TorsoPanel.Get();
            if (panel != null) panel.ClearLine();
        }

        void Note(string text)
        {
            var panel = TorsoPanel.Get();
            if (panel != null) panel.Note("Monet garden", text, 5f);
        }

        // ---- the roundtable --------------------------------------------------------------------------------

        void BuildRotunda()
        {
            _rotunda = new GameObject("Roundtable").transform;
            _rotunda.SetParent(transform, false);
            _rotunda.localPosition = new Vector3(_rotundaLocal.x, 0f, _rotundaLocal.z);
            // A warm glow on the floor and a low round table: her "the rotunda glows warm".
            // Soft: a full-strength additive square washed the whole view brown (Editor capture, 4 Oct).
            var glow = ChapterFeatures.Quad(_rotunda, "Warm glow", new Vector3(0f, 0.03f, 0f), Quaternion.Euler(90f, 0f, 0f), new Vector2(3.4f, 3.4f), ChapterFeatures.Glow(new Color(0.16f, 0.1f, 0.04f)));
            ChapterFeatures.Part(_rotunda, PrimitiveType.Cylinder, "Table", new Vector3(0f, 0.38f, 0f), new Vector3(1.1f, 0.38f, 1.1f), ChapterFeatures.Lit(new Color(0.9f, 0.87f, 0.8f), 0f, 0.4f));
            var sign = new GameObject("Sign").transform;
            sign.SetParent(_rotunda, false);
            sign.localPosition = new Vector3(0f, 2.4f, 0f);
            _tableSign = sign.gameObject.AddComponent<TextMeshPro>();
            _tableSign.text = "Form my answer"; _tableSign.fontSize = 1.6f; _tableSign.alignment = TextAlignmentOptions.Center;
            _tableSign.color = new Color(1f, 0.86f, 0.6f); _tableSign.rectTransform.sizeDelta = new Vector2(3f, 0.5f);
        }

        void Update()
        {
            if (_rotunda == null) return;
            if (_group == null && _layout != null && Arrived) _group = ChapterFeatures.Crowd(_layout, transform);
            var cam = Camera.main;
            if (cam == null) return;
            if (_tableSign != null)
            {
                var away = _tableSign.transform.position - cam.transform.position; away.y = 0f;
                if (away.sqrMagnitude > 1e-4f) _tableSign.transform.rotation = Quaternion.LookRotation(away);
            }
            var flat = new Vector3(cam.transform.position.x, _rotunda.position.y, cam.transform.position.z);
            if (!_tableStarted && Arrived && _undoUntil <= 0f && Vector3.Distance(flat, _rotunda.position) < 2.6f) StartCoroutine(Roundtable());
            if (_undoUntil > 0f && Time.time > _undoUntil) KeepWork();
            if (_answerStage == 1 && _x != null && _x.WasPressedThisFrame()) Rewrite();
            if (_answerStage == 1 && _y != null && _y.WasPressedThisFrame()) SayOwn();
        }

        IEnumerator Roundtable()
        {
            _tableStarted = true;
            var target = _rotunda.GetComponent<CompassTarget>(); if (target != null) target.MarkDone();
            // The companions sit in her 150-degree arc on the far side of the table, facing the visitor.
            if (_group != null)
            {
                _group.Crowd = false;
                var cam = Camera.main.transform;
                var toVisitor = new Vector3(cam.position.x, 0f, cam.position.z) - new Vector3(_rotunda.position.x, 0f, _rotunda.position.z);
                toVisitor = toVisitor.sqrMagnitude > 1e-4f ? toVisitor.normalized : Vector3.back;
                var ids = _group.Ids;
                for (var i = 0; i < ids.Count; i++)
                {
                    var bearing = 180f + (i - (ids.Count - 1) / 2f) * 60f;   // spread across 150 degrees opposite the visitor
                    var dir = Quaternion.Euler(0f, bearing, 0f) * toVisitor;
                    var f = _group.Figures[ids[i]];
                    var at = _rotunda.position + dir * 1.45f; at.y = _rotunda.position.y;
                    f.SetPositionAndRotation(at, Quaternion.LookRotation(-dir, Vector3.up));
                }
            }
            if (_tableSign != null) _tableSign.text = "Roundtable  ·  they look back on your walk";
            Note("Roundtable  ·  they will look back on your walk");

            var result = RoundtableAsk();
            for (float t = 0f; !result.IsCompleted && t < 45f; t += Time.deltaTime) yield return null;
            var lines = new List<KeyValuePair<string, string>>();
            var rt = result.IsCompleted && !result.IsFaulted ? result.Result : null;
            if (result.IsFaulted) Debug.LogWarning("[Roundtable] failed: " + result.Exception);
            else if (rt == null) Debug.LogWarning("[Roundtable] no result (no dialogue, roster or key)");
            else Debug.Log("[Roundtable] live " + rt.Live + " success " + rt.Success + " threads " + (rt.threads != null ? rt.threads.Count : 0) + " error " + rt.Error);
            var company = Masters.Company;
            if (rt != null && rt.Success)
            {
                foreach (var th in rt.threads)
                    foreach (var id in company)
                        if (RosterId(id) == th.speakerId) lines.Add(new KeyValuePair<string, string>(id, th.text));
                _draft = string.IsNullOrWhiteSpace(rt.synthesis) ? LocalDraft() : rt.synthesis;
                if (!string.IsNullOrWhiteSpace(rt.worldTitle)) JourneyMemory.Record.WorldTitle = rt.worldTitle;   // the memento's title
            }
            if (lines.Count == 0) { lines = LocalThreads(); _draft = LocalDraft() + "   (local fallback)"; }
            // A line still running (the time ring's) would make the round refuse to start: wait it out.
            while (_group != null && _group.Busy) yield return null;
            AssignBasedOn(lines);
            if (_group != null) _group.SayInTurn(lines);
            foreach (var kv in lines) ChapterFeatures.Voice(this, kv.Key, kv.Value);
            while (_group != null && _group.Busy) yield return null;
            TorsoPanel.BasedOn.Clear();
            ShowDraft();
        }

        System.Threading.Tasks.Task<RoundtableResult> RoundtableAsk()
        {
            var dialogue = FindAnyObjectByType<MuseumDialogue>();
            if (dialogue == null || dialogue.mastersJson == null) return System.Threading.Tasks.Task.FromResult<RoundtableResult>(null);
            return Ask(dialogue);
        }

        async System.Threading.Tasks.Task<RoundtableResult> Ask(MuseumDialogue dialogue)
        {
            var key = await MusePico.Generation.FallbackKeySource.ForOpenAi().GetKeyAsync();
            if (string.IsNullOrEmpty(key)) return null;
            var roster = MasterRoster.Parse(dialogue.mastersJson.text);
            var ids = new List<string>(); foreach (var id in Masters.Company) ids.Add(RosterId(id));
            var masters = MasterRoster.SelectExactly(roster, ids);
            var session = new VisitSession();
            var rec = JourneyMemory.Record;
            if (!string.IsNullOrEmpty(rec.Question)) session.RecordQuestion(rec.Question);
            if (rec.Palace != null) session.RecordAnswer("the Palace", "kept the " + rec.Palace.Object + (rec.Palace.Reason.Length > 0 ? ", because " + rec.Palace.Reason : ""), "visitor");
            if (rec.Grotto != null) session.RecordAnswer("the Grotto", "set the lamp on the " + rec.Grotto.LampSlot, "visitor");
            if (rec.VanGogh != null) session.RecordAnswer("The Bedroom", "painted one " + rec.VanGogh.Color + " stroke toward the door", "visitor");
            if (rec.Monet != null) { session.RecordArtwork(rec.Monet.Reason, "Claude Monet"); session.RecordAnswer(rec.Monet.Reason, "stopped for it at " + rec.Monet.Preset, "visitor"); }
            var client = new RoundtableClient(new MusePico.Tripo.TripoWebRequestTransport(key, ResponsesCall.DefaultEndpoint), roster);
            return await client.AskAsync(session, masters);
        }

        /// <summary>
        /// Her rule: each turn cites one real record, shown in a small "Based on" line. Each master is given the
        /// record nearest their lens - Monet the garden, Van Gogh the stroke, Socrates the Palace reason - and
        /// any other companion the next one left.
        /// </summary>
        static void AssignBasedOn(List<KeyValuePair<string, string>> lines)
        {
            var rec = JourneyMemory.Record;
            var records = new List<(string key, string text)>();
            if (rec.Monet != null) records.Add(("monet", "Based on: Monet garden  ·  " + Cap(rec.Monet.Preset) + "  ·  " + rec.Monet.Reason));
            if (rec.VanGogh != null) records.Add(("van_gogh", "Based on: Van Gogh studio  ·  your stroke"));
            if (rec.Palace != null) records.Add(("socrates", "Based on: Palace  ·  " + (rec.Palace.Reason.Length > 0 ? "your reason" : "the " + rec.Palace.Object)));
            if (rec.Grotto != null) records.Add(("grotto", "Based on: Grotto  ·  lamp on the " + rec.Grotto.LampSlot));
            TorsoPanel.BasedOn.Clear();
            var used = new HashSet<int>();
            foreach (var kv in lines)
                for (var k = 0; k < records.Count; k++)
                    if (!used.Contains(k) && records[k].key == kv.Key) { TorsoPanel.BasedOn[kv.Key] = records[k].text; used.Add(k); break; }
            foreach (var kv in lines)
            {
                if (TorsoPanel.BasedOn.ContainsKey(kv.Key)) continue;
                for (var k = 0; k < records.Count; k++)
                    if (!used.Contains(k)) { TorsoPanel.BasedOn[kv.Key] = records[k].text; used.Add(k); break; }
            }
        }

        static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        static string RosterId(string id) => id == "frida_kahlo" ? "frida" : id == "hilma_af_klint" ? "hilma" : id == "berthe_morisot" ? "morisot" : id;

        static List<KeyValuePair<string, string>> LocalThreads()
        {
            var rec = JourneyMemory.Record;
            var l = new List<KeyValuePair<string, string>>();
            var monet = rec.Monet != null ? "You stopped at " + rec.Monet.Reason + " at " + rec.Monet.Preset + ". An hour later that water is already other water. What you kept was the moment" : "You walked the garden without stopping long. Even that is a choice about time";
            var vg = rec.VanGogh != null ? "Your stroke left The Bedroom and ran all the way to the door. The place you pressed hardest is the thing you most wanted to say on this walk" : "You never lifted the brush. Some feelings wait until they are sure";
            var soc = rec.Palace != null ? "In the Palace you kept the " + rec.Palace.Object + (rec.Palace.Reason.Length > 0 ? ", \"" + rec.Palace.Reason + "\"" : "") + ". And you, which road do you keep walking?" : "You carried a question the whole way. Is it the same question now?";
            var c = Masters.Company;
            if (c.Count > 0) l.Add(new KeyValuePair<string, string>(c[0], monet));
            if (c.Count > 1) l.Add(new KeyValuePair<string, string>(c[1], vg));
            if (c.Count > 2) l.Add(new KeyValuePair<string, string>(c[2], soc));
            return l;
        }

        static string LocalDraft()
        {
            var rec = JourneyMemory.Record;
            if (rec.Monet != null) return "What is worth keeping is whatever makes me willing to stop at " + rec.Monet.Preset + " and look again";
            return "What is worth keeping is whatever I would walk this way again to see";
        }

        void ShowDraft()
        {
            _answerStage = 1;
            var panel = TorsoPanel.Get();
            if (panel != null) panel.ShowLine(null, "Your answer  ·  draft", "\"" + _draft + "\"", "A keep  ·  X rewrite via Socrates  ·  Y say my own", "Your answer");
            if (_tableSign != null) _tableSign.text = "A keep  ·  X rewrite  ·  Y say my own";
            ConfirmInput.Take(this);
        }

        /// <summary>Her X: adopt Socrates' challenge - a rewrite of the draft, asked live, with a written fallback.</summary>
        async void Rewrite()
        {
            if (_rewriting) return;
            _rewriting = true;
            var panel = TorsoPanel.Get();
            if (panel != null) panel.ShowLine(Masters.Socrates, "Rewrite  ·  through Socrates' question", "Socrates is turning your answer over\u2026", null, "Your answer");
            var rec = JourneyMemory.Record;
            string live = null;
            try { live = await RewriteLive(rec, _draft); }
            catch (System.Exception ex) { Debug.LogWarning("[Roundtable] rewrite failed: " + ex.Message); }
            _rewriting = false;
            if (_tableDone) return;
            _rewrite = !string.IsNullOrWhiteSpace(live) ? live.Trim().Trim('"')
                : rec.Palace != null && rec.Palace.Reason.Length > 0
                    ? "What is worth keeping is what I would still choose if I had not inherited it"
                    : "What is worth keeping is what I know why I keep";
            _answerStage = 2;
            if (panel != null) panel.ShowLine(Masters.Socrates, "Rewrite  ·  through Socrates' question", "\"" + _rewrite + "\"" + (live == null ? "   (local fallback)" : ""), "A use this one  ·  B back to the first", "Your answer");
        }

        [System.Serializable] class RewriteReply { public string answer; }

        static async System.Threading.Tasks.Task<string> RewriteLive(JourneyRecord rec, string draft)
        {
            var key = await MusePico.Generation.FallbackKeySource.ForOpenAi().GetKeyAsync();
            if (string.IsNullOrEmpty(key)) return null;
            var call = new ResponsesCall(new MusePico.Tripo.TripoWebRequestTransport(key, ResponsesCall.DefaultEndpoint));
            var props = new JsonBuilder().Add("answer", new JsonBuilder().Add("type", "string"));
            var schema = new JsonBuilder().Add("type", "object").Add("properties", props)
                .AddStringArray("required", new[] { "answer" }).Add("additionalProperties", false);
            var why = rec.Palace != null && rec.Palace.Reason.Length > 0 ? " In the Palace they kept the " + rec.Palace.Object + " \"" + rec.Palace.Reason + "\"." : "";
            const string instructions =
                "You are Socrates at the end of a museum walk. The visitor drafted one sentence answering their question. " +
                "Ask yourself the one question that most tests it, then rewrite their sentence so it survives that question. " +
                "Keep their voice, first person, one sentence under 22 words, no quotation marks, no preamble.";
            var text = await call.SendAsync(instructions,
                "Question: " + rec.Question + "\nDraft answer: " + draft + why,
                ResponsesCall.TextFormat("rewrite", schema), raw =>
                {
                    try { var r = JsonUtility.FromJson<RewriteReply>(raw); return r != null && !string.IsNullOrWhiteSpace(r.answer) ? null : "empty"; }
                    catch (System.Exception ex) { return ex.Message; }
                });
            return JsonUtility.FromJson<RewriteReply>(text).answer;
        }

        void SayOwn()
        {
            var dialogue = FindAnyObjectByType<MuseumDialogue>();
            if (dialogue == null) return;
            Note("Hold X and say your answer");
            dialogue.TextDictated -= OnOwn; dialogue.TextDictated += OnOwn;
            dialogue.ListenForText();
        }

        void OnOwn(string text)
        {
            var dialogue = FindAnyObjectByType<MuseumDialogue>();
            if (dialogue != null) dialogue.TextDictated -= OnOwn;
            if (string.IsNullOrWhiteSpace(text)) return;
            _draft = text.Trim();
            ShowDraft();
        }

        public bool Confirm()
        {
            if (_undoUntil > 0f) { KeepWork(); return true; }
            if (_answerStage == 0 || _tableDone) return false;
            var final = _answerStage == 2 ? _rewrite : _draft;
            var rec = JourneyMemory.Record;
            rec.FinalAnswer.Draft = _draft; rec.FinalAnswer.Final = final; rec.FinalAnswer.RewrittenBy = _answerStage == 2 ? "socrates" : "self";
            _tableDone = true; _answerStage = 0;
            ConfirmInput.Drop(this);
            var panel = TorsoPanel.Get();
            if (panel != null) panel.Note("Your answer", "\"" + final + "\"", 8f);
            if (_tableSign != null) _tableSign.text = "Your world is rising";
            Debug.Log("[Roundtable] final answer: " + final);
            YourWorldMiniature.Rise(_rotunda);   // her step into Your world: the miniature, its door, a fade
            return true;
        }

        public bool Redo()
        {
            if (_undoUntil > 0f) { UndoWork(); return true; }
            if (_answerStage != 2) return false;
            ShowDraft();
            return true;
        }

        static Mesh Torus(float radius, float tube)
        {
            const int seg = 48, sides = 12;
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
            var m = new Mesh { vertices = verts, triangles = tris, name = "time ring" };
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }
    }
}
