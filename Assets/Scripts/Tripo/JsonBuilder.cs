using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MusePico.Tripo
{
    /// <summary>
    /// A tiny append-only JSON object writer.
    ///
    /// Unity's <c>JsonUtility</c> cannot do the one thing every Tripo request needs: leave a field
    /// out. It serialises every public field, so an unset <c>texture</c> would go over the wire as
    /// <c>"texture": false</c> and silently produce an untextured mesh that still costs credits.
    /// Nullable types do not help — JsonUtility does not support them either.
    ///
    /// So: every <c>Add</c> overload takes a nullable and skips a null. What you set is what is
    /// sent, and the tests assert the exact byte-for-byte body.
    /// </summary>
    public sealed class JsonBuilder
    {
        readonly StringBuilder _sb = new StringBuilder("{");
        bool _empty = true;

        public JsonBuilder Add(string key, string value)
        {
            if (value == null) return this;
            Key(key);
            WriteString(_sb, value);
            return this;
        }

        public JsonBuilder Add(string key, bool? value)
        {
            if (!value.HasValue) return this;
            Key(key);
            _sb.Append(value.Value ? "true" : "false");
            return this;
        }

        public JsonBuilder Add(string key, int? value)
        {
            if (!value.HasValue) return this;
            Key(key);
            _sb.Append(value.Value.ToString(CultureInfo.InvariantCulture));
            return this;
        }

        public JsonBuilder Add(string key, long? value)
        {
            if (!value.HasValue) return this;
            Key(key);
            _sb.Append(value.Value.ToString(CultureInfo.InvariantCulture));
            return this;
        }

        /// <summary>Writes an already-serialised JSON value verbatim. Null is skipped.</summary>
        public JsonBuilder AddRaw(string key, string json)
        {
            if (json == null) return this;
            Key(key);
            _sb.Append(json);
            return this;
        }

        /// <summary>Writes a nested object. A null or empty builder is skipped.</summary>
        public JsonBuilder Add(string key, JsonBuilder value)
        {
            if (value == null || value._empty) return this;
            return AddRaw(key, value.ToString());
        }

        public JsonBuilder AddStringArray(string key, IReadOnlyList<string> values)
        {
            if (values == null) return this;
            var inner = new StringBuilder("[");
            for (var i = 0; i < values.Count; i++)
            {
                if (i > 0) inner.Append(',');
                WriteString(inner, values[i] ?? string.Empty);
            }
            inner.Append(']');
            return AddRaw(key, inner.ToString());
        }

        public JsonBuilder AddObjectArray(string key, IReadOnlyList<JsonBuilder> values)
        {
            if (values == null) return this;
            var inner = new StringBuilder("[");
            for (var i = 0; i < values.Count; i++)
            {
                if (i > 0) inner.Append(',');
                inner.Append(values[i] == null ? "{}" : values[i].ToString());
            }
            inner.Append(']');
            return AddRaw(key, inner.ToString());
        }

        public bool IsEmpty => _empty;

        void Key(string key)
        {
            if (!_empty) _sb.Append(',');
            _empty = false;
            WriteString(_sb, key);
            _sb.Append(':');
        }

        static void WriteString(StringBuilder sb, string value)
        {
            sb.Append('"');
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        // Everything outside printable ASCII is escaped, so a multilingual prompt
                        // survives whatever encoding the transport happens to pick.
                        if (c < ' ' || c > '~')
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        public override string ToString() => _sb.ToString() + "}";
    }
}
