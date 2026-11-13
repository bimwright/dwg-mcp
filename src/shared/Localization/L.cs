using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Localization
{
    public static class L
    {
        private static readonly Lazy<StringTable> English = new Lazy<StringTable>(() =>
            StringTable.Load(LocaleResolver.Default, EmbeddedCatalog.Load, PluginLog.Debug));
        private static volatile StringTable _session;
        private static StringTable Current => _session ?? English.Value;

        internal static string Locale => Current.Locale;
        internal static CultureInfo Culture => Current.Culture;

        // Called before creating UI. Settings only saves the next session's preference.
        internal static void Initialize(string requested)
        {
            var locale = LocaleResolver.Resolve(requested);
            _session = locale == LocaleResolver.Default ? English.Value
                : StringTable.Load(locale, EmbeddedCatalog.Load, PluginLog.Debug);
        }

        public static string T(string key, params (string Name, object Value)[] args) => Current.Text(key, args);
    }

    internal sealed class StringTable
    {
        internal static readonly Regex Placeholder = new Regex(@"\{(?<name>[A-Za-z][A-Za-z0-9_]*)(?<number>:n)?\}",
            RegexOptions.CultureInvariant);
        private readonly IReadOnlyDictionary<string, string> _english;
        private readonly IReadOnlyDictionary<string, string> _localized;

        private StringTable(string locale, IReadOnlyDictionary<string, string> english, IReadOnlyDictionary<string, string> localized)
        {
            Locale = locale;
            Culture = LocaleResolver.Culture(locale);
            _english = english;
            _localized = localized;
        }

        internal string Locale { get; }
        internal CultureInfo Culture { get; }

        internal static StringTable Load(string locale, Func<string, IReadOnlyDictionary<string, string>> load, Action<string> log)
        {
            var english = load(LocaleResolver.Default);
            if (english == null) log?.Invoke("[l10n] English catalog is missing or invalid.");
            if (locale == LocaleResolver.Default) return new StringTable(locale, english, null);

            var translated = load(locale);
            if (translated == null)
            {
                log?.Invoke("[l10n] Catalog missing or invalid: " + locale + ". Using English.");
                return new StringTable(LocaleResolver.Default, english, null);
            }
            return new StringTable(locale, english, translated);
        }

        internal string Text(string key, params (string Name, object Value)[] args)
        {
            if (key == null) return string.Empty;
            string template = null;
            if (!(_localized?.TryGetValue(key, out template) ?? false)
                && !(_english?.TryGetValue(key, out template) ?? false)) return key;
            if (args == null || args.Length == 0 || template.IndexOf('{') < 0) return template;

            return Placeholder.Replace(template, match =>
            {
                foreach (var (name, value) in args)
                    if (name == match.Groups["name"].Value)
                        return Format(value, match.Groups["number"].Success);
                return match.Value;
            });
        }

        private string Format(object value, bool numeric)
        {
            if (value == null) return string.Empty;
            if (numeric && value is IFormattable number && IsNumber(value))
            {
                try { return number.ToString("N0", Culture); }
                catch (FormatException) { }
            }

            try { return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty; }
            catch { return string.Empty; }
        }

        private static bool IsNumber(object value) => value is byte or sbyte or short or ushort
            or int or uint or long or ulong or float or double or decimal;
    }

    internal static class EmbeddedCatalog
    {
        internal static IReadOnlyDictionary<string, string> Load(string locale)
        {
            try
            {
                using (var stream = typeof(EmbeddedCatalog).Assembly.GetManifestResourceStream(
                    "Bimwright.Dwg.Localization.strings." + locale + ".json"))
                {
                    if (stream == null) return null;
                    using (var reader = new StreamReader(stream, new UTF8Encoding(false, true), false))
                        return Parse(reader.ReadToEnd(), locale);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is JsonException || ex is DecoderFallbackException)
            {
                return null;
            }
        }

        internal static IReadOnlyDictionary<string, string> Parse(string json, string locale)
        {
            using (var text = new StringReader(json))
            using (var reader = new JsonTextReader(text) { DateParseHandling = DateParseHandling.None })
            {
                var root = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                if (reader.Read()) throw new JsonSerializationException("Unexpected content after catalog.");
                var entries = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var entry in root.Properties())
                {
                    if (entry.Name == "_meta")
                    {
                        if (!(entry.Value is JObject meta) || (meta["locale"] != null
                            && (meta["locale"].Type != JTokenType.String || !string.Equals((string)meta["locale"], locale, StringComparison.Ordinal))))
                            throw new JsonSerializationException("Invalid catalog metadata.");
                        continue;
                    }
                    if (entry.Value.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)entry.Value))
                        throw new JsonSerializationException("Catalog entry must be a non-empty string: " + entry.Name);
                    entries.Add(entry.Name, (string)entry.Value);
                }
                return entries;
            }
        }
    }
}
