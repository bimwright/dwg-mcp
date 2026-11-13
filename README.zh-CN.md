<!-- mcp-name: io.github.bimwright/dwg-mcp -->

<p align="center">
  <img src="https://raw.githubusercontent.com/bimwright/.github/master/assets/logos/dwg-mcp.png" alt="dwg-mcp" width="180" />
</p>

<h1 align="center">dwg-mcp</h1>

<p align="center">
  <a href="https://github.com/bimwright/dwg-mcp/actions/workflows/build.yml"><img src="https://github.com/bimwright/dwg-mcp/actions/workflows/build.yml/badge.svg" alt="build" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-Apache%202.0-blue.svg" alt="license" /></a>
  <a href="#支持的-autocad-版本"><img src="https://img.shields.io/badge/AutoCAD-2022--2027-186BFF" alt="AutoCAD 2022-2027" /></a>
  <a href="#tools"><img src="https://img.shields.io/badge/MCP-54%20default%20%2B%20optional-6C47FF" alt="MCP tools" /></a>
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

从 [GitHub Releases](https://github.com/bimwright/dwg-mcp/releases/latest) 下载 setup ZIP（`DwgMcp.Setup-*-win-x64.zip`）。请查看已下载ZIP内 `manifest.json` 的 `packedAutocadYears` 和 bundle 的 `PackageContents.xml`，确认实际包含哪些年份的插件；在运行中的AutoCAD上已验证的年份请查阅发行说明。源码提供2022–2027的shell，但打包插件和实机验证范围取决于具体版本。未包含的年份需要用对应的AutoCAD SDK从源码构建插件。解压后运行 `install.ps1`（完整命令见英文 README）。

**NuGet（仅 gateway）：** `dotnet tool install --global Bimwright.Dwg.Server --version 2.2.0` 需要 .NET 8。AutoCAD 插件须另外从相同版本的 Setup ZIP 安装。

**Claude Desktop：** 下载同一发行版的 `dwg-mcp-desktop-2.2.0.mcpb` 及其校验文件，通过 Settings → Extensions → Advanced settings 安装。扩展仅启动 Setup 2.2.0 提供的指定 executable，不安装 gateway/plugin，也不回退到 NuGet/PATH。override 文件必须匹配同一 SHA-256。

**开发者：** 本地 build 后 `pwsh scripts/install.ps1 -Version 2024`，或 `NETLOAD` Debug DLL。

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

写入前先调用 `dwg_list_available_targets`，再调用 `dwg_switch_target(target="pid:<PID>")`。默认 `verify=true` 验证进程及当前图纸；`verify=false` 只允许读取。`--target` 只选择初始目标，不授权写入。多个候选返回 `AMBIGUOUS_TARGET`。

已有绑定后，切换到其他实例必须指定 `pid:<PID>` 或 `id:acad-<year>-<PID>`；离开仍在运行且已确认的目标还需要 `rebind=true`。用 `verify=false` 重新选择同一进程会撤销写入确认。此前预期的图纸生命周期ID会保留，除非用 `rebind=true` 重置，或用明确的 `expected_document_id` 替换。打开或切换图纸后，请检查 `dwg_health().target`；确认另一张图纸需要与其匹配的 `expected_document_id` 或 `rebind=true`。验证失败会保留原绑定，旧绑定下等待的调用会在发送前被拒绝。写入需要同时更新 gateway 和 plugin，旧组件不能提供所需的目标验证。

在 `--read-only` 下，`dwg_switch_target` 不在工具目录中，直接调用也会被拒绝。请用 `--read-only --target pid:<PID>`（或 `--target id:acad-<year>-<PID>`）启动 gateway，然后检查 health 和当前目标。遇到 `TARGET_LOST` 或 `TARGET_CHANGED` 时，调用 `dwg_list_available_targets`，再用明确的预期 `--target` 重启 gateway。绑定丢失后不会自动选择其他实例。

用 PID 或只匹配一个实例的 4 位年份选择初始 AutoCAD 目标：

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

使用 `--read-only` 去掉可写 toolset。使用 `--toolsets all`，或显式列表且**包含**你需要的默认项（自定义列表会**替换**默认集合，例如 `query,modify,meta,view,annotation`）。环境变量：`BIMWRIGHT_DWG_TOOLSETS=…`。

`dwg_send_code` 通过 `meta` 默认注册。用 `--enable-send-code` / `--disable-send-code` 控制 server（JSON `enableSendCode`；环境变量 `BIMWRIGHT_DWG_ENABLE_SEND_CODE`）。禁用时，含 C# 的批处理会在发送任何命令前整体拒绝。`MCPDISABLECODE` / `MCPENABLECODE` 只控制 AutoCAD 会话侧，不能覆盖 server 的禁用设置。`dwg_run_lisp` 保留注册以兼容旧客户端，但 server 和 plugin 均拒绝所有请求。此版本没有 LISP 隔离执行环境或脚本信任机制。`--read-only` 会移除这两个工具。

`dwg_send_code` 仅接受内联同步片段，执行前拒绝 `async`/`await` 和 `#load` 指令。AutoCAD API 必须留在调用线程，不要移至 `Task.Run` 或其他线程。只返回 JSON DTO，嵌套的 AutoCAD/COM 对象也会被拒绝。`dwg_inspect_lisp` 仍可用于静态分析；超过 2,000,000 字符的源码因无法完整检查而被标记为危险，200 条 findings 的显示上限不会停止危险级别检测。正则匹配设有超时；超时扫描会报告检查不完整并标记为危险，不会返回 `clean`。

---

调用日志**默认关闭**。用 `--enable-call-log` / `--disable-call-log`（JSON `enableCallLog`；环境变量 `BIMWRIGHT_DWG_ENABLE_CALL_LOG`）切换。优先级为 JSON → 环境变量 → CLI；同一开关对中最后出现的 flag 生效。关闭时不创建或追加调用日志、调用 journal，也不删除旧日志。开启时，`%LOCALAPPDATA%\Bimwright\dwg-mcp\dwg-mcp-calls.jsonl` 仅记录脱敏参数及耗时/成败元数据，不记录响应。code 字段（包括嵌套 batch）仅含字符长度和原始 UTF-8 body 的 SHA-256；可能回显 source 的代码相关错误正文被省略。尚未实现代码 body cache 或 body journal。ToolBaker adaptive usage、诊断日志和内存 History 是独立功能；plugin 没有持久化调用日志 writer。

## 活动通知与 History

通知默认开启，使用不抢占键盘焦点的单张卡片，显示 `dwg-mcp <年份>`、最新工具名称和 **Success · Failed · Capture** 计数。数字按里程表方式滚动；图像在固定框内居中，至少保留5秒，以 cross-fade 切换。关闭 Windows **Animation effects** 后即时更新。悬停暂停关闭；空闲时间可选10/20/30/60秒（默认20秒）。AutoCAD 最小化或有模态窗口时保留计数，恢复可用后显示。`MCPTOAST` 切换通知，`MCPSETTINGS` 设置通知及品牌标识。Branding 默认关闭，仅悬停时显示，重启后保留选择；保存失败会提示，但本次会话的选择仍生效。

点击卡片主体打开／聚焦 **History** 并关闭卡片；×只关闭；点击缩略图打开 allowlist 允许的图像并关闭卡片。`MCPHISTORY` 打开同一非模态窗口。History 在内存中保留插件处理调用的最近1,000条简短结果，通知关闭时仍记录；支持搜索、Success/Failed 和 Read/Write 筛选、耗时及选中行详情。关闭卡片不清空历史，**New session** 清空列表。不新增持久日志，不保留原始输入、代码正文或 JSON 结果。尚不包含仅在服务器执行的工具通知。`enableToast`、`toastIdleSeconds`、`showBranding` 保存于 `%LOCALAPPDATA%\Bimwright\dwg-mcp\settings.json`。

---

2.1.2 新增 `dwg_send_code(code, data?, compile_only=false)`，JSON 输入与源码分开传递。`data` 是 Newtonsoft `JToken`，支持 object/array/scalar（UTF-8 最大 512 KiB、深度 64、键唯一、数字有限；null 视为未传入），日期字符串保持字符串。`compile_only=true` 只编译，不执行语句，也不生成 stdout；仍需已验证的 target 和 active drawing。检查内部 `result.ok`、`compiled`、`executed`、`diagnostics`（原始源码行列从 1 开始）以及可选的 runtime `location`。gateway/plugin 必须配套更新；含 data/compile_only 的请求须在 batch/baked tool 外直接调用。旧 plugin 会在发送代码前被拒绝，原有仅 code 调用仍可用。输入值不进入 call log；History 仅保留长度和 SHA-256。

## Health 与更新检查（v2.1.1）

`dwg_health()` 默认离线、只读。每次会话先选择目标，再调用一次 `dwg_health(check_updates=true)`；后续常规探测保持离线。它不要求或锁定图纸，返回 host/document 状态及已加载 server/plugin 的 version/build/alignment。无目标时仍保留 gateway identity。旧插件的 `HEALTH_UNSUPPORTED` 或缺失 identity 表示未验证，不表示损坏；UI 忙时仍保留 plugin identity。health 不触发操作 toast，也不在 drawing batch 中运行。

GitHub 检查将两个 component 与同一 stable release/tag 比较，共享 5 秒预算且限制 metadata 大小。`checked` 只表示 lookup 成功，不证明 artifact 可信。SHA-256 只是 capture 时磁盘文件的观察，不证明已加载 memory。缺失证据仍为 `unknown`；版本相同或 `update_available=false` 不证明 release build 一致。

检查两个 `update.components`、完整通知、`lookup_reason`、`protected_component_reasons` 和 `unobserved_components`。`UPDATE_CHECK_UNAVAILABLE` 是外部 lookup 失败，不是 runtime 故障或安装授权请求；`LOCAL_VERSION_AHEAD` 仅是信息，不得降级。只有 `prepare_update` 且 `bundle_update_proposal_allowed=true` 才能提出更新，安装仍需授权。health 不下载 package、安装、修改 client config、保存/关闭图纸或重启进程。

reconnect 后，在同一连接上将新的 initialize 与 `tools/list` 的 `_meta["bimwright/dwg-mcp/runtime"]`、schema、annotations 对照 `client_verification`；server 无法证明 client cache 已刷新。read-only 下仍可用，因 GitHub 选项而设置 `openWorldHint=true`。参见 [agent guide](AGENTS.md)。此功能从 v2.1.1 起提供；请从同一发行版更新 gateway 和 plugin。CI 已构建 AutoCAD 2022–2027 的全部六个插件；验证范围请参阅发行说明。

## Reports、MCP 提示词与编辑

在 AutoCAD 中运行 `MCPREPORTS` 打开 Reports。点击报告中的 `dwg:` 链接即可选中对应实体并缩放至该位置。`dwg_report_task_result` 接受可选的 `call_ids`。关闭 AutoCAD 后仍可用 `dwg_get_report_history` 读取已保存的报告历史。在网关设置 `BIMWRIGHT_DWG_ENABLE_CHANGE_HISTORY=true` 后，支持的图纸编辑会在提交后写入变更历史。

每张图纸的变更历史保存在 `%LOCALAPPDATA%\Bimwright\dwg-mcp\history\<drawing_key>.db`。用户输入的变更原因可能包含敏感数据。备份或删除前，请关闭所有 dwg-mcp 网关，再一起处理该图纸的数据库和关联的 SQLite 文件。引用已删除历史的报告会显示 `history_unavailable`。关闭记录功能会停止写入新记录。

AutoCAD 插件默认连接 `api.github.com` 检查新的稳定版本。后台网络查询每 24 小时最多执行一次。可在 Settings 中关闭 dwg-mcp 新版本检查。

MCP 提示词 `dwg_translate_text` 用于在写回前审核译文，`dwg_review_large_drawing` 用于查看图纸概况并通过 `dwg_cluster_bands` 分析密集区域。只读模式下，翻译流程停在审核环节，图纸审阅流程跳过截图。

`dwg_attach_image` 以指定插入点和宽度附着本地 PNG、JPG、TIFF 或 BMP 图像引用，也可指定高度、旋转、图层和绘制顺序。`dwg_batch_execute` 默认运行逻辑批处理。对支持的图纸命令启用 `atomic=true` 后，任一步骤失败都会触发回滚，包括单个实体报告的错误。

使用 `dwg_send_code(result_contract="task_v1")` 时，脚本返回 `McpTaskResult.Pass(value)` 或 `McpTaskResult.Fail(code, message, details)`。请检查 `ok` 和 `_task.status`。`ACAD_BUSY` 与 `QUEUE_FULL` 表示命令尚未执行，AutoCAD 就绪后可重试。

用 `dwg_list_open_documents` 找到目标图纸，再单独调用 `dwg_activate_document`。切换图纸后，在写入前使用 `dwg_switch_target` 确认新的 `document_id`。在 Settings 中选择英语、越南语或简体中文，重启 AutoCAD 后生效。

## Tools

2.2.0 默认启动暴露 54 个工具：query（含 `dwg_get_drawing_profile` 和 `dwg_inspect_lisp`）、modify、meta（含 `dwg_send_code`/`dwg_run_lisp`）、view 和默认启用的 `dwg_capture_view_image`。可选 ToolBaker、annotation、block、dimension、export 和 drawing 工具集，通过 `--toolsets` 启用，把后端可用的 MCP surface 扩充到 78 个工具。 注册数量包含仅返回拒绝的 `dwg_run_lisp`；任何扫描判定都不授权执行。

通用 CAD 工具作用于所选 AutoCAD 目标中的当前活动文档。实体输入与返回的实体 ID 使用 AutoCAD 十六进制 handle，例如 `7F5AD`，由选择、创建或属性工具返回。创建、复制、偏移和修改操作的响应会用十六进制 handle 标识所生成或修改的实体。

只使用原有query/count参数的调用仍扫描模型空间。旧query保留参数顺序、结果字段、遍历顺序和 `include_geometry` 输出；limit默认500，限制在1–5000。`dwg_select_by_layer` 和 `dwg_select_by_type` 保留模型空间handle列表契约，不改变pickfirst选择。四个type过滤器按实际managed短名称或完整名称进行不区分大小写的匹配，并增加 `TEXT` → `DBText`、`INSERT` → `BlockReference`、`LWPOLYLINE` → `Polyline`；不从 `POLYLINE` 推断额外的DXF类型。

读取大型图纸时，先调用 `dwg_get_drawing_profile()`（`section="summary"`、`offset=0`）。它默认扫描模型空间和所有布局的顶层实体，不展开块定义或xref内容；一个块参照计为一个实体。可选第五参数 `space` 可设为 `all`（默认）、不读取布局定义的 `model`，或精确指定一个命名布局的 `layout:<name>`。使用总数前先检查source、scope和完整性。type按准确的运行时完整名称分组，layer和space也有各自的计数。已观察但不完整的计数以及不可用值会明确标注或返回 `null`，不会当作完整总数。

按照 `output.next_calls` 分页读取 `types`、`layers` 或 `spaces`；建议调用包含 `section`、`offset`、`expected_snapshot`、`max_output_bytes` 和 `space`。第一页可省略 `expected_snapshot`，但 `offset>0` 时必须提供；snapshot变化会返回 `PROFILE_CHANGED`，此时应重新读取summary。结果中的独立 `document_id` 也应保留。每页都会重新扫描图纸，不使用缓存。`max_output_bytes` 默认16384（4096–65536），与全局response budget共同限制最终MCP结果序列化后的大小，不限制扫描时间。snapshot绑定所选scope：model query/count/profile忽略仅viewport的变化，而all/命名layout profile及layout query也检测这些变化。改变scope时应省略旧 `expected_snapshot` 并重新读取summary。指定scope的profile响应必须标明 `scope.requested_space`；忽略scope的旧plugin会以 `PROFILE_SCOPE_UNSUPPORTED` 被拒绝。

| 工具 | 用途 |
|------|------|
| `dwg_health` | host/runtime identity 与可选 stable release 检查；默认离线、只读 |
| `dwg_get_drawing_info` | 读取当前图纸名称、当前图层、当前空间/布局，以及单位标量 |
| `dwg_get_drawing_profile` | 只读计数all（默认）、仅model或一个命名layout的顶层entity，检查带scope的snapshot并限制最终response字节数 |
| `dwg_get_entity_properties` | 读取由 AutoCAD 十六进制 handle 标识的实体属性 |
| `dwg_list_layers` | 列出当前图纸中的图层及其颜色和状态标志 |
| `dwg_query_entities` | 旧模型空间查询，或选择范围/投影字段分页读取，使用签名续页及最终response字节上限 |
| `dwg_count_entities` | 按type/layer/color及可选WCS extents、命名space计数，明确报告无法判定的匹配coverage |
| `dwg_select_by_layer` | 返回一个图层的模型空间实体 handle 列表，且不改变 pickfirst 选择 |
| `dwg_select_by_type` | 返回一个实体类型的模型空间实体 handle 列表，且不改变 pickfirst 选择 |
| `dwg_get_selected_texts` | 旧pickfirst选择的写回分组，或从handles/bbox明确来源分页读取只读文字cluster |
| `dwg_update_texts` | 在一个事务中按 handle 写入新文字 |
| `dwg_create_layer` | 确保某个图层存在，且不覆盖已有图层的属性 |
| `dwg_create_line` | 在当前绘图空间中创建一条直线 |
| `dwg_create_circle` | 在当前绘图空间中创建一个圆 |
| `dwg_attach_image` | 按精确位置和尺寸附着链接的光栅图像（PNG/JPG/TIFF/BMP） |
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
| `dwg_list_available_targets` | 列出每个实例的 PID、目标 ID 和进程启动时间 |
| `dwg_get_current_target` | 显示绑定状态、代次、PID 和已确认的图纸 |
| `dwg_switch_target` | 选择并验证实例；切换实例必须用 `pid:`/`id:`，离开仍在运行且已确认的目标还需 `rebind=true`。只读模式下不可用 |
| `dwg_list_recent_drawings` | 列出当前 AutoCAD 最多 50 个最近 DWG，可按文件名过滤；无需打开图纸 |
| `dwg_open_drawing` | 按绝对路径打开 DWG，可请求只读；复用已打开图纸，不保存或关闭；默认等待 600 秒（[详情](README.md#drawing-session-tools)） |
| `dwg_report_task_result` | 将任务报告（结果、摘要、含 `dwg:<handle>` 实体链接的 Markdown 正文）保存为供 AutoCAD 用户查看的本地文件；不修改图纸 |
| `dwg_batch_execute` | 运行逻辑批处理，或为支持的图纸命令启用 `atomic=true` 回滚 |
| `dwg_send_code` | 在 AutoCAD .NET API 上执行 C# 代码片段（globals `doc`/`db`/`ed`，支持 `return` 返回值 + stdout 捕获，30 秒协作式取消；`MCPDISABLECODE`/`MCPENABLECODE` 按会话开关） |
| `dwg_run_lisp` | 仅用于兼容：所有 `file`/`code`/`command` 输入均以 `lisp_execution_blocked` 拒绝。不读取文件、不发送到 plugin、不执行。请使用 `dwg_inspect_lisp` 做静态分析。 |
| `dwg_inspect_lisp` | 对 `.lsp` 文件或内联 AutoLISP 做静态安全扫描 — 检出进程执行(`startapp`/`shell`)、危险 COM(`WScript.Shell`/`XMLHTTP`)、持久化(`acaddoc.lsp`、注册表写入)、删文件、二级 `(load …)`、混淆(`eval`/`read`)。返回 `verdict` clean/caution/dangerous + findings。纯本地分析;不执行、无需 AutoCAD;`--read-only` 下仍可用。 `clean` 不证明安全。报告均含 `execution_authorized=false` 和 `safety_assured=false`。 |
| `dwg_zoom_extents` | 缩放到绘图视口的图形范围 |
| `dwg_zoom_window` | 缩放到由两个角点定义的窗口 |
| `dwg_zoom_to_entity` | 缩放到由 handle 标识的特定绘图实体的范围 |
| `dwg_capture_view_image` | 在 AutoCAD 内捕获图形；返回内联图像、图像路径、实际尺寸、SHA-256 哈希及文档/视口/相机元数据（默认启用；受路径策略约束） |
| `dwg_inspect_view_region` | 在源图像上指定归一化矩形，缩放并重新生成，返回新图像及父图像ID |
| `dwg_restore_view` | 返回先前截图的相机位置并获取新图像 |

读图流程：截图 → 查看图像 → 选择区域 → `dwg_inspect_view_region` → 查看细节。保留父图像ID，通过 `dwg_restore_view` 返回总览。历史记录在当前AutoCAD进程中最多保留64张、30分钟。首版区域导航仅支持模型空间中的单个视口、俯视正投影、无视图旋转；拒绝过期来源和不支持的视图。响应在原有JSON旁增加MCP图像块（最多8 MiB）。这些工具会改变相机并保存图片，因此在 `--read-only` 下不可用；不会编辑几何或保存DWG。已安装主机的运行验收仍待完成。参见上述工作流程。

**空间过滤和分页entity读取**扩展现有 `dwg_query_entities`、`dwg_count_entities`，不增加MCP工具。提供 `bbox`、`bbox_mode`、`space`、`fields`、`cursor` 或 `max_output_bytes` 即进入分页模式；无过滤分页可用 `paged=true`。只提供 `paged=false` 则保留旧行为。旧五个参数仍在前面。不要把 `include_geometry=true` 与新选项组合，改用 `fields` 选择字段。

| 追加的query选项 | 含义 |
|---|---|
| `paged=false` | 设为true，在没有其他新选项时启用分页 |
| `bbox=null` | JSON字符串 `{"min":[x,y,z],"max":[x,y,z]}`，WCS坐标有限且min ≤ max |
| `bbox_mode=null` | 有box时默认 `intersects`，或 `contains`；必须提供 `bbox` |
| `space=null` | 默认 `model`，或 `layout:<人类可读LayoutName>`，忽略大小写的精确匹配；使用profile中的布局名 |
| `fields=null` | JSON数组字符串，默认 `handle`、`type`、`layer`。仅允许 `handle`、`type`、`runtime_type`、`layer`、`color_index`、`text`、`position`、`extents`；始终包含 `handle` |
| `cursor=null` | 上一页返回的续页token |
| `max_output_bytes=null` | 最终MCP结果上限，默认16384字节，范围4096–65536；更小的全局budget仍适用 |

`bbox` 比较WCS轴对齐三维 `GeometricExtents`，包含接触边界，不是精确图形交集测试。坐标使用图纸单位，不做INSUNITS换算。结果附INSUNITS代码/名称；代码0仍是unitless/unknown。只扫描所选模型空间或命名布局的顶层实体，不展开block、xref或viewport可见内容，也不回退到其他space。`dwg_count_entities` 只向旧三个参数追加 `bbox`、`bbox_mode`、`space`，并报告范围metadata和未知匹配coverage。需要空间handle列表时，query使用 `fields="[\"handle\"]"`，不改变select契约。

分页按AutoCAD handle的数值排序。按照 `result.output.next_calls` 调用并保持filter和fields；签名host-session cursor绑定图纸生命周期、revision和最后**已交付**的handle。可改变 `limit` 或byte cap；改变filter/fields必须省略cursor并重新开始。无效、篡改或图纸变化后的cursor会被拒绝。每页重新扫描space，不使用缓存；byte cap限制输出，不限制扫描时间。`result.output` 报告budget、`omitted_matching`、`has_more`、`next_cursor`、`next_calls`及row受阻时的说明。读取/filter/必要extents失败会在coverage中明确计数，返回 `exact_match_count=null` 和已观察的 `matched_count`，不提供可能跳过未知handle的续页。entity不支持的property以 `null` 和 `unavailable_fields` 标明。扩展query/count只能直接调用；gateway和plugin的batch预检查都会在执行前面项目之前拒绝整个batch。只用旧参数的query/count batch保持原行为，baked query仍不可用。扩展读取需要gateway/plugin共同支持：gateway对未支持该功能的旧plugin返回 `QUERY_FEATURE_UNSUPPORTED` 并丢弃旧格式输出，只用原参数的调用仍可用。

**无需手动选择即可读文字**：给现有 `dwg_get_selected_texts` 指定明确来源。原参数 `groupingStrength="normal"`、`includeEntities=false` 仍在最前，只用这两个参数保留旧选择/写回流程。任一新选项都必须恰好提供 `handles` 或 `bbox` 之一，不会暗中退回选择来源。

| 追加的text-read选项 | 含义 |
|---|---|
| `handles=null` | 十六进制handle的JSON数组字符串，最多5000项并去重；`"[]"` 是合法空来源 |
| `bbox=null` | JSON对象字符串 `{"min":[x,y,z],"max":[x,y,z]}`，沿用空间query的WCS、图纸单位extents规则 |
| `bbox_mode=null` | 有box时默认 `intersects`，或 `contains`；必须提供 `bbox` |
| `space=null` | 默认 `model`，或忽略大小写精确匹配的 `layout:<name>`；不回退到其他space |
| `cursor=null` | 从 `output.next_calls` 取得的签名text-cluster续页 |
| `max_output_bytes=null` | 最终MCP结果上限，默认16384字节，4096–65536，仍受全局budget限制 |

新来源仅读取顶层直接遇到的DBText、MText、MLeader、属性参照及常量属性定义，不展开BlockReference、嵌套block或xref；旧选择路径仍保留原来一层block处理。model来源的snapshot忽略仅viewport的变化，layout来源则检测这些变化。完整来源最多5000条文字record、合计8 MiB UTF-8文字。来源不完整时返回coverage，不返回cluster或cursor；`TEXT_SOURCE_TOO_LARGE` 要求缩小来源。保持原空间聚类算法，先对完整受理来源整体聚类，再以稳定顺序按完整cluster分页。

按照 `result.output.next_calls` 调用，保持MCP camelCase参数 `groupingStrength`、`includeEntities`。cursor绑定document/scope revision、来源、grouping设置和文字专用cluster序号，不是entity handle。byte fitting只移除整个cluster，只推进到已交付的cluster；检查 `omitted_clusters`、blocked说明和续页信息。cluster太大时，可提高cap至65536（若全局budget更小也需调整），或使用 `includeEntities=false`、较弱grouping或更窄来源。改变source/grouping/includeEntities必须不用cursor重新开始。

新来源结果为 `read_only=true`、`writeback_ready=false`，返回opaque `cluster_key`，没有旧数字ID或 `rewrite_mode`。新来源读取会保留已有的rewrite分组，也不改变CAD几何或选择。按实体翻译时，用 `includeEntities=true` 读取，再按handle调用 `dwg_update_texts` 写回；`dwg_translate_and_rewrite` 仍需**手动选择**读取，本阶段不允许从新来源页按簇写回。新来源读取只能直接调用，gateway/plugin在执行前拒绝整个batch及增强baked调用；旧选择batch保持不变。旧plugin不支持来源时，gateway返回 `TEXT_FEATURE_UNSUPPORTED` 并丢弃selection payload，不会回退到选择结果。

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
- **View 与只读**：只读模式保留临时缩放工具。`dwg_capture_view_image`、`dwg_inspect_view_region` 和 `dwg_restore_view` 会创建图片文件，因此被移除。
- **绘图操作与只读**：`drawing` 工具集在只读模式下保留 `dwg_get_variables`，但剔除 `dwg_set_system_variable`、`dwg_save_drawing` 和 `dwg_purge_drawing`。
- **延迟的角度标注**：注意当前仅支持线性、对齐、半径和直径标注类型。角度标注已被推迟，尚未实现。
- **原生视口捕获**：`dwg_capture_view_image` 使用 AutoCAD 的 `Document.CapturePreviewImage` API，无桌面自动化、不保存 DWG。命令进行中或捕获期间文档/相机变化会被拒绝。可选 `expected_document_fingerprint` 可将后续捕获绑定到先前结果。构建阅读图集时请把返回的元数据与图像一起保存；工具本身只写入图像。实际输出尺寸可能与请求值略有差异。相机元数据描述活动视口，并非平铺视图或图纸布局的通用像素-世界映射。参见上述工作流程。
- **延迟的文件导出工具**：`dwg_export_pdf` 和 `dwg_export_image` 仍为延迟状态；视口捕获不配置也不校验打印输出。

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
| `run_lisp` | `dwg_run_lisp` |

---

## 标准工作流

此写回流程使用手动选择及原有 `groupingStrength`/`includeEntities` 两个参数；只读来源的 `cluster_key` 不能替代其数字ID。

```
1. 用户在 AutoCAD 中选择文字实体
2. Agent 调用 dwg_get_selected_texts -> 收到聚类后的文字组
3. Agent 翻译每个分组
4. Agent 调用 dwg_translate_and_rewrite([{id, new_text}, ...])
   工具负责：锚定、删除、MText、字体样式、高度。完成。
5. 必要时用户运行 REGEN
```

rewrite成功时，`final_text_height` 表示 `new_handle` 对应entity的实际高度property，在rewrite transaction内完成Unicode归一化和最终scale后读取；无法读取时为 `null`。`style_only` 报告anchor的值，其他组成员的高度可能不同。block definition内的rewrite按definition局部单位报告高度，不代表world渲染高度。

从 agent 的视角看只有两步：读取，然后写入。

---

## 支持的 AutoCAD 版本

| 版本 | ObjectARX 发布 | Plugin TFM | 状态 |
|------|----------------|------------|------|
| AutoCAD 2022 | 24.1 | `net48` | 已搭建 shell 脚手架；基于 Autodesk 的 `AutoCAD.NET` 包构建 |
| AutoCAD 2023 | 24.2 | `net48` | 已搭建 shell 脚手架；基于 Autodesk 的 `AutoCAD.NET` 包构建 |
| AutoCAD 2024 | 24.3 | `net48` | 默认支持的 shell 与常规 solution 构建 |
| AutoCAD 2025 | 25.0 | `net8.0-windows` | 已搭建 shell 脚手架；基于 Autodesk 的 `AutoCAD.NET` 包构建 |
| AutoCAD 2026 | 25.1 | `net8.0-windows` | 已搭建 shell 脚手架；与 2025 二进制兼容，但作为独立 shell 构建 |
| AutoCAD 2027 | 26.0 | `net10.0-windows` | 已搭建 shell 脚手架；与 2025/2026 不二进制兼容 |

所有 shell 都针对 nuget.org 上 Autodesk 的 `AutoCAD.NET` 引用包编译，因此装有 .NET 8 和 .NET 10 SDK 的机器无需安装 AutoCAD 即可构建全部六个年份。这些包仅在编译时使用；Autodesk DLL 永远不会被复制到插件输出或 Setup ZIP 中。构建成功并不能证明插件在该 AutoCAD 年份中可以正常工作。

---

## 安全

`dwg_send_code` 以 AutoCAD 和本地文件系统的完整权限执行任意 C#，仅供可信 agent 使用，并非沙箱。`dwg_run_lisp` 无论输入、扫描判定或 `MCPENABLECODE` 状态如何均被拒绝。不创建 wrapper，不排队执行 LISP，不修改 trusted paths 或 SECURELOAD。不得通过 C#、batch 或 ToolBaker 绕过拒绝。`dwg_inspect_lisp` 只是静态分析，不是杀毒软件或整机防护；`clean` 仅表示未匹配已知模式。所有报告均返回 `execution_authorized=false` 和 `safety_assured=false`。需更新 server 和 plugin 并重启才能应用此限制；旧二进制文件仍保留旧行为。

安全模型依赖以下几点：

- **仅本地传输**——AutoCAD 2022–2024 走 127.0.0.1 上的 TCP，2025–2027 走 loopback Named Pipe，无远程访问。
- **每会话鉴权令牌**——在每次 plugin 启动时轮换，按请求校验。
- **会话级 kill-switch** — `MCPDISABLECODE` / `MCPENABLECODE` 控制 `dwg_send_code`。启用代码或重启 listener 后 LISP 仍被拒绝。
- **超时边界**——脚本在持有文档锁的线程上内联执行，带 30 秒协作式取消。
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

连接 AI 助手与 BIM、CAD 应用的开源工具。

**bimwright** 这个名字由 **BIM** 和 **wright** 组成。wright 是英语中表示制作者或建造者的旧词，如 *shipwright*（造船工）。

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

## 权限与自动模式

除 `dwg_send_code` 外，每个工具都显式声明 read-only、destructive、idempotent 和 open-world 标记。完整模式有 78 个工具；默认只读模式有 23 个，`--toolsets all --read-only` 提供下列 29 个只读工具。缩放可能改变临时界面状态。只读模式会移除目标切换工具；截图、区域查看和视图恢复也因创建图片文件而被移除。`dwg_health` 为只读工具，显式更新检查可以访问 GitHub。

仅为需要的工具设置精确权限规则。避免 `mcp__dwg-mcp__*` 通配规则，因为它也允许修改图纸、写文件和执行代码。`dwg_send_code` 没有 annotations，且不在此只读列表中；持久允许执行它必须由用户在 MCP 客户端中明确选择。

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
