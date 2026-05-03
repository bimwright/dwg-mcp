<!-- mcp-name: io.github.bimwright/dwg-mcp -->

<p align="center">
  <img src="https://raw.githubusercontent.com/bimwright/.github/master/assets/logos/dwg-mcp.png" alt="dwg-mcp" width="180" />
</p>

<h1 align="center">dwg-mcp</h1>

<p align="center">
  <a href="https://github.com/bimwright/dwg-mcp/actions/workflows/build.yml"><img src="https://github.com/bimwright/dwg-mcp/actions/workflows/build.yml/badge.svg" alt="build" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-Apache%202.0-blue.svg" alt="license" /></a>
  <a href="#phien-ban-autocad-ho-tro"><img src="https://img.shields.io/badge/AutoCAD-2024-186BFF" alt="AutoCAD 2024" /></a>
  <a href="#cong-cu"><img src="https://img.shields.io/badge/MCP-5%20default%20%2B%201%20opt--in-6C47FF" alt="MCP tools" /></a>
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

`dwg-mcp` là cổng MCP cục bộ cho Autodesk AutoCAD 2024.

Gồm hai phần:

- **Bimwright.Dwg.Server**: MCP server .NET 8, được Claude Code, Cursor, OpenCode hoặc MCP client khác khởi chạy.
- **Bimwright.Dwg.Plugin**: Add-in AutoCAD chạy bên trong AutoCAD, thực thi lệnh trực tiếp lên database bản vẽ.

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
pwsh scripts/install.ps1 -WhatIf    # xem truoc
pwsh scripts/install.ps1             # cai dat
pwsh scripts/install.ps1 -Uninstall  # go bo
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

`send_code` bị ẩn khỏi danh sách tool mặc định. Muốn bật, phải opt-in ở cả server và AutoCAD:

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

---

## Cong cu

| Tool | Muc dich |
|------|----------|
| `get_selected_texts` | Doc text dang chon, cluster khong gian, tra ve nhom text |
| `translate_and_rewrite` | **Uu tien.** Ghi text da dich — tu dong xu ly anchor, xoa, MText, font, chieu cao |
| `collapse_and_rewrite` | Rewrite low-level voi kiem soat hinh hoc chi tiet |
| `update_texts` | Ghi text theo handle (legacy) |
| `apply_unicode_style` | Dam bao style `Bimwright_Unicode` ton tai va ap dung |
| `send_code` | **Chi opt-in.** Chay C# tren AutoCAD .NET API sau khi bat flag/env server va dong y phia AutoCAD bang `MCPENABLECODE` |

---

## Quy trinh tieu chuan

```
1. Nguoi dung chon text trong AutoCAD
2. Agent goi get_selected_texts -> nhan nhom text da cluster
3. Agent dich tung cluster
4. Agent goi translate_and_rewrite([{id, new_text}, ...])
   Tool tu xu ly: anchor, xoa, MText, font, chieu cao. Xong.
```

---

## Phien ban AutoCAD ho tro

| Phien ban | Trang thai | .NET |
|-----------|-----------|------|
| AutoCAD 2024 | Ho tro | .NET 4.8 |
| AutoCAD 2025 | Du kien | .NET 8 |
| AutoCAD 2026 | Du kien | .NET 8 |

---

## Bao mat

`send_code` chay C# tuy y voi toan quyen truy cap process AutoCAD va filesystem. Tool nay khong nam trong danh sach MCP mac dinh. Muon dung, khoi dong server voi `--enable-send-code` hoac `BIMWRIGHT_DWG_ENABLE_SEND_CODE=1`, roi chay `MCPENABLECODE` trong AutoCAD cho session plugin hien tai.

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
