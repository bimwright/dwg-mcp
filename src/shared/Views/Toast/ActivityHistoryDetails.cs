using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Views.Toast
{
    /// <summary>Session display copies only. Never retain mutable/raw call data.</summary>
    internal static class ActivityHistoryDetails
    {
        internal const int MaxCharacters = 4096;
        private const string Omitted = "[omitted: session detail limit]";
        internal const string CodeOutputOmitted = "Output/error omitted: code-capable calls may echo source.";
        // JSON fragments in prose/errors do not pass through structured-key redaction.
        private static readonly Regex QuotedSecret = new Regex(
            @"(?<prefix>[""'](?:[\w.-]*(?:password|secret|cookie)[\w.-]*|[\w.-]*(?:token|api[_-]?key)|authorization|credential)[""']\s*[:=]\s*)(?:""(?:\\.|[^""\\])*(?:""|\z)|'(?:\\.|[^'\\])*(?:'|\z)|[^\s,;}]+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        internal static bool MayEchoCode(string command, JToken parameters)
        {
            var name = ToolActivityClassifier.Normalize(command);
            if (name == "send_code" || name == "batch_execute" || name == "run_baked_tool" || name == "apply_bake") return true;
            var remaining = 256;
            return HasCode(parameters, 0, ref remaining);
        }

        private static bool HasCode(JToken token, int depth, ref int remaining)
        {
            if (token == null) return false;
            if (depth > 12 || --remaining < 0) return true;
            if (token is JObject obj)
                foreach (var property in obj.Properties())
                    if (IsCode(Key(property.Name)) || HasCode(property.Value, depth + 1, ref remaining)) return true;
            if (token is JArray array)
                foreach (var item in array)
                    if (HasCode(item, depth + 1, ref remaining)) return true;
            return false;
        }

        internal static string Text(string text)
            => Clip(ErrorSanitizer.Sanitize(BakeRedactor.RedactSource(
                QuotedSecret.Replace(text ?? string.Empty, match => match.Groups["prefix"].Value + "\"<secret>\""))));

        internal static string Json(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return string.Empty;
            var remaining = 256;
            return Clip(Redact(token, 0, ref remaining).ToString(Formatting.Indented));
        }

        internal static string Result(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return string.Empty;
            try { return Json(JToken.Parse(json)); }
            catch (JsonException) { return "[unavailable: invalid result JSON]"; }
        }

        private static JToken Redact(JToken token, int depth, ref int remaining)
        {
            if (depth > 12 || --remaining < 0) return new JValue(Omitted);
            if (token is JObject obj)
            {
                var copy = new JObject();
                foreach (var property in obj.Properties())
                {
                    if (remaining <= 0) { copy["_history_omitted"] = Omitted; break; }
                    var key = Key(property.Name);
                    var label = Text(property.Name);
                    if (IsCode(key)) copy[label] = BodyMetadata(property.Value);
                    else if (key.Contains("password") || key.Contains("secret") || key.EndsWith("token", StringComparison.Ordinal)
                        || key.EndsWith("apikey", StringComparison.Ordinal) || key == "authorization" || key == "credential" || key.Contains("cookie")) copy[label] = "<secret>";
                    else if (key.Contains("base64") || key == "imagedata") copy[label] = "[binary omitted]";
                    else copy[label] = Redact(property.Value, depth + 1, ref remaining);
                    remaining--;
                }
                return copy;
            }
            if (token is JArray array)
            {
                var copy = new JArray();
                foreach (var item in array)
                {
                    if (remaining <= 0) { copy.Add(Omitted); break; }
                    copy.Add(Redact(item, depth + 1, ref remaining));
                }
                return copy;
            }
            if (token.Type == JTokenType.String)
            {
                var value = token.Value<string>() ?? string.Empty;
                // Results sometimes embed JSON in a string; apply the same key policy.
                var trimmed = value.TrimStart();
                if (trimmed.StartsWith("{") || trimmed.StartsWith("["))
                    try { return new JValue(Redact(JToken.Parse(value), depth + 1, ref remaining).ToString(Formatting.None)); }
                    catch (JsonException) { return new JValue("[unavailable: invalid embedded JSON]"); }
                return new JValue(value.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ? "[binary omitted]" : Text(value));
            }
            return token.DeepClone();
        }

        private static string Key(string name) => name.Replace("_", "").Replace("-", "").ToLowerInvariant();
        private static bool IsCode(string key) => key == "code" || key == "sourcecode";
        private static JToken BodyMetadata(JToken value)
        {
            if (value.Type != JTokenType.String) return JValue.CreateNull();
            var body = value.Value<string>();
            using (var sha = SHA256.Create())
                return new JObject { ["length"] = body.Length, ["sha256"] = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(body))).Replace("-", "").ToLowerInvariant() };
        }

        private static string Clip(string text)
        {
            if (text.Length <= MaxCharacters) return text;
            var length = MaxCharacters - Omitted.Length - 1;
            if (char.IsHighSurrogate(text[length - 1])) length--;
            return text.Substring(0, length) + "\n" + Omitted;
        }
    }
}
