# DWG model-reading stack

Date: 2026-09-22 · Updated: 2026-09-24 · Status: visual reading first slice implemented in source; installed-host acceptance pending. Other tiers remain proposals.

## Accepted first slice: visual reading

The original proposal recognized progressive reading but treated vision as already complete. Capture plus coordinate-based zoom leaves an important gap: the agent recognizes a detail in an image but cannot directly ask to inspect that image region.

The first implementation therefore takes priority over Tier 0/Tier 3:

1. `dwg_capture_view_image` returns an inline MCP image with JSON metadata and a capture ID.
2. `dwg_inspect_view_region` accepts a rectangle in normalized image coordinates and the source capture ID. The plugin validates the source, fits the region, regenerates, checks the actual camera, and captures a new image linked to its parent.
3. Repeat only after inspecting each new image. `dwg_restore_view` returns to an ancestor camera and produces a fresh capture.
4. Reject stale sources and unsupported views. Initial region mapping is limited to one model-space viewport, orthographic top-down, zero twist, no clipping, matching camera/image aspect ratios.
5. Interpret geometry visually, then verify labels, dimensions and handles from CAD data. Images alone do not establish semantic identity or engineering correctness.

See [visual reading contract and live acceptance checklist](2026-09-24-visual-reading-loop.md). This slice does not implement a persistent atlas, regional entity/text queries or semantic classification.

## Problem statement

An agent needs to **read and understand a DWG model**, but ordinary 2D drafting entities do not necessarily encode domain objects:

- Revit = typed object graph (a Wall knows it is a Wall, has params, connects to Levels/Rooms). "Reading the model" = querying params on an existing graph.
- DWG = a bag of geometric primitives + text. Semantics live in **convention**, not schema:
  - layer names carry discipline (`I-WATE-PIPE-…`, ISO 13567-style)
  - blocks + attributes carry "objects" (equipment tags, title block)
  - spatial grouping carries meaning (text next to a polyline is its label)
  - layouts/viewports carry scale and presentation intent

→ Reading a DWG is **reconstructing** semantics, not querying them.

Second problem — the **context gap**: an LLM cannot ingest ~50k flat entity records. Humans read drawings progressively (title block → legend → plan → zoom into region). The current surface forces the agent the other way (flat dumps).

## Current reading surface vs gaps

| Need | Exists | Missing |
|---|---|---|
| Overall glance | `dwg_get_drawing_info` (name/layer/units) | Digest: entity histogram by layer/type, block usage, xrefs |
| Structure | `dwg_list_blocks` (name + entity/attdef count) | `list_layouts`, `list_xrefs`, block anatomy + insertion sites |
| Spatial queries | `dwg_query_entities` (type/layer/color, model-space only) | `bbox`/`within`/`near` filters, layout scope |
| Text at scale | `dwg_get_selected_texts` (clusters, but needs user pickfirst selection) | Same clustering over a region / whole drawing, no selection |
| Eyes and visual navigation | Inline capture + `dwg_inspect_view_region` + `dwg_restore_view` | Installed-host acceptance; broader viewport/layout support; regional data lookup |

Key asset already in the codebase: `SpatialClusterer` (used by `get_selected_texts`) — most of Tier 3 is reuse.

## Proposed tiers — mirrors how a human reads a drawing

- **Tier 0 — `dwg_describe_model`**: one compact digest per response — layouts, extents, entity histogram by layer/type (top N), block usage table (name → insert count → has attrs), xref list, text stats. The agent's first "glance at the sheet".
- **Tier 1 — structure**: `dwg_list_layouts` (model/paperspace + viewports + scale), `dwg_list_xrefs` (xref graph), `dwg_describe_block` (definition anatomy: member-type histogram, attdef tags, nested blocks, insertion sites).
- **Tier 2 — spatial queries**: extend `query_entities`/`select_*` with `bbox`/`within`/`near_handle`/`layout` scope; `dwg_read_region` = "what is drawn in this window" — entities + texts, clustered.
- **Tier 3 — `dwg_map_texts`**: `SpatialClusterer` over a region or the whole model space without requiring a selection. Likely the highest-value tier — most DWG semantics live in annotation.
- **Tier 4 — vision (implemented first)**: choose a region directly on an image, inspect a fresh zoomed capture, and return to an overview. No prior entity handle or world-coordinate dump is needed. See the accepted first slice above.
- **Tier 5 — conventions (later)**: heuristics — layer-name discipline parsing, title-block detection (block with attributes in paperspace corner), scale inference. Highest domain value but error-prone; build only after Tiers 0–3 prove out, keep thin and overridable.

## Non-goals / honesty

- No "semantic BIM inference" first — entity relationship graphs (dimension↔entity binding, pipe-network reconstruction) are expensive and error-prone; only worth it if Tiers 0–3 prove insufficient.
- Dedicated tools make common reading operations predictable without arbitrary scripts. `run_lisp` execution is blocked; it is not a fallback for this workflow.

## Open questions (for owner review)

1. After visual navigation passes live acceptance, prioritize regional text/entity lookup versus **Tier 0 digest + Tier 3 map_texts** based on actual reading sessions.
2. Are conventions stable enough to encode? Which layer-naming standard; is the title block a block with attributes or bare geometry?
3. Region/whole-drawing text scan on very large models — acceptable latency/response budget? (ResponseSizeGuard exists; may need result caps.)
4. Naming: `dwg_describe_model` vs `dwg_get_model_digest`; `dwg_map_texts` vs `dwg_read_texts` — pick at implementation time.
