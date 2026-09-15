using System.Collections.Generic;
using System.Text;

namespace MusePico.Dialogue
{
    /// <summary>
    /// Splits a reading into sentence-bounded segments short enough for one T2A request.
    ///
    /// Ported from muse-infinity's <c>services/voiceNarrator.js</c>, and it is a correctness fix
    /// rather than a nicety. Her note records why, from a previous project: <b>MiniMax T2A
    /// synthesises ONE utterance per request, and a long reply that is not split gets cut off
    /// mid-sentence.</b>
    ///
    /// This project was sending the whole reading in one request. MEASURED: a 44-word reading is
    /// 232 characters, so a <b>compliant</b> 50-word reading is around 265 and stays comfortably
    /// inside one segment. Segmentation is therefore not the common path — <b>it is the safety net
    /// for the readings that breach the word limit</b>, and her own prompt notes record that they
    /// do: four breaches in thirty replies, one of them at 59 words, which is about 310 characters
    /// and would have been cut off mid-sentence with no error to show for it.
    ///
    /// Splitting on sentence boundaries, not on a character count, is the other half: a segment
    /// that ends mid-clause is synthesised with the wrong intonation, because the model cannot
    /// hear the rest of the sentence coming.
    ///
    /// Pure string work, so the boundaries are pinned by tests.
    /// </summary>
    public static class SpeechSegmenter
    {
        /// <summary>Her limit, kept identical so both projects segment the same reading the same way.</summary>
        public const int SegmentCharLimit = 280;

        static readonly char[] SentenceEnders = { '.', '!', '?', '…' };

        /// <summary>
        /// Segments in speaking order. Text at or under the limit comes back as a single segment,
        /// which is the common case and must not be disturbed.
        /// </summary>
        public static List<string> Split(string text, int limit = SegmentCharLimit)
        {
            var segments = new List<string>();
            var clean = (text ?? string.Empty).Trim();
            if (clean.Length == 0) return segments;
            if (clean.Length <= limit) { segments.Add(clean); return segments; }

            var current = new StringBuilder();
            foreach (var sentence in Sentences(clean))
            {
                // Break BEFORE adding, exactly as the original: a sentence that would overflow
                // starts the next segment rather than being chopped.
                if (current.Length > 0 && current.Length + sentence.Length > limit)
                {
                    segments.Add(current.ToString().Trim());
                    current.Clear();
                }

                current.Append(sentence);

                // A single sentence longer than the whole limit cannot be split on a boundary
                // that does not exist. Emit it and let the service deal with it — better one
                // over-long segment than silently dropping the words.
                if (current.Length >= limit && LooksComplete(current))
                {
                    segments.Add(current.ToString().Trim());
                    current.Clear();
                }
            }

            var tail = current.ToString().Trim();
            if (tail.Length > 0) segments.Add(tail);
            return segments;
        }

        /// <summary>
        /// Sentences, each keeping its terminator and trailing whitespace — the equivalent of the
        /// original's <c>/[^.!?…]+[.!?…]*\s*/g</c>. Text with no terminator at all comes back as
        /// one piece rather than as nothing.
        /// </summary>
        static IEnumerable<string> Sentences(string text)
        {
            var start = 0;

            for (var i = 0; i < text.Length; i++)
            {
                if (!IsEnder(text[i])) continue;

                // Absorb a run of terminators ("...", "?!") and the whitespace after them.
                var end = i;
                while (end + 1 < text.Length && IsEnder(text[end + 1])) end++;
                while (end + 1 < text.Length && char.IsWhiteSpace(text[end + 1])) end++;

                yield return text.Substring(start, end - start + 1);
                start = end + 1;
                i = end;
            }

            if (start < text.Length) yield return text.Substring(start);
        }

        static bool IsEnder(char c)
        {
            foreach (var ender in SentenceEnders) if (c == ender) return true;
            return false;
        }

        static bool LooksComplete(StringBuilder sb)
        {
            for (var i = sb.Length - 1; i >= 0; i--)
            {
                if (char.IsWhiteSpace(sb[i])) continue;
                return IsEnder(sb[i]);
            }
            return false;
        }
    }
}
