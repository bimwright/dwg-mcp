using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Bimwright.Dwg.Plugin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Server.Logging
{
    internal static class CallLogPrivacy
    {
        internal static JToken Redact(object value)
        {
            var token = value == null ? JValue.CreateNull() : value as JToken ?? JToken.FromObject(value);
            return RedactToken(token);
        }

        internal static string Serialize(object value)
            => Redact(value).ToString(Formatting.None);

        private static JToken RedactToken(JToken token)
        {
            if (token is JObject obj)
            {
                var copy = new JObject();
                foreach (var property in obj.Properties())
                {
                    var key = property.Name.Replace("_", "").Replace("-", "").ToLowerInvariant();
                    if (key == "code" || key == "sourcecode")
                    {
                        // Hash original UTF-8 bytes, not a redacted/normalized body.
                        // Null stays null; never stringify a non-string body into a log.
                        copy[property.Name] = property.Value.Type == JTokenType.String
                            ? BodyMetadata(property.Value.Value<string>()) : JValue.CreateNull();
                    }
                    else if (IsSecretKey(key))
                        copy[property.Name] = "<secret>";
                    else
                        copy[property.Name] = RedactToken(property.Value);
                }
                return copy;
            }
            if (token is JArray array)
                return new JArray(array.Select(RedactToken));
            if (token.Type == JTokenType.String)
                return new JValue(ErrorSanitizer.Sanitize(BakeRedactor.RedactSource(token.Value<string>())));
            return token.DeepClone();
        }

        private static bool IsSecretKey(string key)
            => key.Contains("password") || key.Contains("secret") || key.EndsWith("token", StringComparison.Ordinal)
                || key.EndsWith("apikey", StringComparison.Ordinal) || key == "authorization" || key == "credential";

        private static JObject BodyMetadata(string body)
        {
            using var sha = SHA256.Create();
            return new JObject
            {
                ["length"] = body.Length,
                ["sha256"] = Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(body))).ToLowerInvariant()
            };
        }
    }
}
