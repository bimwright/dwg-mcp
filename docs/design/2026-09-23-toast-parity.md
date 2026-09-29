# DWG toast: RVT parity design

Status: implementation and automated M1/M2 validation complete; installed M3 acceptance is partial. See [validation record](../testing/2026-09-23-toast-validation.md).

## Baselines and scope

- DWG branch: `feature/dwg-toast-parity`, based on `895bf61`.
- Worktree: `D:/Projects/bimwright/.worktrees/dwg-toast-parity`.
- Original checkout: `D:/Projects/bimwright/dwg-mcp`; its native-capture work remains there, uncommitted and excluded from this branch.
- RVT reference: `2856e36e46163ef73579e6ee9c7d5c9bb193bb43`, especially `src/shared/Views/Toast/`, `Commands/ToggleToastCommand.cs`, `Config/RvtMcpConfig.cs`, and `Infrastructure/McpEventHandler.cs`.
- Owner intent: nearly full RVT behavior and appearance, with the changes required to work correctly inside AutoCAD. This is not a text-only or command-only substitute.
- Historical RVT specs saying default OFF are superseded by current source: `DefaultEnableToast = true`. Keep the coordinate/animation fix from `05a9429`.

This design was agreed before implementation. Source changes stay on the isolated feature branch; the original checkout's capture work remains excluded.

## Required parity

| Capability | DWG acceptance |
| --- | --- |
| Completion-only | One notification for a completed, authenticated top-level plugin request; no progress/start toast. |
| Appearance | Reuse RVT theme, fonts, icons, 340 DIP card width, spacing, borders, typography, animation and content hierarchy. No independent visual redesign. |
| Position and stack | Owner-relative top-left positioning, 16 DIP edge margin, 8 DIP gap, newest first, maximum four cards; evict oldest cleanly. |
| Timing | RVT timing: query 3 seconds, write 6 seconds, failure 8 seconds, capture thumbnail 9 seconds. Hover pauses; leave restarts the interval, as in RVT. |
| Interaction | Close button dismisses; card click dismisses. Successful capture click opens only its validated image through the OS image association, then dismisses. |
| Thumbnail | PNG/JPG/JPEG and DWG-supported BMP; maximum 8 MiB input, decode width 300, image area bounded to RVT dimensions. Missing, oversized or corrupt images degrade to a text card. |
| Toggle | Default ON; AutoCAD ribbon toggle plus `MCPTOAST` command access, both sharing the same state. State persists across host restarts. OFF dismisses all cards, including work already queued for presentation. |
| Labels | Keep RVT categories and visual semantics: query, modified, script, export, snapshot, failed. Replace Revit-specific vocabulary with drawing/entities/layers/text. |
| Lifetime | Cleanly dismiss on MCP stop, toggle OFF, and host termination; restart never duplicates windows, subscriptions or ribbon controls. |
| Reliability | Notification failure never changes command execution, undo behavior, serialized response, or success/failure returned to the client. |

Near parity means the same product experience, not identical Revit-specific tool names or APIs. The following necessary adaptations are explicit:

1. Use AutoCAD window ownership and modeless UI integration, not Revit `ExternalEvent` or `IExternalCommand`.
2. Do not activate a toast or interrupt command-line input. Keep the RVT appearance while testing focus, minimized state and modal interactions specifically in AutoCAD.
3. Interpret DWG result envelopes, command classifications and capture paths correctly.
4. Partial batches use the existing failure styling with a clear partial-completion summary; do not introduce a new visual theme or claim all operations succeeded.

## Completion flow and thread ownership

The AutoCAD-facing seam is the top-level authenticated request in `CommandDispatcher.Dispatch`. Inner `ExecuteCommand` calls used by batches and ToolBaker do not emit their own notifications.

1. Authenticate and validate as today. Unauthorized/malformed traffic does not create UI notifications.
2. Execute through the existing `DocumentInvoker`/`DwgApiExecutor`. Sample any drawing context while its document is locked. Carry strings/scalars only; never send `Document`, `Database`, `DBObject` or COM objects to the UI.
3. Release the document lock, finalize the existing wire response, and build a bounded completion snapshot. Validated command refusals may also produce failure snapshots.
4. Queue presentation without waiting for UI work. Notification preparation and queue submission are independently guarded; the finalized response must remain unchanged if either fails.
5. On the AutoCAD UI dispatcher, check current enable/lifetime generation again, create the card and show it modelessly owned by `Application.MainWindow.Handle`.

Capture the host dispatcher from a known AutoCAD UI callback during initialization/idle. Do not discover a dispatcher on a transport worker or synchronously invoke the UI while holding a document lock. If the UI is not ready, skip presentation rather than creating an independent WPF `Application` inside AutoCAD. Retain only bounded pending state.

`MCPSTOP` dismisses and invalidates queued work. `MCPSTART` reuses/reinitializes one host integration cleanly. `Terminate` unsubscribes callbacks and closes windows without waiting for a worker that might itself be waiting on the UI. A stale completion from an earlier listener generation must not appear after restart.

This feature does not change the existing DWG command executor or its threading policy. Host-native UI work is a separate path from document execution.

## DWG completion semantics

Create a small completion DTO from known fields, not a copy of arbitrary params or result JSON. Expected fields: command, display title/category, success status, compact summary/detail, elapsed milliseconds, optional drawing basename and validated capture image path.

- Ordinary commands use `CommandResult.Ok` and sanitized errors.
- `send_code` also checks its nested `result.ok`. Compile/runtime/DTO errors represented as outer success must render as failures. Never display script source or raw stdout/return objects.
- `batch_execute` checks `partial_failure` and item results, including nested `send_code` failure envelopes even if the existing batch flag is false. Emit one summary with succeeded/failed counts, not a card for every child. Preserve the existing response contract.
- ToolBaker emits one outer completion. Its nested logical failures must not be presented as all-success.
- Drawing mutations such as `move_entities`, `rotate_entities`, `scale_entities`, `copy_entities`, `erase_entities`, `offset_entities`, `explode_block`, translation/rewrite and `send_code` are writes. Do not copy RVT's classifier unchanged: several of these verbs are absent there.
- View navigation changes the view without implying a geometry write. Capture is a snapshot with a file output, not a geometry modification.
- Query/count/selection/text summaries use current DWG DTO field names; fall back to a short completed message when optional fields are unavailable. Missing fields must not crash a toast.
- Capture reads `output_path`, actual `width`/`height`, and `image_format`. Optional native-capture `document` metadata may enrich the card but is not required: this branch must compile and function before the separate native-capture changes are merged.
- Thumbnail/open-image authorization is limited to the actual successful capture result, approved image types, and temp/DWG captures roots with directory-boundary checks. User-specified paths outside those roots still get a text completion. Never expand arbitrary `send_code` results into clickable paths.
- Server-only inspection/routing and server-side LISP refusals do not reach the plugin and therefore create no AutoCAD toast. Do not add a hidden transport call just to display them; this also preserves offline behavior.

## Preferences and ribbon

Keep toast preferences plugin-side under `%LOCALAPPDATA%/Bimwright/Dwg/`; the .NET 8 server stays free of WPF/Autodesk references. Do not reference server-only `DwgMcpConfig` from the plugin. Use a small UI settings file (`ui-settings.json`, key `enableToast`) with merge-preserving writes. Failure to save must be reported to the user without affecting CAD/MCP execution; malformed existing data must be preserved, not silently destroyed.

Match RVT's default ON and optional environment override, scoped to DWG (`BIMWRIGHT_DWG_ENABLE_TOAST`). Document precedence and the fact that a host-process environment override is reapplied on restart. Ribbon and command changes apply immediately to the running session and persist the preference.

The ribbon button must reflect the effective state and have a descriptive tooltip. Register after ribbon availability; handle workspace/ribbon recreation without duplicate tabs, panels or controls. If the ribbon is unavailable, `MCPTOAST` remains usable. Do not create WPF windows as a side effect of turning the setting ON; show only upon a later completion.

## Reuse boundaries and expected files

- Copy the RVT visual implementation into `src/shared/Views/Toast/`, recording upstream commit and necessary deltas. Reuse `McpToastWindow`, theme, manager, view model, thumbnail loader and name formatting where applicable.
- Adapt the notifier/host for AutoCAD; replace RVT application logging hooks. No binary dependency on `rvt-mcp`, an external design-system assembly or a new family-wide UI package.
- Add DWG content normalization/classification and safe image-path policy as host-independent logic.
- Add plugin UI preference handling and ribbon/command integration to the shared plugin source and the common `plugin-acad24/App.cs` used by other shells.
- Add explicit WPF references/settings across plugin years 2022–2027. The server remains `net8.0` and does not become Windows/WPF-dependent.
- Wire completion at the common request boundary for both TCP and named-pipe paths; avoid duplicating notification logic in each handler.
- Add behavior tests to `Bimwright.Dwg.Tests` and a real-WPF STA executable `Bimwright.Dwg.Toast.Tests`. Keep AutoCAD runtime tests in the live checklist, not disguised as unit tests.

## Milestones and acceptance

### M1 — Completion contract and content

Implement the pure completion mapping, DWG command categories, preference semantics and image policy with behavioral regressions. Include nested script errors, partial batches, privacy and compatibility with the existing capture DTO. Test one behavior at a time at the boundaries in the test matrix. Do not claim visual parity from M1.

### M2 — Full WPF surface and plugin integration

Port the RVT visuals and interaction, wire the host UI dispatcher, owner, lifecycle, command and ribbon toggle. Prove notification exceptions cannot alter responses. Run the WPF harness and build the available AutoCAD 2024/2027 shells to isolated output directories. Review a rendered comparison against the RVT baseline. M2 completion is source/build/WPF evidence, not installed AutoCAD acceptance.

### M3 — Installed AutoCAD acceptance

Install only when the host can be closed safely, then test direct MCP calls on disposable DWGs in AutoCAD 2024 and 2027. Cover focus/input, modal state, multiple drawings, capture clicking, ribbon recreation, DPI/monitors, stop/start and exit. Verify no drawing-content or selection changes attributable to notification display. Record exact server/plugin hashes, fixture drawings, calls and results. Other years remain explicitly unverified without their own SDK/host checks.

All three milestones are required for the near-full parity claim. Milestones organize acceptance; they do not reduce the requested feature scope. Stop and report the exact blocked gate if an SDK, host session, reproducible failure or design conflict prevents progress. Do not silently replace the feature with a reduced implementation.

Detailed cases: [toast test matrix](../testing/2026-09-23-toast-test-matrix.md).
