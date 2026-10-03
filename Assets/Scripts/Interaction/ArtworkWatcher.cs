using System;
using MuseXR.Slots;
using UnityEngine;

namespace MuseXR.Interaction
{
    /// <summary>
    /// One hung work's attention (her UI 4.1 and record.dwell): <see cref="CardWanted"/> after a 0.4 s
    /// ray dwell on the frame or on stepping within 1.2 m of its viewing mark, and gaze timing, with
    /// <see cref="Seen"/> once the gaze has rested on it 4 s in total. It draws nothing: the artwork
    /// card is the UI layer's, and the dwell goes to JourneyRecord.AddDwell.
    /// </summary>
    public sealed class ArtworkWatcher : MonoBehaviour
    {
        public ArtworkAttention Attention { get; private set; }
        public Pointable Frame { get; private set; }
        /// <summary>Where the visitor stands to view it: 1.8-2.5 m in front, at floor height.</summary>
        public Transform ViewingMark { get; private set; }
        public Transform Head { get; set; }

        public string ArtworkId => Attention.ArtworkId;
        public float GazeSeconds => Attention.GazeSeconds;

        public event Action<ArtworkWatcher> CardWanted;
        public event Action<ArtworkWatcher, float> Seen;

        /// <summary>Every watcher's events in one place, for the record and the card.</summary>
        public static event Action<ArtworkWatcher> AnyCardWanted;
        public static event Action<ArtworkWatcher, float> AnySeen;

        public static ArtworkWatcher Make(GameObject frame, string artworkId, Transform viewingMark)
        {
            var w = frame.AddComponent<ArtworkWatcher>();
            w.Attention = new ArtworkAttention(artworkId);
            w.Frame = Pointable.Make(frame, artworkId);
            w.ViewingMark = viewingMark;
            w.Attention.CardWanted += _ => { w.CardWanted?.Invoke(w); AnyCardWanted?.Invoke(w); };
            w.Attention.BecameSeen += (_, s) => { w.Seen?.Invoke(w, s); AnySeen?.Invoke(w, s); };
            return w;
        }

        void Update()
        {
            if (Attention == null) return;
            if (Head == null && Camera.main != null) Head = Camera.main.transform;
            var rayOn = Pointer.AnyOn(Frame);
            var dist = float.MaxValue;
            var gaze = false;
            if (Head != null)
            {
                if (ViewingMark != null)
                {
                    var flat = Head.position - ViewingMark.position; flat.y = 0f;
                    dist = flat.magnitude;
                }
                var centre = Frame.GetComponentInChildren<Renderer>() is Renderer r ? r.bounds.center : transform.position;
                var to = centre - Head.position;
                gaze = to.magnitude < 8f && ArtworkAttention.GazeOn(Vector3.Angle(Head.forward, to));
            }
            Attention.Update(rayOn, dist, gaze, Time.deltaTime);
        }
    }
}
