using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Painters.Unity
{
    /// <summary>
    /// Test panel for the painter characters: choose who is controlled, then idle, listen, any talk or walk
    /// style, shuffle talks, pause, playback speed and turn. The buttons are placed in the scene; this only
    /// wires them to <see cref="PainterAnims"/> and the Animators. Desktop (Game view, mouse).
    /// </summary>
    public sealed class PainterControlPanel : MonoBehaviour
    {
        static readonly int SpeedId = Animator.StringToHash("Speed"), TalkingId = Animator.StringToHash("Talking"),
            ListeningId = Animator.StringToHash("Listening"), TalkStyleId = Animator.StringToHash("TalkStyle"),
            WalkStyleId = Animator.StringToHash("WalkStyle");

        [SerializeField] Animator[] actors = new Animator[0];
        [Tooltip("One per actor, then one for all of them")]
        [SerializeField] Button[] targetButtons = new Button[0];
        [Tooltip("In PainterAnims.TalkNames order")]
        [SerializeField] Button[] talkButtons = new Button[0];
        [Tooltip("In PainterAnims.WalkNames order")]
        [SerializeField] Button[] walkButtons = new Button[0];
        [SerializeField] Button idleButton, listenButton, shuffleButton, pauseButton;
        [SerializeField] TMP_Text pauseLabel, status;
        [SerializeField] Slider playbackSpeed, turn;
        [SerializeField] Color selectedColor = new Color(0.35f, 0.55f, 0.95f), normalColor = Color.white;
        [Tooltip("Drawn only beside the panel, so the actors are never behind it whatever the window's shape")]
        [SerializeField] Camera viewCamera;
        [SerializeField] RectTransform panelRect;

        readonly Vector3[] corners = new Vector3[4];

        PainterAnimState[] states;
        float[] baseYaw;
        int target = -1;          // -1 = all actors
        bool paused;
        readonly System.Random rng = new System.Random();

        void Start()
        {
            states = new PainterAnimState[actors.Length];
            baseYaw = new float[actors.Length];
            for (int i = 0; i < actors.Length; i++) baseYaw[i] = actors[i].transform.eulerAngles.y;

            for (int i = 0; i < targetButtons.Length; i++)
            {
                int t = i < actors.Length ? i : -1;
                targetButtons[i].onClick.AddListener(() => { target = t; Refresh(); });
            }
            for (int i = 0; i < talkButtons.Length; i++) { int s = i; talkButtons[i].onClick.AddListener(() => Apply(st => PainterAnims.Talk(st, s))); }
            for (int i = 0; i < walkButtons.Length; i++) { int s = i; walkButtons[i].onClick.AddListener(() => Apply(st => PainterAnims.Walk(st, s))); }
            if (idleButton) idleButton.onClick.AddListener(() => Apply(PainterAnims.Idle));
            if (listenButton) listenButton.onClick.AddListener(() => Apply(PainterAnims.Listen));
            if (shuffleButton) shuffleButton.onClick.AddListener(() => Apply(st => PainterAnims.Talk(st, PainterAnims.OtherTalk(st.TalkStyle, rng))));
            if (pauseButton) pauseButton.onClick.AddListener(() => { paused = !paused; ApplySpeed(); Refresh(); });
            if (playbackSpeed) playbackSpeed.onValueChanged.AddListener(_ => { ApplySpeed(); Refresh(); });
            if (turn) turn.onValueChanged.AddListener(Turn);
            Refresh();
        }

        void LateUpdate()
        {
            if (!viewCamera || !panelRect) return;
            panelRect.GetWorldCorners(corners);          // overlay canvas: corners are in screen pixels
            float right = Mathf.Clamp01(corners[2].x / Mathf.Max(1, Screen.width));
            viewCamera.rect = new Rect(right, 0f, 1f - right, 1f);
        }

        void Apply(Func<PainterAnimState, PainterAnimState> change)
        {
            for (int i = 0; i < actors.Length; i++)
            {
                if (target >= 0 && i != target) continue;
                states[i] = change(states[i]);
                var a = actors[i];
                a.SetFloat(SpeedId, states[i].Speed);
                a.SetBool(TalkingId, states[i].Talking);
                a.SetBool(ListeningId, states[i].Listening);
                a.SetInteger(TalkStyleId, states[i].TalkStyle);
                a.SetInteger(WalkStyleId, states[i].WalkStyle);
            }
            Refresh();
        }

        void ApplySpeed()
        {
            float s = paused ? 0f : (playbackSpeed ? playbackSpeed.value : 1f);
            foreach (var a in actors) a.speed = s;
        }

        void Turn(float degrees)
        {
            for (int i = 0; i < actors.Length; i++)
                actors[i].transform.rotation = Quaternion.Euler(0f, baseYaw[i] + degrees, 0f);
        }

        void Mark(Button b, bool on)
        {
            if (b) b.image.color = on ? selectedColor : normalColor;
        }

        void Refresh()
        {
            for (int i = 0; i < targetButtons.Length; i++)
            {
                bool on = (i < actors.Length ? i : -1) == target;
                targetButtons[i].image.color = on ? selectedColor : normalColor;
            }
            // the active animation of the controlled actors, only when they all agree
            string shown = null;
            for (int i = 0; i < actors.Length; i++)
            {
                if (target >= 0 && i != target) continue;
                string d = PainterAnims.Describe(states[i]);
                shown = shown == null ? d : shown == d ? d : "";
            }
            for (int i = 0; i < talkButtons.Length; i++) Mark(talkButtons[i], shown == "talking: " + PainterAnims.TalkNames[i]);
            for (int i = 0; i < walkButtons.Length; i++) Mark(walkButtons[i], shown == "walking: " + PainterAnims.WalkNames[i]);
            Mark(idleButton, shown == "idle");
            Mark(listenButton, shown == "listening");
            if (pauseLabel) pauseLabel.text = paused ? "Play" : "Pause";
            if (!status) return;
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < actors.Length; i++)
                sb.Append(actors[i].name).Append(": ").Append(PainterAnims.Describe(states[i])).Append('\n');
            sb.Append(paused ? "paused" : "speed " + (playbackSpeed ? playbackSpeed.value : 1f).ToString("0.0") + "x");
            status.text = sb.ToString();
        }
    }
}
