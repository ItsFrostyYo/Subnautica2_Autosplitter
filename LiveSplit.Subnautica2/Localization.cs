using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LiveSplit.Subnautica2
{
    public static class Localization
    {
        private static IReadOnlyDictionary<string, string> _translations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly string[] ResourcePaths =
        {
            "LiveSplit.Subnautica2.Resources.RecipeNames.json"
        };

        private static string StripJsonComments(string s)
        {
            s = Regex.Replace(s, @"^\s*//.*$", "", RegexOptions.Multiline);
            s = Regex.Replace(s, @"/\*.*?\*/", "", RegexOptions.Singleline);
            return s;
        }

        private static string EscapeInvalidStringChars(string s)
        {
            var sb = new StringBuilder(s.Length + 64);
            bool inString = false;
            bool escaped = false;

            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];

                if (inString)
                {
                    if (escaped)
                    {
                        sb.Append(c);
                        escaped = false;
                    }
                    else
                    {
                        if (c == '\\') { sb.Append(c); escaped = true; }
                        else if (c == '"') { sb.Append(c); inString = false; }
                        else if (c == '\n') sb.Append("\\n");
                        else if (c == '\r') sb.Append("\\r");
                        else if (c == '\t') sb.Append("\\t");
                        else if (c < 0x20) sb.Append("\\u" + ((int)c).ToString("X4"));
                        else sb.Append(c);
                    }
                }
                else
                {
                    if (c == '"') { sb.Append(c); inString = true; }
                    else sb.Append(c);
                }
            }
            return sb.ToString();
        }

        private static T DeserializeWithComments<T>(string json)
        {
            var reader = new Utf8JsonReader(
                Encoding.UTF8.GetBytes(json),
                new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

            return JsonSerializer.Deserialize<T>(ref reader, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }

        public static void Load()
        {
            var asm = Assembly.GetExecutingAssembly();
            var translations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string resourcePath in ResourcePaths)
            {
                using (Stream stream = asm.GetManifestResourceStream(resourcePath))
                {
                    if (stream == null) throw new FileNotFoundException("Embedded resource not found: " + resourcePath);
                    using (var sr = new StreamReader(stream, Encoding.UTF8, true))
                    {
                        string json = EscapeInvalidStringChars(StripJsonComments(sr.ReadToEnd()));
                        var dictionary = DeserializeWithComments<Dictionary<string, string>>(json);
                        if (dictionary == null) continue;
                        foreach (var entry in dictionary)
                            translations[entry.Key] = entry.Value;
                    }
                }
            }

            _translations = translations;
        }

        public static string GetDisplayName(object key)
        {
            if (_translations == null)
                throw new InvalidOperationException("Translations not loaded.");

            var keyString = key.ToString();

            foreach (string candidate in new[] { keyString, "Ency_" + keyString, "Log_" + keyString, "EncyPath_" + keyString })
            {
                if (_translations.TryGetValue(candidate, out var value) && !string.IsNullOrWhiteSpace(value))
                    return CleanDisplayName(value);
            }

            string fallback = Regex.Replace(keyString, @"^(?:DA_|DAT_|BP_)+", "", RegexOptions.IgnoreCase);
            fallback = Regex.Replace(
                fallback,
                @"_(?:DatabankEntry|ItemType|Recipe|ConstructData|ItemBrushData(?:_Snap)?)$",
                "",
                RegexOptions.IgnoreCase);
            fallback = fallback.Replace('_', ' ');
            fallback = Regex.Replace(fallback, @"(?<=[a-z0-9])(?=[A-Z])", " ");
            return CleanDisplayName(fallback);
        }

        private static string CleanDisplayName(string value)
        {
            string cleaned = Regex.Replace(value ?? string.Empty, @"[\r\n\t]+", " ");
            cleaned = Regex.Replace(cleaned, @"^\s*#+\s*", string.Empty);
            cleaned = Regex.Replace(
                cleaned,
                @"\[\s*\[?\s*(?:deprecated|placeholder)[^\]]*\]\s*\]?|\(\s*(?:deprecated|placeholder)[^\)]*\)",
                string.Empty,
                RegexOptions.IgnoreCase);
            return Regex.Replace(cleaned, @"\s{2,}", " ").Trim();
        }

        public static string GetRawName(object value)
        {
            if (_translations == null)
                throw new InvalidOperationException("Translations not loaded.");

            var valueString = value.ToString();

            var key = _translations.FirstOrDefault(x => string.Equals(x.Value, valueString, StringComparison.OrdinalIgnoreCase)).Key;

            if (key != null)
                return key;

            return valueString;
        }
    }
}
