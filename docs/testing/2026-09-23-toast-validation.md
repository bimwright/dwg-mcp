# Toast validation — 2026-09-23

Feature branch `feature/dwg-toast-parity`, base `895bf614f63a51f8479ac64b9223734f9ee5bfdb`.
The implementation is uncommitted development source, not a release build. No push.
RVT reference: `2856e36e46163ef73579e6ee9c7d5c9bb193bb43`.

## Automated evidence

| Gate | Result |
| --- | --- |
| M1: content, privacy, paths, preferences, bounded queue | PASS; included in 529/529 xUnit tests (458 baseline + 71 added), zero failed/skipped. |
| M2: actual request boundary with native registration/document-lock doubles | PASS; responses identical with missing/present/throwing observers; one outer event, nested failures, auth/malformed/refusal cases, callback after lock release. These doubles do not certify native API execution. |
| M2: production WPF on real STA dispatcher, .NET Framework 4.8 | PASS, 8 scenarios. |
| M2: same WPF harness on .NET 10 | PASS, 8 scenarios. |
| AutoCAD 2024 Release build | PASS, zero warnings/errors. |
| AutoCAD 2027 Release build | PASS, four pre-existing warnings: two Roslyn version conflicts and obsolete ServicePointManager/WebClient. No new warning category. |
| Server boundary | PASS; existing net8 server builds/tests without UI or Autodesk references. |
| AutoCAD 2022/2023/2025/2026 builds | NOT RUN: corresponding SDK directories unavailable. WPF/AdWindows references added to every shell. |

Commands (run from the feature worktree):

```powershell
dotnet test tests/Bimwright.Dwg.Tests -c Release -v quiet
dotnet run --project tests/Bimwright.Dwg.Toast.Tests -c Release -f net48
dotnet run --project tests/Bimwright.Dwg.Toast.Tests -c Release -f net10.0-windows
dotnet build src/plugin-acad24 -c Release -v quiet
dotnet build src/plugin-acad27 -c Release -v quiet
```

WPF scenarios cover burst cap/newest-first order, finite initial coordinates, owner
and non-activation flags, moving the owner, immediate shutdown during a fade/restart,
four image formats with 300-pixel decoding and unlocked files, click/close behavior,
invalid/deleted-image fallback, real 3/6/8/9-second timers, and hover pause/restart.
The harness caught and now regresses the inherited immediate-close-during-fade defect.
The CI workflow includes both WPF runtimes on Windows; remote CI has not been run yet.

For visual comparison, the pinned RVT window/theme and DWG window were rendered with
the same Vietnamese text and PNG fixture. Both 356×234 PNG files have SHA-256
`3D28D9E7B3505D5BD191EACB25FBB829527C713FE589F4891CF578A7F775EBD3`.
This certifies that fixture's visual parity, not every host/DPI/layout combination.
Local images and executable probe: `build/toast-evidence/{rvt,dwg}-toast.png` and
`build/toast-visual-reference/`. Set `DWG_TOAST_TEST_ARTIFACTS` to render a DWG fixture.

## Installed development binaries

Installed while AutoCAD was closed, with build/deployed SHA-256 equality verified:

| Year | Plugin DLL SHA-256 |
| --- | --- |
| 2024 | `53493224BE29E5E47251C17837AE1A8909E15A8D2904A7405E922F45E840C8CE` |
| 2027 | `B05E19FE8C0E1396128343D085DC5DC607F24B914CBA22069A5083B7B09A15F4` |

Bundle: `%APPDATA%/Autodesk/ApplicationPlugins/Bimwright.Dwg.bundle`.
The existing manifest was preserved and a 2027 component added; both years are registered.
Backup of the entire prior bundle:
`%LOCALAPPDATA%/Bimwright/Dwg/backups/toast-20260923-193239/Bimwright.Dwg.bundle`.
Deployment record: `build/toast-evidence/deployment.json`.
The running MCP server was not replaced; this feature's changes are plugin-side.

## Installed AutoCAD 2024 smoke

New process PID 12340, scratch `Drawing1.dwg` from `acadiso.dwt`; no existing drawings
were opened or edited. `send_code` readback confirms the installed 2024 DLL path.

- Handshake and read-only count/drawing calls PASS.
- Live UI observation: transport worker thread 20, AutoCAD UI thread 1, visible card,
  `IsActive=false`, `ShowActivated=false`, owner HWND equal to the AutoCAD main HWND.
- Exactly one ribbon tab and `Toast ON` button observed.
- Compile and runtime failures produce `MCP · Failed`; canary password is redacted.
- A two-item batch with nested script failure produces one `Batch Execute` failure card,
  `1 succeeded · 1 failed`, even though the existing wire `partial_failure` is false.
- Native PNG/JPG/BMP captures return 640×224; visible cards show matching filenames,
  dimensions and `Click to open`. JPG/BMP image controls were observed with decoded
  width 300 pixels. Actual shell opening in AutoCAD is still a manual gate.
- A burst of eight real count calls leaves exactly four visible query cards.
- UI-thread calls to the public `MCPTOAST` action: OFF closes all cards, reloads false;
  ON reloads true and shows zero cards. STOP/START also leaves zero cards and one tab.
  Subsequent real MCP requests reconnect and display normally.
- Final drawing readback remains `DBMOD=0`, implied selection count 0, and entity count 0.
  Native view before/after and undo were not tested; no geometry mutation was requested.

Evidence JSON lives under `build/toast-evidence/host-2024-*.json`. The reusable
[`Observe-Toast.csx`](../../scripts/testing/Observe-Toast.csx) schedules a bounded,
one-shot observation on AutoCAD Idle and serializes UI values only. AutoCAD does not
have a WPF `Application.Current`; the observer uses public `PresentationSource`
roots. It does not inspect private manager fields or create a WPF Application.

## Remaining M3 gates

M3 is PARTIAL, not full acceptance. AutoCAD 2027 was started (PID 71288, installed
host is the Civil 3D distribution), but no MCP target appeared. Its COM API reported
`RPC_E_CALL_REJECTED`; startup/modal state needs user confirmation. A running process
is not evidence that the plugin loaded. No 2027 live behavior is certified.

Still NOT RUN: typing/active-command/modal focus, ribbon button click and workspace
recreation, OS viewer click, multiple-document switching,
native writes/undo, ToolBaker in the live host, multi-monitor 100/150/200% DPI,
minimize/restore, host exit/restart persistence, read-only server-mode isolation,
and the full matrix on 2027. The automated tests/builds above do not close these gates.

The original `dwg-mcp` checkout's capture WIP and the RVT reference checkout were
left unchanged. Keep the feature branch isolated until the remaining host checks
are accepted; do not describe this checkpoint as full RVT parity acceptance.
