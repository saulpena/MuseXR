using TMPro;
using UnityEngine;

namespace MusePico.Journey
{
    /// <summary>
    /// A master's reading, shown in a bubble over their head and kept there.
    ///
    /// The panel used to show one reading at a time ("1 / 3", "2 / 3", "3 / 3"), each replacing the
    /// last, so the three could never be read side by side (Saul, 27 Sep: "they're all over each
    /// other... not a very friendly UI"). A bubble per master shows who said what, all at once:
    /// each appears as its master starts speaking and stays until the next question.
    ///
    /// Sized in WORLD units under a scaled companion (like the name plate), faces the viewer, and
    /// grows upward from its anchor so a longer reading never covers the head below it.
    /// </summary>
    public sealed class SpeechBubble : MonoBehaviour
    {
        public const float Width = 1.35f;
        const float Pad = 0.07f;

        TextMeshPro _text;
        Transform _back;
        Material _mat;

        static readonly Color Rest = new Color(0.10f, 0.08f, 0.06f, 0.86f);
        static readonly Color Speaking = new Color(0.22f, 0.17f, 0.10f, 0.94f);

        /// <summary>A hidden bubble under <paramref name="master"/>, its bottom edge at world height <paramref name="bottomY"/>.</summary>
        public static SpeechBubble Create(Transform master, float bottomY)
        {
            var go = new GameObject("Speech Bubble");
            go.transform.SetParent(master, false);
            go.transform.position = new Vector3(master.position.x, bottomY, master.position.z);
            go.transform.localScale = Vector3.one / Mathf.Max(master.lossyScale.y, 1e-3f);
            go.AddComponent<FaceViewer>();

            var b = go.AddComponent<SpeechBubble>();
            b.Build();
            go.SetActive(false);
            return b;
        }

        void Build()
        {
            var back = GameObject.CreatePrimitive(PrimitiveType.Quad);
            back.name = "Back";
            Destroy(back.GetComponent<Collider>());
            back.transform.SetParent(transform, false);
            _mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            _mat.SetColor("_BaseColor", Rest);
            _mat.SetFloat("_Surface", 1f);
            _mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _mat.SetFloat("_ZWrite", 0f);
            _mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            _mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent - 5;
            back.GetComponent<MeshRenderer>().sharedMaterial = _mat;
            _back = back.transform;

            _text = new GameObject("Text").AddComponent<TextMeshPro>();
            _text.transform.SetParent(transform, false);
            _text.fontSize = 0.62f;
            _text.alignment = TextAlignmentOptions.TopLeft;
            _text.enableWordWrapping = true;
            _text.richText = true;
        }

        /// <summary>Show a reading. <paramref name="speaking"/> lights the bubble of the master talking now.</summary>
        public void Show(string speaker, string body, bool speaking)
        {
            gameObject.SetActive(true);
            _text.text = "<color=#C9AA72><size=80%><cspace=0.14em>" + (speaker ?? string.Empty).ToUpperInvariant() +
                         "</cspace></size></color>\n" + (body ?? string.Empty);

            var w = Width - Pad * 2f;
            var h = _text.GetPreferredValues(_text.text, w, 0f).y;
            _text.rectTransform.sizeDelta = new Vector2(w, h);
            // localPosition is the rect's CENTRE; seat the text so the bubble grows upward from y 0.
            _text.rectTransform.localPosition = new Vector3(0f, Pad + h * 0.5f, 0f);
            // Behind the text is +Z: a flat thing is seen with its +Z pointing away from the viewer.
            _back.localPosition = new Vector3(0f, Pad + h * 0.5f, 0.01f);
            _back.localScale = new Vector3(Width, h + Pad * 2f, 1f);
            SetSpeaking(speaking);
        }

        public void SetSpeaking(bool speaking) => _mat.SetColor("_BaseColor", speaking ? Speaking : Rest);

        public void Hide() => gameObject.SetActive(false);
    }
}
