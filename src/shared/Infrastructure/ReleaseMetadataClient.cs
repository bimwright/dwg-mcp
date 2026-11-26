#nullable enable
using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin;

internal sealed class StableRelease
{
    internal string Tag { get; }
    internal string VersionText { get; }
    internal Version Version { get; }
    internal string? ReleaseNotes { get; }
    internal string Url => "https://github.com/bimwright/dwg-mcp/releases/tag/" + Tag;

    private StableRelease(string tag, string versionText, Version version, string? releaseNotes)
    {
        Tag = tag;
        VersionText = versionText;
        Version = version;
        ReleaseNotes = string.IsNullOrWhiteSpace(releaseNotes) ? null : releaseNotes;
    }

    internal static StableRelease? FromTag(string? tag, string? releaseNotes = null)
    {
        if (tag == null) return null;
        var text = tag.StartsWith("v", StringComparison.Ordinal) ? tag.Substring(1) : tag;
        return TryVersion(text, out var version) ? new StableRelease(tag, text, version!, releaseNotes) : null;
    }

    internal static string? FormatReleaseNotes(string? markdown, int maxChars = 1500)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return null;
        var lines = markdown!.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var sb = new StringBuilder();
        var prevBlank = true;
        foreach (var raw in lines)
        {
            var trimmed = raw.Trim();
            if (trimmed.StartsWith("### Distribution", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("### Validation", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("Source commit:", StringComparison.OrdinalIgnoreCase))
                break;

            if (trimmed.Length == 0)
            {
                if (!prevBlank && sb.Length > 0)
                {
                    sb.AppendLine();
                    prevBlank = true;
                }
                continue;
            }

            var line = StripMarkdownInline(trimmed);
            if (line.StartsWith("### ") || line.StartsWith("## ") || line.StartsWith("# "))
                line = line.TrimStart('#', ' ');
            else if (line.StartsWith("- ") || line.StartsWith("* "))
                line = "• " + line.Substring(2).Trim();

            if (sb.Length > 0 && sb.Length + line.Length + 2 > maxChars)
            {
                var remaining = maxChars - sb.Length - 1;
                if (remaining > 20)
                    sb.Append(line.Substring(0, remaining).TrimEnd()).Append('…');
                else
                    sb.Append('…');
                break;
            }

            sb.AppendLine(line);
            prevBlank = false;
        }
        var result = sb.ToString().Trim();
        return result.Length == 0 ? null : result;
    }

    private static string StripMarkdownInline(string text)
    {
        var sb = new StringBuilder(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            if (text[i] == '[')
            {
                var closeBracket = text.IndexOf(']', i + 1);
                if (closeBracket > i && closeBracket + 1 < text.Length && text[closeBracket + 1] == '(')
                {
                    var closeParen = text.IndexOf(')', closeBracket + 2);
                    if (closeParen > closeBracket + 1)
                    {
                        sb.Append(text, i + 1, closeBracket - i - 1);
                        i = closeParen + 1;
                        continue;
                    }
                }
            }
            sb.Append(text[i++]);
        }
        return sb.ToString().Replace("**", string.Empty).Replace("`", string.Empty);
    }

    internal static bool TryVersion(string value, out Version? version)
    {
        version = null;
        return Regex.IsMatch(value, "\\A(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\z")
            && Version.TryParse(value, out version);
    }
}

internal sealed class ReleaseMetadataClient
{
    internal const int MaxResponseBytes = 256 * 1024;
    internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private const string RepositoryApi = "https://api.github.com/repos/bimwright/dwg-mcp";
    private static readonly HttpClient SharedClient = new(new HttpClientHandler { AllowAutoRedirect = false });
    private readonly HttpClient _client;
    private readonly string _version;

    internal ReleaseMetadataClient(string version, HttpClient? client = null)
    {
        _version = version;
        _client = client ?? SharedClient;
    }

    internal async Task<StableRelease> GetLatestAsync(CancellationToken ct)
    {
        var metadata = await GetAsync("/releases/latest", ct).ConfigureAwait(false);
        if (metadata["draft"]?.Type != JTokenType.Boolean || metadata["prerelease"]?.Type != JTokenType.Boolean
            || metadata.Value<bool>("draft") || metadata.Value<bool>("prerelease") || metadata["tag_name"]?.Type != JTokenType.String)
            throw new InvalidDataException("The API did not return a published stable release.");
        var notes = StableRelease.FormatReleaseNotes(metadata["body"]?.Type == JTokenType.String ? metadata.Value<string>("body") : null);
        return StableRelease.FromTag(metadata.Value<string>("tag_name"), notes)
            ?? throw new InvalidDataException("Release version is not a supported semantic version.");
    }

    internal async Task<JObject> GetAsync(string path, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, RepositoryApi + path);
        request.Headers.UserAgent.ParseAdd("dwg-mcp/" + _version);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
#if NETFRAMEWORK
            throw new HttpRequestException("Release API failed.");
#else
            throw new HttpRequestException("Release API failed.", null, response.StatusCode);
#endif
        if (response.Content.Headers.ContentLength > MaxResponseBytes) throw new InvalidDataException("Metadata exceeds the response limit.");
#if NETFRAMEWORK
        // The legacy response stream may ignore the read token after headers arrive.
        using var closeOnCancel = ct.Register(response.Dispose);
        using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
#else
        using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
#endif
        using var bytes = new MemoryStream();
        var buffer = new byte[4096];
        int count;
        while ((count = await stream.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
        {
            if (bytes.Length + count > MaxResponseBytes) throw new InvalidDataException("Metadata exceeds the response limit.");
            bytes.Write(buffer, 0, count);
        }
        using var reader = new JsonTextReader(new StringReader(Encoding.UTF8.GetString(bytes.ToArray())))
            { MaxDepth = 32, DateParseHandling = DateParseHandling.None };
        var metadata = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
        if (reader.Read()) throw new InvalidDataException("Trailing metadata.");
        return metadata;
    }
}
