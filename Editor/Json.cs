using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace BaseGameLab.Vfx.Editor
{
    /// <summary>A read-only view of parsed JSON. A missing key gives an empty J, so optional fields read cleanly.</summary>
    internal readonly struct J
    {
        readonly object value;
        public J(object value) { this.value = value; }

        public bool Exists => value != null;
        public J this[string key] => value is Dictionary<string, object> d && d.TryGetValue(key, out var v) ? new J(v) : default;
        public J this[int index] => value is List<object> l && index < l.Count ? new J(l[index]) : default;
        public int Count => value is List<object> l ? l.Count : 0;
        public float F(float fallback = 0) => value is double d ? (float)d : fallback;
        public int I(int fallback = 0) => value is double d ? (int)d : fallback;
        public bool B(bool fallback = false) => value is bool b ? b : fallback;
        public string S(string fallback = null) => value as string ?? fallback;
        public Vector2 V2 => new Vector2(this[0].F(), this[1].F());

        public IEnumerable<J> Items
        {
            get { if (value is List<object> l) foreach (var x in l) yield return new J(x); }
        }

        public IEnumerable<KeyValuePair<string, J>> Pairs
        {
            get { if (value is Dictionary<string, object> d) foreach (var p in d) yield return new KeyValuePair<string, J>(p.Key, new J(p.Value)); }
        }

        public float[] Floats()
        {
            var a = new float[Count];
            for (int i = 0; i < a.Length; i++) a[i] = this[i].F();
            return a;
        }
    }

    /// <summary>A small strict JSON reader. The package has no dependencies, so it does not pull Newtonsoft into a game.</summary>
    internal static class Json
    {
        public static J Parse(string text)
        {
            int i = 0;
            var v = Value(text, ref i);
            Space(text, ref i);
            if (i != text.Length) throw Bad("text after the end of the document", i);
            return new J(v);
        }

        static FormatException Bad(string what, int at) => new FormatException($"{what} at character {at}");

        static void Space(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\n' || s[i] == '\r' || s[i] == '\t')) i++;
        }

        static object Value(string s, ref int i)
        {
            Space(s, ref i);
            if (i >= s.Length) throw Bad("unexpected end", i);
            switch (s[i])
            {
                case '{': return Obj(s, ref i);
                case '[': return Arr(s, ref i);
                case '"': return Str(s, ref i);
                case 't': return Word(s, ref i, "true", true);
                case 'f': return Word(s, ref i, "false", false);
                case 'n': return Word(s, ref i, "null", null);
                default: return Num(s, ref i);
            }
        }

        static object Word(string s, ref int i, string word, object v)
        {
            if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0) throw Bad("bad value", i);
            i += word.Length;
            return v;
        }

        static object Num(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            if (start == i || !double.TryParse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                throw Bad("bad number", start);
            return d;
        }

        static string Str(string s, ref int i)
        {
            var b = new StringBuilder();
            for (i++; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '"') { i++; return b.ToString(); }
                if (c != '\\') { b.Append(c); continue; }
                if (++i >= s.Length) break;
                switch (s[i])
                {
                    case 'n': b.Append('\n'); break;
                    case 't': b.Append('\t'); break;
                    case 'r': b.Append('\r'); break;
                    case 'b': b.Append('\b'); break;
                    case 'f': b.Append('\f'); break;
                    case 'u':
                        if (i + 4 >= s.Length) throw Bad("bad escape", i);
                        b.Append((char)Convert.ToInt32(s.Substring(i + 1, 4), 16));
                        i += 4;
                        break;
                    default: b.Append(s[i]); break;
                }
            }
            throw Bad("unterminated string", i);
        }

        static List<object> Arr(string s, ref int i)
        {
            var l = new List<object>();
            i++;
            Space(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return l; }
            while (true)
            {
                l.Add(Value(s, ref i));
                Space(s, ref i);
                if (i >= s.Length) throw Bad("unterminated array", i);
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; return l; }
                throw Bad("expected , or ]", i);
            }
        }

        static Dictionary<string, object> Obj(string s, ref int i)
        {
            var d = new Dictionary<string, object>();
            i++;
            Space(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return d; }
            while (true)
            {
                Space(s, ref i);
                if (i >= s.Length || s[i] != '"') throw Bad("expected a key", i);
                var k = Str(s, ref i);
                Space(s, ref i);
                if (i >= s.Length || s[i] != ':') throw Bad("expected :", i);
                i++;
                d[k] = Value(s, ref i);
                Space(s, ref i);
                if (i >= s.Length) throw Bad("unterminated object", i);
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; return d; }
                throw Bad("expected , or }", i);
            }
        }
    }
}
