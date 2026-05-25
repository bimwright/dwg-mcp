using Newtonsoft.Json;

namespace Bimwright.Dwg.Plugin
{
    public static class ResponseSizeGuard
    {
        public const int DefaultMaxSerializedChars = 1000000;

        public static object ApplyResult(object result, int maxSerializedChars = DefaultMaxSerializedChars)
        {
            var json = JsonConvert.SerializeObject(result);
            if (json == null || json.Length <= maxSerializedChars)
            {
                return result;
            }

            var previewLength = maxSerializedChars < 0 ? 0 : maxSerializedChars;
            if (previewLength > json.Length)
            {
                previewLength = json.Length;
            }

            return new
            {
                truncated = true,
                original_length = json.Length,
                preview = json.Substring(0, previewLength)
            };
        }
    }
}
