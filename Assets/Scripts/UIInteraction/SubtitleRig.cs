using MuseXR.Interaction;
using UnityEngine;

namespace MuseXR.UI
{
    /// <summary>
    /// Her 4.2 subtitle on top of musexr-b's <see cref="CompanionGroup"/>: when a companion's line
    /// starts, her dialogue panel appears 0.4 m upper-right of that companion's head and lazy-follows
    /// (<see cref="SubtitleFollow"/>); when the line ends it goes. One per group.
    /// </summary>
    public sealed class SubtitleRig : MonoBehaviour
    {
        public CompanionGroup Group;
        const float HeadHeight = 1.7f;   // above a figure's feet, for a master or a standee alike

        readonly SubtitleFollow _follow = new SubtitleFollow();
        Transform _panel, _speaker;
        Transform _eye;

        void OnEnable()
        {
            if (Group == null) Group = GetComponent<CompanionGroup>();
            if (Group == null) return;
            Group.LineStarted += Show;
            Group.LineEnded += Hide;
        }

        void OnDisable()
        {
            if (Group == null) return;
            Group.LineStarted -= Show;
            Group.LineEnded -= Hide;
        }

        void Show(string id, string line)
        {
            Hide(id);
            if (Group == null || !Group.Figures.TryGetValue(id, out _speaker) || _speaker == null) return;
            _eye = Group.Head != null ? Group.Head : (Camera.main != null ? Camera.main.transform : null);
            if (_eye == null) return;

            string next = null;
            var ids = Group.Ids;
            for (int i = 0; i < ids.Count - 1; i++) if (ids[i] == id) next = ids[i + 1];
            var d = new DialogueLine
            {
                SpeakerId = id, SpeakerName = MuseXR.Slots.Masters.Name(id), Line = line,
                NextSpeakerId = next, NextSpeakerName = next != null ? MuseXR.Slots.Masters.Name(next) : null,
            };
            _panel = new GameObject("Subtitle " + id).transform;
            _panel.SetParent(transform, false);
            var head = SpeakerHead();
            // Her A advances at once (Turns.Advance); LineFinished would start the 2 s auto-advance pause.
            var panel = MuseScreens.Dialogue(_panel, d, Vector3.Distance(_eye.position, head), () => Group.Confirm());
            var disclaimer = FindText(panel, "Disclaimer");
            if (disclaimer != null) { disclaimer.fontSize = 12f; disclaimer.color = MuseTheme.Ink2; }   // readable without zoom
            // No wider than MaxWidth beside a figure, and lifted so its lower edge clears the head.
            float half = Fit(_panel, MaxWidth);
            _follow.ExtraUp = Mathf.Max(0f, half + HeadClearance - SubtitleFollow.Up);
            _follow.Reset(head, _eye.position);
            _panel.position = _follow.Current;
            Face();
        }

        void Hide(string id)
        {
            if (_panel != null) Destroy(_panel.gameObject);
            _panel = null;
        }

        void LateUpdate()
        {
            if (_panel == null || _speaker == null || _eye == null) return;
            _panel.position = _follow.Tick(SpeakerHead(), _eye.position, _eye.forward, Time.deltaTime);
            Face();
        }

        Vector3 SpeakerHead() => _speaker.position + Vector3.up * HeadHeight;

        const float MaxWidth = 1.0f;        // metres; the full panel at her 1-degree size is ~1.9 m at 2.5 m
        const float HeadClearance = 0.22f;  // from the head point to the panel's lower edge

        /// <summary>Scale the panel so it is at most <paramref name="metres"/> wide; returns its half height.</summary>
        static float Fit(Transform anchor, float metres)
        {
            Canvas.ForceUpdateCanvases();
            float Measure(int axis)
            {
                float lo = float.MaxValue, hi = float.MinValue; var c = new Vector3[4];
                foreach (var rt in anchor.GetComponentsInChildren<RectTransform>())
                {
                    rt.GetWorldCorners(c);
                    foreach (var v in c) { float x = axis == 0 ? Vector3.Dot(v - anchor.position, anchor.right) : v.y - anchor.position.y; lo = Mathf.Min(lo, x); hi = Mathf.Max(hi, x); }
                }
                return hi - lo;
            }
            float w = Measure(0);
            if (w > metres && w > 1e-4f) anchor.localScale *= metres / w;
            return Measure(1) / 2f;
        }

        static TMPro.TextMeshProUGUI FindText(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true)) if (t.name == name) return t;
            return null;
        }

        void Face()
        {
            var away = _panel.position - _eye.position; away.y = 0f;   // +Z away from the viewer reads
            if (away.sqrMagnitude > 1e-6f) _panel.rotation = Quaternion.LookRotation(away, Vector3.up);
        }
    }
}
