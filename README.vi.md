<!-- mcp-name: io.github.bimwright/dwg-mcp -->

<p align="center">
  <img src="https://raw.githubusercontent.com/bimwright/.github/master/assets/logos/dwg-mcp.png" alt="dwg-mcp" width="180" />
</p>

<h1 align="center">dwg-mcp</h1>

<p align="center">
  <a href="https://github.com/bimwright/dwg-mcp/actions/workflows/build.yml"><img src="https://github.com/bimwright/dwg-mcp/actions/workflows/build.yml/badge.svg" alt="build" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-Apache%202.0-blue.svg" alt="license" /></a>
  <a href="#phien-ban-autocad-ho-tro"><img src="https://img.shields.io/badge/AutoCAD-2022--2027-186BFF" alt="AutoCAD 2022-2027" /></a>
  <a href="#cong-cu"><img src="https://img.shields.io/badge/MCP-35%20default%20%2B%20optional-6C47FF" alt="MCP tools" /></a>
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

`dwg-mcp` la cong MCP cuc bo cho Autodesk AutoCAD 2022-2027.

Gồm hai phần:

- **Bimwright.Dwg.Server**: MCP server .NET 8, được Claude Code, Cursor, OpenCode hoặc MCP client khác khởi chạy.
- **Bimwright.Dwg.Plugin**: cac shell add-in theo tung phien ban AutoCAD, thuc thi lenh truc tiep len database ban ve.

Agent nói MCP. Server nói TCP với plugin. Plugin nói AutoCAD .NET API.

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
              | TCP (127.0.0.1)
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

---

## Cai dat

### 1. Server

```bash
dotnet tool install -g Bimwright.Dwg.Server
bimwright-dwg --help
```

### 2. Plugin

**Auto-deploy:**

```powershell
pwsh scripts/install.ps1 -Version 2024 -WhatIf    # xem truoc
pwsh scripts/install.ps1 -Version 2024            # cai dat
pwsh scripts/install.ps1 -Uninstall               # go bo
```

**Thu cong:** Trong AutoCAD: `NETLOAD` -> chon DLL.

### 3. Cau hinh MCP client

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

`dwg_send_code` bi an khoi danh sach tool mac dinh. Muon bat, phai opt-in o ca server va AutoCAD:

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

De pin mot AutoCAD cu the, dung nam 4 chu so:

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

Dung `--read-only` de chi mo tool doc/routing, cong them ToolBaker read tools neu toolset nay duoc bat. Dung `--toolsets query,modify,meta,annotation` (hoac `--toolsets all`) de bat cac toolset tuy chon, hoac thiet lap bien moi truong `BIMWRIGHT_DWG_TOOLSETS=query,modify,meta,annotation`.

---

## Cong cu

Mặc định server expose 35 tool: query, modify, routing/meta, batch, và view. Các toolset tùy chọn ToolBaker, annotation, block, dimension, export, và drawing được kích hoạt qua `--toolsets`, cùng với `dwg_send_code` nâng tổng diện tích bề mặt MCP lên 60 tool.

CAD tool chay tren active document hien tai cua AutoCAD target dang chon. Entity input va entity id tra ve dung AutoCAD hex handle, vi du `7F5AD`, do tool selection, creation, hoac properties tra ve. Creation, copy, offset, va modify response identify entity tao/sua bang hex handle.

Plan 2 query expansion chi quet model space: `dwg_query_entities`, `dwg_count_entities`, `dwg_select_by_layer`, va `dwg_select_by_type` khong quet paper-space/layout entity. `dwg_select_by_layer` va `dwg_select_by_type` tra ve handle list cho caller; chung khong doi AutoCAD pickfirst selection.

| Tool | Muc dich |
|------|----------|
| `dwg_get_drawing_info` | Doc ten drawing, current layer, current space/layout, va unit scalar |
| `dwg_get_entity_properties` | Doc property cua entity theo AutoCAD hex handle |
| `dwg_list_layers` | Liet ke layer trong drawing hien tai kem color va state flag |
| `dwg_query_entities` | Query model-space entity theo type, layer, color, limit, va geometry flag tuy chon |
| `dwg_count_entities` | Dem model-space entity theo type, layer, hoac color filter tuy chon |
| `dwg_select_by_layer` | Tra ve handle list cua model-space entity tren mot layer, khong doi pickfirst selection |
| `dwg_select_by_type` | Tra ve handle list cua model-space entity theo mot entity type, khong doi pickfirst selection |
| `dwg_get_selected_texts` | Doc text dang chon, cluster khong gian, tra ve nhom text |
| `dwg_update_texts` | Ghi text theo handle trong mot transaction |
| `dwg_create_layer` | Dam bao layer ton tai, khong ghi de property cua layer da co |
| `dwg_create_line` | Tao mot line trong drawing space hien tai |
| `dwg_create_circle` | Tao mot circle trong drawing space hien tai |
| `dwg_create_point` | Tao mot point va tra ve hex handle |
| `dwg_create_polyline` | Tao lightweight polyline tu vertices va tra ve hex handle |
| `dwg_create_rectangle` | Tao rectangle polyline va tra ve hex handle |
| `dwg_create_arc` | Tao mot arc va tra ve hex handle |
| `dwg_create_ellipse` | Tao mot ellipse va tra ve hex handle |
| `dwg_change_layer` | Chuyen entity theo hex handle sang layer khac |
| `dwg_change_color` | Doi color entity bang AutoCAD color index |
| `dwg_move_entities` | Move entity theo hex handle bang displacement vector |
| `dwg_rotate_entities` | Rotate entity theo hex handle quanh base point |
| `dwg_scale_entities` | Scale entity theo hex handle quanh base point |
| `dwg_copy_entities` | Copy entity theo hex handle va tra ve copied handle |
| `dwg_erase_entities` | Erase entity theo hex handle |
| `dwg_offset_entities` | Offset curve entity va tra ve generated handle |
| `dwg_translate_and_rewrite` | **Uu tien.** Ghi text da dich, tu dong xu ly anchor, xoa, MText, font, chieu cao |
| `dwg_apply_unicode_style` | Dam bao style `Bimwright_Unicode` ton tai va ap dung |
| `dwg_collapse_and_rewrite` | Rewrite low-level voi kiem soat hinh hoc chi tiet |
| `dwg_list_available_targets` | Liet ke AutoCAD dang chay tu discovery v2 va legacy 2024 |
| `dwg_get_current_target` | Xem target dang pin |
| `dwg_switch_target` | Pin server sang AutoCAD `2022` den `2027` |
| `dwg_batch_execute` | Chay nhieu wire command noi bo nhu mot logical batch |
| `dwg_send_code` | **Chi opt-in.** Chay C# sau khi bat flag/env server va dong y phia AutoCAD bang `MCPENABLECODE` |
| `dwg_zoom_extents` | Zoom đến giới hạn của viewport bản vẽ |
| `dwg_zoom_window` | Zoom viewport đến một cửa sổ được xác định bởi hai điểm góc |
| `dwg_zoom_to_entity` | Zoom viewport đến giới hạn của một entity cụ thể theo handle |

ToolBaker la toolset tuy chon:

| Tool | Muc dich |
|------|----------|
| `dwg_list_baked_tools` | Liet ke baked tool da accept trong SQLite registry cua server |
| `dwg_run_baked_tool` | Chay baked tool da accept |
| `dwg_list_bake_suggestions` | Liet ke goi y workflow lap lai |
| `dwg_accept_bake_suggestion` | Validate, smoke-test, va accept goi y |
| `dwg_dismiss_bake_suggestion` | Dismiss hoac suppress goi y |
| `dwg_create_bake_issue_draft` | Tao GitHub issue draft cho goi y ma khong submit |

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

### Checklist smoke thu cong

Trong scratch DWG:

1. Chay `dwg_get_drawing_info`.
2. Chay `dwg_list_layers`.
3. Tao `BIMWRIGHT_TEST` bang `dwg_create_layer`.
4. Tao point, polyline, rectangle, arc, va ellipse tren `BIMWRIGHT_TEST` bang `dwg_create_point`, `dwg_create_polyline`, `dwg_create_rectangle`, `dwg_create_arc`, va `dwg_create_ellipse`; ghi lai hex handle tra ve va giu rieng mot curve, vi du arc hoac ellipse, de check color va offset.
5. Query, count, va select cac entity do theo layer va type bang `dwg_query_entities`, `dwg_count_entities`, `dwg_select_by_layer`, va `dwg_select_by_type`; xac nhan select tool tra ve handle list va khong doi pickfirst selection.
6. Move, rotate, va scale scratch entity khong reserve bang `dwg_move_entities`, `dwg_rotate_entities`, va `dwg_scale_entities`.
7. Copy mot scratch entity khong reserve bang `dwg_copy_entities`, roi chi erase disposable copied temp entity do bang `dwg_erase_entities`.
8. Doi color reserved curve bang `dwg_change_color`, sau do offset curve do bang `dwg_offset_entities` va xac nhan generated handle tra ve la hex handle.
9. Xac nhan workflow dich text cu van chay: chon scratch text, chay `dwg_get_selected_texts`, roi rewrite bang `dwg_translate_and_rewrite`.

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

### Migration tu ten tool 0.1.x

Ten MCP tool nay co prefix `dwg_`. Ten command raw trong plugin chi con la wire command noi bo.

| Ten MCP 0.1.x | Ten MCP 1.0 |
|---------------|-------------|
| `get_selected_texts` | `dwg_get_selected_texts` |
| `update_texts` | `dwg_update_texts` |
| `translate_and_rewrite` | `dwg_translate_and_rewrite` |
| `apply_unicode_style` | `dwg_apply_unicode_style` |
| `collapse_and_rewrite` | `dwg_collapse_and_rewrite` |
| `send_code` | `dwg_send_code` |

---

## Quy trinh tieu chuan

```
1. Nguoi dung chon text trong AutoCAD
2. Agent goi dwg_get_selected_texts -> nhan nhom text da cluster
3. Agent dich tung cluster
4. Agent goi dwg_translate_and_rewrite([{id, new_text}, ...])
   Tool tu xu ly: anchor, xoa, MText, font, chieu cao. Xong.
```

---

## Phien ban AutoCAD ho tro

| Phien ban | ObjectARX release | Plugin TFM | Trang thai |
|-----------|-------------------|------------|-----------|
| AutoCAD 2022 | 24.1 | `net48` | Da scaffold shell; release build can Autodesk refs tuong ung |
| AutoCAD 2023 | 24.2 | `net48` | Da scaffold shell; release build can Autodesk refs tuong ung |
| AutoCAD 2024 | 24.3 | `net48` | Shell mac dinh va normal solution build |
| AutoCAD 2025 | 25.0 | `net8.0-windows` | Da scaffold shell; release build can Autodesk refs tuong ung |
| AutoCAD 2026 | 25.1 | `net8.0-windows` | Da scaffold shell; binary-compatible voi 2025 nhung build thanh shell rieng |
| AutoCAD 2027 | 26.0 | `net10.0-windows` | Da scaffold shell; khong binary-compatible voi 2025/2026 |

Server va tests co the pass khi chua build release tat ca shell. Muon ship mot nam AutoCAD thi phai build shell do tren may da co managed assemblies Autodesk tuong ung.

---

## Bao mat

`dwg_send_code` chay C# tuy y voi toan quyen truy cap process AutoCAD va filesystem. Tool nay khong nam trong danh sach MCP mac dinh. Muon dung, khoi dong server voi `--enable-send-code` hoac `BIMWRIGHT_DWG_ENABLE_SEND_CODE=1`, roi chay `MCPENABLECODE` trong AutoCAD cho session plugin hien tai.

Bao mat dua tren:

- **Chi local** — TCP tren 127.0.0.1.
- **Auth token moi session** — xoay khi plugin khoi dong lai.
- **Opt-in hai phia** — server dang ky tool va AutoCAD xac nhan cho phep.
- **Gioi han timeout** — script chay tren thread rieng, co cancellation va abort khi qua timeout.
- **Gia dinh agent tin cay** — chi dung voi MCP client ban kiem soat.

---

## Giay phep

[Apache License 2.0](LICENSE)

Thong bao ben thu ba: [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)

---

*Du an nay khong lien ket voi Autodesk, Inc. AutoCAD la nhan hieu dang ky cua Autodesk, Inc.*
