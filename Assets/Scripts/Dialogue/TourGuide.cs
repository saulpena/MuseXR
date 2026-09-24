using UnityEngine;

namespace MusePico.Dialogue
{
    /// <summary>Where the next stop is, from where the visitor is standing and looking.</summary>
    public readonly struct TourStop
    {
        /// <summary>Zero-based position in the walk.</summary>
        public readonly int Index;

        public readonly int Total;
        public readonly string Title;
        public readonly string Artist;

        /// <summary>Metres, on the ground plane — the number her HUD prints.</summary>
        public readonly float Distance;

        /// <summary>Degrees from dead ahead. Positive is to the right, as hers is.</summary>
        public readonly float Bearing;

        public readonly bool Arrived;

        public TourStop(int index, int total, string title, string artist,
                        float distance, float bearing, bool arrived)
        {
            Index = index; Total = total;
            Title = title ?? string.Empty; Artist = artist ?? string.Empty;
            Distance = distance; Bearing = bearing; Arrived = arrived;
        }

        public bool HasStop => Total > 0;
    }

    /// <summary>
    /// The guided walk's compass, ported from muse-infinity's <c>emitTourUpdate</c> and
    /// <c>updateTourGuide</c> (<c>lib/museum3d.js:758</c>, <c>app.js:707</c>).
    ///
    /// Hers walks the visitor from artwork to artwork: the HUD names the next stop, points at it
    /// with a bearing arrow, and counts down a live distance; once they are standing at it the
    /// companions leave their walking slots and gather beside that frame.
    ///
    /// <b>The bearing is relative to where the visitor is LOOKING</b>, not to world north — 0 is
    /// dead ahead and positive is to the right. That is what makes an arrow usable rather than a
    /// map you have to orient yourself against, and it matters more in a headset than on a screen.
    ///
    /// Pure arithmetic, so the whole compass can be argued about in EditMode. The stop ORDER comes
    /// from <see cref="GalleryWall.TourOrder"/>, which is also pure and already tested.
    /// </summary>
    public static class TourGuide
    {
        /// <summary>
        /// Stand this close and you have arrived. Hers: <c>distance &lt; 3.4</c>.
        ///
        /// Kept at her number rather than retuned for VR. It is a comfortable distance to read a
        /// painting from, and shrinking it would make the visitor walk into the wall to satisfy
        /// the HUD.
        /// </summary>
        public const float ArrivedWithin = 3.4f;

        /// <summary>
        /// Degrees from dead ahead to <paramref name="target"/>, positive to the right.
        ///
        /// Her three.js version builds the forward and right vectors by hand from the yaw; this is
        /// the same calculation expressed in Unity's left-handed frame, which is why it reads
        /// differently and means the same thing.
        /// </summary>
        public static float Bearing(Vector3 from, float yawDegrees, Vector3 target)
        {
            var to = new Vector3(target.x - from.x, 0f, target.z - from.z);
            if (to.sqrMagnitude < 1e-8f) return 0f;

            to.Normalize();
            var rotation = Quaternion.Euler(0f, yawDegrees, 0f);
            var forward = rotation * Vector3.forward;
            var right = rotation * Vector3.right;

            return Mathf.Atan2(Vector3.Dot(to, right), Vector3.Dot(to, forward)) * Mathf.Rad2Deg;
        }

        /// <summary>Describe the current stop. <paramref name="index"/> is clamped into range.</summary>
        public static TourStop Describe(
            Vector3 visitor, float yawDegrees, Vector3 stop, int index, int total,
            string title = null, string artist = null)
        {
            if (total <= 0) return new TourStop(0, 0, null, null, 0f, 0f, false);

            index = Mathf.Clamp(index, 0, total - 1);
            var distance = GalleryWall.GroundDistance(visitor, stop);
            return new TourStop(index, total, title, artist,
                distance, Bearing(visitor, yawDegrees, stop), distance < ArrivedWithin);
        }

        /// <summary>Her <c>#tourStep</c> line.</summary>
        public static string Step(TourStop stop)
        {
            if (!stop.HasStop) return string.Empty;
            var counter = (stop.Index + 1) + " / " + stop.Total;
            return stop.Arrived ? "STOP " + counter + " · YOU ARE HERE" : "WALK TO STOP " + counter;
        }

        /// <summary>
        /// Her <c>#tourHint</c> line: the distance and the artist while walking, and what to do
        /// once you are there. Hers says "CLICK THE PAINTING"; ours says what hands do.
        /// </summary>
        public static string Hint(TourStop stop)
        {
            if (!stop.HasStop) return string.Empty;
            if (stop.Arrived) return "POINT AT THE PAINTING AND PULL THE TRIGGER";

            var metres = Mathf.RoundToInt(stop.Distance) + " M";
            return string.IsNullOrEmpty(stop.Artist) ? metres : metres + " · " + stop.Artist;
        }

        /// <summary>
        /// True when the HUD would visibly change, so it is only rebuilt when it has to be.
        ///
        /// Hers throttles on a signature of index, arrival, distance to 0.1 m and bearing to the
        /// nearest 4 degrees. Same thresholds here: below them nobody can see the difference, and
        /// rebuilding TMP geometry every frame while walking is not free.
        /// </summary>
        public static bool Differs(TourStop a, TourStop b)
        {
            if (a.Index != b.Index || a.Arrived != b.Arrived || a.Total != b.Total) return true;
            if (Mathf.Abs(a.Distance - b.Distance) >= 0.1f) return true;
            return Mathf.Abs(Mathf.DeltaAngle(a.Bearing, b.Bearing)) >= 4f;
        }
    }
}
