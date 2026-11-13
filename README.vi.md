<!-- mcp-name: io.github.bimwright/dwg-mcp -->

<p align="center">
  <img src="https://raw.githubusercontent.com/bimwright/.github/master/assets/logos/dwg-mcp.png" alt="dwg-mcp" width="180" />
</p>

<h1 align="center">dwg-mcp</h1>

<p align="center">
  <a href="https://github.com/bimwright/dwg-mcp/actions/workflows/build.yml"><img src="https://github.com/bimwright/dwg-mcp/actions/workflows/build.yml/badge.svg" alt="build" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-Apache%202.0-blue.svg" alt="license" /></a>
  <a href="#phiên-bản-autocad-hỗ-trợ"><img src="https://img.shields.io/badge/AutoCAD-2022--2027-186BFF" alt="AutoCAD 2022-2027" /></a>
  <a href="#công-cụ"><img src="https://img.shields.io/badge/MCP-45%20default%20%2B%20optional-6C47FF" alt="MCP tools" /></a>
</p>

<p align="center">
  <a href="README.md">English</a> · Tiếng Việt · <a href="README.zh-CN.md">简体中文</a> · <a href="README.ja.md">日本語</a>
</p>

---

## Dịch bản vẽ không nên dừng lại ở Copy-Paste thủ công

Bản vẽ thi công và kỹ thuật chứa rất nhiều text — ghi chú kỹ thuật, chú thích vật liệu, legend, kích thước. Khi bản vẽ đến bằng tiếng nước ngoài, việc dịch là bắt buộc trước khi đội dự án có thể làm việc.

Quy trình thông thường rất đau đầu: chọn text từng entity, copy sang translator, paste lại, sửa font (vì font SHX không hiển thị được tiếng Việt hay CJK), chỉnh chiều cao, hy vọng không có gì bị lệch. Nhân lên hàng trăm text fragment mỗi sheet, hàng chục sheet mỗi dự án.

`dwg-mcp` nén quy trình đó thành hai bước: chọn text, để AI agent đọc, dịch, và ghi lại tại chỗ — đúng font, đúng chiều cao, đúng nhóm spatial, và một lần undo duy nhất.

---

## dwg-mcp là gì

`dwg-mcp` là cổng MCP cục bộ cho Autodesk AutoCAD 2022-2027.

Gồm hai phần:

- **Bimwright.Dwg.Server**: MCP server .NET 8, được Claude Code, Cursor, OpenCode hoặc MCP client khác khởi chạy.
- **Bimwright.Dwg.Plugin**: các shell add-in theo từng phiên bản AutoCAD, thực thi lệnh trực tiếp lên database bản vẽ.

Agent nói MCP. Server nói với plugin qua local wire: TCP NDJSON cho AutoCAD 2022–2024, và Named Pipe (loopback, tránh firewall prompt) cho 2025–2027. Plugin nói AutoCAD .NET API.

Mọi thứ chạy trên máy bạn.

---

## Tại sao quan trọng

AI agent cho phép mô tả "dịch tất cả text đã chọn sang tiếng Việt" và nó xảy ra — đúng — ngay trong bản vẽ. Nhưng chỉ intent thôi chưa đủ. Thao tác text trong AutoCAD đòi hỏi hiểu layout không gian, nhóm fragment, giới hạn font, MText vs DBText, block reference, và scaling chiều cao.

`dwg-mcp` xử lý sự phức tạp đó:

- **Spatial clustering** nhóm text rời rạc thành câu logic (theo block, hàng, cột, đoạn).
- **Tự động xử lý font** tạo text style Unicode và áp dụng — không còn dấu hỏi SHX.
- **Scaling chiều cao** bù trừ cho mật độ thị giác khác nhau giữa Latin và CJK.
- **Chuyển đổi MText** nâng cấp single-line fragment thành multi-line text khi an toàn.
- **Một lần undo** gói mỗi thao tác trong transaction.

---

## Bằng chứng sử dụng

220 tool call hoàn thành trong 19 ngày sử dụng thực tế trên bản vẽ thi công. Tỷ lệ thành công 98.2%.

| Tool | Lượt gọi |
|------|----------|
| get_selected_texts | ~100 |
| translate_and_rewrite | ~77 |
| send_code | ~28 |
| collapse_and_rewrite | ~11 |
| update_texts | ~10 |
| apply_unicode_style | ~4 |

---

## Kiến trúc

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
               | TCP NDJSON (2022-2024) / Named Pipe (2025-2027)
              | token auth
              v
+---------------------------+
| Bimwright.Dwg.Plugin      |
| AutoCAD 2022-2027 shells |
+---------------------------+
              |
              | LockDocument()
              v
+---------------------------+
| AutoCAD .NET API          |
| ObjectARX 2022-2027       |
+---------------------------+
```

Xem [ARCHITECTURE.md](ARCHITECTURE.md) để biết chi tiết về threading, discovery, và auth.

---

## Cài đặt

Tải setup ZIP từ [GitHub Releases](https://github.com/bimwright/dwg-mcp/releases/latest) (`DwgMcp.Setup-*-win-x64.zip`). Xem `packedAutocadYears` trong `manifest.json` và `PackageContents.xml` của bundle trong ZIP đã tải để biết các năm có plugin; xem ghi chú phát hành để biết các năm đã kiểm chứng trên AutoCAD đang chạy. Source có shell cho 2022–2027, còn plugin đóng gói và host đã kiểm chứng tùy từng bản phát hành. Năm chưa có trong gói cần build plugin từ source với AutoCAD SDK tương ứng. Giải nén rồi chạy `install.ps1` (xem README tiếng Anh để copy-paste đầy đủ).

**NuGet (chỉ gateway):** `dotnet tool install --global Bimwright.Dwg.Server --version 2.1.2` cần .NET 8 và không cài plugin AutoCAD. Cài plugin cùng phiên bản từ Setup ZIP.

**Claude Desktop:** tải `dwg-mcp-desktop-2.1.2.mcpb` và checksum trong cùng release, rồi cài qua Settings → Extensions → Advanced settings. Extension chỉ chạy đúng executable của Setup 2.1.2, không cài gateway/plugin và không tự chọn bản NuGet/PATH khác. File override phải khớp SHA-256.

**Developer:** `pwsh scripts/install.ps1 -Version 2024` từ repo sau khi build; hoặc `NETLOAD` DLL Debug.

### 3. Cấu hình MCP client

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

`dwg_send_code` mặc định có sẵn qua `meta`. `--enable-send-code` / `--disable-send-code` điều khiển server (JSON `enableSendCode`; env `BIMWRIGHT_DWG_ENABLE_SEND_CODE`). Khi tắt, batch chứa C# cũng bị từ chối trước khi gửi bất kỳ lệnh nào. `MCPDISABLECODE` tắt C# trong session AutoCAD; `MCPENABLECODE` chỉ bật lại phía host, không ghi đè công tắc server. `dwg_run_lisp` giữ tên để tương thích nhưng từ chối mọi yêu cầu ở cả server và plugin. Bản này chưa có môi trường cách ly LISP hoặc cơ chế xác lập script tin cậy. `--read-only` gỡ cả hai tool.

Từ 2.1.2, `dwg_send_code(code, data?, compile_only=false)` hỗ trợ JSON input riêng. `data` là JSON riêng, truy cập trong C# bằng `Newtonsoft.Json.Linq.JToken`, tối đa 512 KiB UTF-8, độ sâu 64, key không trùng và số hữu hạn. Nhận object/array/scalar; JSON null được coi là không truyền data, chuỗi ngày giữ nguyên. Ví dụ `code: "return (double)data[\"width\"] * (double)data[\"height\"];", data: {"width":12.5,"height":4}`. `compile_only=true` chỉ biên dịch, không chạy câu lệnh và không sinh stdout; vẫn cần target đã xác minh và drawing active. Đọc `result.ok`, `compiled`, `executed`, `diagnostics` (nguồn, dòng/cột từ 1, mã lỗi, nội dung, mức độ) và `location` nếu có lỗi runtime. Dòng lỗi khớp code gốc vì không chèn data vào source. Cần gateway/plugin cùng hỗ trợ; plugin cũ bị từ chối trước khi gửi code. Lệnh có data/compile_only phải gọi trực tiếp, không đặt trong batch/baked tool. Cách gọi chỉ có code vẫn được hỗ trợ. Giá trị data không vào call log; History chỉ giữ độ dài và SHA-256.

`dwg_send_code` chỉ nhận snippet đồng bộ được gửi trực tiếp: `async`/`await` và directive `#load` bị chặn trước thực thi. Giữ AutoCAD API trên luồng gọi; không chuyển sang `Task.Run` hoặc luồng khác. Chỉ trả DTO JSON; object AutoCAD/COM bị từ chối kể cả khi lồng nhau. `dwg_inspect_lisp` vẫn phân tích tĩnh: nguồn quá 2.000.000 ký tự bị đánh dấu không thể kiểm tra đầy đủ/nguy hiểm; giới hạn hiển thị 200 findings không làm mất phát hiện mức nguy hiểm. Regex có timeout; scan hết thời gian được báo chưa đầy đủ/nguy hiểm, không trả `clean`.

Trước khi ghi, gọi `dwg_list_available_targets`, rồi `dwg_switch_target(target="pid:<PID>")`. Mặc định `verify=true` xác minh process và drawing đang active; `verify=false` chỉ cho đọc. `--target` chỉ chọn đích ban đầu, không cho phép ghi. Có nhiều instance phù hợp sẽ trả `AMBIGUOUS_TARGET`.

Khi đã có binding, chuyển sang instance khác phải dùng `pid:<PID>` hoặc `id:acad-<year>-<PID>`; rời target đang sống và đã xác minh còn cần `rebind=true`. Chọn lại cùng process với `verify=false` gỡ xác nhận ghi. ID vòng đời drawing dự kiến trước đó được giữ, trừ khi `rebind=true` xóa pin cũ hoặc `expected_document_id` được chỉ rõ để thay pin. Sau khi mở/chuyển drawing, xem `dwg_health().target`; xác nhận drawing khác cần `expected_document_id` khớp drawing đó hoặc `rebind=true`. Xác minh thất bại giữ binding trước đó; lệnh đang chờ dưới binding cũ bị từ chối trước khi gửi. Cần cập nhật gateway và plugin cùng nhau: plugin cũ không đủ giao thức xác minh ghi, plugin mới từ chối ghi không có target guard từ gateway cũ.

Trong `--read-only`, `dwg_switch_target` không có trong catalog và gọi trực tiếp cũng bị từ chối. Khởi động gateway với `--read-only --target pid:<PID>` (hoặc `--target id:acad-<year>-<PID>`), rồi kiểm tra health và target hiện tại. Nếu gặp `TARGET_LOST` hoặc `TARGET_CHANGED`, gọi `dwg_list_available_targets` rồi khởi động lại gateway với `--target` cụ thể đã chọn. Binding mất không tự chuyển sang instance khác.

Để chọn AutoCAD ban đầu, dùng PID hoặc năm 4 chữ số chỉ khớp một instance:

```json
{
  "mcpServers": {
    "bimwright-dwg": {
      "command": "bimwright-dwg",
      "args": ["--target", "pid:12345"]
    }
  }
}
```

Dùng `--read-only` để gỡ toolset write-capable. Dùng `--toolsets all` hoặc danh sách **có đủ default bạn cần** (list tùy chỉnh **thay** default — ví dụ `query,modify,meta,view,annotation`). Env: `BIMWRIGHT_DWG_TOOLSETS=…`.

---

Call log **mặc định tắt**. Dùng `--enable-call-log` / `--disable-call-log` (JSON `enableCallLog`; env `BIMWRIGHT_DWG_ENABLE_CALL_LOG`). Thứ tự ưu tiên: JSON → env → CLI; flag bật/tắt xuất hiện cuối thắng. Khi tắt, server không tạo/ghi thêm call log hoặc call journal và không xoá log cũ. Khi bật, `%LOCALAPPDATA%\Bimwright\dwg-mcp\dwg-mcp-calls.jsonl` chỉ ghi tham số đã redact và metadata thời lượng/kết quả, không ghi response. Trường code, kể cả trong batch lồng nhau, chỉ lưu độ dài ký tự và SHA-256 của body UTF-8 gốc; bỏ nội dung lỗi liên quan code vì có thể chứa source. Chưa triển khai cache body hoặc body journal. ToolBaker adaptive usage, diagnostic log và History trong RAM độc lập với công tắc này; plugin không có writer call log bền vững.

## Toast hoạt động và History

Toast mặc định bật, dùng một thẻ không lấy focus bàn phím: tiêu đề `dwg-mcp <năm>`, dòng tool mới nhất và bộ đếm **Success · Failed · Capture**. Bộ đếm cuộn kiểu odometer; ảnh nằm giữa khung cố định, giữ ít nhất 5 giây và chuyển ảnh bằng cross-fade. Tắt **Animation effects** trong Windows để cập nhật ngay không animation. Hover giữ thẻ mở; thời gian idle là 10/20/30/60 giây (mặc định 20). AutoCAD thu nhỏ hoặc modal thì giữ số đếm và hiện lại khi có thể. `MCPTOAST` bật/tắt; `MCPSETTINGS` chỉnh toast và branding. Branding mặc định tắt, chỉ hiện khi hover và lưu qua lần khởi động; lỗi lưu được báo nhưng lựa chọn vẫn có hiệu lực trong phiên.

Bấm thân thẻ mở/focus **History** rồi đóng thẻ; × chỉ đóng; bấm thumbnail mở ảnh qua allowlist rồi đóng thẻ. `MCPHISTORY` mở cùng cửa sổ modeless. History giữ 1.000 kết quả gọn gần nhất của lệnh qua plugin trong RAM, kể cả khi toast tắt; hỗ trợ tìm kiếm, lọc Success/Failed và Read/Write, thời lượng và chi tiết khi chọn dòng. Đóng thẻ không xoá History; **New session** xoá danh sách. History không thêm file call log và không giữ raw input, body code hay JSON kết quả. Tool chỉ chạy ở server chưa có trong History. `enableToast`, `toastIdleSeconds`, `showBranding` lưu tại `%LOCALAPPDATA%\Bimwright\dwg-mcp\settings.json`.

---

## Health runtime và kiểm tra cập nhật (v2.1.1)

`dwg_health()` mặc định offline, read-only. Đầu phiên, chọn đúng target rồi gọi `dwg_health(check_updates=true)` một lần; các probe thường về sau vẫn offline. Tool báo trạng thái host/drawing, version/build server và plugin đang nạp, cùng alignment; không cần hoặc lock drawing. Không có target vẫn giữ identity gateway với `ok:false`, `host_connected:false`. Plugin cũ trả `HEALTH_UNSUPPORTED` chỉ chứng minh có phản hồi, chưa xác minh được health/identity. UI bận giữ identity plugin nhưng document state chưa quan sát được. Health không lên toast và không chạy trong drawing batch.

SHA-256 chỉ quan sát file trên disk lúc capture, không chứng minh memory đã reload hay publisher đáng tin. Check GitHub so riêng hai component với một stable release/tag revision, tổng ngân sách mạng 5 giây và metadata có giới hạn. `checked` chỉ xác nhận lookup; thiếu bằng chứng vẫn `unknown`, version bằng nhau hoặc `update_available=false` không chứng minh trùng build release.

Đọc cả `update.components`, thông báo đầy đủ, `lookup_reason`, `protected_component_reasons` và `unobserved_components`. `UPDATE_CHECK_UNAVAILABLE` là lookup bên ngoài thất bại, không phải lỗi runtime hay lý do xin phép cài; `LOCAL_VERSION_AHEAD` chỉ thông báo bản local mới hơn, không downgrade. Chỉ mode `prepare_update` cùng `bundle_update_proposal_allowed=true` cho phép đề xuất update, và vẫn cần quyền cài đặt. Các mode khác chỉ check/report/review. Health không tự cài, tải package, sửa config client, save/close drawing hay restart process. Xem [hướng dẫn agent](AGENTS.md).

Sau reconnect, đối chiếu initialize và `tools/list` mới trên cùng connection với `client_verification`, gồm `_meta["bimwright/dwg-mcp/runtime"]`, schema và annotations. Server không thể tự chứng minh client đã refresh cache. Health vẫn có trong read-only; `openWorldHint=true` vì tùy chọn GitHub. Có từ v2.1.1; cập nhật gateway và plugin từ cùng release. CI đã build đủ sáu plugin AutoCAD 2022–2027; xem release notes để biết phạm vi kiểm chứng.

## Công cụ

Bản 2.1.2 mặc định expose 45 tool: query (gồm `dwg_get_drawing_profile` và `dwg_inspect_lisp`), modify, meta (gồm `dwg_send_code`/`dwg_run_lisp`), view, và `dwg_capture_view_image` mặc định bật. Các toolset tùy chọn ToolBaker, annotation, block, dimension, export, và drawing được kích hoạt qua `--toolsets`, nâng tổng diện tích bề mặt MCP lên 69 tool. Số tool đăng ký có tính `dwg_run_lisp`, hiện chỉ trả từ chối; không verdict nào cấp quyền thực thi.

CAD tool chạy trên active document hiện tại của AutoCAD target đang chọn. Entity input và entity id trả về dùng AutoCAD hex handle, ví dụ `7F5AD`, do tool selection, creation, hoặc properties trả về. Creation, copy, offset, và modify response identify entity tạo/sửa bằng hex handle.

Lời gọi chỉ dùng tham số query/count cũ vẫn quét model space. Query cũ giữ thứ tự tham số, các trường kết quả, thứ tự duyệt và output `include_geometry`; limit mặc định 500, clamp trong 1–5000. `dwg_select_by_layer` và `dwg_select_by_type` giữ contract trả handle model-space, không đổi pickfirst selection. Cả bốn bộ lọc type so tên managed ngắn hoặc đầy đủ, không phân biệt hoa thường; bổ sung alias `TEXT` → `DBText`, `INSERT` → `BlockReference`, `LWPOLYLINE` → `Polyline`, không suy diễn thêm loại DXF từ `POLYLINE`.

Bắt đầu đọc bản vẽ lớn bằng `dwg_get_drawing_profile()` (`section="summary"`, `offset=0`). Mặc định tool quét entity cấp trên cùng trong model space và mọi layout, không mở rộng block definition hay nội dung xref; một block reference được tính là một entity. Tham số thứ năm `space` tùy chọn nhận `all` (mặc định), `model` không đọc layout definition, hoặc `layout:<name>` cho đúng một layout có tên. Xem source, scope và trạng thái đầy đủ trước khi dùng tổng số. Nhóm type giữ nguyên tên runtime đầy đủ; nhóm layer và space có số đếm riêng. Số đã quan sát/chưa đầy đủ và giá trị chưa xác định được ghi rõ hoặc trả `null`, không coi là tổng đầy đủ.

Theo `output.next_calls` để phân trang `types`, `layers` hoặc `spaces`; mỗi lời gọi đề xuất chứa `section`, `offset`, `expected_snapshot`, `max_output_bytes` và `space`. Trang đầu có thể bỏ `expected_snapshot`, nhưng `offset>0` bắt buộc có; snapshot đổi trả `PROFILE_CHANGED`, khi đó đọc lại summary. Giữ `document_id` riêng cùng kết quả. Mỗi trang quét lại drawing, không dùng cache. `max_output_bytes` mặc định 16384 (4096–65536), giới hạn kết quả MCP cuối đã serialize và còn chịu global response budget; nó giới hạn dung lượng trả về, không giới hạn thời gian quét. Snapshot gắn với scope đã chọn: query/count/profile model bỏ qua thay đổi chỉ ở viewport, còn profile all/layout và query layout tính cả thay đổi đó. Đổi scope cần đọc lại summary, bỏ `expected_snapshot` cũ. Response profile có scope phải ghi `scope.requested_space`; plugin cũ bỏ qua scope bị từ chối với `PROFILE_SCOPE_UNSUPPORTED`.

| Tool | Mục đích |
|------|----------|
| `dwg_health` | Probe health/identity server-plugin; tùy chọn check stable release, mặc định offline/read-only |
| `dwg_get_drawing_info` | Đọc tên drawing, current layer, current space/layout, và unit scalar |
| `dwg_get_drawing_profile` | Đếm entity cấp trên cùng chỉ đọc cho all (mặc định), riêng model hoặc một layout có tên; snapshot theo scope và giới hạn byte response cuối |
| `dwg_get_entity_properties` | Đọc property của entity theo AutoCAD hex handle |
| `dwg_list_layers` | Liệt kê layer trong drawing hiện tại kèm color và state flag |
| `dwg_query_entities` | Query model-space cũ hoặc opt-in phân trang theo vùng/trường, cursor có chữ ký và giới hạn byte kết quả cuối |
| `dwg_count_entities` | Đếm theo type/layer/color, tùy chọn extents WCS và space có tên; báo rõ coverage của các match chưa xác định |
| `dwg_select_by_layer` | Trả về handle list của model-space entity trên một layer, không đổi pickfirst selection |
| `dwg_select_by_type` | Trả về handle list của model-space entity theo một entity type, không đổi pickfirst selection |
| `dwg_get_selected_texts` | Cluster từ pickfirst để ghi lại theo flow cũ, hoặc nguồn handles/bbox để đọc phân trang cluster chỉ đọc |
| `dwg_update_texts` | Ghi text theo handle trong một transaction |
| `dwg_create_layer` | Đảm bảo layer tồn tại, không ghi đè property của layer đã có |
| `dwg_create_line` | Tạo một line trong drawing space hiện tại |
| `dwg_create_circle` | Tạo một circle trong drawing space hiện tại |
| `dwg_attach_image` | Chèn ảnh raster liên kết (PNG/JPG/TIFF/BMP) đúng vị trí và kích thước |
| `dwg_create_point` | Tạo một point và trả về hex handle |
| `dwg_create_polyline` | Tạo lightweight polyline từ vertices và trả về hex handle |
| `dwg_create_rectangle` | Tạo rectangle polyline và trả về hex handle |
| `dwg_create_arc` | Tạo một arc và trả về hex handle |
| `dwg_create_ellipse` | Tạo một ellipse và trả về hex handle |
| `dwg_change_layer` | Chuyển entity theo hex handle sang layer khác |
| `dwg_change_color` | Đổi color entity bằng AutoCAD color index |
| `dwg_move_entities` | Move entity theo hex handle bằng displacement vector |
| `dwg_rotate_entities` | Rotate entity theo hex handle quanh base point |
| `dwg_scale_entities` | Scale entity theo hex handle quanh base point |
| `dwg_copy_entities` | Copy entity theo hex handle và trả về copied handle |
| `dwg_erase_entities` | Erase entity theo hex handle |
| `dwg_offset_entities` | Offset curve entity và trả về generated handle |
| `dwg_translate_and_rewrite` | **Ưu tiên.** Ghi text đã dịch, tự động xử lý anchor, xóa, MText, font, chiều cao |
| `dwg_apply_unicode_style` | Đảm bảo style `Bimwright_Unicode` tồn tại và áp dụng |
| `dwg_collapse_and_rewrite` | Rewrite low-level với kiểm soát hình học chi tiết |
| `dwg_list_available_targets` | Liệt kê từng instance với PID, target ID và thời điểm khởi động process |
| `dwg_get_current_target` | Xem trạng thái binding, generation, PID và drawing đã xác minh |
| `dwg_switch_target` | Chọn và xác minh instance; chuyển instance cần `pid:`/`id:`, thêm `rebind=true` khi rời target đang sống và đã xác minh. Không có trong read-only |
| `dwg_list_recent_drawings` | Liệt kê tối đa 50 DWG recent của AutoCAD đang kết nối, có lọc tên; không cần drawing active |
| `dwg_open_drawing` | Mở DWG theo path tuyệt đối, tùy chọn đọc-only; tái sử dụng file đã mở, không lưu/đóng drawing; mặc định chờ 600s ([chi tiết](README.md#drawing-session-tools)) |
| `dwg_report_task_result` | Lưu báo cáo công việc (kết quả, tóm tắt, nội dung Markdown có link entity `dwg:<handle>`) thành file local cho người dùng AutoCAD; không sửa drawing |
| `dwg_batch_execute` | Chạy nhiều wire command nội bộ như một logical batch |
| `dwg_send_code` | Chạy C# snippet trên AutoCAD .NET API (globals `doc`/`db`/`ed`, hỗ trợ `return` value + stdout capture, cooperative cancel 30s; `MCPDISABLECODE`/`MCPENABLECODE` bật/tắt theo session) |
| `dwg_run_lisp` | Giữ để tương thích: mọi input `file`/`code`/`command` đều bị từ chối với `lisp_execution_blocked`. Tool không đọc file, không gửi plugin và không thực thi. Dùng `dwg_inspect_lisp` để phân tích tĩnh. |
| `dwg_inspect_lisp` | Quét an toàn tĩnh cho file `.lsp` hoặc AutoLISP inline — phát hiện exec process (`startapp`/`shell`), COM nguy hiểm (`WScript.Shell`/`XMLHTTP`), persistence (`acaddoc.lsp`, ghi registry), xóa file, `(load …)` tầng hai, obfuscation (`eval`/`read`). Trả `verdict` clean/caution/dangerous + findings. Phân tích local thuần; không thực thi, không cần AutoCAD; dùng được cả dưới `--read-only`. `clean` không chứng nhận an toàn. Report luôn có `execution_authorized=false` và `safety_assured=false`. |
| `dwg_zoom_extents` | Zoom đến giới hạn của viewport bản vẽ |
| `dwg_zoom_window` | Zoom viewport đến một cửa sổ được xác định bởi hai điểm góc |
| `dwg_zoom_to_entity` | Zoom viewport đến giới hạn của một entity cụ thể theo handle |
| `dwg_capture_view_image` | Chụp đồ họa trong AutoCAD; trả ảnh trực tiếp qua MCP, đường dẫn ảnh, kích thước thực, hash, metadata bản vẽ/viewport/camera (mặc định bật; path policy) |
| `dwg_inspect_view_region` | Chọn vùng 0–1 trên ảnh nguồn; zoom, regenerate và trả ảnh mới trực tiếp qua MCP, có liên kết ảnh cha |
| `dwg_restore_view` | Quay lại camera của ảnh trước và nhận ảnh chụp mới |

Đọc bản vẽ: chụp → xem ảnh → chọn vùng → `dwg_inspect_view_region` → xem chi tiết. Giữ ID ảnh cha và dùng `dwg_restore_view` để quay lại tổng thể. Lịch sử giữ tối đa 64 ảnh / 30 phút trong tiến trình AutoCAD. Bản đầu hỗ trợ một viewport model space, nhìn từ trên xuống, không xoay, trực giao; từ chối nguồn cũ hoặc view ngoài phạm vi. Capture trả thêm image block MCP (tối đa 8 MiB) cạnh JSON hiện có. Các tool thay đổi camera và ghi ảnh, và không dùng được trong `--read-only`; không sửa geometry hay lưu DWG. Xem release notes để biết phạm vi kiểm chứng và quy trình mô tả ở trên.

**Đọc entity theo vùng và phân trang** mở rộng `dwg_query_entities` và `dwg_count_entities`, không thêm MCP tool. Cung cấp `bbox`, `bbox_mode`, `space`, `fields`, `cursor` hoặc `max_output_bytes` sẽ bật phân trang; dùng `paged=true` nếu muốn phân trang không lọc. Chỉ có `paged=false` thì vẫn giữ hành vi cũ. Năm tham số query cũ vẫn đứng đầu. Không kết hợp `include_geometry=true` với tùy chọn mới; dùng `fields` để chọn dữ liệu.

| Tùy chọn query nối thêm | Ý nghĩa |
|---|---|
| `paged=false` | Đặt true để bật phân trang khi không có tùy chọn mới khác |
| `bbox=null` | Chuỗi JSON `{"min":[x,y,z],"max":[x,y,z]}` với tọa độ WCS hữu hạn, min ≤ max |
| `bbox_mode=null` | `intersects` mặc định khi có box, hoặc `contains`; cần `bbox` |
| `space=null` | Mặc định `model`, hoặc `layout:<tên LayoutName hiển thị>` khớp chính xác không phân biệt hoa thường; lấy tên từ profile |
| `fields=null` | Chuỗi mảng JSON; mặc định `handle`, `type`, `layer`. Chỉ nhận `handle`, `type`, `runtime_type`, `layer`, `color_index`, `text`, `position`, `extents`; luôn có `handle` |
| `cursor=null` | Token tiếp trang từ kết quả trước |
| `max_output_bytes=null` | Giới hạn MCP result cuối, mặc định 16384 byte, khoảng 4096–65536; global budget nhỏ hơn vẫn áp dụng |

`bbox` so `GeometricExtents` dạng hộp 3D theo trục WCS, kể cả biên chạm nhau; không phải kiểm tra giao hình học chính xác. Tọa độ là drawing units, không quy đổi INSUNITS. Kết quả kèm code/tên INSUNITS; code 0 giữ nghĩa unitless/unknown. Chỉ duyệt entity cấp trên cùng của model space hoặc layout đã chọn, không mở rộng block/xref/nội dung nhìn qua viewport, không fallback sang space khác. `dwg_count_entities` chỉ nối `bbox`, `bbox_mode`, `space` vào ba tham số cũ, kèm metadata vùng và coverage match chưa rõ. Muốn lấy handle theo vùng, query với `fields="[\"handle\"]"`; contract select không đổi.

Trang sắp theo giá trị số của handle AutoCAD. Theo `result.output.next_calls`, giữ nguyên filter và fields; cursor có chữ ký của host session gắn với vòng đời drawing, revision và handle cuối **đã giao**. Có thể đổi `limit` hoặc byte cap; đổi filter/fields phải bỏ cursor và bắt đầu lại. Cursor sai, bị sửa hoặc drawing đổi bị từ chối. Mỗi trang quét lại space, không cache; byte cap chỉ giới hạn output, không giới hạn thời gian quét. `result.output` báo budget, `omitted_matching`, `has_more`, `next_cursor`, `next_calls` và hướng dẫn nếu row bị chặn. Lỗi đọc/filter/extents cần thiết được đếm trong coverage, trả `exact_match_count=null`, `matched_count` đã quan sát và không có continuation để tránh bỏ qua handle chưa rõ. Property không hỗ trợ trên entity trả `null` cùng `unavailable_fields`. Query/count mở rộng phải gọi trực tiếp: gateway và plugin đều preflight từ chối cả batch trước khi lệnh trước đó chạy. Batch query/count chỉ dùng tham số cũ giữ nguyên hành vi; baked query vẫn không được phép. Đọc mở rộng cần gateway/plugin cùng hỗ trợ: gateway trả `QUERY_FEATURE_UNSUPPORTED` khi plugin cũ chưa hỗ trợ và loại output dạng cũ, còn lời gọi chỉ dùng tham số cũ vẫn dùng được.

**Đọc text không cần chọn trước** bằng nguồn rõ ràng trong tool `dwg_get_selected_texts` hiện có. Hai tham số cũ `groupingStrength="normal"`, `includeEntities=false` vẫn đứng đầu; chỉ dùng hai tham số này thì giữ quy trình selection/writeback cũ. Bất kỳ tùy chọn mới nào cũng yêu cầu đúng một nguồn `handles` hoặc `bbox`, không ngầm quay về selection.

| Tùy chọn text-read nối thêm | Ý nghĩa |
|---|---|
| `handles=null` | Chuỗi mảng JSON chứa tối đa 5000 giá trị handle hex, được loại trùng; `"[]"` là nguồn rỗng hợp lệ |
| `bbox=null` | Chuỗi object JSON `{"min":[x,y,z],"max":[x,y,z]}`, cùng quy tắc extents WCS/drawing units như spatial query |
| `bbox_mode=null` | `intersects` mặc định khi có box, hoặc `contains`; cần `bbox` |
| `space=null` | Mặc định `model`, hoặc đúng tên `layout:<name>` không phân biệt hoa thường; không fallback |
| `cursor=null` | Continuation có chữ ký cho cluster text, từ `output.next_calls` |
| `max_output_bytes=null` | Giới hạn MCP result cuối, mặc định 16384 byte, khoảng 4096–65536, còn chịu global budget |

Nguồn mới chỉ đọc DBText, MText, MLeader, attribute reference và attribute definition constant gặp trực tiếp ở cấp trên cùng; không mở rộng BlockReference, block lồng hay xref. Đường selection cũ giữ cách đọc block sâu một cấp. Snapshot nguồn model bỏ qua thay đổi chỉ ở viewport; nguồn layout có tính thay đổi đó. Toàn bộ nguồn giới hạn 5000 text record và tổng 8 MiB text UTF-8. Nguồn chưa đầy đủ trả coverage, không trả cluster/cursor; `TEXT_SOURCE_TOO_LARGE` yêu cầu thu hẹp nguồn. Spatial clusterer không đổi, xử lý toàn bộ nguồn hợp lệ trước khi phân trang nguyên cluster theo thứ tự ổn định.

Theo `result.output.next_calls`, giữ tên tham số MCP camelCase `groupingStrength` và `includeEntities`. Cursor gắn với document/scope revision, nguồn và tùy chọn grouping, chứa thứ tự cluster riêng cho text, không phải entity handle. Giới hạn byte chỉ bỏ nguyên cluster và chỉ tiến qua cluster đã giao; xem `omitted_clusters`, ghi chú cluster bị chặn và continuation. Nếu cluster quá lớn, tăng cap tối đa 65536 (cả global budget nếu đang giới hạn), hoặc đặt `includeEntities=false`, giảm độ gom nhóm hay thu hẹp nguồn. Đổi source/grouping/includeEntities phải bắt đầu lại không dùng cursor.

Kết quả nguồn mới có `read_only=true`, `writeback_ready=false`, dùng `cluster_key` opaque thay vì ID số hay `rewrite_mode` cũ. Đọc nguồn mới giữ nguyên nhóm rewrite cũ và không sửa hình học hay selection CAD. Để dịch từng entity, đọc với `includeEntities=true` rồi ghi bằng `dwg_update_texts` theo handle; `dwg_translate_and_rewrite` vẫn cần lần đọc **selection thủ công**, và trang nguồn mới chưa cho phép ghi theo cụm trong đợt này. Phải gọi đọc nguồn mới trực tiếp: gateway/plugin preflight từ chối cả batch và chặn enhanced baked call trước thực thi. Selection batch cũ giữ nguyên. Gateway trả `TEXT_FEATURE_UNSUPPORTED` và loại payload selection từ plugin cũ chưa hỗ trợ nguồn, không fallback.

ToolBaker là toolset tùy chọn:

| Tool | Mục đích |
|------|----------|
| `dwg_list_baked_tools` | Liệt kê baked tool đã accept trong SQLite registry của server |
| `dwg_run_baked_tool` | Chạy baked tool đã accept |
| `dwg_list_bake_suggestions` | Liệt kê gợi ý workflow lặp lại |
| `dwg_accept_bake_suggestion` | Validate, smoke-test, và accept gợi ý |
| `dwg_dismiss_bake_suggestion` | Dismiss hoặc suppress gợi ý |
| `dwg_create_bake_issue_draft` | Tạo GitHub issue draft cho gợi ý mà không submit |

Các tool Annotation tùy chọn hiển thị khi toolset `annotation` được bật:

| Tool | Mục đích |
|------|----------|
| `dwg_create_text` | Tạo chữ dòng đơn (DBText) với chiều cao, góc xoay và các thuộc tính mục tiêu |
| `dwg_create_mtext` | Tạo chữ đa dòng (MText) với định dạng và chiều rộng |
| `dwg_create_leader` | Tạo multileader (MLeader) với nội dung text tùy chọn |
| `dwg_create_table` | Tạo bảng AutoCAD với nội dung văn bản hàng/cột được chỉ định |

Các tool Block tùy chọn hiển thị khi toolset `block` được bật:

| Tool | Mục đích |
|------|----------|
| `dwg_list_blocks` | Liệt kê các định nghĩa block trong bản vẽ hiện tại (an toàn read-only) |
| `dwg_get_block_attributes` | Đọc thuộc tính của một block reference bằng handle (an toàn read-only) |
| `dwg_insert_block` | Chèn một block reference, hỗ trợ import từ file DWG bên ngoài |
| `dwg_set_block_attributes` | Thiết lập thuộc tính của một block reference bằng handle |
| `dwg_explode_block` | Phá vỡ (explode) block reference và trả về handle của các phần tử được tạo ra |

Các tool Dimension (Kích thước) tùy chọn hiển thị khi toolset `dimension` được bật:

| Tool | Mục đích |
|------|----------|
| `dwg_create_linear_dimension` | Tạo kích thước tuyến tính xoay (Rotated Dimension) với góc xoay cụ thể |
| `dwg_create_aligned_dimension` | Tạo kích thước song song (Aligned Dimension) giữa hai điểm |
| `dwg_create_radial_dimension` | Tạo kích thước bán kính (Radial Dimension) cho đường tròn hoặc cung tròn |
| `dwg_create_diameter_dimension` | Tạo kích thước đường kính (Diametric Dimension) cho đường tròn hoặc cung tròn |

Các tool Export tùy chọn hiển thị khi toolset `export` được bật:

| Tool | Mục đích |
|------|----------|
| `dwg_export_dxf` | Xuất bản vẽ ra file DXF (được bảo vệ bởi chính sách đường dẫn đầu ra) |

Các tool Drawing tùy chọn hiển thị khi toolset `drawing` được bật:

| Tool | Mục đích |
|------|----------|
| `dwg_get_variables` | Đọc giá trị hiện tại của danh sách các biến hệ thống AutoCAD |
| `dwg_set_system_variable` | Thiết lập giá trị cho một biến hệ thống AutoCAD |
| `dwg_save_drawing` | Lưu bản vẽ hiện tại ra file (yêu cầu confirm=true) |
| `dwg_purge_drawing` | Purge các đối tượng không sử dụng (blocks, layers, styles) (hỗ trợ dry_run=true, thực tế purge cần confirm=true) |

### Chính sách đường dẫn đầu ra (Output Path Policy)
Tất cả các hoạt động xuất/lưu file được kiểm soát nghiêm ngặt bởi một chính sách bảo vệ:
- Đường dẫn đầu ra phải là đường dẫn tuyệt đối.
- Phần mở rộng của file phải khớp chính xác (ví dụ `.dxf` cho xuất DXF).
- Không tự động ghi đè file hiện có trừ khi có `overwrite_existing=true`.
- Từ chối ghi đè vào thư mục gốc của repository trừ khi có `allow_repo_output=true`.

### Các Toolset tùy chọn và Hành vi Read-Only

Theo mặc định, chỉ có các toolset `query`, `modify`, `meta`, và `view` được bật. Bạn có thể bật các toolset khác bằng tham số `--toolsets` (ví dụ: `--toolsets all` hoặc `--toolsets query,modify,meta,view,annotation,block,dimension,export,drawing`).

- **Hành vi Read-Only (`--read-only`)**: Khi chế độ read-only được kích hoạt, tất cả các toolset có khả năng chỉnh sửa (`modify`, `code`, `annotation`, `dimension`, `export`, và `drawing` write tools) sẽ bị vô hiệu hóa hoàn toàn.
- **Phân tách Toolset Block**: Toolset `block` được phân tách thành các công cụ read-only và write-capable. Nếu `--read-only` được bật, các công cụ `dwg_list_blocks` và `dwg_get_block_attributes` vẫn hoạt động bình thường để kiểm tra thông tin, nhưng các công cụ chỉnh sửa (`dwg_insert_block`, `dwg_set_block_attributes`, `dwg_explode_block`) sẽ bị loại bỏ.
- **View và Read-Only**: Read-only giữ các tool zoom thay đổi UI tạm thời. `dwg_capture_view_image`, `dwg_inspect_view_region` và `dwg_restore_view` tạo file ảnh nên bị loại.
- **Drawing Operations và Read-Only**: Toolset `drawing` giữ lại `dwg_get_variables` ở chế độ read-only, nhưng loại bỏ `dwg_set_system_variable`, `dwg_save_drawing`, và `dwg_purge_drawing`.
- **Hoãn hỗ trợ Angular Dimension**: Kích thước góc (angular dimensions) tạm thời bị hoãn và chưa được thực hiện.
- **Chụp view trong AutoCAD**: `dwg_capture_view_image` dùng API `Document.CapturePreviewImage`, không cần điều khiển desktop và không lưu DWG. Tool từ chối khi có lệnh đang chạy hoặc bản vẽ/camera thay đổi trong lúc chụp. `expected_document_fingerprint` tùy chọn giúp ràng buộc lần chụp sau với bản vẽ của lần trước. Agent cần lưu metadata trả về cạnh ảnh khi lập atlas; tool chỉ ghi file ảnh. Kích thước thực có thể lệch nhẹ do AutoCAD làm tròn. Camera mô tả viewport active, chưa phải phép chiếu pixel → WCS chung cho nhiều viewport hoặc paper layout. Xem quy trình mô tả ở trên.
- **Tạm hoãn các công cụ xuất khác**: `dwg_export_pdf` và `dwg_export_image` vẫn tạm hoãn; chụp viewport không thiết lập hay kiểm chứng cấu hình in.

### Checklist smoke thủ công

Trong scratch DWG:

1. Chạy `dwg_get_drawing_info`.
2. Chạy `dwg_list_layers`.
3. Tạo `BIMWRIGHT_TEST` bằng `dwg_create_layer`.
4. Tạo point, polyline, rectangle, arc, và ellipse trên `BIMWRIGHT_TEST` bằng `dwg_create_point`, `dwg_create_polyline`, `dwg_create_rectangle`, `dwg_create_arc`, và `dwg_create_ellipse`; ghi lại hex handle trả về và giữ riêng một curve, ví dụ arc hoặc ellipse, để check color và offset.
5. Query, count, và select các entity đó theo layer và type bằng `dwg_query_entities`, `dwg_count_entities`, `dwg_select_by_layer`, và `dwg_select_by_type`; xác nhận select tool trả về handle list và không đổi pickfirst selection.
6. Move, rotate, và scale scratch entity không reserve bằng `dwg_move_entities`, `dwg_rotate_entities`, và `dwg_scale_entities`.
7. Copy một scratch entity không reserve bằng `dwg_copy_entities`, rồi chỉ erase disposable copied temp entity đó bằng `dwg_erase_entities`.
8. Đổi color reserved curve bằng `dwg_change_color`, sau đó offset curve đó bằng `dwg_offset_entities` và xác nhận generated handle trả về là hex handle.
9. Xác nhận workflow dịch text cũ vẫn chạy: chọn scratch text, chạy `dwg_get_selected_texts`, rồi rewrite bằng `dwg_translate_and_rewrite`.

### Smoke tùy chọn — annotation / block / dimension

Bật các toolset tùy chọn trước (`--toolsets …` hoặc `--toolsets all`). Trên DWG scratch:

1. Tạo text, mtext, leader, và table trong scratch DWG với `dwg_create_text`, `dwg_create_mtext`, `dwg_create_leader`, và `dwg_create_table`.
2. Liệt kê định nghĩa block với `dwg_list_blocks`.
3. Chèn một block đã biết từ bản vẽ hoặc đường dẫn DWG bên ngoài với `dwg_insert_block`.
4. Đọc và ghi các thuộc tính block reference với `dwg_get_block_attributes` and `dwg_set_block_attributes`.
5. Phá vỡ (explode) một block reference với `dwg_explode_block`.
6. Tạo kích thước linear, aligned, radial, và diameter với `dwg_create_linear_dimension`, `dwg_create_aligned_dimension`, `dwg_create_radial_dimension`, và `dwg_create_diameter_dimension`; xác nhận validator cho projected-distance hoạt động đúng như mong đợi.

### Smoke tùy chọn — view / export / drawing

Khi đã bật toolset `view`, `export`, `drawing` (tùy nhu cầu):

1. Chạy `dwg_zoom_extents`.
2. Chạy `dwg_zoom_window` với toạ độ cụ thể.
3. Zoom đến một entity với `dwg_zoom_to_entity` sử dụng handle.
4. Đọc biến bản vẽ với `dwg_get_variables`.
5. Xuất bản vẽ ra dxf với `dwg_export_dxf`.
6. Chạy `dwg_purge_drawing` with `dry_run=true`, rồi với `confirm=true` (chỉ chạy thử trên bản vẽ copy bỏ đi).
7. Chạy `dwg_save_drawing` với `confirm=true` (chỉ chạy thử trên bản vẽ copy bỏ đi).

### Migration từ tên tool 0.1.x

Tên MCP tool này có prefix `dwg_`. Tên command raw trong plugin chỉ còn là wire command nội bộ.

| Tên MCP 0.1.x | Tên MCP 1.0 |
|---------------|-------------|
| `get_selected_texts` | `dwg_get_selected_texts` |
| `update_texts` | `dwg_update_texts` |
| `translate_and_rewrite` | `dwg_translate_and_rewrite` |
| `apply_unicode_style` | `dwg_apply_unicode_style` |
| `collapse_and_rewrite` | `dwg_collapse_and_rewrite` |
| `send_code` | `dwg_send_code` |
| `run_lisp` | `dwg_run_lisp` |

---

## Quy trình tiêu chuẩn

Flow ghi lại này dùng selection thủ công và chỉ hai tham số `groupingStrength`/`includeEntities` cũ; không dùng `cluster_key` từ nguồn chỉ đọc thay cho ID số.

```
1. Người dùng chọn text trong AutoCAD
2. Agent gọi dwg_get_selected_texts -> nhận nhóm text đã cluster
3. Agent dịch từng cluster
4. Agent gọi dwg_translate_and_rewrite([{id, new_text}, ...])
   Tool tự xử lý: anchor, xóa, MText, font, chiều cao. Xong.
5. Nếu cần, người dùng chạy REGEN
```

Với rewrite thành công, `final_text_height` là property chiều cao thực của entity theo `new_handle`, đọc trong transaction rewrite sau chuẩn hóa Unicode và scale cuối; không đọc được thì trả `null`. Với `style_only`, số này mô tả anchor nên các thành viên khác trong nhóm có thể có chiều cao khác. Rewrite trong block definition báo chiều cao theo đơn vị cục bộ của definition, không phải chiều cao hiển thị trong world.

---

## Phiên bản AutoCAD hỗ trợ

| Phiên bản | ObjectARX release | Plugin TFM | Trạng thái |
|-----------|-------------------|------------|-----------|
| AutoCAD 2022 | 24.1 | `net48` | Đã scaffold shell; build từ package `AutoCAD.NET` của Autodesk |
| AutoCAD 2023 | 24.2 | `net48` | Đã scaffold shell; build từ package `AutoCAD.NET` của Autodesk |
| AutoCAD 2024 | 24.3 | `net48` | Shell mặc định và normal solution build |
| AutoCAD 2025 | 25.0 | `net8.0-windows` | Đã scaffold shell; build từ package `AutoCAD.NET` của Autodesk |
| AutoCAD 2026 | 25.1 | `net8.0-windows` | Đã scaffold shell; binary-compatible với 2025 nhưng build thành shell riêng |
| AutoCAD 2027 | 26.0 | `net10.0-windows` | Đã scaffold shell; không binary-compatible với 2025/2026 |

Mọi shell đều build với các package tham chiếu `AutoCAD.NET` của Autodesk trên nuget.org, nên máy có .NET 8 và .NET 10 SDK build được cả sáu năm mà không cần cài AutoCAD. Package chỉ dùng lúc compile; DLL của Autodesk không bao giờ được copy vào output plugin hay Setup ZIP. Build thành công không chứng minh plugin chạy đúng trên năm AutoCAD đó.

---

## Bảo mật

`dwg_send_code` chạy C# tùy ý với toàn quyền của AutoCAD và filesystem, chỉ dành cho agent tin cậy, không phải sandbox. `dwg_run_lisp` bị chặn bất kể input, verdict hay `MCPENABLECODE`. Không tạo wrapper, không xếp lệnh LISP vào hàng đợi, không đổi trusted paths hoặc SECURELOAD. Không lách từ chối qua C#, batch hoặc ToolBaker. `dwg_inspect_lisp` chỉ phân tích tĩnh, không phải antivirus hay cơ chế bảo vệ toàn máy; `clean` chỉ có nghĩa chưa thấy mẫu đã biết. Mọi report trả `execution_authorized=false` và `safety_assured=false`. Cần cập nhật cả server/plugin rồi khởi động lại để áp dụng; binary cũ vẫn giữ hành vi cũ.

Bảo mật dựa trên:

- **Chỉ local** — TCP trên 127.0.0.1 cho AutoCAD 2022–2024, loopback Named Pipe cho 2025–2027.
- **Auth token mỗi session** — xoay khi plugin khởi động lại.
- **Kill-switch theo session** — `MCPDISABLECODE` / `MCPENABLECODE` điều khiển `dwg_send_code`. LISP vẫn bị chặn sau khi bật code hoặc khởi động lại listener.
- **Giới hạn timeout** — script chạy inline trên thread giữ document lock, cooperative cancel sau 30s.
- **Giả định agent tin cậy** — chỉ dùng với MCP client bạn kiểm soát.

---

## Cấu trúc dự án

```
dwg-mcp/
├── src/
│   ├── Bimwright.Dwg.sln
│   ├── server/            # .NET 8 MCP server (global tool)
│   ├── shared/            # Handlers, clustering, rewriting, unicode
│   ├── plugin-acad22/     # AutoCAD 2022 shell (.NET 4.8)
│   ├── plugin-acad23/     # AutoCAD 2023 shell (.NET 4.8)
│   ├── plugin-acad24/     # AutoCAD 2024 shell (.NET 4.8)
│   ├── plugin-acad25/     # AutoCAD 2025 shell (.NET 8)
│   ├── plugin-acad26/     # AutoCAD 2026 shell (.NET 8)
│   └── plugin-acad27/     # AutoCAD 2027 shell (.NET 10)
├── tests/                 # xUnit
├── scripts/               # install/uninstall PowerShell
├── lib/acad24/            # Notes only; Autodesk DLLs are never committed
└── .github/workflows/     # CI
```

---

## Họ bimwright

Các công cụ mã nguồn mở kết nối trợ lý AI với ứng dụng BIM và CAD.

Tên **bimwright** ghép **BIM** với **wright**, một từ tiếng Anh cổ chỉ người thợ chế tạo hoặc xây dựng — như trong *shipwright* (thợ đóng tàu).

Xem [cách đặt tên các gateway](https://github.com/bimwright/.github/blob/master/profile/README.vi.md#cách-đặt-tên).

- [**rvt-mcp**](https://github.com/bimwright/rvt-mcp) — Autodesk® Revit®
- [**dwg-mcp**](https://github.com/bimwright/dwg-mcp) — Autodesk® AutoCAD®
- [**nwd-mcp**](https://github.com/bimwright/nwd-mcp) — Autodesk® Navisworks®
- [**ipt-mcp**](https://github.com/bimwright/ipt-mcp) — Autodesk® Inventor®
- [**bim-wiki**](https://github.com/bimwright/bim-wiki) — Kho kiến thức BIM ưu tiên tiếng Việt

---

## Tuyên bố miễn trừ

Autodesk, AutoCAD và DWG là nhãn hiệu hoặc nhãn hiệu đã đăng ký của Autodesk, Inc. và/hoặc các công ty con, công ty liên kết. DWG là định dạng tệp gốc của phần mềm Autodesk AutoCAD. bimwright là dự án mã nguồn mở độc lập, không liên kết, không được tài trợ hay chứng thực bởi Autodesk, Inc.

---

## Giấy phép

[Apache License 2.0](LICENSE)

Thông báo bên thứ ba: [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)

## Quyền và chế độ tự động

Mọi tool trừ `dwg_send_code` khai báo đủ cờ read-only, destructive, idempotent và open-world. Chế độ full có 69 tool; read-only mặc định có 17, còn `--toolsets all --read-only` expose 23 tool đọc bên dưới. Zoom có thể đổi trạng thái UI tạm thời. Chuyển target bị loại trong read-only; capture, inspect region và restore cũng bị loại vì tạo file ảnh. `dwg_health` là tool đọc; tùy chọn kiểm tra cập nhật có thể gọi GitHub.

Chỉ cấp quyền bằng rule chính xác cho tool bạn muốn dùng. Tránh wildcard `mcp__dwg-mcp__*` vì nó cấp cả quyền sửa bản vẽ/file và thực thi mã. `dwg_send_code` không có annotations và không nằm trong allowlist đọc này; quyền cho phép lâu dài phải là lựa chọn rõ ràng trong MCP client.

<!-- read-only-allowlist:start -->
```text
mcp__dwg-mcp__dwg_cluster_bands
mcp__dwg-mcp__dwg_count_entities
mcp__dwg-mcp__dwg_create_bake_issue_draft
mcp__dwg-mcp__dwg_get_block_attributes
mcp__dwg-mcp__dwg_get_change_records
mcp__dwg-mcp__dwg_get_current_target
mcp__dwg-mcp__dwg_get_drawing_info
mcp__dwg-mcp__dwg_get_drawing_profile
mcp__dwg-mcp__dwg_get_entity_properties
mcp__dwg-mcp__dwg_get_report_history
mcp__dwg-mcp__dwg_get_selected_texts
mcp__dwg-mcp__dwg_get_variables
mcp__dwg-mcp__dwg_health
mcp__dwg-mcp__dwg_inspect_lisp
mcp__dwg-mcp__dwg_list_available_targets
mcp__dwg-mcp__dwg_list_bake_suggestions
mcp__dwg-mcp__dwg_list_baked_tools
mcp__dwg-mcp__dwg_list_blocks
mcp__dwg-mcp__dwg_list_layers
mcp__dwg-mcp__dwg_list_open_documents
mcp__dwg-mcp__dwg_list_recent_drawings
mcp__dwg-mcp__dwg_query_entities
mcp__dwg-mcp__dwg_report_task_result
mcp__dwg-mcp__dwg_select_by_layer
mcp__dwg-mcp__dwg_select_by_type
mcp__dwg-mcp__dwg_survey_change_impact
mcp__dwg-mcp__dwg_zoom_extents
mcp__dwg-mcp__dwg_zoom_to_entity
mcp__dwg-mcp__dwg_zoom_window
```
<!-- read-only-allowlist:end -->
