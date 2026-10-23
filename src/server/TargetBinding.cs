using System;
using System.Diagnostics;
using Bimwright.Dwg.Plugin;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Server
{
    internal sealed class TargetCall
    {
        internal DiscoveryInfo Discovery;
        internal long Generation;
        internal bool Confirmed;
        internal string DocumentId;
        internal string DocumentTitle;

        internal JObject Context => Discovery.Pid <= 0 || Discovery.ProcessStartUtc == null ? null : new JObject
        {
            ["protocol"] = 1, ["pid"] = Discovery.Pid,
            ["process_start_utc"] = Discovery.ProcessStartUtc,
            ["selection_confirmed"] = Confirmed, ["document_id"] = DocumentId
        };
    }

    // A gateway lifetime owns one binding. Discovery is used only on initial selection
    // or explicit switch; a lost instance never falls through to another candidate.
    internal sealed class TargetBinding
    {
        private readonly object gate = new object();
        private readonly Func<int, DateTime?> processStart;
        private TargetCall bound;
        private long generation;
        private bool lost;

        internal TargetBinding(Func<int, DateTime?> processStart = null)
        { this.processStart = processStart ?? Probe; }

        internal long Generation { get { lock (gate) return generation; } }

        internal TargetCall Capture(Func<DiscoveryInfo> discover, string command)
        {
            lock (gate)
            {
                if (bound == null)
                {
                    bound = Candidate(discover());
                    bound.Generation = ++generation;
                }
                Validate(bound);
                if (TargetContract.IsWrite(command) && !bound.Confirmed)
                    throw new InvalidOperationException("TARGET_UNCONFIRMED: call dwg_switch_target with the intended PID to verify the instance and active drawing before writes.");
                if (TargetContract.IsWrite(command) && command != "open_drawing" && bound.DocumentId == null)
                    throw new InvalidOperationException("DOCUMENT_UNCONFIRMED: verify the active drawing with dwg_switch_target before writes.");
                return Copy(bound);
            }
        }

        internal TargetCall Candidate(DiscoveryInfo discovery)
        {
            var copy = JObject.FromObject(discovery).ToObject<DiscoveryInfo>();
            copy.DiscoveryFile = discovery.DiscoveryFile;
            if (copy.Pid > 0 && copy.ProcessStartUtc == null) copy.ProcessStartUtc = processStart(copy.Pid);
            return new TargetCall { Discovery = copy };
        }

        // Called under the client's write gate immediately before transport bytes.
        internal void Validate(TargetCall call)
        {
            lock (gate)
            {
                if (call.Generation != generation)
                    throw new InvalidOperationException("TARGET_CHANGED: routing changed while this call was pending; command was not sent.");
                if (lost) throw new InvalidOperationException("TARGET_LOST: select and verify the intended PID again; no other instance was selected.");
                var expected = call.Discovery;
                if (expected.Pid <= 0) return; // Host-free/legacy transport probes have no process identity.
                var actual = processStart(expected.Pid);
                if (actual == null || actual != expected.ProcessStartUtc)
                {
                    lost = true;
                    throw new InvalidOperationException(actual == null
                        ? "TARGET_LOST: the selected process is unavailable; no other instance was selected."
                        : "TARGET_CHANGED: PID was reused by another process; select and verify the intended instance again.");
                }
            }
        }

        internal void Commit(long expectedGeneration, TargetCall candidate, JObject stamp, bool verify)
        {
            lock (gate)
            {
                if (generation != expectedGeneration)
                    throw new InvalidOperationException("TARGET_CHANGED: another switch completed during verification; this switch was not applied.");
                var actualStart = processStart(candidate.Discovery.Pid);
                if (actualStart == null || actualStart != candidate.Discovery.ProcessStartUtc)
                    throw new InvalidOperationException("TARGET_CHANGED: selected process changed during verification; this switch was not applied.");
                candidate.Confirmed = verify;
                candidate.DocumentId = verify ? stamp?.Value<string>("document_id") : null;
                candidate.DocumentTitle = verify ? stamp?.Value<string>("document_title") : null;
                candidate.Generation = ++generation;
                bound = Copy(candidate);
                lost = false;
            }
        }

        internal JObject Snapshot()
        {
            lock (gate)
            {
                if (bound != null && !lost) { try { Validate(bound); } catch (InvalidOperationException) { } }
                var info = bound?.Discovery;
                return new JObject
                {
                    ["state"] = bound == null ? "none" : lost ? "lost" : "bound",
                    ["generation"] = generation, ["selection_confirmed"] = bound?.Confirmed ?? false,
                    ["target"] = info?.Target, ["target_id"] = info?.TargetId,
                    ["pid"] = info == null ? JValue.CreateNull() : new JValue(info.Pid),
                    ["process_start_utc"] = info?.ProcessStartUtc,
                    ["document_id"] = bound?.DocumentId, ["document_title"] = bound?.DocumentTitle
                };
            }
        }

        private static TargetCall Copy(TargetCall call) => new TargetCall
        {
            Discovery = call.Discovery, Generation = call.Generation,
            Confirmed = call.Confirmed, DocumentId = call.DocumentId, DocumentTitle = call.DocumentTitle
        };

        private static DateTime? Probe(int pid)
        {
            try { using var process = Process.GetProcessById(pid); return process.HasExited ? null : process.StartTime.ToUniversalTime(); }
            catch { return null; }
        }
    }
}
