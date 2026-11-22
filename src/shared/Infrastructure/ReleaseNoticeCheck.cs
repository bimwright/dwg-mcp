using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Bimwright.Dwg.Plugin;

internal static class ReleaseNoticeCheck
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    internal static async Task<StableRelease> CheckAsync(string path, Func<CancellationToken, Task<StableRelease>> fetch, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        // A per-user file lease covers the read, reservation and lookup across AutoCAD processes.
        using var state = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        using var reader = new StreamReader(state, Encoding.UTF8, false, 1024, leaveOpen: true);
        var lines = state.Length <= 512 ? reader.ReadToEnd().Split('\n') : Array.Empty<string>();
        var now = DateTimeOffset.UtcNow;
        if (lines.Length >= 2 && DateTimeOffset.TryParseExact(lines[0], "O", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var attempted) && now - attempted < Interval)
            return lines.Length == 3 && lines[2].Length == 0 ? StableRelease.FromTag(lines[1]) : null;

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
        return release;
    }

    private static void Append(FileStream state, string line)
    {
        var bytes = Encoding.UTF8.GetBytes(line + "\n");
        state.Write(bytes, 0, bytes.Length);
        state.Flush(flushToDisk: true);
    }
}
