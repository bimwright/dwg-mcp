# Translate drawing text   (arguments: target_language, source_language)

Goal: translate the text I select in AutoCAD into {target_language}, show me every translation before anything is written, write the confirmed ones back, and verify them.
Source language: {source_language}
Session: {mode}

Before you start
- Call dwg_get_current_target. If no target is bound or several AutoCAD instances match, call dwg_list_available_targets and ask me which one; in writable sessions bind it with dwg_switch_target. Tell me the active drawing title before any write.
- Writeback needs my manual selection. Ask me to select the text in AutoCAD and to tell you when I am done. Do not select text for me. Reads with handles or bbox are read-only (writeback_ready=false) and cannot be written back.

Steps
1. Call dwg_get_selected_texts with groupingStrength=normal (weak when nearby labels must stay separate) and includeEntities=true. If nothing is selected, ask me to select again. Keep each cluster id, its text and its entity handles.
2. Translate each cluster as one label or sentence. Keep numbers, units, dimensions, grid and axis labels, drawing numbers, codes and AutoCAD codes (\P, %%c, %%d, %%p) unchanged. Leave a cluster untranslated when it holds only numbers or codes. Use one term consistently for the same source term.
3. Show me a table: cluster id | original | translation | note (kept as-is, unsure term, looks over-grouped). When two labels look merged into one cluster, propose reading again with groupingStrength=weak, or translating those entities one by one.
4. Wait until I confirm the table. In a read-only session stop here: the table is the result.
5. Write back:
   - Normal case: dwg_translate_and_rewrite with translations=[{"id":..., "new_text":...}] for the confirmed clusters. Omit clusters that need no translation; they still get the Unicode style. Keep finalScale at its default unless I ask.
   - Clusters I asked to handle per entity: dwg_update_texts with items=[{"handle":..., "new_text":...}] and applyUnicodeStyle=true.
   - Use dwg_collapse_and_rewrite only if I ask for expert control.
   Send each write once. After a timeout or an unclear result, read the current text before any retry; never resend a write that may already have run.
6. Verify: read the surviving handles from the write response back with dwg_get_selected_texts(handles='[...]', includeEntities=true) and compare them with the confirmed table. Fragments merged into one cluster are erased on purpose. If text shows as "?" or boxes, the font lacks those characters: call dwg_apply_unicode_style on those handles.
7. When the view toolset is enabled, dwg_capture_view_image lets me check overlaps after the longer text.

Report
- Call dwg_report_task_result once before your final answer: outcome, a one-line summary, and a body with [label](dwg:<handle>,...) links to the rewritten text and to clusters that failed or were skipped.
- Tell me how many clusters were translated, skipped and failed, list the terms you were unsure about, and remind me that AutoCAD UNDO right away reverses the writeback before saving.

Do not
- Do not write before I confirm the table, and do not translate text outside my selection.
- Do not save, close or switch drawings, and do not change layers or styles beyond what the text tools do.
- Do not use dwg_send_code to work around a refused or failed text tool; tell me instead.
