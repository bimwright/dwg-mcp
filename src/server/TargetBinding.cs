using System;
using System.Diagnostics;
using Bimwright.Dwg.Plugin;
using Newtonsoft.Json;
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
                {
                    var observed = stamp?.Value<string>("document_id");
                    throw new InvalidOperationException("DOCUMENT_CHANGED: verification of " + Identity(candidate) + " expected document_id=" +
                        JsonConvert.SerializeObject(candidate.ExpectedDocumentId) + " but observed document_id=" + JsonConvert.SerializeObject(observed) +
                        ". Previous binding was preserved. Inspect the observed drawing before confirming a different lifetime. " +
                        (observed == null ? "Make the intended drawing active, then call " + SwitchCommand(ExplicitSelector(candidate, selector), candidate.ExpectedDocumentId, rebind) + "."
                            : "If it is intended, call " + SwitchCommand(ExplicitSelector(candidate, selector), observed, rebind) + "; rebind=true also requires that review."));
                }
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
                throw new InvalidOperationException("TARGET_CHANGED: selected process changed for requested selector " + JsonConvert.SerializeObject(selector ?? "auto") +
                    "; this switch was not applied. " + RecoveryFor(candidate, false));
            if (bound == null || SameInstance(bound, candidate)) return;
            var currentLive = !lost && bound.Discovery.Pid > 0 && bound.Discovery.ProcessStartUtc != null
                && processStart(bound.Discovery.Pid) == bound.Discovery.ProcessStartUtc;
            if (selector == null || !(selector.StartsWith("pid:", StringComparison.Ordinal) || selector.StartsWith("id:", StringComparison.Ordinal)))
                throw new InvalidOperationException("TARGET_REBIND_REQUIRED: a year or automatic selection cannot move an existing binding. " +
                    (currentLive ? "Current binding: " : "Historical binding identity: ") + Identity(bound) +
                    ". Requested selector " + JsonConvert.SerializeObject(selector ?? "auto") + " resolved to " + Identity(candidate) +
                    ". Inspect that intended instance, then select it explicitly with " + SwitchCommand(ExplicitSelector(candidate, selector), rebind: true) + ".");
            if (!rebind && currentLive && bound.Confirmed)
                throw new InvalidOperationException("TARGET_REBIND_REQUIRED: the current instance is still live and confirmed: " + Identity(bound) +
                    ". Requested candidate: " + Identity(candidate) + ". Inspect that candidate and, only if you intend to leave the current binding, call " +
                    SwitchCommand(ExplicitSelector(candidate, selector), rebind: true) + ".");
        }

        private static bool SameInstance(TargetCall first, TargetCall second)
            => first != null && first.Discovery.Pid == second.Discovery.Pid
                && first.Discovery.ProcessStartUtc == second.Discovery.ProcessStartUtc;

        internal string Recovery(TargetCall rejected = null)
        {
            lock (gate)
            {
                // A refusal from an older call must not discredit or redirect a newer binding.
                var rejectedCurrent = rejected != null && rejected.Generation == generation && SameInstance(bound, rejected);
                var trusted = bound != null && !lost && !rejectedCurrent && bound.Discovery.Pid > 0
                    && bound.Discovery.ProcessStartUtc != null && processStart(bound.Discovery.Pid) == bound.Discovery.ProcessStartUtc;
                return RecoveryFor(bound, trusted);
            }
        }

        private static string RecoveryFor(TargetCall call, bool trusted)
        {
            if (!trusted)
                return "Historical binding identity: " + Identity(call) + ". Inspect the intended instance; do not reuse this PID without checking its process start. " +
                    (ServerState.IsReadOnly
                        ? "Call dwg_list_available_targets, then restart this read-only gateway with --read-only --target pid:<PID> or id:<target_id> for the reviewed live instance."
                        : "Call dwg_list_available_targets, then dwg_switch_target with the reviewed live pid:<PID> or id:<target_id> to verify the instance and active drawing before writes.");
            return "Current binding: " + Identity(call) + ". Inspect that instance and drawing; if the process identity still matches, " +
                (ServerState.IsReadOnly
                    ? "restart this read-only gateway with --read-only --target " + ExplicitSelector(call, null) + "."
                    : "call " + SwitchCommand(ExplicitSelector(call, null), call.DocumentId ?? call.ExpectedDocumentId) + " before writes.")
                + " If it no longer matches, call dwg_list_available_targets.";
        }

        private static string Identity(TargetCall call) => call == null ? "none"
            : "pid:" + call.Discovery.Pid + ", process_start_utc=" + (call.Discovery.ProcessStartUtc?.ToString("O") ?? "unknown")
                + ", document_id=" + JsonConvert.SerializeObject(call.DocumentId) + ", expected_document_id=" + JsonConvert.SerializeObject(call.ExpectedDocumentId);

        private static string ExplicitSelector(TargetCall call, string selector)
            => selector != null && (selector.StartsWith("pid:", StringComparison.Ordinal) || selector.StartsWith("id:", StringComparison.Ordinal))
                ? selector : "pid:" + call.Discovery.Pid;

        private static string SwitchCommand(string selector, string expectedDocumentId = null, bool rebind = false)
            => "dwg_switch_target(target=" + JsonConvert.SerializeObject(selector) + ", verify=true"
                + (expectedDocumentId == null ? "" : ", expected_document_id=" + JsonConvert.SerializeObject(expectedDocumentId))
                + (rebind ? ", rebind=true" : "") + ")";

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
