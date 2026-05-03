<!-- mcp-name: io.github.bimwright/dwg-mcp -->

<p align="center">
  <img src="https://raw.githubusercontent.com/bimwright/.github/master/assets/logos/dwg-mcp.png" alt="dwg-mcp" width="180" />
</p>

<h1 align="center">dwg-mcp</h1>

<p align="center">
  <a href="https://github.com/bimwright/dwg-mcp/actions/workflows/build.yml"><img src="https://github.com/bimwright/dwg-mcp/actions/workflows/build.yml/badge.svg" alt="build" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-Apache%202.0-blue.svg" alt="license" /></a>
  <a href="#supported-autocad-versions"><img src="https://img.shields.io/badge/AutoCAD-2024-186BFF" alt="AutoCAD 2024" /></a>
  <a href="#tools"><img src="https://img.shields.io/badge/MCP-5%20default%20%2B%201%20opt--in-6C47FF" alt="MCP tools" /></a>
</p>

<p align="center">
  English · <a href="README.vi.md">Tiếng Việt</a>
</p>

---

## Drawing Translation Should Not Stop At Manual Copy-Paste

Construction and engineering drawings carry dense technical text — specifications, notes, dimensions, material callouts, legends. When those drawings arrive in a foreign language, translation is not optional. It is required before the project team can act.

The usual workflow is painful: select text one entity at a time, copy to a translator, paste back, fix the font (because SHX fonts cannot render Vietnamese or CJK), adjust the height, hope nothing shifted. Multiply by hundreds of text fragments per sheet, dozens of sheets per project.

`dwg-mcp` exists to compress that loop into two steps: select the text, let the AI agent read, translate, and rewrite it in place — with correct font, correct height, correct spatial grouping, and a single undo.

---

## What dwg-mcp Is

`dwg-mcp` is a local MCP gateway for Autodesk AutoCAD 2024.

It has two parts:

- **Bimwright.Dwg.Server**: a .NET 8 MCP server launched by Claude Code, Cursor, OpenCode, or another stdio MCP client.
- **Bimwright.Dwg.Plugin**: an AutoCAD add-in loaded inside AutoCAD, executing commands against the drawing database.

The agent talks MCP. The server talks to the plugin over localhost TCP. The plugin talks to the AutoCAD .NET API.

Everything stays on your machine.

---

## Why It Matters

AI agents make it possible to describe "translate all selected text to Vietnamese" and have it happen — correctly — in the drawing. But intent alone is not enough. AutoCAD text operations require understanding spatial layout, fragment grouping, font limitations, MText vs DBText, block references, and height scaling.

`dwg-mcp` handles that complexity:

- **Spatial clustering** groups fragmented text into logical sentences (by block, row, column, paragraph).
- **Automatic font handling** creates a Unicode-capable text style and applies it — no more SHX question marks.
- **Height scaling** compensates for the different visual density of Latin vs CJK text.
- **MText conversion** upgrades single-line fragments into multi-line text when safe.
- **Single undo** wraps each operation in a transaction.

---

## Usage Evidence

220 completed tool calls over 19 days of active use on production construction drawings. 98.2% success rate.

| Tool | Calls |
|------|-------|
| get_selected_texts | ~100 |
| translate_and_rewrite | ~77 |
| send_code | ~28 |
| collapse_and_rewrite | ~11 |
| update_texts | ~10 |
| apply_unicode_style | ~4 |

---

## Architecture

```text
+---------------------------+
| AI Client                 |
| Claude / Cursor / OpenCode|
+---------------------------+
              |
              | stdio MCP
              v
+---------------------------+
| Bimwright.Dwg.Server      |
| .NET 8 / C#               |
+---------------------------+
              |
              | TCP (127.0.0.1)
              | token auth
              v
+---------------------------+
| Bimwright.Dwg.Plugin      |
| .NET 4.8 / AutoCAD 2024  |
+---------------------------+
              |
              | LockDocument()
              v
+---------------------------+
| AutoCAD .NET API          |
| ObjectARX 2024            |
+---------------------------+
```

See [ARCHITECTURE.md](ARCHITECTURE.md) for threading, discovery, and auth details.

---

## Install

### 1. Server — .NET global tool

```bash
dotnet tool install -g Bimwright.Dwg.Server
bimwright-dwg --help
```

Requires .NET 8 SDK.

### 2. Plugin — AutoCAD add-in

**Option A: Auto-deploy (.bundle)**

Download the plugin from [GitHub Releases](https://github.com/bimwright/dwg-mcp/releases/latest):

```powershell
pwsh scripts/install.ps1 -WhatIf    # preview
pwsh scripts/install.ps1             # install
pwsh scripts/install.ps1 -Uninstall  # remove
```

The script deploys to `%APPDATA%\Autodesk\ApplicationPlugins\Bimwright.Dwg.bundle\`. Restart AutoCAD to load.

**Option B: Manual NETLOAD (dev)**

In AutoCAD: `NETLOAD` → pick `src/plugin-acad24/bin/Debug/net48/Bimwright.Dwg.Plugin.Acad24.dll`. Listener auto-starts.

### 3. Wire up your MCP client

Add to your MCP client config (e.g., `.mcp.json`):

```json
{
  "mcpServers": {
    "bimwright-dwg": {
      "command": "bimwright-dwg",
      "args": []
    }
  }
}
```

`send_code` is hidden from the default tool list. To expose it, opt in on both sides:

```json
{
  "mcpServers": {
    "bimwright-dwg": {
      "command": "bimwright-dwg",
      "args": ["--enable-send-code"]
    }
  }
}
```

Then run `MCPENABLECODE` inside AutoCAD for the current plugin session. `MCPDISABLECODE` turns it off again.

---

## Tools

| Tool | Purpose |
|------|---------|
| `get_selected_texts` | Read pickfirst selection, spatially cluster text entities, return grouped text with rewrite mode hints |
| `translate_and_rewrite` | **Preferred.** Write translated text back — auto-handles anchor, deletion, MText, font, height |
| `collapse_and_rewrite` | Low-level rewrite primitive with explicit geometric control |
| `update_texts` | Write new text by handle (legacy, single transaction) |
| `apply_unicode_style` | Ensure `Bimwright_Unicode` style exists and apply to targets |
| `send_code` | **Opt-in only.** Execute C# against AutoCAD .NET API after server flag/env enablement and AutoCAD-side `MCPENABLECODE` consent |

---

## Standard Workflow

```
1. User selects text entities in AutoCAD
2. Agent calls get_selected_texts → receives clustered text groups
3. Agent translates each cluster
4. Agent calls translate_and_rewrite([{id, new_text}, ...])
   Tool handles: anchor, delete, MText, font style, height. Done.
5. User runs REGEN if needed
```

Two steps from the agent's perspective: read, then write.

---

## Supported AutoCAD Versions

| Version | Status | .NET |
|---------|--------|------|
| AutoCAD 2024 | Supported | .NET 4.8 |
| AutoCAD 2025 | Planned | .NET 8 |
| AutoCAD 2026 | Planned | .NET 8 |

---

## Security

`send_code` executes arbitrary C# with full access to the AutoCAD process and local filesystem. It is not registered in the default MCP tool surface. To use it, start the server with `--enable-send-code` or `BIMWRIGHT_DWG_ENABLE_SEND_CODE=1`, then run `MCPENABLECODE` inside AutoCAD for that plugin session.

The security model relies on:

- **Local-only transport** — TCP on 127.0.0.1, no remote access.
- **Per-session auth token** — rotates on each plugin start, verified per request.
- **Two-sided code opt-in** — server-side tool registration plus AutoCAD-side consent.
- **Timeout boundary** — script execution runs on a dedicated thread with cancellation and abort on timeout.
- **Trusted agent assumption** — only use with MCP clients you control.

Do not expose the plugin port to the network.

---

## Project Structure

```
dwg-mcp/
├── src/
│   ├── Bimwright.Dwg.sln
│   ├── server/            # .NET 8 MCP server (global tool)
│   ├── shared/            # Handlers, clustering, rewriting, unicode
│   └── plugin-acad24/     # AutoCAD 2024 shell (.NET 4.8)
├── tests/                 # xUnit (86 tests)
├── scripts/               # install/uninstall PowerShell
├── lib/acad24/            # Notes only; Autodesk DLLs are never committed
└── .github/workflows/     # CI
```

---

## Disclaimer

This project is not affiliated with, endorsed by, or sponsored by Autodesk, Inc. AutoCAD is a registered trademark of Autodesk, Inc.

---

## License

[Apache License 2.0](LICENSE)

Third-party notices: [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)
