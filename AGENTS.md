# AGENTS.md — dwg-mcp health and update contract

These instructions apply to the development health feature; published v2.0.1 does not expose `dwg_health`.

## Session startup

Select the intended AutoCAD target with `dwg_list_available_targets` / `dwg_switch_target` when needed. Call `dwg_health(check_updates=true)` once at session start if network access is permitted; later routine probes remain offline. If network access is unavailable or disallowed, report not checked/unknown, never up to date.

Health is read-only, does not require or lock a drawing, and never creates, activates, saves or closes one. A successful host/state probe requires `host_connected=true` and `ok=true`. No target preserves the gateway identity. A responding legacy dispatcher may return `HEALTH_UNSUPPORTED` with `host_connected=true`, but health/state and the plugin's loaded identity remain unverified. A busy UI retains observed plugin identity and marks document state unavailable. Do not infer corruption, a closed host or missing installation from unavailable evidence.

## Runtime evidence

Preserve `setup_gateway` and `setup_plugin`. Informational version and MVID describe loaded assemblies. `artifact_sha256` is a disk observation at identity capture, not a hash of loaded memory or proof that copying a new file reloaded the process. Version/revision alignment is not proof of compatibility or publisher trust.

Inspect both `update.components`, not only the notification's affected component list. Read the full `update.notification.message`, `lookup_reason`, `protected_components`, `protected_component_reasons` and `unobserved_components` before summarizing:

- `NEW_RELEASE_AVAILABLE`: report the newer version; discovery alone is not authorization to replace a bundle.
- `RELEASE_REVIEW_REQUIRED` / `RELEASE_IDENTITY_UNVERIFIED`: review the missing or mismatched evidence. A review warning does not mean a newer version exists.
- `LOCAL_VERSION_AHEAD`: informational; preserve newer components and never downgrade.
- `UPDATE_CHECK_UNAVAILABLE`: report the external lookup reason, not a local runtime fault or request for installation authorization.

Unobserved components need the intended connection and runtime metadata; observed newer/unresolved builds must be preserved. `comparison_reason=release_revision_unavailable` describes missing external tag evidence even if the notification says identity unverified. Stop the update workflow, not otherwise healthy AutoCAD work.

`update.status=checked` confirms lookup only. Equal versions, matching revisions, semantic `update_available=false`, or null notification do not verify artifact bytes or independently authenticate a publisher.

## Update proposals and installation

Only `update_guidance.mode=prepare_update` with `update.notification.bundle_update_proposal_allowed=true` contains installation steps and permits a bundle update proposal. Installation needs explicit user authorization; reuse an existing authorization for the same scope. Other modes contain only check/report/review actions. Authorization alone does not override blocked eligibility, and development builds must never be automatically replaced.

For an eligible authorized update, keep the exact release tag from the fresh health response. Use its official `DwgMcp.Setup-*-win-x64.zip`; verify the package and manifest before previewing replacement. Do not silently substitute another release or a source build. Back up the installed server, plugin bundle and client configuration; the current installer does not provide an atomic server+bundle rollback. Preview `install.ps1 -WhatIf -Client none` first, then use `-Client none` for installation to leave MCP client configuration untouched. Any client-config edit needs its own exact preview and approval. Close AutoCAD only with the user's involvement; do not save/close drawings or kill processes automatically to enable installation. Restore backups on upgrade failure instead of uninstalling as rollback. Preserve personal data.

Health only reports metadata/guidance: it never downloads packages, installs, changes configs, saves/closes drawings or restarts processes.

## Reconnect verification

After an authorized upgrade, restart AutoCAD and reconnect/restart the MCP client. On the same new connection, record fresh `initialize.serverInfo`, `tools/list`, then health:

- Compare server name/full informational version with `client_verification.expected_server_info`.
- Compare the health tool's `_meta["bimwright/dwg-mcp/runtime"]` with `client_verification.expected_runtime` (version, MVID, PID, start time).
- Check optional boolean `check_updates`, read-only true, destructive false, idempotent true, open-world true.
- Verify loaded plugin identity and a successful host/state probe; do not rely on tool count alone.

The server returns `not_observable_by_server`, not a client-cache refresh pass. If the client cannot expose its fresh handshake/catalog evidence, report that portion unverified. A separate direct-server probe does not replace actual client evidence.
