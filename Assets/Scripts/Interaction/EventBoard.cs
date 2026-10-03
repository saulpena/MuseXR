using System.Collections.Generic;
using UnityEngine;

namespace MuseXR.Interaction
{
    /// <summary>
    /// A pale board that lists what just happened, newest first, so a tester in the headset can see
    /// every press, choice and save without the console. Test scenes only.
    /// </summary>
    public sealed class EventBoard : MonoBehaviour
    {
        public const int Lines = 9;

        TMPro.TextMeshPro _text;
        readonly List<string> _events = new List<string>();

        /// <summary>A board at <paramref name="at"/>, facing <paramref name="viewer"/> (+Z away from it, so it reads).</summary>
        public static EventBoard Make(Transform parent, Vector3 at, Vector3 viewer, string title)
        {
            var away = at - viewer; away.y = 0f;
            var go = new GameObject("Event Board");
            go.transform.SetParent(parent, true);
            go.transform.SetPositionAndRotation(at, Quaternion.LookRotation(away.normalized, Vector3.up));
            var b = go.AddComponent<EventBoard>();
            var back = GameObject.CreatePrimitive(PrimitiveType.Quad);
            DestroyImmediate(back.GetComponent<Collider>());
            back.transform.SetParent(go.transform, false);
            back.transform.localPosition = new Vector3(0f, 0f, 0.01f);
            back.transform.localScale = new Vector3(1.7f, 0.95f, 1f);
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            m.SetColor("_BaseColor", new Color(0.95f, 0.93f, 0.88f));
            back.GetComponent<Renderer>().sharedMaterial = m;
            b._text = new GameObject("Text").AddComponent<TMPro.TextMeshPro>();
            b._text.transform.SetParent(go.transform, false);
            b._text.rectTransform.sizeDelta = new Vector2(1.6f, 0.88f);
            b._text.enableAutoSizing = true;   // long lines (the record) shrink rather than run off the board
            b._text.fontSizeMax = 0.022f * (0.6f / 0.016f);
            b._text.fontSizeMin = 0.011f * (0.6f / 0.016f);
            b._text.color = new Color(0.2f, 0.16f, 0.12f);
            b._text.alignment = TMPro.TextAlignmentOptions.TopLeft;
            b._text.enableWordWrapping = true;
            b._title = title;
            b.Note("Ready.");
            return b;
        }

        string _title = "";

        public void Note(string line)
        {
            Debug.Log(line);
            _events.Insert(0, System.DateTime.Now.ToString("HH:mm:ss") + "  " + line);
            if (_events.Count > Lines) _events.RemoveAt(_events.Count - 1);
            if (_text != null) _text.text = "<b>" + _title + "</b>\n<size=80%>" + string.Join("\n", _events) + "</size>";
        }
    }
}
