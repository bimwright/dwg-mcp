<!-- mcp-name: io.github.bimwright/dwg-mcp -->

<p align="center">
  <img src="https://raw.githubusercontent.com/bimwright/.github/master/assets/logos/dwg-mcp.png" alt="dwg-mcp" width="180" />
</p>

<h1 align="center">dwg-mcp</h1>

<p align="center">
  <a href="https://github.com/bimwright/dwg-mcp/actions/workflows/build.yml"><img src="https://github.com/bimwright/dwg-mcp/actions/workflows/build.yml/badge.svg" alt="build" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-Apache%202.0-blue.svg" alt="license" /></a>
  <a href="#phiên-bản-autocad-hỗ-trợ"><img src="https://img.shields.io/badge/AutoCAD-2022--2027-186BFF" alt="AutoCAD 2022-2027" /></a>
  <a href="#công-cụ"><img src="https://img.shields.io/badge/MCP-35%20default%20%2B%20optional-6C47FF" alt="MCP tools" /></a>
</p>

<p align="center">
  <a href="README.md">English</a> · Tiếng Việt
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

### 1. Server

```bash
dotnet tool install -g Bimwright.Dwg.Server
bimwright-dwg --help
```

Yêu cầu .NET 8 SDK.

### 2. Plugin

**Auto-deploy:**

Tải plugin từ [GitHub Releases](https://github.com/bimwright/dwg-mcp/releases/latest):

```powershell
pwsh scripts/install.ps1 -Version 2024 -WhatIf    # xem trước
pwsh scripts/install.ps1 -Version 2024            # cài đặt
pwsh scripts/install.ps1 -Uninstall               # gỡ bỏ
```

Script triển khai vào `%APPDATA%\Autodesk\ApplicationPlugins\Bimwright.Dwg.bundle\`. Khởi động lại AutoCAD để tải.

**Thủ công:** Trong AutoCAD: `NETLOAD` → chọn `src/plugin-acad24/bin/Debug/net48/Bimwright.Dwg.Plugin.Acad24.dll`. Listener tự động khởi động.

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

`dwg_send_code` bị ẩn khỏi danh sách tool mặc định. Muốn bật, phải opt-in ở cả server và AutoCAD:

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

Sau đó chạy `MCPENABLECODE` trong AutoCAD cho session plugin hiện tại. `MCPDISABLECODE` tắt lại quyền này.

Để pin một AutoCAD cụ thể, dùng năm 4 chữ số:

```json
{
  "mcpServers": {
    "bimwright-dwg": {
      "command": "bimwright-dwg",
      "args": ["--target", "2024"]
    }
  }
}
```

Dùng `--read-only` để chỉ mở tool đọc/routing, cộng thêm ToolBaker read tools nếu toolset này được bật. Dùng `--toolsets query,modify,meta,annotation` (hoặc `--toolsets all`) để bật các toolset tùy chọn, hoặc thiết lập biến môi trường `BIMWRIGHT_DWG_TOOLSETS=query,modify,meta,annotation`.

---

## Công cụ

Mặc định server expose 35 tool: query, modify, meta, và view. Các toolset tùy chọn ToolBaker, annotation, block, dimension, export, và drawing được kích hoạt qua `--toolsets`, cùng với `dwg_send_code` nâng tổng diện tích bề mặt MCP lên 60 tool.

CAD tool chạy trên active document hiện tại của AutoCAD target đang chọn. Entity input và entity id trả về dùng AutoCAD hex handle, ví dụ `7F5AD`, do tool selection, creation, hoặc properties trả về. Creation, copy, offset, và modify response identify entity tạo/sửa bằng hex handle.

Plan 2 query expansion chỉ quét model space: `dwg_query_entities`, `dwg_count_entities`, `dwg_select_by_layer`, và `dwg_select_by_type` không quét paper-space/layout entity. `dwg_select_by_layer` và `dwg_select_by_type` trả về handle list cho caller; chúng không đổi AutoCAD pickfirst selection.

| Tool | Mục đích |
|------|----------|
| `dwg_get_drawing_info` | Đọc tên drawing, current layer, current space/layout, và unit scalar |
| `dwg_get_entity_properties` | Đọc property của entity theo AutoCAD hex handle |
| `dwg_list_layers` | Liệt kê layer trong drawing hiện tại kèm color và state flag |
| `dwg_query_entities` | Query model-space entity theo type, layer, color, limit, và geometry flag tùy chọn |
| `dwg_count_entities` | Đếm model-space entity theo type, layer, hoặc color filter tùy chọn |
| `dwg_select_by_layer` | Trả về handle list của model-space entity trên một layer, không đổi pickfirst selection |
| `dwg_select_by_type` | Trả về handle list của model-space entity theo một entity type, không đổi pickfirst selection |
| `dwg_get_selected_texts` | Đọc text đang chọn, cluster không gian, trả về nhóm text |
| `dwg_update_texts` | Ghi text theo handle trong một transaction |
| `dwg_create_layer` | Đảm bảo layer tồn tại, không ghi đè property của layer đã có |
| `dwg_create_line` | Tạo một line trong drawing space hiện tại |
| `dwg_create_circle` | Tạo một circle trong drawing space hiện tại |
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
| `dwg_list_available_targets` | Liệt kê AutoCAD đang chạy từ discovery v2 và legacy 2024 |
| `dwg_get_current_target` | Xem target đang pin |
| `dwg_switch_target` | Pin server sang AutoCAD `2022` đến `2027` |
| `dwg_batch_execute` | Chạy nhiều wire command nội bộ như một logical batch |
| `dwg_send_code` | **Chỉ opt-in.** Chạy C# sau khi bật flag/env server và đồng ý phía AutoCAD bằng `MCPENABLECODE` |
| `dwg_zoom_extents` | Zoom đến giới hạn của viewport bản vẽ |
| `dwg_zoom_window` | Zoom viewport đến một cửa sổ được xác định bởi hai điểm góc |
| `dwg_zoom_to_entity` | Zoom viewport đến giới hạn của một entity cụ thể theo handle |

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
- **View Navigation và Read-Only**: Toolset `view` mặc định bật và giữ lại các công cụ zoom (`dwg_zoom_extents`, `dwg_zoom_window`, `dwg_zoom_to_entity`) ở chế độ read-only, loại bỏ tool capture_view tạm hoãn.
- **Drawing Operations và Read-Only**: Toolset `drawing` giữ lại `dwg_get_variables` ở chế độ read-only, nhưng loại bỏ `dwg_set_system_variable`, `dwg_save_drawing`, và `dwg_purge_drawing`.
- **Hoãn hỗ trợ Angular Dimension**: Kích thước góc (angular dimensions) tạm thời bị hoãn và chưa được thực hiện.
- **Tạm hoãn các công cụ xuất/chụp ảnh khác**: `dwg_export_pdf`, `dwg_export_image`, và `dwg_capture_view` tạm thời bị hoãn để đảm bảo độ tin cậy tuyệt đối của xuất bản vẽ.

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

### Plan 3 Manual Smoke Checklist (Manual Smoke Pending)

Kiểm tra smoke thủ công cho các công cụ annotation, block, và dimension của Plan 3 hiện tại **đang chờ** chạy thực tế trên AutoCAD (manual smoke pending), nhưng các kịch bản sau đã được thiết kế sẵn:

1. Tạo text, mtext, leader, và table trong scratch DWG với `dwg_create_text`, `dwg_create_mtext`, `dwg_create_leader`, và `dwg_create_table`.
2. Liệt kê định nghĩa block với `dwg_list_blocks`.
3. Chèn một block đã biết từ bản vẽ hoặc đường dẫn DWG bên ngoài với `dwg_insert_block`.
4. Đọc và ghi các thuộc tính block reference với `dwg_get_block_attributes` and `dwg_set_block_attributes`.
5. Phá vỡ (explode) một block reference với `dwg_explode_block`.
6. Tạo kích thước linear, aligned, radial, và diameter với `dwg_create_linear_dimension`, `dwg_create_aligned_dimension`, `dwg_create_radial_dimension`, và `dwg_create_diameter_dimension`; xác nhận validator cho projected-distance hoạt động đúng như mong đợi.

### Plan 4 Manual Smoke Checklist (Manual Smoke Pending)

Kiểm tra smoke thủ công cho các công cụ view, export và drawing của Plan 4 hiện tại **đang chờ** chạy thực tế trên AutoCAD (manual smoke pending), nhưng các kịch bản sau đã được thiết kế sẵn:

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

---

## Quy trình tiêu chuẩn

```
1. Người dùng chọn text trong AutoCAD
2. Agent gọi dwg_get_selected_texts -> nhận nhóm text đã cluster
3. Agent dịch từng cluster
4. Agent gọi dwg_translate_and_rewrite([{id, new_text}, ...])
   Tool tự xử lý: anchor, xóa, MText, font, chiều cao. Xong.
5. Nếu cần, người dùng chạy REGEN
```

---

## Phiên bản AutoCAD hỗ trợ

| Phiên bản | ObjectARX release | Plugin TFM | Trạng thái |
|-----------|-------------------|------------|-----------|
| AutoCAD 2022 | 24.1 | `net48` | Đã scaffold shell; release build cần Autodesk refs tương ứng |
| AutoCAD 2023 | 24.2 | `net48` | Đã scaffold shell; release build cần Autodesk refs tương ứng |
| AutoCAD 2024 | 24.3 | `net48` | Shell mặc định và normal solution build |
| AutoCAD 2025 | 25.0 | `net8.0-windows` | Đã scaffold shell; release build cần Autodesk refs tương ứng |
| AutoCAD 2026 | 25.1 | `net8.0-windows` | Đã scaffold shell; binary-compatible với 2025 nhưng build thành shell riêng |
| AutoCAD 2027 | 26.0 | `net10.0-windows` | Đã scaffold shell; không binary-compatible với 2025/2026 |

Server và tests có thể pass khi chưa build release tất cả shell. Muốn ship một năm AutoCAD thì phải build shell đó trên máy đã có managed assemblies Autodesk tương ứng.

---

## Bảo mật

`dwg_send_code` chạy C# tùy ý với toàn quyền truy cập process AutoCAD và filesystem. Tool này không nằm trong danh sách MCP mặc định. Muốn dùng, khởi động server với `--enable-send-code` hoặc `BIMWRIGHT_DWG_ENABLE_SEND_CODE=1`, rồi chạy `MCPENABLECODE` trong AutoCAD cho session plugin hiện tại.

Bảo mật dựa trên:

- **Chỉ local** — TCP trên 127.0.0.1 cho AutoCAD 2022–2024, loopback Named Pipe cho 2025–2027.
- **Auth token mỗi session** — xoay khi plugin khởi động lại.
- **Opt-in hai phía** — server đăng ký tool và AutoCAD xác nhận cho phép.
- **Giới hạn timeout** — script chạy trên thread riêng, có cancellation và abort khi quá timeout.
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

Các MCP gateway hand-forged cho toolchain AEC — cùng một kiến trúc, predictable / auditable / reversible:

- [**rvt-mcp**](https://github.com/bimwright/rvt-mcp) — Autodesk® Revit®
- [**dwg-mcp**](https://github.com/bimwright/dwg-mcp) — Autodesk® AutoCAD®
- [**nwd-mcp**](https://github.com/bimwright/nwd-mcp) — Autodesk® Navisworks®
- [**ipt-mcp**](https://github.com/bimwright/ipt-mcp) — Autodesk® Inventor®
- [**bim-wiki**](https://github.com/bimwright/bim-wiki) — Kho kiến thức BIM ưu tiên tiếng Việt

---

## Tuyên bố miễn trừ

AutoCAD và Autodesk là thương hiệu đã đăng ký của Autodesk, Inc. bimwright là dự án open-source độc lập, không liên kết, không được tài trợ và không được bảo chứng bởi Autodesk, Inc.

---

## Giấy phép

[Apache License 2.0](LICENSE)

Thông báo bên thứ ba: [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)
