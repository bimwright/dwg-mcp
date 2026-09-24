# Native drawing capture

## Scope and acceptance

Upgrade the existing `dwg_capture_view_image` tool to capture through AutoCAD's in-process `DocumentExtension.CapturePreviewImage` API. Keep existing arguments and the `output_path`, `width`, `height`, `image_format` result fields. No desktop automation, automatic zoom, geometry writes, LISP, DWG save, or automatic fallback to a different renderer.

Acceptance: a valid raster with actual dimensions; camera/document metadata from the same capture; optional expected-document binding; unchanged path/overwrite protections; commands in progress and changing views rejected before image publication. Tests/builds and installed-tool runtime acceptance are separate checks.

## Request and result

Existing format/path/size options remain. `expected_document_fingerprint` is an optional GUID from a previous capture; a mismatch fails without switching documents. An absent value captures the current active drawing.

The result adds:

- `capture_id`, `source=autocad_document_preview`, `captured_at` (ISO 8601 with timezone), `duration_ms`, `sha256`, and `requested_size`. `width`/`height` are the actual bitmap size; the native API can round dimensions.
- `document`: drawing basename, fingerprint, layout, current-space handle, tile mode.
- `viewport`: active viewport number and screen pixel dimensions.
- `camera`: DCS center, WCS target/direction, width/height in drawing units, twist in radians, perspective, lens length and clipping settings.
- `capture_scope=active_document`, `camera_scope=active_viewport`. For tiled views and paper layouts, a single active camera does not necessarily describe the entire image. No pixel-to-world selection mapping or object visibility claim is returned.

The service reads context before and after the native capture and requires it to remain unchanged. The check guards against detectable document/view changes, not every possible graphics/model revision. The image is written after validation with `CreateNew` unless overwrite is explicitly enabled. Generated names include a GUID. The image hash links the response to exact saved bytes.

Only the image is written. A reading-session orchestrator must persist this result beside its image and parent/region IDs. This change does not implement an atlas, object index, selection, or zoom completion protocol. Wait for a zoom/command to finish, then capture.

## Validation evidence and remaining gates

- A preceding live `send_code` probe on AutoCAD 2024 tested the native API at three zoom levels, including readable fabrication notes. The API plus PNG save took 53–87 ms in that session; this is not a benchmark or end-to-end latency promise.
- That probe restored the original camera and observed unchanged entity count, handseed, DBMOD and selection. Sampled invariants are not a full drawing-content hash.
- 471 automated tests passed, including 19 new contract cases covering document mismatch, camera/layout/viewport changes, native size rounding, metadata isolation, empty image rejection and schema compatibility.
- AutoCAD 2024 and 2027 Release plugin builds passed. The 2027 build retained four existing Roslyn/obsolete-network-API warnings; 2024 built without warnings.
- The production capture service/math/contract source was also executed through `send_code` on the live AutoCAD 2024 host, with only namespace wrappers removed for C# scripting. A valid PNG and actual dimensions/hash/camera were returned; an incorrect fingerprint produced no image, and an existing image was not overwritten. This validates the service path, not the newly installed MCP tool registration/transport.
- Pending installed-tool acceptance: restart with the rebuilt plugin/server, call `dwg_capture_view_image` directly, inspect output and metadata, test incorrect fingerprint/no overwrite, and verify drawing/view state. Do not overwrite add-in DLLs while AutoCAD is running.
- Other host years, paper layouts, tiled views, 3D visual styles and inactive/minimized-window behavior require their own live validation. Do not treat a successful compile as proving these cases.

API references: [Autodesk managed reference](https://help.autodesk.com/cloudhelp/2022/ENU/OARX-ManagedRefGuide/files/OARX-ManagedRefGuide-Autodesk_AutoCAD_ApplicationServices_DocumentExtension_CapturePreviewImage_this_Document_uint_modoptIsLong_uint_modoptIsLong.html), [Autodesk capture example](https://blog.autodesk.io/capturepreviewimage-api-to-create-the-image-from-a-autocad-document/).
