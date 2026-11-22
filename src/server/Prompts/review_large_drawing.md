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
2. Find the area: dwg_get_drawing_info gives the drawing units (insunits_code=0 means unitless). Ask me for WCS bounds if a specific area is needed, or locate relevant entities with a small, filtered dwg_query_entities page using paged=true, space='{space}', fields=["handle","extents"] and no bbox. Use their WCS entity extents to bound the area they cover. Do not derive a WCS bbox from camera center, width or height.{visual_review}
3. Count before reading: dwg_count_entities with space and, when the question allows, entity_type or layer. For a region with known WCS bounds, pass bbox={"min":[x,y,z],"max":[x,y,z]} in drawing units. For a whole-space review without verified bounds, count and page in the selected space without bbox.
4. Find bands in a region that exceeds one page: call dwg_cluster_bands with the region's WCS bbox and space='{space}'. Choose axis='x' or 'y' along the direction you want to partition. Use a gap in drawing units justified by known spacing, or start with gap=0. If the count used an entity_type, pass that type in the JSON array types. Keep layer filters on the follow-up count and query. Follow output.next_calls with the same arguments and cursor. Treat band bbox and entity_count as a guide for where to read. Check coverage.extents_unavailable for entities excluded because they have no usable WCS bounds. Set sample_texts=true if short labels would help. If the scan is incomplete, the tool is unavailable, or bands are still too dense, split the verified WCS region into strips and count each strip.
5. Read each region or the selected space: dwg_query_entities with paged=true, the same space and filters, and only the fields you need (for example ["handle","type","layer","text","extents"]). Use each band's bbox intersected with the original region's bbox so the read stays within scope. Deduplicate by handle across overlapping reads. Follow output.next_calls with its cursor until nothing is left. For labels, dwg_get_selected_texts with bbox or handles as the explicit source returns clustered text.
6. Keep a record for each band, strip or space you review: bbox if used, count, and read status (complete, partial or not read). Use query results to establish coverage. If a scan was incomplete or the output omitted rows, report the gap. If you used images, check labels and dimensions against CAD text and entity data.

Report
- Answer the question with evidence: handles, layers and coordinates in drawing units.
- Coverage table: band, strip or space | bbox if used | entities counted | read status.
- Say what was not read: block and xref contents are not expanded, and other spaces are separate reads.
- When you found entities I should look at, call dwg_report_task_result once before your final answer, with [label](dwg:<handle>,...) links to them. Skip it for an answer with nothing to check.

Do not
- Do not edit, save or purge the drawing.
- Do not claim that areas you did not read are reviewed or empty.
- Do not use dwg_send_code to read everything at once unless I agree.
