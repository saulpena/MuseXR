using System.Collections.Generic;
using UnityEngine;

namespace MuseXR.Interaction
{
    /// <summary>
    /// Her Van Gogh stroke (chapter C, P0): "One stroke only: trigger down to draw, release to end. Max 256 points,
    /// smoothed into a thick ribbon". The brush's arithmetic, with no scene in it, so it is tested on its own:
    /// <list type="bullet">
    /// <item>each sample is eased toward the hand (<see cref="Follow"/>), so the tremor of a held controller never
    /// reaches the paint;</item>
    /// <item>samples closer than <see cref="Spacing"/> are skipped; at <see cref="MaxPoints"/> every other point is
    /// dropped and the spacing doubles - a long stroke keeps drawing at a coarser grain instead of stopping dead;</item>
    /// <item><see cref="Curve"/> runs a Catmull-Rom spline through the kept points for the drawn ribbon, and
    /// <see cref="BuildRibbon"/> makes it a flat band turned with the brush, swelling from the press and tapering off.</item>
    /// </list>
    /// </summary>
    public sealed class StrokeBrush
    {
        public const int MaxPoints = 256;
        public const float StartSpacing = 0.012f;
        /// <summary>How far each frame's point moves toward the hand: lower is smoother and lags more.</summary>
        public const float Follow = 0.35f;
        /// <summary>The band's full width, metres: her "thick ribbon", about a loaded brush's breadth.</summary>
        public const float Width = 0.055f;

        readonly List<Vector3> _points = new List<Vector3>();
        readonly List<Vector3> _ups = new List<Vector3>();
        Vector3 _eased, _easedUp;
        bool _started;

        public IReadOnlyList<Vector3> Points => _points;
        /// <summary>The brush's flat side at each point: the band lies across it.</summary>
        public IReadOnlyList<Vector3> Ups => _ups;
        public float Spacing { get; private set; } = StartSpacing;
        public int Count => _points.Count;

        public void Clear() { _points.Clear(); _ups.Clear(); _started = false; Spacing = StartSpacing; }

        /// <summary>One frame of the brush: where the tip is and which way its flat side faces. True when a point was kept.</summary>
        public bool Add(Vector3 tip, Vector3 up)
        {
            if (!_started) { _eased = tip; _easedUp = up; _started = true; }
            else
            {
                _eased = Vector3.Lerp(_eased, tip, Follow);
                _easedUp = Vector3.Slerp(_easedUp, up, Follow);
            }
            if (_points.Count > 0 && Vector3.Distance(_eased, _points[_points.Count - 1]) < Spacing) return false;
            if (_points.Count >= MaxPoints) Halve();
            _points.Add(_eased); _ups.Add(_easedUp.normalized);
            return true;
        }

        /// <summary>Keep every other point (always the first) and double the spacing: the stroke stays whole, coarser.</summary>
        void Halve()
        {
            var p = new List<Vector3>(); var u = new List<Vector3>();
            for (var i = 0; i < _points.Count; i += 2) { p.Add(_points[i]); u.Add(_ups[i]); }
            _points.Clear(); _points.AddRange(p); _ups.Clear(); _ups.AddRange(u);
            Spacing *= 2f;
        }

        /// <summary>A Catmull-Rom spline through <paramref name="pts"/>, <paramref name="perSegment"/> samples a span.</summary>
        public static List<Vector3> Curve(IReadOnlyList<Vector3> pts, int perSegment = 4)
        {
            var o = new List<Vector3>();
            if (pts == null || pts.Count == 0) return o;
            if (pts.Count < 3) { o.AddRange(pts); return o; }
            for (var i = 0; i < pts.Count - 1; i++)
            {
                var p0 = pts[Mathf.Max(i - 1, 0)]; var p1 = pts[i]; var p2 = pts[i + 1]; var p3 = pts[Mathf.Min(i + 2, pts.Count - 1)];
                for (var s = 0; s < perSegment; s++)
                {
                    var t = s / (float)perSegment; var t2 = t * t; var t3 = t2 * t;
                    o.Add(0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (3f * p1 - p0 - 3f * p2 + p3) * t3));
                }
            }
            o.Add(pts[pts.Count - 1]);
            return o;
        }

        /// <summary>The same spline for the ups, so the band turns as smoothly as it bends.</summary>
        static List<Vector3> CurveUps(IReadOnlyList<Vector3> ups, int perSegment, int count)
        {
            var o = Curve(ups, perSegment);
            while (o.Count < count) o.Add(o.Count > 0 ? o[o.Count - 1] : Vector3.up);
            return o;
        }

        /// <summary>
        /// The band's width at <paramref name="t"/> (0 start .. 1 end): it swells over the first tenth as the brush is
        /// pressed, holds, and tapers over the last third as it lifts - the shape of a loaded brush stroke.
        /// </summary>
        public static float WidthAt(float t) => WidthAt(t, 1f);

        /// <summary>As above, <paramref name="scale"/> times as wide: the stroke at 1.5x in Your world is 1.5x as broad.</summary>
        public static float WidthAt(float t, float scale)
        {
            var press = Mathf.SmoothStep(0.35f, 1f, Mathf.Clamp01(t / 0.1f));
            var lift = Mathf.SmoothStep(0.15f, 1f, Mathf.Clamp01((1f - t) / 0.33f));
            return Width * scale * press * lift;
        }

        /// <summary>
        /// Fill <paramref name="mesh"/> with the stroke as a flat band: two vertices per spline sample, spread across
        /// the brush's flat side (its up), bent away from the direction of travel so it never folds edge-on.
        /// </summary>
        public static void BuildRibbon(Mesh mesh, IReadOnlyList<Vector3> pts, IReadOnlyList<Vector3> ups, int perSegment = 4, float widthScale = 1f)
        {
            mesh.Clear();
            if (pts == null || pts.Count < 2) return;
            var c = Curve(pts, perSegment);
            var u = CurveUps(ups, perSegment, c.Count);
            var n = c.Count;
            var verts = new Vector3[n * 2]; var norms = new Vector3[n * 2]; var uv = new Vector2[n * 2];
            var tris = new int[(n - 1) * 6];
            var length = 0f; var along = new float[n];
            for (var i = 1; i < n; i++) { length += Vector3.Distance(c[i - 1], c[i]); along[i] = length; }
            var lastSide = Vector3.right;
            for (var i = 0; i < n; i++)
            {
                var fwd = (c[Mathf.Min(i + 1, n - 1)] - c[Mathf.Max(i - 1, 0)]);
                if (fwd.sqrMagnitude < 1e-10f) fwd = Vector3.forward;
                fwd.Normalize();
                // Across the band: the brush's up with the travel direction taken out of it.
                var side = Vector3.ProjectOnPlane(u[i], fwd);
                if (side.sqrMagnitude < 1e-6f) side = lastSide;
                side.Normalize();
                if (Vector3.Dot(side, lastSide) < 0f && i > 0) side = -side;   // no half twists
                lastSide = side;
                var t = length > 1e-5f ? along[i] / length : 0f;
                var half = WidthAt(t, widthScale) * 0.5f;
                verts[i * 2] = c[i] - side * half; verts[i * 2 + 1] = c[i] + side * half;
                var normal = Vector3.Cross(fwd, side).normalized;
                norms[i * 2] = norms[i * 2 + 1] = normal;
                uv[i * 2] = new Vector2(t, 0f); uv[i * 2 + 1] = new Vector2(t, 1f);
            }
            for (var i = 0; i < n - 1; i++)
            {
                var k = i * 6; var a = i * 2;
                tris[k] = a; tris[k + 1] = a + 2; tris[k + 2] = a + 1;
                tris[k + 3] = a + 1; tris[k + 4] = a + 2; tris[k + 5] = a + 3;
            }
            mesh.vertices = verts; mesh.normals = norms; mesh.uv = uv; mesh.triangles = tris;
            mesh.RecalculateBounds();
        }

        /// <summary>
        /// A brush's flat side for a stroke that was saved without one (the journey record keeps points only): level,
        /// across the direction of travel, so a stroke hung overhead shows its full breadth to someone under it.
        /// </summary>
        public static List<Vector3> LevelUps(IReadOnlyList<Vector3> pts)
        {
            var ups = new List<Vector3>(pts.Count);
            var last = Vector3.right;
            for (var i = 0; i < pts.Count; i++)
            {
                var d = pts[Mathf.Min(i + 1, pts.Count - 1)] - pts[Mathf.Max(i - 1, 0)];
                var side = Vector3.Cross(Vector3.up, d);
                if (side.sqrMagnitude < 1e-8f) side = last;
                side.Normalize(); last = side;
                ups.Add(side);
            }
            return ups;
        }

        /// <summary>
        /// The stroke in words, for the masters to answer: how long, which way it travelled, whether it wandered or
        /// went straight, and where it was quick. Never numbers in the sentence - they recite numbers back.
        /// </summary>
        public static string Describe(IReadOnlyList<Vector3> pts)
        {
            if (pts == null || pts.Count < 2) return "a single touch of the brush";
            float length = 0f, turning = 0f;
            for (var i = 1; i < pts.Count; i++) length += Vector3.Distance(pts[i - 1], pts[i]);
            for (var i = 1; i < pts.Count - 1; i++)
            {
                var a = pts[i] - pts[i - 1]; var b = pts[i + 1] - pts[i];
                if (a.sqrMagnitude > 1e-8f && b.sqrMagnitude > 1e-8f) turning += Vector3.Angle(a, b);
            }
            var start = pts[0]; var end = pts[pts.Count - 1];
            var chord = Vector3.Distance(start, end);
            var rise = end.y - start.y;
            var size = length < 0.35f ? "short" : length < 1.2f ? "arm's-length" : "long, sweeping";
            var way = Mathf.Abs(rise) > 0.25f && Mathf.Abs(rise) > chord * 0.5f ? (rise > 0f ? "rising upward" : "falling downward") : "running sideways";
            var path = turning > 540f ? "curling back on itself" : turning > 200f ? "winding" : turning > 70f ? "gently curved" : "nearly straight";
            var closed = chord < length * 0.25f && length > 0.3f ? ", ending close to where it began" : "";
            return "a " + size + " stroke, " + way + ", " + path + closed;
        }
    }
}
