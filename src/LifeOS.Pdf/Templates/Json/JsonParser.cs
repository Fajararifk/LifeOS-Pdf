using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LifeOS.Pdf.Templates.Json
{
    /// <summary>
    /// Strict RFC 8259 JSON parser (the core package cannot depend on System.Text.Json).
    /// Objects → <see cref="Dictionary{TKey, TValue}"/> (case-insensitive keys), arrays →
    /// <see cref="List{T}"/>, numbers → <see cref="decimal"/> (exact money values) or
    /// <see cref="double"/> when out of range, plus string, bool and null.
    /// </summary>
    internal sealed class JsonParser
    {
        private const int MaxDepth = 256;

        private readonly string _s;
        private int _pos;
        private int _depth;

        private JsonParser(string s)
        {
            _s = s;
        }

        public static object? Parse(string json)
        {
            if (json == null)
            {
                throw new ArgumentNullException(nameof(json));
            }

            var p = new JsonParser(json);
            p.SkipWhitespace();
            var value = p.ParseValue();
            p.SkipWhitespace();
            if (p._pos != json.Length)
            {
                throw p.Error("Unexpected text after the JSON value");
            }

            return value;
        }

        private object? ParseValue()
        {
            if (_pos >= _s.Length)
            {
                throw Error("Unexpected end of JSON");
            }

            switch (_s[_pos])
            {
                case '{':
                    return ParseObject();
                case '[':
                    return ParseArray();
                case '"':
                    return ParseString();
                case 't':
                    Expect("true");
                    return true;
                case 'f':
                    Expect("false");
                    return false;
                case 'n':
                    Expect("null");
                    return null;
                default:
                    if (_s[_pos] == '-' || (_s[_pos] >= '0' && _s[_pos] <= '9'))
                    {
                        return ParseNumber();
                    }

                    throw Error("Unexpected character '" + _s[_pos] + "'");
            }
        }

        private Dictionary<string, object?> ParseObject()
        {
            Enter();
            var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            _pos++; // {
            SkipWhitespace();
            if (Peek() == '}')
            {
                _pos++;
                _depth--;
                return result;
            }

            while (true)
            {
                SkipWhitespace();
                if (Peek() != '"')
                {
                    throw Error(Peek() == '}' ? "Trailing comma before '}' is not allowed in JSON" : "Expected a property name in double quotes");
                }

                var key = ParseString();
                SkipWhitespace();
                if (Peek() != ':')
                {
                    throw Error("Expected ':' after property name \"" + key + "\"");
                }

                _pos++;
                SkipWhitespace();
                result[key] = ParseValue();
                SkipWhitespace();
                var c = Peek();
                _pos++;
                if (c == '}')
                {
                    break;
                }

                if (c != ',')
                {
                    throw Error("Expected ',' or '}' in object", _pos - 1);
                }
            }

            _depth--;
            return result;
        }

        private List<object?> ParseArray()
        {
            Enter();
            var result = new List<object?>();
            _pos++; // [
            SkipWhitespace();
            if (Peek() == ']')
            {
                _pos++;
                _depth--;
                return result;
            }

            while (true)
            {
                SkipWhitespace();
                if (Peek() == ']')
                {
                    throw Error("Trailing comma before ']' is not allowed in JSON");
                }

                result.Add(ParseValue());
                SkipWhitespace();
                var c = Peek();
                _pos++;
                if (c == ']')
                {
                    break;
                }

                if (c != ',')
                {
                    throw Error("Expected ',' or ']' in array", _pos - 1);
                }
            }

            _depth--;
            return result;
        }

        private string ParseString()
        {
            _pos++; // opening quote
            var sb = new StringBuilder();
            while (true)
            {
                if (_pos >= _s.Length)
                {
                    throw Error("Unterminated string");
                }

                var c = _s[_pos++];
                if (c == '"')
                {
                    return sb.ToString();
                }

                if (c < 0x20)
                {
                    throw Error("Control characters must be escaped in JSON strings", _pos - 1);
                }

                if (c != '\\')
                {
                    sb.Append(c);
                    continue;
                }

                if (_pos >= _s.Length)
                {
                    throw Error("Unterminated escape sequence");
                }

                var e = _s[_pos++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (_pos + 4 > _s.Length || !int.TryParse(_s.Substring(_pos, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
                        {
                            throw Error("Invalid \\u escape", _pos - 2);
                        }

                        sb.Append((char)code); // surrogate pairs arrive as two escapes and join naturally
                        _pos += 4;
                        break;
                    default:
                        throw Error("Invalid escape '\\" + e + "'", _pos - 2);
                }
            }
        }

        private object ParseNumber()
        {
            var start = _pos;
            if (Peek() == '-')
            {
                _pos++;
            }

            if (Peek() == '0')
            {
                _pos++;
            }
            else if (Peek() >= '1' && Peek() <= '9')
            {
                while (char.IsDigit(Peek()))
                {
                    _pos++;
                }
            }
            else
            {
                throw Error("Invalid number");
            }

            if (Peek() == '.')
            {
                _pos++;
                if (!char.IsDigit(Peek()))
                {
                    throw Error("Expected digits after the decimal point");
                }

                while (char.IsDigit(Peek()))
                {
                    _pos++;
                }
            }

            if (Peek() == 'e' || Peek() == 'E')
            {
                _pos++;
                if (Peek() == '+' || Peek() == '-')
                {
                    _pos++;
                }

                if (!char.IsDigit(Peek()))
                {
                    throw Error("Expected digits in the exponent");
                }

                while (char.IsDigit(Peek()))
                {
                    _pos++;
                }
            }

            var text = _s.Substring(start, _pos - start);
            if (decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            {
                return d;
            }

            return double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        private void Expect(string word)
        {
            if (string.CompareOrdinal(_s, _pos, word, 0, word.Length) != 0)
            {
                throw Error("Unexpected token");
            }

            _pos += word.Length;
        }

        private void Enter()
        {
            if (++_depth > MaxDepth)
            {
                throw Error("JSON is nested more than " + MaxDepth + " levels deep");
            }
        }

        private char Peek() => _pos < _s.Length ? _s[_pos] : '\0';

        private void SkipWhitespace()
        {
            while (_pos < _s.Length && (_s[_pos] == ' ' || _s[_pos] == '\t' || _s[_pos] == '\n' || _s[_pos] == '\r'))
            {
                _pos++;
            }
        }

        private TemplateException Error(string message, int? at = null)
        {
            var position = at ?? _pos;
            int line = 1, column = 1;
            for (var i = 0; i < position && i < _s.Length; i++)
            {
                if (_s[i] == '\n')
                {
                    line++;
                    column = 1;
                }
                else
                {
                    column++;
                }
            }

            return new TemplateException("Invalid JSON: " + message + ".", line, column);
        }
    }
}
