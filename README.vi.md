<!-- mcp-name: io.github.bimwright/dwg-mcp -->

<p align="center">
  <img src="https://raw.githubusercontent.com/bimwright/.github/master/assets/logos/dwg-mcp.png" alt="dwg-mcp" width="180" />
</p>

<h1 align="center">dwg-mcp</h1>

<p align="center">
  <a href="https://github.com/bimwright/dwg-mcp/actions/workflows/build.yml"><img src="https://github.com/bimwright/dwg-mcp/actions/workflows/build.yml/badge.svg" alt="build" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-Apache%202.0-blue.svg" alt="license" /></a>
  <a href="#phien-ban-autocad-ho-tro"><img src="https://img.shields.io/badge/AutoCAD-2022--2027-186BFF" alt="AutoCAD 2022-2027" /></a>
  <a href="#cong-cu"><img src="https://img.shields.io/badge/MCP-16%20default%20%2B%20optional-6C47FF" alt="MCP tools" /></a>
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

Dung `--read-only` de chi mo tool doc/routing, cong them ToolBaker read tools neu toolset nay duoc bat. Dung `--toolsets query,modify,meta,toolbaker` hoac `--toolsets all` de bat ToolBaker.

---

## Cong cu

Mac dinh server expose 16 tool: query, modify, routing/meta, va batch. ToolBaker bat qua `--toolsets`, va `dwg_send_code` la opt-in; tong backed MCP surface la 23 tool.

CAD tool chay tren active document hien tai cua AutoCAD target dang chon. Entity input dung AutoCAD hex handle, vi du `7F5AD`, do tool selection, creation, hoac properties tra ve.

| Tool | Muc dich |
|------|----------|
| `dwg_get_drawing_info` | Doc ten drawing, current layer, current space/layout, va unit scalar |
| `dwg_get_entity_properties` | Doc property cua entity theo AutoCAD hex handle |
| `dwg_list_layers` | Liet ke layer trong drawing hien tai kem color va state flag |
| `dwg_get_selected_texts` | Doc text dang chon, cluster khong gian, tra ve nhom text |
| `dwg_update_texts` | Ghi text theo handle trong mot transaction |
| `dwg_create_layer` | Dam bao layer ton tai, khong ghi de property cua layer da co |
| `dwg_create_line` | Tao mot line trong drawing space hien tai |
| `dwg_create_circle` | Tao mot circle trong drawing space hien tai |
| `dwg_change_layer` | Chuyen entity theo hex handle sang layer khac |
| `dwg_translate_and_rewrite` | **Uu tien.** Ghi text da dich, tu dong xu ly anchor, xoa, MText, font, chieu cao |
| `dwg_apply_unicode_style` | Dam bao style `Bimwright_Unicode` ton tai va ap dung |
| `dwg_collapse_and_rewrite` | Rewrite low-level voi kiem soat hinh hoc chi tiet |
| `dwg_list_available_targets` | Liet ke AutoCAD dang chay tu discovery v2 va legacy 2024 |
| `dwg_get_current_target` | Xem target dang pin |
| `dwg_switch_target` | Pin server sang AutoCAD `2022` den `2027` |
| `dwg_batch_execute` | Chay nhieu wire command noi bo nhu mot logical batch |
| `dwg_send_code` | **Chi opt-in.** Chay C# sau khi bat flag/env server va dong y phia AutoCAD bang `MCPENABLECODE` |

ToolBaker la toolset tuy chon:

| Tool | Muc dich |
|------|----------|
| `dwg_list_baked_tools` | Liet ke baked tool da accept trong SQLite registry cua server |
| `dwg_run_baked_tool` | Chay baked tool da accept |
| `dwg_list_bake_suggestions` | Liet ke goi y workflow lap lai |
| `dwg_accept_bake_suggestion` | Validate, smoke-test, va accept goi y |
| `dwg_dismiss_bake_suggestion` | Dismiss hoac suppress goi y |
| `dwg_create_bake_issue_draft` | Tao GitHub issue draft cho goi y ma khong submit |

### Checklist smoke thu cong

Trong scratch DWG:

1. Chay `dwg_get_drawing_info`.
2. Chay `dwg_list_layers`.
3. Tao `BIMWRIGHT_TEST` bang `dwg_create_layer`.
4. Tao mot line va mot circle tren layer khac, vi du current layer `0`, bang `dwg_create_line` va `dwg_create_circle`; ghi lai handle tra ve.
5. Doc ca hai handle tra ve bang `dwg_get_entity_properties`.
6. Chuyen ca hai entity sang `BIMWRIGHT_TEST` bang `dwg_change_layer`.
7. Xac nhan mot lan AutoCAD undo revert transaction cua tung write command.

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
