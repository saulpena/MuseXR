using System;
using System.Collections.Generic;

namespace MusePico.Dialogue
{
    /// <summary>One master's reading. Field names match the model's JSON so JsonUtility can fill it.</summary>
    [Serializable]
    public class Perspective
    {
        public string speakerId;
        public string speaker;
        public string text;
        /// <summary>One of the shared visual-effect vocabulary; drives scene lighting.</summary>
        public string effect;

        public override string ToString() => speaker + ": " + text;
    }

    /// <summary>
    /// Conformance checking for a returned perspective.
    ///
    /// Ported with its original reasoning intact: strict <c>json_schema</c> makes a malformed
    /// reply rare, not impossible, and the array-length keywords in the strict subset are
    /// <b>not provably enforced</b> by every proxy. So the count and the shape are asserted here
    /// as well. That DETECTS a bad reply; it does not PREVENT one — which is survivable only
    /// because a detected mismatch is retryable.
    ///
    /// Pure, so the rules are pinned by tests.
    /// </summary>
    public static class PerspectiveValidation
    {
        /// <summary>Returns null when valid, or a human-readable reason when not.</summary>
        public static string DescribeInvalid(Perspective item, IReadOnlyList<string> effects)
        {
            if (item == null) return "perspective was not an object";
            if (string.IsNullOrWhiteSpace(item.speakerId)) return "speakerId was empty";
            if (string.IsNullOrWhiteSpace(item.speaker)) return "speaker was empty";
            if (string.IsNullOrWhiteSpace(item.text)) return "text was empty";
            return DescribeInvalidEffect(item.effect, effects);
        }

        public static string DescribeInvalidEffect(string effect, IReadOnlyList<string> effects)
        {
            if (string.IsNullOrWhiteSpace(effect)) return "effect was empty";
            if (effects == null || effects.Count == 0) return null;

            foreach (var known in effects)
                if (string.Equals(known, effect, StringComparison.OrdinalIgnoreCase)) return null;

            return "effect \"" + effect + "\" is outside the shared vocabulary";
        }

        /// <summary>
        /// Checks a whole set: the right number, all valid, and no two masters claiming the same
        /// speakerId — which would put two readings under one name and lose one entirely.
        /// </summary>
        public static string DescribeInvalidSet(
            IReadOnlyList<Perspective> perspectives, int expected, IReadOnlyList<string> effects)
        {
            if (perspectives == null) return "perspectives was not an array";
            if (perspectives.Count != expected)
                return "expected " + expected + " perspectives, received " + perspectives.Count;

            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in perspectives)
            {
                var problem = DescribeInvalid(item, effects);
                if (problem != null) return problem;
                ids.Add(item.speakerId.Trim());
            }

            return ids.Count != expected ? "two perspectives claimed the same speakerId" : null;
        }
    }

    /// <summary>The whole answer to one question, plus how it was produced.</summary>
    public class DialogueResult
    {
        public List<Perspective> Perspectives = new List<Perspective>();
        /// <summary>False when this came from the offline fallback rather than the model.</summary>
        public bool Live;
        public string Model;
        public string Error;
        public float Seconds;

        public bool Success => Perspectives.Count > 0 && string.IsNullOrEmpty(Error);
    }
}
