using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Disappearance.UIControls
{
    internal static class Stage2Json
    {
        internal static bool TryParse(string json, out object value, out string error)
        {
            value = null;
            error = null;
            if (json == null) { error = "null_json"; return false; }
            if (Encoding.UTF8.GetByteCount(json) > 65536) { error = "message_too_large"; return false; }
            try
            {
                var parser = new Parser(json);
                value = parser.ParseValue();
                parser.SkipWhitespace();
                if (!parser.AtEnd) throw new FormatException("trailing_data");
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        internal static bool TryObject(object value, out Dictionary<string, object> result)
        {
            result = value as Dictionary<string, object>;
            return result != null;
        }

        internal static bool TryArray(Dictionary<string, object> source, string key, out List<object> value)
        {
            object raw;
            value = null;
            return source.TryGetValue(key, out raw) && (value = raw as List<object>) != null;
        }

        internal static bool TryString(Dictionary<string, object> source, string key, out string value)
        {
            object raw;
            value = null;
            return source.TryGetValue(key, out raw) && (value = raw as string) != null;
        }

        internal static bool TryBool(Dictionary<string, object> source, string key, out bool value)
        {
            object raw;
            value = false;
            if (!source.TryGetValue(key, out raw) || !(raw is bool)) return false;
            value = (bool)raw;
            return true;
        }

        internal static bool TryInteger(Dictionary<string, object> source, string key, out int value)
        {
            object raw;
            value = 0;
            if (!source.TryGetValue(key, out raw) || !(raw is long)) return false;
            long number = (long)raw;
            if (number < int.MinValue || number > int.MaxValue) return false;
            value = (int)number;
            return true;
        }

        internal static bool TryNumber(Dictionary<string, object> source, string key, out double value)
        {
            object raw;
            value = 0;
            if (!source.TryGetValue(key, out raw)) return false;
            if (raw is long) value = (long)raw;
            else if (raw is double) value = (double)raw;
            else return false;
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private sealed class Parser
        {
            private readonly string text;
            private int index;

            internal Parser(string text) { this.text = text; }
            internal bool AtEnd => index >= text.Length;

            internal void SkipWhitespace()
            {
                while (!AtEnd && char.IsWhiteSpace(text[index])) index++;
            }

            internal object ParseValue()
            {
                SkipWhitespace();
                if (AtEnd) throw new FormatException("unexpected_end");
                char c = text[index];
                if (c == '{') return ParseObject();
                if (c == '[') return ParseArray();
                if (c == '"') return ParseString();
                if (c == 't') { Expect("true"); return true; }
                if (c == 'f') { Expect("false"); return false; }
                if (c == 'n') { Expect("null"); return null; }
                if (c == '-' || char.IsDigit(c)) return ParseNumber();
                throw new FormatException("unexpected_token");
            }

            private Dictionary<string, object> ParseObject()
            {
                index++;
                var result = new Dictionary<string, object>(StringComparer.Ordinal);
                SkipWhitespace();
                if (!AtEnd && text[index] == '}') { index++; return result; }
                while (true)
                {
                    SkipWhitespace();
                    if (AtEnd || text[index] != '"') throw new FormatException("object_key_expected");
                    string key = ParseString();
                    if (result.ContainsKey(key)) throw new FormatException("duplicate_key");
                    SkipWhitespace();
                    if (AtEnd || text[index++] != ':') throw new FormatException("colon_expected");
                    result.Add(key, ParseValue());
                    SkipWhitespace();
                    if (AtEnd) throw new FormatException("unexpected_end");
                    char next = text[index++];
                    if (next == '}') return result;
                    if (next != ',') throw new FormatException("comma_expected");
                }
            }

            private List<object> ParseArray()
            {
                index++;
                var result = new List<object>();
                SkipWhitespace();
                if (!AtEnd && text[index] == ']') { index++; return result; }
                while (true)
                {
                    result.Add(ParseValue());
                    SkipWhitespace();
                    if (AtEnd) throw new FormatException("unexpected_end");
                    char next = text[index++];
                    if (next == ']') return result;
                    if (next != ',') throw new FormatException("comma_expected");
                }
            }

            private string ParseString()
            {
                index++;
                var result = new StringBuilder();
                while (!AtEnd)
                {
                    char c = text[index++];
                    if (c == '"') return result.ToString();
                    if (c < 0x20) throw new FormatException("control_character");
                    if (c != '\\') { result.Append(c); continue; }
                    if (AtEnd) throw new FormatException("unexpected_end");
                    c = text[index++];
                    switch (c)
                    {
                        case '"': result.Append('"'); break;
                        case '\\': result.Append('\\'); break;
                        case '/': result.Append('/'); break;
                        case 'b': result.Append('\b'); break;
                        case 'f': result.Append('\f'); break;
                        case 'n': result.Append('\n'); break;
                        case 'r': result.Append('\r'); break;
                        case 't': result.Append('\t'); break;
                        case 'u':
                            if (index + 4 > text.Length) throw new FormatException("bad_unicode_escape");
                            int code;
                            if (!int.TryParse(text.Substring(index, 4), NumberStyles.HexNumber,
                                CultureInfo.InvariantCulture, out code)) throw new FormatException("bad_unicode_escape");
                            result.Append((char)code);
                            index += 4;
                            break;
                        default: throw new FormatException("bad_escape");
                    }
                }
                throw new FormatException("unterminated_string");
            }

            private object ParseNumber()
            {
                int start = index;
                if (text[index] == '-') index++;
                if (AtEnd) throw new FormatException("bad_number");
                if (text[index] == '0') index++;
                else
                {
                    if (!char.IsDigit(text[index])) throw new FormatException("bad_number");
                    while (!AtEnd && char.IsDigit(text[index])) index++;
                }
                bool fractional = false;
                if (!AtEnd && text[index] == '.')
                {
                    fractional = true; index++;
                    int digits = index;
                    while (!AtEnd && char.IsDigit(text[index])) index++;
                    if (digits == index) throw new FormatException("bad_number");
                }
                if (!AtEnd && (text[index] == 'e' || text[index] == 'E'))
                {
                    fractional = true; index++;
                    if (!AtEnd && (text[index] == '+' || text[index] == '-')) index++;
                    int digits = index;
                    while (!AtEnd && char.IsDigit(text[index])) index++;
                    if (digits == index) throw new FormatException("bad_number");
                }
                string token = text.Substring(start, index - start);
                if (!fractional)
                {
                    long integer;
                    if (long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out integer))
                        return integer;
                }
                double number;
                if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out number) ||
                    double.IsNaN(number) || double.IsInfinity(number)) throw new FormatException("bad_number");
                return number;
            }

            private void Expect(string expected)
            {
                if (index + expected.Length > text.Length ||
                    !string.Equals(text.Substring(index, expected.Length), expected, StringComparison.Ordinal))
                    throw new FormatException("unexpected_token");
                index += expected.Length;
            }
        }
    }
}
