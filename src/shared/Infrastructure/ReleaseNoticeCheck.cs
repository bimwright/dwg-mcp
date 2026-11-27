using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Bimwright.Dwg.Plugin;

internal static class ReleaseNoticeCheck
{
    internal static readonly TimeSpan Interval = TimeSpan.FromHours(24);
    internal static readonly TimeSpan InteractiveInterval = TimeSpan.FromMinutes(30);

    internal static async Task<StableRelease> CheckAsync(
        string path,
        Func<CancellationToken, Task<StableRelease>> fetch,
        CancellationToken ct,
        TimeSpan? interval = null)
    {
        var effectiveInterval = interval ?? Interval;
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        // A per-user file lease covers the read, reservation and lookup across AutoCAD processes.
        using var state = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        using var reader = new StreamReader(state, Encoding.UTF8, false, 1024, leaveOpen: true);
        var raw = state.Length <= 8192 ? reader.ReadToEnd() : string.Empty;
        var lines = raw.Replace("\r\n", "\n").Split('\n');
        var now = DateTimeOffset.UtcNow;
        if (lines.Length >= 2 && DateTimeOffset.TryParseExact(lines[0], "O", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var attempted) && now - attempted < effectiveInterval)
        {
            var tag = lines[1].Trim();
            if (tag.Length == 0) return null;
            string notes = null;
            if (lines.Length > 2)
                notes = string.Join("\n", lines, 2, lines.Length - 2).Trim();
            return StableRelease.FromTag(tag, notes);
        }

        ct.ThrowIfCancellationRequested();
        state.SetLength(0);
        state.Position = 0;
        // Reserve the interval before the request, including when the network is unavailable.
        Append(state, now.ToString("O", CultureInfo.InvariantCulture));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(ReleaseMetadataClient.Timeout);
        var release = await fetch(timeout.Token).ConfigureAwait(false);
        // Append only after success so a partial cache write cannot erase the reservation.
        Append(state, release.Tag);
        if (!string.IsNullOrWhiteSpace(release.ReleaseNotes))
            Append(state, release.ReleaseNotes);
        return release;
    }

    internal static bool IsSkipped(string skipPath, string versionText)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(skipPath) || !File.Exists(skipPath)) return false;
            var saved = File.ReadAllText(skipPath, Encoding.UTF8).Trim().TrimStart('v', 'V');
            var candidate = (versionText ?? string.Empty).Trim().TrimStart('v', 'V');
            return saved.Length > 0 && string.Equals(saved, candidate, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    internal static void SkipVersion(string skipPath, string versionText)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(skipPath) || string.IsNullOrWhiteSpace(versionText)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(skipPath));
            File.WriteAllText(skipPath, versionText.Trim().TrimStart('v', 'V'), new UTF8Encoding(false));
        }
        catch { }
    }

    private static void Append(FileStream state, string line)
    {
        var bytes = Encoding.UTF8.GetBytes(line + "\n");
        state.Write(bytes, 0, bytes.Length);
        state.Flush(flushToDisk: true);
    }
}
