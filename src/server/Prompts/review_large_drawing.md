# Review a large drawing   (arguments: question, space)

Goal: answer my question by reading the drawing in bounded pieces (profile first, then counted regions, then pages), never by loading every entity at once.
Question: {question}
Space: {space}
Session: read-only workflow. It reads entities and may move the view; it never edits the drawing.

Before you start
- Call dwg_get_current_target. If no target is bound or several AutoCAD instances match, call dwg_list_available_targets and ask me which one; in writable sessions bind it with dwg_switch_target. Tell me the active drawing title.
- If the question is empty or unclear, ask me what I want to find before reading.

Steps
1. Profile: dwg_get_drawing_profile(section='summary', space='{space}'). Note scope, completeness and the type, layer and space counts. Follow output.next_calls for the types, layers or spaces tables only as far as the question needs, passing expected_snapshot. PROFILE_CHANGED means the drawing changed: start again from the summary.
2. Find the area: dwg_get_drawing_info gives units (insunits; 0 means unitless). When the view toolset is enabled, dwg_zoom_extents returns the drawing extents (center, width, height) but moves the view, so say so before calling it; dwg_capture_view_image gives an overview, and dwg_inspect_view_region zooms into a part of that image. Without the view toolset, ask me for the area or take it from entity extents in step 4.
3. Count before reading: dwg_count_entities with bbox={"min":[x,y,z],"max":[x,y,z]} in drawing units, space, and entity_type or layer when the question allows. When a region holds more entities than one page can carry, split it into equal strips along its longer side and count each strip; keep splitting until each strip is small enough.
4. Read each strip: dwg_query_entities with paged=true, bbox, bbox_mode, space and only the fields you need (for example ["handle","type","layer","text","extents"]). Follow output.next_calls with its cursor until nothing is left. For labels, dwg_get_selected_texts with bbox (an explicit, read-only source) returns clustered text.
5. Keep a record per strip: bbox, count, and read status (complete, partial or not read). Incomplete coverage (null exact_match_count, omitted output, blocked notes) means the strip was not fully read: say so, never treat it as empty.
6. Check labels and dimensions you inferred from images against CAD text and entity data.

Report
- Answer the question with evidence: handles, layers and coordinates in drawing units.
- Coverage table: strip | bbox | entities counted | read status.
- Say what was not read: block and xref contents are not expanded, and other spaces are separate reads.
- When you found entities I should look at, call dwg_report_task_result once before your final answer, with [label](dwg:<handle>,...) links to them. Skip it for an answer with nothing to check.

Do not
- Do not edit, save or purge the drawing.
- Do not claim that strips you did not read are reviewed or empty.
- Do not use dwg_send_code to read everything at once unless I agree.
