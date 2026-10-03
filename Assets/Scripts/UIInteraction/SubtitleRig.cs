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
            _follow.Reset(head, _eye.position);
            _panel.position = _follow.Current;
            MuseScreens.Dialogue(_panel, d, Vector3.Distance(_eye.position, head), () => Group.LineFinished());
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

        void Face()
        {
            var away = _panel.position - _eye.position; away.y = 0f;   // +Z away from the viewer reads
            if (away.sqrMagnitude > 1e-6f) _panel.rotation = Quaternion.LookRotation(away, Vector3.up);
        }
    }
}
