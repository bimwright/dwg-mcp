using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin
{
    /// <summary>Self-contained query checkpoints. The signing key never leaves this plugin process lifetime.</summary>
    internal static class EntityQueryCursor
    {
        internal const int MaxTokenLength = 1024;
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

        // The gateway links the shared contract but does not call Issue/Read.
        // Keeping the key behind a nested type avoids initializing it there.
        private static class Signing
        {
            internal static readonly byte[] Key = CreateKey();
            private static byte[] CreateKey()
            {
                var key = new byte[32];
                using (var random = RandomNumberGenerator.Create()) random.GetBytes(key);
                return key;
            }
        }

        internal static string Issue(string snapshot, string queryHash, string afterHandle)
        {
            if (!Context(snapshot) || !Context(queryHash) || !Handle(afterHandle, out var numeric)) throw Invalid();
            var payload = Utf8.GetBytes(new JObject
            {
                ["v"] = 1, ["snapshot"] = snapshot, ["query_hash"] = queryHash,
                ["after_handle"] = numeric.ToString("X16", CultureInfo.InvariantCulture)
            }.ToString(Formatting.None));
            var token = Encode(payload) + "." + Encode(Sign(payload));
            if (token.Length > MaxTokenLength) throw Invalid();
            return token;
        }

        internal static ulong Read(string token, string expectedSnapshot, string queryHash)
        {
            JObject payload;
            ulong after;
            try
            {
                if (token == null || token.Length == 0 || token.Length > MaxTokenLength || !Context(expectedSnapshot) || !Context(queryHash)) throw Invalid();
                var parts = token.Split('.');
                if (parts.Length != 2) throw Invalid();
                var bytes = Decode(parts[0]);
                var signature = Decode(parts[1]);
                if (!Equal(signature, Sign(bytes))) throw Invalid();
                payload = JObject.Parse(Utf8.GetString(bytes), new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                var handle = payload["after_handle"]?.Type == JTokenType.String ? payload.Value<string>("after_handle") : null;
                if (payload.Count != 4 || payload["v"]?.Type != JTokenType.Integer || payload.Value<int>("v") != 1
                    || payload["snapshot"]?.Type != JTokenType.String || !Context(payload.Value<string>("snapshot"))
                    || payload["query_hash"]?.Type != JTokenType.String || !Context(payload.Value<string>("query_hash"))
                    || !Handle(handle, out after) || handle != after.ToString("X16", CultureInfo.InvariantCulture)) throw Invalid();
            }
            catch (Exception ex) when (ex is ArgumentException || ex is FormatException || ex is JsonException
                || ex is OverflowException || ex is InvalidOperationException)
            { throw Invalid(); }
            if (!string.Equals(payload.Value<string>("query_hash"), queryHash, StringComparison.Ordinal))
                throw new InvalidOperationException("CURSOR_QUERY_MISMATCH: filters, space, bounds or fields changed; restart the query without a cursor.");
            if (!string.Equals(payload.Value<string>("snapshot"), expectedSnapshot, StringComparison.Ordinal))
                throw new InvalidOperationException("QUERY_CHANGED: the drawing lifetime or revision changed; restart the query without a cursor.");
            return after;
        }

        private static bool Context(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 128) return false;
            foreach (var c in value) if (c < ' ' || c > '~') return false;
            return true;
        }

        private static bool Handle(string value, out ulong handle)
        {
            handle = 0;
            if (string.IsNullOrEmpty(value) || value.Length > 16) return false;
            foreach (var c in value)
                if (!(c >= '0' && c <= '9' || c >= 'A' && c <= 'F' || c >= 'a' && c <= 'f')) return false;
            return ulong.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out handle) && handle != 0;
        }

        private static byte[] Sign(byte[] payload)
        { using (var hmac = new HMACSHA256(Signing.Key)) return hmac.ComputeHash(payload); }

        private static bool Equal(byte[] first, byte[] second)
        {
            if (first.Length != 32 || second.Length != 32) return false;
            var difference = 0;
            for (var i = 0; i < 32; i++) difference |= first[i] ^ second[i];
            return difference == 0;
        }

        private static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private static byte[] Decode(string value)
        {
            if (value.Length == 0) throw Invalid();
            foreach (var c in value)
                if (!(c >= 'A' && c <= 'Z' || c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '-' || c == '_')) throw Invalid();
            var base64 = value.Replace('-', '+').Replace('_', '/');
            var bytes = Convert.FromBase64String(base64.PadRight((base64.Length + 3) / 4 * 4, '='));
            if (Encode(bytes) != value) throw Invalid();
            return bytes;
        }

        private static InvalidOperationException Invalid()
            => new InvalidOperationException("CURSOR_INVALID: cursor verification failed; restart the query without a cursor.");
    }
}
