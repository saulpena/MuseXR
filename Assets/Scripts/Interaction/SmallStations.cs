using System;
using MuseXR.Slots;
using Unity.XR.CoreUtils;
using UnityEngine;

namespace MuseXR.Interaction
{
    /// <summary>
    /// The Palace card fallback (2.4): with no miniatures, "two exhibit cards that are ray-selected,
    /// then flip and glow". Point and pull the trigger to choose; the chosen card turns face up and the
    /// other turns back; A keeps, B within 3 s turns it back. The turn itself is done here (a 0.35 s
    /// flip about the card's vertical axis); the glow and the card art are the UI layer's, from
    /// <see cref="Logic"/>.Flipped and <see cref="FaceUp"/>.
    /// </summary>
    public sealed class CardChoiceStation : MonoBehaviour, IConfirmable
    {
        public const float FlipSeconds = 0.35f;

        public CardChoice Logic { get; private set; }
        public Transform[] Cards { get; private set; }
        public Chapter Chapter { get; private set; } = Chapter.Palace;
        public int FaceUp => Logic.FaceUp;

        public event Action<CardChoiceStation, string> Kept;

        Quaternion[] _back;
        float[] _turn;   // 0 = back showing, 1 = face showing

        public static CardChoiceStation Make(GameObject host, Transform[] cards, string[] ids)
        {
            var s = host.AddComponent<CardChoiceStation>();
            s.Cards = cards;
            s.Logic = new CardChoice(ids);
            s._back = new Quaternion[cards.Length];
            s._turn = new float[cards.Length];
            for (var i = 0; i < cards.Length; i++)
            {
                s._back[i] = cards[i].localRotation;
                var index = i;
                Pointable.Make(cards[i].gameObject, ids[i]).Selected += (_, pointer) => s.Pick(index, pointer);
            }
            s.Logic.Flipped += (card, up) => { if (up) ChimePlayer.Play(ChimePlayer.TickClip(), cards[card].position, 0.4f); };
            return s;
        }

        public bool Pick(int card, Pointer pointer)
        {
            if (!Logic.Select(card)) return false;
            pointer?.Source.Buzz(SlotRules.LightAmplitude * 1.5f, SlotRules.LightSeconds);
            if (Logic.FaceUp >= 0) ConfirmInput.Take(this);
            return true;
        }

        public bool Confirm()
        {
            if (!Logic.Confirm()) return false;
            ChimePlayer.Play(SlotRules.SoundOf(Chapter), Cards[Logic.FaceUp].position);
            Kept?.Invoke(this, Logic.Ids[Logic.FaceUp]);
            ConfirmInput.Drop(this);
            return true;
        }

        public bool Redo() => Logic.Redo();

        void Update()
        {
            Logic?.Choice.Tick(Time.deltaTime);
            if (Cards == null) return;
            for (var i = 0; i < Cards.Length; i++)
            {
                var goal = Logic.FaceUp == i ? 1f : 0f;
                _turn[i] = Mathf.MoveTowards(_turn[i], goal, Time.deltaTime / FlipSeconds);
                Cards[i].localRotation = _back[i] * Quaternion.Euler(0f, 180f * Mathf.SmoothStep(0f, 1f, _turn[i]), 0f);
            }
        }
    }

    /// <summary>
    /// Her Your-world plinths (2.9): "each plinth replays its chapter chime as you approach". Once per
    /// approach; stepping back past the margin re-arms it.
    /// </summary>
    public sealed class ApproachChime : MonoBehaviour
    {
        public const float Radius = 1.2f;

        public ChapterSound Sound { get; private set; }
        public Transform Head { get; set; }
        public int Played { get; private set; }

        public event Action<ApproachChime> Rang;

        ApproachTrigger _trigger;

        public static ApproachChime Make(GameObject plinth, ChapterSound sound)
        {
            var c = plinth.AddComponent<ApproachChime>();
            c.Sound = sound;
            c._trigger = new ApproachTrigger(Radius);
            return c;
        }

        void Update()
        {
            if (Head == null && Camera.main != null) Head = Camera.main.transform;
            if (Head == null || _trigger == null) return;
            var d = Head.position - transform.position; d.y = 0f;
            if (!_trigger.Update(d.magnitude)) return;
            Played++;
            ChimePlayer.Play(Sound, transform.position + Vector3.up * 1f);
            Rang?.Invoke(this);
        }
    }

    /// <summary>
    /// Her one-time height calibration (§3.5). <see cref="Calibrate"/> measures the eye once and lifts
    /// (or lowers) the camera's offset so the eye sits at a standing height; seated and standing then
    /// play the same flow. It moves the rig's Camera Offset, not the XR Origin, so the body and its
    /// collider stay on the floor. The prompt that asks for it is the UI layer's.
    /// </summary>
    public sealed class HeightCalibrator : MonoBehaviour
    {
        public XROrigin Origin { get; set; }
        public float Applied { get; private set; }
        public bool Done { get; private set; }

        public event Action<float> Calibrated;

        /// <summary>Measure now and apply. Returns the offset applied, metres.</summary>
        public float Calibrate()
        {
            if (Origin == null) Origin = FindFirstObjectByType<XROrigin>();
            if (Origin == null || Origin.Camera == null || Origin.CameraFloorOffsetObject == null) return 0f;
            var offset = Origin.CameraFloorOffsetObject.transform;
            // The eye above the origin's floor, without whatever was applied before.
            var eye = Origin.Camera.transform.position.y - Origin.transform.position.y - Applied;
            var add = HeightCalibration.Offset(eye);
            offset.localPosition += Vector3.up * (add - Applied);
            Applied = add;
            Done = true;
            Calibrated?.Invoke(add);
            return add;
        }
    }
}
