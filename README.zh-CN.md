<!-- mcp-name: io.github.bimwright/dwg-mcp -->

<p align="center">
  <img src="https://raw.githubusercontent.com/bimwright/.github/master/assets/logos/dwg-mcp.png" alt="dwg-mcp" width="180" />
</p>

<h1 align="center">dwg-mcp</h1>

<p align="center">
  <a href="https://github.com/bimwright/dwg-mcp/actions/workflows/build.yml"><img src="https://github.com/bimwright/dwg-mcp/actions/workflows/build.yml/badge.svg" alt="build" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-Apache%202.0-blue.svg" alt="license" /></a>
  <a href="#支持的-autocad-版本"><img src="https://img.shields.io/badge/AutoCAD-2022--2027-186BFF" alt="AutoCAD 2022-2027" /></a>
  <a href="#tools"><img src="https://img.shields.io/badge/MCP-36%20default%20%2B%20optional-6C47FF" alt="MCP tools" /></a>
</p>

<p align="center">
  <a href="README.md">English</a> · <a href="README.vi.md">Tiếng Việt</a> · 简体中文 · <a href="README.ja.md">日本語</a>
</p>

---

## 图纸翻译不应止步于手工复制粘贴

施工与工程图纸承载着密集的技术文字——规格说明、注释、尺寸、材料标注、图例。当这些图纸以外语形式送达时，翻译不是可有可无的，而是项目团队动手之前必须完成的工作。

常见的流程非常痛苦：逐个元素选中文字，复制到翻译器，再粘贴回去，然后修补字体（因为 SHX 字体无法显示越南语或中日韩文字），调整高度，还得祈祷布局没有偏移。再乘以每张图纸数百个文字片段、每个项目数十张图纸。

`dwg-mcp` 存在的意义，就是把这个冗长循环压缩成两步：选中文字，让 AI agent 读取、翻译，并就地改写——字体正确、高度正确、空间分组正确，且只需一次撤销。

---

## dwg-mcp 是什么

`dwg-mcp` 是一个面向 Autodesk AutoCAD 2022–2027 DWG 工作流的本地 MCP gateway。

它由两部分组成：

- **Bimwright.Dwg.Server**：一个 .NET 8 MCP server，由 Claude Code、Cursor、OpenCode 或其他 stdio MCP client 启动。
- **Bimwright.Dwg.Plugin**：按版本拆分的 AutoCAD add-in shells，加载在 AutoCAD 内部，针对图形数据库执行命令。

Agent 说 MCP。Server 通过本地链路和 plugin 通信：AutoCAD 2022–2024 走 TCP NDJSON，2025–2027 走 Named Pipe（loopback，避免防火墙弹窗）。Plugin 则调用 AutoCAD .NET API。

一切都在你的机器上完成。

---

## 为什么它重要

AI agent 让“把选中的文字全部翻译成越南语”这类意图可以正确发生在图纸里。但仅有意图还不够。AutoCAD 文字操作需要理解空间布局、片段分组、字体限制、MText 与 DBText 的差异、块参照，以及高度缩放。

`dwg-mcp` 负责处理这些复杂性：

- **空间聚类（Spatial clustering）** 把碎片化的文字按逻辑聚合成完整句子（按块、行、列、段落）。
- **自动字体处理** 创建一个支持 Unicode 的文字样式并应用——再也不会出现 SHX 问号。
- **高度缩放** 补偿拉丁文字与中日韩文字在视觉密度上的差异。
- **MText 转换** 在安全的前提下，把单行片段升级为多行文字。
- **单次撤销** 把每个操作包在事务（transaction）中。

---

## 使用证据

在 19 天的生产施工图纸实际使用期间，完成了 220 次工具调用，成功率 98.2%。

| 工具 | 调用次数 |
|------|---------|
| get_selected_texts | ~100 |
| translate_and_rewrite | ~77 |
| send_code | ~28 |
| collapse_and_rewrite | ~11 |
| update_texts | ~10 |
| apply_unicode_style | ~4 |

---

## 架构

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

线程、发现（discovery）和鉴权细节见 [ARCHITECTURE.md](ARCHITECTURE.md)。

---

## 安装

### 1. Server —— .NET global tool

```bash
dotnet tool install -g Bimwright.Dwg.Server
bimwright-dwg --help
```

需要 .NET 8 SDK。

### 2. Plugin —— AutoCAD add-in

**方式 A：自动部署（.bundle）**

从 [GitHub Releases](https://github.com/bimwright/dwg-mcp/releases/latest) 下载 plugin：

```powershell
pwsh scripts/install.ps1 -Version 2024 -WhatIf    # 预览
pwsh scripts/install.ps1 -Version 2024            # 安装
pwsh scripts/install.ps1 -Uninstall               # 卸载
```

脚本会部署到 `%APPDATA%\Autodesk\ApplicationPlugins\Bimwright.Dwg.bundle\`。重启 AutoCAD 以加载。

**方式 B：手动 NETLOAD（开发用）**

在 AutoCAD 中：`NETLOAD` → 选择 `src/plugin-acad24/bin/Debug/net48/Bimwright.Dwg.Plugin.Acad24.dll`。监听器自动启动。

### 3. 接好你的 MCP client

加入你的 MCP client 配置（例如 `.mcp.json`）：

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

用 4 位目标年份锁定某个特定的 AutoCAD 实例：

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

使用 `--read-only` 去掉可写 toolset。使用 `--toolsets all`，或显式列表且**包含**你需要的默认项（自定义列表会**替换**默认集合，例如 `query,modify,meta,view,annotation`）。环境变量：`BIMWRIGHT_DWG_TOOLSETS=…`。

`dwg_send_code` 默认隐藏在工具列表之外。要暴露它，必须在**两侧**都选择启用：先用 `--enable-send-code`（或 `BIMWRIGHT_DWG_ENABLE_SEND_CODE=1`）启动 server，然后在 AutoCAD 中针对该 plugin 会话运行 `MCPENABLECODE` 命令（`MCPDISABLECODE` 可撤销该授权）：

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

---

## Tools

默认启动暴露 36 个工具：query、modify、meta、view 和默认启用的 `dwg_capture_view_image`。可选 ToolBaker、annotation、block、dimension、export 和 drawing 工具集，通过 `--toolsets` 启用，再加上 `dwg_send_code`，把后端可用的 MCP surface 扩充到 61 个工具。

通用 CAD 工具作用于所选 AutoCAD 目标中的当前活动文档。实体输入与返回的实体 ID 使用 AutoCAD 十六进制 handle，例如 `7F5AD`，由选择、创建或属性工具返回。创建、复制、偏移和修改操作的响应会用十六进制 handle 标识所生成或修改的实体。

Plan 2 的查询扩展仅限模型空间：`dwg_query_entities`、`dwg_count_entities`、`dwg_select_by_layer` 和 `dwg_select_by_type` 扫描的是模型空间，而非图纸空间/布局实体。`dwg_select_by_layer` 和 `dwg_select_by_type` 返回给调用方的 handle 列表；它们不会改变 AutoCAD 的 pickfirst 选择。

| 工具 | 用途 |
|------|------|
| `dwg_get_drawing_info` | 读取当前图纸名称、当前图层、当前空间/布局，以及单位标量 |
| `dwg_get_entity_properties` | 读取由 AutoCAD 十六进制 handle 标识的实体属性 |
| `dwg_list_layers` | 列出当前图纸中的图层及其颜色和状态标志 |
| `dwg_query_entities` | 按可选的类型、图层、颜色、数量和几何标志查询模型空间实体 |
| `dwg_count_entities` | 按可选的类型、图层或颜色过滤统计模型空间实体数量 |
| `dwg_select_by_layer` | 返回一个图层的模型空间实体 handle 列表，且不改变 pickfirst 选择 |
| `dwg_select_by_type` | 返回一个实体类型的模型空间实体 handle 列表，且不改变 pickfirst 选择 |
| `dwg_get_selected_texts` | 读取 pickfirst 选择，对文字实体做空间聚类，返回带改写模式提示的分组文字 |
| `dwg_update_texts` | 在一个事务中按 handle 写入新文字 |
| `dwg_create_layer` | 确保某个图层存在，且不覆盖已有图层的属性 |
| `dwg_create_line` | 在当前绘图空间中创建一条直线 |
| `dwg_create_circle` | 在当前绘图空间中创建一个圆 |
| `dwg_create_point` | 创建一个点并返回其十六进制 handle |
| `dwg_create_polyline` | 由顶点创建一条轻量多段线并返回其十六进制 handle |
| `dwg_create_rectangle` | 创建一个矩形多段线并返回其十六进制 handle |
| `dwg_create_arc` | 创建一段圆弧并返回其十六进制 handle |
| `dwg_create_ellipse` | 创建一个椭圆并返回其十六进制 handle |
| `dwg_change_layer` | 把由十六进制 handle 标识的实体移动到另一个图层 |
| `dwg_change_color` | 按 AutoCAD 颜色索引（color index）改变实体颜色 |
| `dwg_move_entities` | 按位移向量移动由十六进制 handle 标识的实体 |
| `dwg_rotate_entities` | 绕基点旋转由十六进制 handle 标识的实体 |
| `dwg_scale_entities` | 绕基点缩放由十六进制 handle 标识的实体 |
| `dwg_copy_entities` | 复制由十六进制 handle 标识的实体并返回复制后的 handle |
| `dwg_erase_entities` | 擦除由十六进制 handle 标识的实体 |
| `dwg_offset_entities` | 偏移曲线实体并返回生成的 handle |
| `dwg_translate_and_rewrite` | **首选。** 把翻译后的文字写回：锚定、删除、MText、字体、高度 |
| `dwg_apply_unicode_style` | 确保 `Bimwright_Unicode` 样式存在并应用到目标实体 |
| `dwg_collapse_and_rewrite` | 具有显式几何控制能力的底层改写原语 |
| `dwg_list_available_targets` | 列出从 v2 JSON 和旧版 2024 发现文件探测到的运行中的 AutoCAD 目标 |
| `dwg_get_current_target` | 显示锁定的目标年份（若有） |
| `dwg_switch_target` | 把本 server 进程锁定到 AutoCAD `2022` 至 `2027` |
| `dwg_batch_execute` | 把多个内部 wire 命令作为逻辑批处理运行 |
| `dwg_zoom_extents` | 缩放到绘图视口的图形范围 |
| `dwg_zoom_window` | 缩放到由两个角点定义的窗口 |
| `dwg_zoom_to_entity` | 缩放到由 handle 标识的特定绘图实体的范围 |
| `dwg_capture_view_image` | 将活动视图截取为图像文件（默认启用；受路径策略约束） |

`dwg_send_code` **不在**上表 — 仅双侧 opt-in（见安装 / 安全）。

启用 `toolbaker` 工具集时会暴露可选 ToolBaker 工具：

| 工具 | 用途 |
|------|------|
| `dwg_list_baked_tools` | 列出来自 server 自有 SQLite 注册表的已接受 baked 工具 |
| `dwg_run_baked_tool` | 按名称运行一个已接受的 baked 工具 |
| `dwg_list_bake_suggestions` | 列出检测到的重复 workflow 建议 |
| `dwg_accept_bake_suggestion` | 校验、冒烟测试并接受一条建议 |
| `dwg_dismiss_bake_suggestion` | 关闭或抑制一条建议 |
| `dwg_create_bake_issue_draft` | 为一条建议生成 GitHub issue 草稿（不提交） |

启用 `annotation` 工具集时会暴露可选 Annotation 工具：

| 工具 | 用途 |
|------|------|
| `dwg_create_text` | 创建单行文字（DBText），可指定目标高度、旋转和属性 |
| `dwg_create_mtext` | 创建多行文字（MText），支持格式和宽度 |
| `dwg_create_leader` | 创建一个多引线（MLeader），可带引线文字 |
| `dwg_create_table` | 创建一个带有指定行列文字内容的 AutoCAD 表格 |

启用 `block` 工具集时会暴露可选 Block 工具：

| 工具 | 用途 |
|------|------|
| `dwg_list_blocks` | 列出当前图纸中的块定义（只读安全） |
| `dwg_get_block_attributes` | 按 handle 读取块参照的属性（只读安全） |
| `dwg_insert_block` | 插入一个块参照，可选从外部 DWG 导入 |
| `dwg_set_block_attributes` | 按 handle 设置块参照的属性 |
| `dwg_explode_block` | 分解块参照并返回生成部件的 handle |

启用 `dimension` 工具集时会暴露可选 Dimension 工具：

| 工具 | 用途 |
|------|------|
| `dwg_create_linear_dimension` | 创建带旋转角度的线性标注 |
| `dwg_create_aligned_dimension` | 创建两点之间的对齐标注 |
| `dwg_create_radial_dimension` | 为圆或圆弧创建半径标注 |
| `dwg_create_diameter_dimension` | 为圆或圆弧创建直径标注 |

启用 `export` 工具集时会暴露可选 Export 工具：

| 工具 | 用途 |
|------|------|
| `dwg_export_dxf` | 把图纸导出为 DXF 文件（受输出路径策略约束） |

启用 `drawing` 工具集时会暴露可选 Drawing 工具：

| 工具 | 用途 |
|------|------|
| `dwg_get_variables` | 读取绘图系统变量的当前值 |
| `dwg_set_system_variable` | 设置绘图系统变量的值 |
| `dwg_save_drawing` | 把当前图纸保存到文件（需 confirm=true） |
| `dwg_purge_drawing` | 清理未使用的命名对象（块、图层、样式）（支持 dry_run=true，实际清理需 confirm=true） |

### 输出路径策略

所有导出操作都受到路径策略的严格约束，要求：

- 输出路径必须是绝对路径。
- 文件扩展名必须匹配对应的工具（例如 DXF 导出必须是 `.dxf`）。
- 除非显式提供 `overwrite_existing=true`，否则不覆盖已有文件。
- 除非设置了 `allow_repo_output=true`，否则拒绝写入仓库根目录。

### 可选工具集与只读行为

默认仅启用 `query`、`modify`、`meta` 和 `view` 工具集。你可以使用 `--toolsets` 标志选择启用其他工具集（例如 `--toolsets all` 或 `--toolsets query,modify,meta,view,annotation,block,dimension,export,drawing`）。

- **只读模式（`--read-only`）**：当只读模式生效时，所有可写的工具集（`modify`、`code`、`annotation`、`dimension`、`export` 和 `drawing` 的写入工具）都会被完全禁用。
- **Block 工具集拆分**：`block` 工具集拆分为只读和可写两部分。若 `--read-only` 生效，`dwg_list_blocks` 和 `dwg_get_block_attributes` 仍然可用（安全的只读检查），但变更/创建类工具（`dwg_insert_block`、`dwg_set_block_attributes`、`dwg_explode_block`）会被剔除。
- **View 与只读**：只读模式下仍注册完整 `view` toolset（含 zoom 与 `dwg_capture_view_image`）。Capture 在 MCP schema 上标记为 read-only，但仍会按路径策略写图片文件——在 `--read-only` 下请注意输出路径。
- **绘图操作与只读**：`drawing` 工具集在只读模式下保留 `dwg_get_variables`，但剔除 `dwg_set_system_variable`、`dwg_save_drawing` 和 `dwg_purge_drawing`。
- **延迟的角度标注**：注意当前仅支持线性、对齐、半径和直径标注类型。角度标注已被推迟，尚未实现。
- **延迟的文件导出工具**：`dwg_export_pdf` 和 `dwg_export_image` 工具已被推迟，而 `dwg_capture_view_image` 默认完全启用，以确保绘图视图截图与打印配置的绝对可靠性。

### 手动冒烟检查清单

在一个临时 DWG 中：

1. 运行 `dwg_get_drawing_info`。
2. 运行 `dwg_list_layers`。
3. 用 `dwg_create_layer` 创建 `BIMWRIGHT_TEST`。
4. 在 `BIMWRIGHT_TEST` 上用 `dwg_create_point`、`dwg_create_polyline`、`dwg_create_rectangle`、`dwg_create_arc` 和 `dwg_create_ellipse` 创建一个点、多段线、矩形、圆弧和椭圆；记录返回的十六进制 handle，并预留一条曲线（例如圆弧或椭圆）用于颜色和偏移检查。
5. 用 `dwg_query_entities`、`dwg_count_entities`、`dwg_select_by_layer` 和 `dwg_select_by_type` 按图层和类型查询、统计并选择这些实体；确认 select 工具返回 handle 列表且不会改变 pickfirst 选择。
6. 用 `dwg_move_entities`、`dwg_rotate_entities` 和 `dwg_scale_entities` 移动、旋转和缩放非预留的临时实体。
7. 用 `dwg_copy_entities` 复制一条非预留的临时实体，再用 `dwg_erase_entities` 仅擦除那条可丢弃的复制临时实体。
8. 在预留的曲线上用 `dwg_change_color` 改变颜色，再用 `dwg_offset_entities` 偏移该曲线，并确认返回的已生成 handle 是十六进制 handle。
9. 确认既有的文字翻译 workflow 仍可用：选中临时文字，运行 `dwg_get_selected_texts`，再用 `dwg_translate_and_rewrite` 改写它。

### 可选冒烟 — annotation / block / dimension

先启用可选 toolset（`--toolsets …` 或 `--toolsets all`）。在 scratch DWG 上：

1. 在临时 DWG 中用 `dwg_create_text`、`dwg_create_mtext`、`dwg_create_leader` 和 `dwg_create_table` 创建文字、多行文字、引线和表格。
2. 用 `dwg_list_blocks` 列出块定义。
3. 用 `dwg_insert_block` 从图纸或绝对外部 DWG 路径插入一个已知块。
4. 用 `dwg_get_block_attributes` 和 `dwg_set_block_attributes` 读取和设置块属性。
5. 用 `dwg_explode_block` 分解一个块参照。
6. 用 `dwg_create_linear_dimension`、`dwg_create_aligned_dimension`、`dwg_create_radial_dimension` 和 `dwg_create_diameter_dimension` 创建线性、对齐、半径和直径标注，确认线性投影距离校验按预期通过/拒绝。

### 可选冒烟 — view / export / drawing

在启用 `view`、`export`、`drawing` 等 toolset 后（按需）：

1. 运行 `dwg_zoom_extents`。
2. 用坐标运行 `dwg_zoom_window`。
3. 用记录的十六进制 handle 通过 `dwg_zoom_to_entity` 缩放到某个实体。
4. 用 `dwg_get_variables` 读取绘图变量。
5. 用 `dwg_export_dxf` 把图纸导出为 DXF。
6. 在复制/可丢弃的 DWG 上，先以 `dry_run=true` 运行 `dwg_purge_drawing`，再以 `confirm=true` 运行。
7. 在复制/可丢弃的 DWG 上，以 `confirm=true` 运行 `dwg_save_drawing`。

### 从 0.1.x 工具名迁移

MCP 工具名现在使用 `dwg_` 前缀。原始的 plugin 命令名仍是内部 wire 命令。

| 0.1.x MCP 名 | 1.0 MCP 名 |
|--------------|-----------|
| `get_selected_texts` | `dwg_get_selected_texts` |
| `update_texts` | `dwg_update_texts` |
| `translate_and_rewrite` | `dwg_translate_and_rewrite` |
| `apply_unicode_style` | `dwg_apply_unicode_style` |
| `collapse_and_rewrite` | `dwg_collapse_and_rewrite` |
| `send_code` | `dwg_send_code` |

---

## 标准工作流

```
1. 用户在 AutoCAD 中选择文字实体
2. Agent 调用 dwg_get_selected_texts -> 收到聚类后的文字组
3. Agent 翻译每个分组
4. Agent 调用 dwg_translate_and_rewrite([{id, new_text}, ...])
   工具负责：锚定、删除、MText、字体样式、高度。完成。
5. 必要时用户运行 REGEN
```

从 agent 的视角看只有两步：读取，然后写入。

---

## 支持的 AutoCAD 版本

| 版本 | ObjectARX 发布 | Plugin TFM | 状态 |
|------|----------------|------------|------|
| AutoCAD 2022 | 24.1 | `net48` | 已搭建 shell 脚手架；发布构建需要本地 Autodesk 引用 |
| AutoCAD 2023 | 24.2 | `net48` | 已搭建 shell 脚手架；发布构建需要本地 Autodesk 引用 |
| AutoCAD 2024 | 24.3 | `net48` | 默认支持的 shell 与常规 solution 构建 |
| AutoCAD 2025 | 25.0 | `net8.0-windows` | 已搭建 shell 脚手架；发布构建需要本地 Autodesk 引用 |
| AutoCAD 2026 | 25.1 | `net8.0-windows` | 已搭建 shell 脚手架；与 2025 二进制兼容，但作为独立 shell 构建 |
| AutoCAD 2027 | 26.0 | `net10.0-windows` | 已搭建 shell 脚手架；与 2025/2026 不二进制兼容 |

Server 和测试不需要每个 AutoCAD shell 都完成发布构建即可通过。要发布某个 AutoCAD 年份，需要在装有对应 Autodesk 托管程序集的就绪机器上构建该 shell。

---

## 安全

`dwg_send_code` 执行任意 C#，拥有对 AutoCAD 进程和本地文件系统的完全访问权。它不会注册在默认的 MCP 工具 surface 中。要使用它，须以 `--enable-send-code` 或 `BIMWRIGHT_DWG_ENABLE_SEND_CODE=1` 启动 server，然后在 AutoCAD 中运行 `MCPENABLECODE` 为该 plugin 会话授予插件侧的同意。

安全模型依赖以下几点：

- **仅本地传输**——AutoCAD 2022–2024 走 127.0.0.1 上的 TCP，2025–2027 走 loopback Named Pipe，无远程访问。
- **每会话鉴权令牌**——在每次 plugin 启动时轮换，按请求校验。
- **双侧代码选择启用**——`dwg_send_code` 仅在 server 以 `--enable-send-code`（或 `BIMWRIGHT_DWG_ENABLE_SEND_CODE=1`）启动，**且**用户在 AutoCAD 中针对该 plugin 会话运行 `MCPENABLECODE` 时才会注册。
- **超时边界**——脚本执行运行在专用线程上，超时会取消并中止。
- **可信 agent 假设**——仅在与你可控的 MCP client 一起使用时才启用。

不要将 plugin 端口暴露到网络。

---

## 项目结构

```
dwg-mcp/
├── src/
│   ├── Bimwright.Dwg.sln
│   ├── server/            # .NET 8 MCP server（global tool）
│   ├── shared/            # Handlers、聚类、改写、unicode
│   ├── plugin-acad22/     # AutoCAD 2022 shell（.NET 4.8）
│   ├── plugin-acad23/     # AutoCAD 2023 shell（.NET 4.8）
│   ├── plugin-acad24/     # AutoCAD 2024 shell（.NET 4.8）
│   ├── plugin-acad25/     # AutoCAD 2025 shell（.NET 8）
│   ├── plugin-acad26/     # AutoCAD 2026 shell（.NET 8）
│   └── plugin-acad27/     # AutoCAD 2027 shell（.NET 10）
├── tests/                 # xUnit
├── scripts/               # install/uninstall PowerShell
├── lib/acad24/            # 仅有说明；Autodesk DLL 永不提交
└── .github/workflows/     # CI
```

---

## bimwright 家族

为 AEC 工具链亲手打造的 MCP gateway——同一套架构，predictable / auditable / reversible：

- [**rvt-mcp**](https://github.com/bimwright/rvt-mcp) —— Autodesk® Revit®
- [**dwg-mcp**](https://github.com/bimwright/dwg-mcp) —— Autodesk® AutoCAD®
- [**nwd-mcp**](https://github.com/bimwright/nwd-mcp) —— Autodesk® Navisworks®
- [**ipt-mcp**](https://github.com/bimwright/ipt-mcp) —— Autodesk® Inventor®
- [**bim-wiki**](https://github.com/bimwright/bim-wiki) —— 越南语优先的 BIM 知识库

---

## 免责声明

AutoCAD 和 Autodesk 是 Autodesk, Inc. 的注册商标。bimwright 是一个独立的开源项目，与 Autodesk, Inc. 无关联、无赞助、无背书。

---

## License

[Apache License 2.0](LICENSE)

第三方声明：[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)
