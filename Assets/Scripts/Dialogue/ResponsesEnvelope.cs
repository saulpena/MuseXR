using System;
using UnityEngine;

namespace MusePico.Dialogue
{
    /// <summary>
    /// Digs the model's text out of an OpenAI Responses envelope.
    ///
    /// The payload is <c>{ output: [ { content: [ { type, text } ] } ] }</c> — the reply is not at
    /// a fixed path, it is the first <c>output_text</c> content part anywhere in the output array.
    /// A response can also carry reasoning items with no content at all, so this walks rather than
    /// indexes.
    ///
    /// JsonUtility handles it: the shape is arrays of objects with known field names, and unknown
    /// fields are ignored. A hand-rolled parser here would be more code for the same result, and
    /// a reflective one would be the IL2CPP trap.
    /// </summary>
    public static class ResponsesEnvelope
    {
        [Serializable] class Envelope { public Output[] output; }
        [Serializable] class Output { public Content[] content; }
        [Serializable] class Content { public string type; public string text; }

        /// <summary>The first <c>output_text</c> part, or empty when there is none.</summary>
        public static string ExtractOutputText(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return string.Empty;

            Envelope envelope;
            try { envelope = JsonUtility.FromJson<Envelope>(json); }
            catch (Exception) { return string.Empty; }

            if (envelope?.output == null) return string.Empty;

            foreach (var item in envelope.output)
            {
                if (item?.content == null) continue;
                foreach (var content in item.content)
                {
                    if (content == null) continue;
                    if (content.type == "output_text" && !string.IsNullOrEmpty(content.text))
                        return content.text;
                }
            }

            return string.Empty;
        }
    }
}
