using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MuseXR.DynamicWorlds
{
    /// <summary>
    /// Times the steps of one generation. Pure: the caller supplies the clock reading, so tests
    /// can drive it and the headset can feed it Time.realtimeSinceStartup. Every line it produces
    /// starts with <see cref="Tag"/> so <c>adb logcat -s Unity | findstr DynamicWorld</c> finds them.
    /// </summary>
    public sealed class StepClock
    {
        public const string Tag = "[DynamicWorld]";

        public readonly struct Step
        {
            public readonly string Name;
            public readonly double Seconds;
            public readonly string Detail;
            public Step(string name, double seconds, string detail) { Name = name; Seconds = seconds; Detail = detail; }
        }

        readonly List<Step> _steps = new List<Step>();
        readonly double _origin;
        double _stepStart;
        string _current;

        public StepClock(double now) { _origin = now; _stepStart = now; }

        public IReadOnlyList<Step> Steps => _steps;
        public string Current => _current;

        /// <summary>Starts a step; returns the log line.</summary>
        public string Begin(string name, double now)
        {
            _current = name;
            _stepStart = now;
            return $"{Tag} t={Since(now)} BEGIN {name}";
        }

        /// <summary>Ends the current step; returns the log line.</summary>
        public string End(double now, string detail = null)
        {
            double secs = now - _stepStart;
            _steps.Add(new Step(_current, secs, detail));
            string line = $"{Tag} t={Since(now)} END   {_current}  {F(secs)} s" + (string.IsNullOrEmpty(detail) ? "" : "  " + detail);
            _current = null;
            return line;
        }

        public string Note(string text, double now) => $"{Tag} t={Since(now)} {text}";

        /// <summary>One block with every step's time and the total, for the log and the panel.</summary>
        public string Summary(double now)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{Tag} SUMMARY  total {F(now - _origin)} s since launch");
            foreach (var s in _steps)
                sb.AppendLine($"{Tag}   {s.Name,-28} {F(s.Seconds),8} s" + (string.IsNullOrEmpty(s.Detail) ? "" : "  " + s.Detail));
            return sb.ToString().TrimEnd();
        }

        string Since(double now) => F(now - _origin);
        static string F(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);
    }
}
