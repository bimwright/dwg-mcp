# DWG toast test design

Status: M1/M2 implemented and validated; M3 partially exercised on installed AutoCAD 2024. Per-gate evidence and remaining cases: [validation record](2026-09-23-toast-validation.md).

Design: [RVT parity](../design/2026-09-23-toast-parity.md). Base DWG `895bf61`; RVT `2856e36`.

## Test boundaries

1. **Completion input -> display DTO:** known tool/result contracts, safe summaries, logical status, image authorization. No native host dependency.
2. **Settings API -> effective/persisted preference:** isolated temporary file/environment lookup; observe reload and public state, not private methods.
3. **Top-level request -> response + completion observer:** same finalized response with notifications enabled, disabled or failing; exactly one event and no event from nested execution. A native-free test double is valid for the notification/UI boundary only; real AutoCAD dispatch is still a separate live gate.
4. **Completion DTO -> WPF card/window behavior:** actual production WPF on an STA dispatcher, no AutoCAD. Observe rendered window geometry, displayed content and interactions. Do not replace animations/timers with a mock and call that WPF acceptance.
5. **Installed MCP -> AutoCAD UI:** actual host, loaded binaries, transport and disposable DWG; this is the authority for input focus, owner, ribbon, native lifecycle and document invariants.

Keep regression assertions at these boundaries. Use known fixture outcomes, not snapshots generated from the implementation under test. Reuse RVT tests where the contract is the same; replace Revit-specific fixtures with real DWG result shapes. Do not use reflection into private fields as the primary test interface. WPF visual snapshots supplement behavioral assertions; font/antialiasing noise is not a functional failure.

## M1: automated content/settings cases

| ID | Input/action | Required observable outcome |
| --- | --- | --- |
| C01 | Completed query and write DTOs | Correct RVT category/color kind, command title, bounded summary and duration. |
| C02 | All registered DWG mutation commands | No mutation is classified as read; explicitly cover move/rotate/scale/copy/erase/offset/explode/translation/send_code. A newly added command requires a deliberate expected category. |
| C03 | Query/count/selection/clustered-text results, zero and nonzero | Accurate count or safe generic message; absent optional fields do not throw. |
| C04 | `send_code` outer OK, inner `ok=false` | Failure card with sanitized error; no successful green card. |
| C05 | `send_code` DTO/object/null/stdout containing canary secrets | Successful execution may get a generic script summary; no raw code/stdout/object/secret in the display DTO. |
| C06 | Mixed-success batch and batch whose script child has nested failure | One partial-completion card with correct totals; never all-success, even if the original batch flag missed a nested failure. |
| C07 | ToolBaker outer success with nested failure | One correctly classified failure/partial completion; no duplicate inner cards. |
| C08 | Current capture result (`output_path`, `width`, `height`, `image_format`) | Filename, actual dimensions and validated thumbnail; no dependency on RVT `saved_path`. |
| C09 | Capture DTO with optional new native document/camera fields | Compatible with old DTO; optional context enriches display without changing required fields. |
| C10 | PNG/JPG/JPEG/BMP under captures/temp; sibling-directory escape and non-image payload | Valid capture paths may preview/open; invalid or outside-root paths become text-only. Never derive an open action from arbitrary script results. |
| C11 | Missing/corrupt/>8 MiB thumbnail | Completion stays successful, no thumbnail, no exception; image decode is bounded to 300 pixels wide. |
| C12 | Invalid JSON/field types/oversized summaries/redacted placeholders | No notifier failure leaks into the tool response; no unbounded UI string or unsafe path operation. |
| S01 | Missing settings, explicit ON/OFF, environment override | Default ON; documented precedence; process-wide setting does not depend on selected drawing. |
| S02 | Toggle through public setting action, then reload | Same effective state for ribbon and command; unknown JSON keys retained. |
| S03 | Malformed/unwritable settings | Existing contents preserved; save failure is visible; CAD command behavior unaffected. |
| S04 | OFF with already queued notifications, then ON | Existing/queued cards stay dismissed; ON alone shows nothing; later completion can show. |

## M2: response isolation and WPF

| ID | Input/action | Required observable outcome |
| --- | --- | --- |
| I01 | Same successful/failed request with notifier absent, enabled, or throwing | Identical response JSON and CAD execution count; notifier cannot overwrite errors/results. |
| I02 | Batch/ToolBaker nested dispatch | Exactly one outer completion; no inner-card storm. |
| I03 | Auth failure, malformed request, valid command refusal | First two produce no cards; authenticated command refusal may produce one sanitized failure. |
| I04 | UI unavailable/shutting down/queue rejects work | Request completes without UI wait or deadlock; no worker-thread window creation. |
| I05 | Stop/restart with pending completion | Old-generation completion cannot show after restart; one active notifier/ribbon integration. |
| W01 | First window with unset WPF coordinates | No NaN animation exception; finite initial placement (RVT regression). |
| W02 | Close during animated reflow and immediately add another | Correct final positions; maximum four cards; oldest evicted (RVT regression). |
| W03 | Query/write/error/thumbnail card | Appearance matches the pinned RVT reference; durations are 3/6/8/9 seconds respectively. Use controllable clock at the policy boundary plus a real-timer smoke, not long timing sleeps in every test. |
| W04 | Mouse enter/leave, close button and card click | Hover pauses; leave restarts; close and click dismiss exactly once with no orphan timer/window. |
| W05 | Successful capture card click vs generic/error card | Only authorized capture opens once; test an injected image-opening boundary rather than launching the user's default viewer in unit/WPF suites. |
| W06 | Valid thumbnail then missing/corrupt/oversized image | Visual card remains usable; no image file stays locked after decoding/dismissal. |
| W07 | Preference OFF and host shutdown | All cards disappear; no delayed recreation or duplicate close callbacks. |
| W08 | Owner, no-activation flags and DPI positioning | Assert supported window configuration in WPF; final proof is live F01/F04, not a mock HWND. |
| B01 | Build server and plugin shells separately | Server has no WPF/Autodesk reference; 2024/2027 build without new unexplained warnings. 2022/2023/2025/2026 status stays unverified until their SDKs are checked. |

Use isolated output paths for plugin builds. Never copy DLLs into an open host merely to make a test pass. Keep the original RVT source unchanged; the comparison fixture records its commit and any approved DWG-only differences.

## M3: live AutoCAD matrix

Run F01–F08 on AutoCAD 2024 and 2027, with the installed server/plugin versions and hashes recorded. Use a disposable DWG and explicit handles/layers for any write/undo checks. Preserve the user's real documents and sessions.

| ID | Scenario | Pass evidence |
| --- | --- | --- |
| F01 | Input focus: idle, typing in command line, active native command, modal dialog | Toast never steals typing/selection focus, cancels a command or hides a modal dialog; no UI/document-lock deadlock. |
| F02 | Success, native failure, C# compile/runtime failure, partial batch, ToolBaker | Accurate single completion per top-level call; no false green; MCP response and drawing result unchanged by enabling toast. |
| F03 | Capture PNG/JPEG/BMP, actual dimensions, click open, image deletion before click | Thumbnail/summary correct; click opens the intended file once or degrades gracefully; drawing/view invariants preserved. |
| F04 | Window moved/resized/minimized/restored; 100/150/200% DPI and two monitors | Stack belongs to the correct AutoCAD window, remains reachable, and does not activate another application. Record unavailable monitor/DPI cases as untested. |
| F05 | Switch/close drawings while completion is queued; two AutoCAD instances | Context belongs to the executed request, not the later active DWG; no access to disposed native objects or cross-instance notification. |
| F06 | Ribbon toggle, `MCPTOAST`, restart, workspace/ribbon recreation | Same persisted state, correct button feedback, no duplicate controls; command works without ribbon. |
| F07 | Burst >4 completions, OFF/ON, MCPSTOP/MCPSTART, host exit | Stack cap holds; no orphan windows, late cards, handler leaks, deadlock or exit crash. |
| F08 | Read-only mode; server-only inspection/routing; server-side LISP refusal | Toast does not enable tools, bypass guards, dispatch LISP or mutate DWG; no artificial host call for server-only work. |

Before/after evidence should cover document identity, DBMOD, selection, active view and known fixture entities. For intentional mutations record the expected change and verify undo separately; do not assert unchanged DBMOD after a legitimate write.

## Evidence and stop rules

- Record base and feature commit, git status, commands, exit codes, test totals, warning deltas and artifact locations per milestone.
- M1 green does not certify WPF; WPF green does not certify AutoCAD; compile green does not certify installed binaries.
- Do not label a milestone PASS with an unrun required case. Use PASS / FAIL / NOT RUN with a concrete reason.
- A reproducible host crash, focus regression, false-success toast, secret leak or response corruption blocks full acceptance.
- If workload or host access prevents M3, finish reviewable M1/M2 work and report the exact remaining cases instead of reducing parity or claiming completion.

## Preparation checkpoint

- [x] Isolated branch/worktree created from `895bf61`.
- [x] Baseline in this worktree: `dotnet test tests/Bimwright.Dwg.Tests/Bimwright.Dwg.Tests.csproj -c Release --nologo` passed 458/458 (0 failed, 0 skipped) on 2026-09-23. This runs existing tests only.
- [x] RVT source behavior and relevant classes inspected at `2856e36`.
- [x] Prior assessment: RVT toast-related tests 40/40 and real WPF harness 2/2 passed; compile-only compatibility probes passed for AutoCAD 2024/2027, with assembly warnings in the 2027 probe. These are feasibility evidence, not DWG toast acceptance.
- [x] New DWG M1 behavior tests and implementation.
- [x] New DWG M2 WPF/integration tests and implementation.
- [ ] Installed DWG M3 host acceptance.
