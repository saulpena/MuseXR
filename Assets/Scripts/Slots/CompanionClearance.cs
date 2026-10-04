using System;
using System.Collections.Generic;

namespace MuseXR.Slots
{
    /// <summary>
    /// Saul, 4 Oct 2026: a companion must never be in front of the visitor's eyes. They walk alongside, at
    /// the sides and the edge of the view, and step aside; they are never in the way and never in the
    /// camera. This judges one companion against one eye pose. Pure: no scene, no rig.
    /// </summary>
    public static class CompanionClearance
    {
        /// <summary>A body nearer the eye than this fills the view (and the near plane clips it).</summary>
        public const float TooCloseMetres = 0.8f;
        /// <summary>Within this of the gaze, measured to the nearest edge of the body...</summary>
        public const float InTheWayDegrees = 25f;
        /// <summary>...and nearer than this, a companion blocks what the visitor is looking at. (3 m let a
        /// companion crossing the centre of the view at 3.5 m pass; Saul's rule is "never in front".)</summary>
        public const float InTheWayMetres = 4.0f;
        /// <summary>A companion's half width, for the edge of the body rather than its centre.</summary>
        public const float BodyRadius = 0.3f;
        /// <summary>How long a companion may be in the way while it steps aside (a turn sweeps the gaze
        /// across a companion standing at the side; that is passing, not blocking).</summary>
        public const float GraceSeconds = 0.6f;

        public enum Kind { Clear, InTheWay, TooClose }

        /// <summary>Flat distance from the eye (ex, ez) to the body's edge at (fx, fz), metres (never below 0).</summary>
        public static float EdgeDistance(float ex, float ez, float fx, float fz)
        {
            var d = MathF.Sqrt((fx - ex) * (fx - ex) + (fz - ez) * (fz - ez));
            return MathF.Max(0f, d - BodyRadius);
        }

        /// <summary>Degrees between the flat gaze (gx, gz) and the nearest edge of the body (0 when the gaze hits it).</summary>
        public static float EdgeAngle(float ex, float ez, float gx, float gz, float fx, float fz)
        {
            float tx = fx - ex, tz = fz - ez;
            var tl = MathF.Sqrt(tx * tx + tz * tz); var gl = MathF.Sqrt(gx * gx + gz * gz);
            if (tl < 1e-3f || gl < 1e-3f) return 0f;
            var cos = Math.Clamp((tx * gx + tz * gz) / (tl * gl), -1f, 1f);
            var centre = MathF.Acos(cos) * 180f / MathF.PI;
            var half = MathF.Atan2(BodyRadius, tl) * 180f / MathF.PI;
            return MathF.Max(0f, centre - half);
        }

        public static Kind Judge(float ex, float ez, float gx, float gz, float fx, float fz)
        {
            var d = EdgeDistance(ex, ez, fx, fz);
            if (d < TooCloseMetres) return Kind.TooClose;
            if (d < InTheWayMetres && EdgeAngle(ex, ez, gx, gz, fx, fz) < InTheWayDegrees) return Kind.InTheWay;
            return Kind.Clear;
        }
    }

    /// <summary>
    /// A run's record: per companion, how long each spell in the way lasted and how close anyone came.
    /// A spell in the way longer than <see cref="CompanionClearance.GraceSeconds"/> fails; so does any
    /// frame too close. Pure.
    /// </summary>
    public sealed class ClearanceLog
    {
        public sealed class Fault
        {
            public string Id, Phase;
            public CompanionClearance.Kind Kind;
            public float Seconds, Distance, Angle, Time;
            public override string ToString() =>
                $"{Kind} {Id} in '{Phase}' for {Seconds:F2} s at {Distance:F2} m, {Angle:F0} deg (t={Time:F1})";
        }

        readonly Dictionary<string, float> _spell = new Dictionary<string, float>();
        readonly Dictionary<string, Fault> _open = new Dictionary<string, Fault>();
        public readonly List<Fault> Faults = new List<Fault>();
        public int Frames { get; private set; }
        public float NearestMetres { get; private set; } = float.MaxValue;

        public bool Passed => Faults.Count == 0;

        /// <summary>
        /// One frame for one companion. Returns a fault the moment one is confirmed (to capture it), or null.
        /// </summary>
        public Fault Add(string id, string phase, float ex, float ez, float gx, float gz, float fx, float fz, float dt, float time)
        {
            Frames++;
            var kind = CompanionClearance.Judge(ex, ez, gx, gz, fx, fz);
            var dist = CompanionClearance.EdgeDistance(ex, ez, fx, fz);
            var ang = CompanionClearance.EdgeAngle(ex, ez, gx, gz, fx, fz);
            NearestMetres = MathF.Min(NearestMetres, dist);
            if (kind == CompanionClearance.Kind.Clear) { _spell[id] = 0f; _open.Remove(id); return null; }
            _spell.TryGetValue(id, out var s);
            s += dt; _spell[id] = s;
            if (_open.TryGetValue(id, out var open))
            {
                open.Seconds = s;
                if (kind == CompanionClearance.Kind.TooClose) open.Kind = kind;
                open.Distance = MathF.Min(open.Distance, dist);
                return null;
            }
            if (kind == CompanionClearance.Kind.TooClose || s > CompanionClearance.GraceSeconds)
            {
                var f = new Fault { Id = id, Phase = phase, Kind = kind, Seconds = s, Distance = dist, Angle = ang, Time = time };
                _open[id] = f;
                Faults.Add(f);
                return f;
            }
            return null;
        }

        public string Verdict() => Passed
            ? $"VERDICT: PASS ({Frames} companion-frames, nearest {NearestMetres:F2} m)"
            : $"VERDICT: NOT YET - {Faults.Count} fault(s), nearest {NearestMetres:F2} m";
    }
}
