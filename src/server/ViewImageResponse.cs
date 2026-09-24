using System;
using System.IO;
using System.Security.Cryptography;
using ModelContextProtocol.Protocol;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Bimwright.Dwg.Plugin;

namespace Bimwright.Dwg.Server
{
    internal static class ViewImageResponse
    {
        internal const int MaxImageBytes = 8 * 1024 * 1024;

        internal static CallToolResult FromJson(string json)
        {
            var response = JObject.Parse(json);
            var result = new CallToolResult { IsError = response["ok"]?.Value<bool>() != true };
            ImageContentBlock image = null;
            if (result.IsError != true)
            {
                try
                {
                    var data = response["result"];
                    string path = (string)data?["output_path"];
                    string mime = Path.GetExtension(path)?.ToLowerInvariant() switch
                    {
                        ".png" => "image/png",
                        ".jpg" or ".jpeg" => "image/jpeg",
                        ".bmp" => "image/bmp",
                        _ => throw new InvalidOperationException("Capture returned an unsupported image format.")
                    };
                    byte[] bytes;
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        if (stream.Length <= 0 || stream.Length > MaxImageBytes)
                            throw new InvalidOperationException("Inline image must be at most 8 MiB; retry capture with a smaller pixel_size. The saved image remains available.");
                        bytes = new byte[(int)stream.Length];
                        stream.ReadExactly(bytes);
                    }
                    string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                    if (!string.Equals(hash, (string)data["sha256"], StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Saved capture bytes no longer match the capture SHA-256; capture again.");
                    image = ImageContentBlock.FromBytes(bytes, mime);
                    response["image_delivery"] = new JObject { ["ok"] = true, ["mime_type"] = mime };
                }
                catch (Exception ex)
                {
                    // Preserve successful capture metadata even if file access/delivery
                    // fails. Navigation has already completed; do not retry it blindly.
                    result.IsError = true;
                    response["image_delivery"] = new JObject
                    {
                        ["ok"] = false,
                        ["error"] = ErrorSanitizer.Sanitize(ex.Message)
                    };
                }
            }
            result.Content.Add(new TextContentBlock { Text = response.ToString(Formatting.None) });
            if (image != null) result.Content.Add(image);
            return result;
        }
    }
}
