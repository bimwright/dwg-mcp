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
        internal string ExpectedDocumentId;

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
                    throw new InvalidOperationException("TARGET_UNCONFIRMED: the instance and active drawing must be verified before writes. " + Recovery());
                if (TargetContract.IsWrite(command) && command != "open_drawing" && bound.DocumentId == null)
                    throw new InvalidOperationException("DOCUMENT_UNCONFIRMED: the active drawing must be verified before writes. " + Recovery());
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

        internal void PrepareSwitch(long expectedGeneration, TargetCall candidate, string selector, bool rebind, string expectedDocumentId)
        {
            lock (gate)
            {
                if (generation != expectedGeneration)
                    throw new InvalidOperationException("TARGET_CHANGED: another switch completed during target resolution; this switch was not applied. " + Recovery());
                CheckSwitch(candidate, selector, rebind);
                candidate.ExpectedDocumentId = expectedDocumentId ?? (SameInstance(bound, candidate) && !rebind
                    ? bound.DocumentId ?? bound.ExpectedDocumentId : null);
            }
        }

        // Called under the client's write gate immediately before transport bytes.
        internal void Validate(TargetCall call)
        {
            lock (gate)
            {
                if (call.Generation != generation)
                    throw new InvalidOperationException("TARGET_CHANGED: routing changed while this call was pending; command was not sent. " + Recovery());
                if (lost) throw new InvalidOperationException("TARGET_LOST: no other instance was selected. " + Recovery());
                var expected = call.Discovery;
                if (expected.Pid <= 0) return; // Host-free/legacy transport probes have no process identity.
                var actual = processStart(expected.Pid);
                if (actual == null || actual != expected.ProcessStartUtc)
                {
                    lost = true;
                    throw new InvalidOperationException(actual == null
                        ? "TARGET_LOST: the selected process is unavailable; no other instance was selected. " + Recovery()
                        : "TARGET_CHANGED: PID was reused by another process; no other instance was selected. " + Recovery());
                }
            }
        }

        internal void Commit(long expectedGeneration, TargetCall candidate, JObject stamp, bool verify, string selector, bool rebind)
        {
            lock (gate)
            {
                if (generation != expectedGeneration)
                    throw new InvalidOperationException("TARGET_CHANGED: another switch completed during verification; this switch was not applied. " + Recovery());
                CheckSwitch(candidate, selector, rebind);
                if (verify && candidate.ExpectedDocumentId != null && stamp?.Value<string>("document_id") != candidate.ExpectedDocumentId)
                    throw new InvalidOperationException("DOCUMENT_CHANGED: verification found a different drawing. Previous binding was preserved. " +
                        "Review the active drawing, then provide its expected_document_id or rebind=true to confirm it.");
                candidate.Confirmed = verify;
                candidate.DocumentId = verify ? stamp?.Value<string>("document_id") : null;
                candidate.DocumentTitle = verify ? stamp?.Value<string>("document_title") : null;
                if (verify) candidate.ExpectedDocumentId = null;
                candidate.Generation = ++generation;
                bound = Copy(candidate);
                lost = false;
            }
        }

        // Called before the verification probe and again atomically with commit.
        private void CheckSwitch(TargetCall candidate, string selector, bool rebind)
        {
            var actualStart = processStart(candidate.Discovery.Pid);
            if (actualStart == null || actualStart != candidate.Discovery.ProcessStartUtc)
                throw new InvalidOperationException("TARGET_CHANGED: selected process changed; this switch was not applied. " + Recovery());
            if (bound == null || SameInstance(bound, candidate)) return;
            if (selector == null || !(selector.StartsWith("pid:", StringComparison.Ordinal) || selector.StartsWith("id:", StringComparison.Ordinal)))
                throw new InvalidOperationException("TARGET_REBIND_REQUIRED: changing an existing binding requires an explicit pid:<PID> or id:<target_id>; a year or automatic selection cannot move it.");
            if (!rebind && !lost && bound.Confirmed && processStart(bound.Discovery.Pid) == bound.Discovery.ProcessStartUtc)
                throw new InvalidOperationException("TARGET_REBIND_REQUIRED: the current instance is still live and confirmed. Review it and pass rebind=true only if you intend to leave it.");
        }

        private static bool SameInstance(TargetCall first, TargetCall second)
            => first != null && first.Discovery.Pid == second.Discovery.Pid
                && first.Discovery.ProcessStartUtc == second.Discovery.ProcessStartUtc;

        internal static string Recovery() => ServerState.IsReadOnly
            ? "Call dwg_list_available_targets, then restart this read-only gateway with --read-only --target pid:<PID> or id:<target_id>."
            : "Call dwg_list_available_targets, then dwg_switch_target with the intended pid:<PID> or id:<target_id> to verify the instance and active drawing before writes.";

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
                    ["document_id"] = bound?.DocumentId, ["document_title"] = bound?.DocumentTitle,
                    ["expected_document_id"] = bound?.ExpectedDocumentId
                };
            }
        }

        private static TargetCall Copy(TargetCall call) => new TargetCall
        {
            Discovery = call.Discovery, Generation = call.Generation,
            Confirmed = call.Confirmed, DocumentId = call.DocumentId, DocumentTitle = call.DocumentTitle,
            ExpectedDocumentId = call.ExpectedDocumentId
        };

        private static DateTime? Probe(int pid)
        {
            try { using var process = Process.GetProcessById(pid); return process.HasExited ? null : process.StartTime.ToUniversalTime(); }
            catch { return null; }
        }
    }
}
