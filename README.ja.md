<!-- mcp-name: io.github.bimwright/dwg-mcp -->

<p align="center">
  <img src="https://raw.githubusercontent.com/bimwright/.github/master/assets/logos/dwg-mcp.png" alt="dwg-mcp" width="180" />
</p>

<h1 align="center">dwg-mcp</h1>

<p align="center">
  <a href="https://github.com/bimwright/dwg-mcp/actions/workflows/build.yml"><img src="https://github.com/bimwright/dwg-mcp/actions/workflows/build.yml/badge.svg" alt="build" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-Apache%202.0-blue.svg" alt="license" /></a>
  <a href="#サポート対象autocadバージョン"><img src="https://img.shields.io/badge/AutoCAD-2022--2027-186BFF" alt="AutoCAD 2022-2027" /></a>
  <a href="#ツール"><img src="https://img.shields.io/badge/MCP-45%20default%20%2B%20optional-6C47FF" alt="MCP tools" /></a>
</p>

<p align="center">
  <a href="README.md">English</a> · <a href="README.vi.md">Tiếng Việt</a> · <a href="README.zh-CN.md">简体中文</a> · 日本語
</p>

---

## 図面翻訳を手動コピーペーストで止めてはいけない

建設・エンジニアリング図面には、仕様、注記、寸法、材料呼称、凡例など、密度の高い技術テキストが含まれています。それらの図面が外国語で届いた場合、翻訳は選択肢ではなく必須です。プロジェクトチームが行動を開始する前に、翻訳が完了していなければなりません。

通常のワークフローは苦痛を伴います。エンティティを一つずつ選択し、翻訳ツールにコピーし、貼り付け、フォントを修正し（SHXフォントはベトナム語やCJKをレンダリングできません）、高さを調整し、位置がずれていないことを確認します。これをシートあたり数百のテキスト断片、プロジェクトあたり数十枚のシートについて繰り返します。

`dwg-mcp` はこのループをたった2ステップに圧縮します。テキストを選択し、AIエージェントが読み取り、翻訳し、その場で書き換えます — 正しいフォント、正しい高さ、正しい空間グルーピング、そして1回のアンドゥで。

---

## dwg-mcp とは

`dwg-mcp` は、Autodesk AutoCAD 2022-2027 のDWGワークフローのためのローカルMCPゲートウェイです。

2つの部分から構成されます:

- **Bimwright.Dwg.Server**: .NET 8 のMCPサーバーで、Claude Code、Cursor、OpenCode、またはその他の stdio MCP クライアントから起動されます。
- **Bimwright.Dwg.Plugin**: バージョン固有のAutoCADアドインシェルで、AutoCAD内にロードされ、図面データベースに対してコマンドを実行します。

エージェントはMCPで通信します。サーバーはローカルワイヤー経由でプラグインと通信します。AutoCAD 2022–2024 ではTCP NDJSON、2025–2027 では名前付きパイプ（ループバック、ファイアウォールプロンプトを回避）を使用します。プラグインはAutoCAD .NET APIと通信します。

すべてはあなたのマシン上で動作します。

---

## なぜ重要か

AIエージェントは、「選択したすべてのテキストをベトナム語に翻訳して」と指示し、それを図面上で正しく実行することを可能にします。しかし、意図だけでは十分ではありません。AutoCADのテキスト操作には、空間レイアウトの理解、断片のグルーピング、フォントの制限、MTextとDBTextの違い、ブロック参照、高さスケーリングへの対応が必要です。

`dwg-mcp` はその複雑さを処理します:

- **空間クラスタリング**: 断片化されたテキストを論理的な文にグループ化します（ブロック、行、列、段落ごと）。
- **自動フォント処理**: Unicode対応のテキストスタイルを作成して適用します — SHXの疑問符はもうありません。
- **高さスケーリング**: ラテン文字とCJKテキストの視覚的密度の違いを補正します。
- **MText変換**: 安全な場合、単行の断片を複数行テキストにアップグレードします。
- **単一アンドゥ**: 各操作をトランザクションでラップします。

---

## 使用実績

実際の建設図面での19日間のアクティブ使用における220件の完了したツールコール。98.2%の成功率。

| ツール | コール数 |
|------|-------|
| get_selected_texts | ~100 |
| translate_and_rewrite | ~77 |
| send_code | ~28 |
| collapse_and_rewrite | ~11 |
| update_texts | ~10 |
| apply_unicode_style | ~4 |

---

## アーキテクチャ

```text
+---------------------------+
| AI クライアント            |
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
              | TCP NDJSON (2022-2024) / 名前付きパイプ (2025-2027)
              | トークン認証
              v
+---------------------------+
| Bimwright.Dwg.Plugin      |
| AutoCAD 2022-2027 シェル  |
+---------------------------+
              |
              | LockDocument()
              v
+---------------------------+
| AutoCAD .NET API          |
| ObjectARX 2022-2027       |
+---------------------------+
```

スレッド、検出、認証の詳細は [ARCHITECTURE.md](ARCHITECTURE.md) を参照してください。

---

## インストール

[GitHub Releases](https://github.com/bimwright/dwg-mcp/releases/latest) から setup ZIP（`DwgMcp.Setup-*-win-x64.zip`）を入手してください。同梱する年次バージョンは、ダウンロードしたZIP内の `manifest.json` の `packedAutocadYears` と bundle の `PackageContents.xml` で確認し、実行中のAutoCADで検証済みの年次バージョンはリリースノートで確認してください。ソースには2022–2027のシェルがありますが、同梱プラグインと実ホストの検証範囲はリリースごとに異なります。未同梱の年次バージョンは、対応するAutoCAD SDKでソースからビルドします。展開して `install.ps1` を実行してください（全文は英語 README）。

**NuGet（ゲートウェイのみ）:** `dotnet tool install --global Bimwright.Dwg.Server --version 2.1.2` は .NET 8 が必要です。AutoCAD プラグインは同じバージョンの Setup ZIP から別途インストールしてください。

**Claude Desktop:** 同じリリースの `dwg-mcp-desktop-2.1.2.mcpb` とチェックサムを取得し、Settings → Extensions → Advanced settings からインストールします。拡張機能は Setup 2.1.2 の特定の実行ファイルだけを起動し、ゲートウェイやプラグインをインストールしません。NuGet/PATH へのフォールバックはなく、override も同じ SHA-256 が必要です。

**開発者:** ビルド後 `pwsh scripts/install.ps1 -Version 2024`、または Debug DLL を `NETLOAD`。

### 3. MCP クライアントの接続設定

MCP クライアント設定（例: `.mcp.json`）に追加:

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

書き込み前に `dwg_list_available_targets`、次に `dwg_switch_target(target="pid:<PID>")` を呼びます。既定の `verify=true` はプロセスとアクティブ図面を確認し、`verify=false` は読み取りのみ許可します。`--target` は初期選択であり、書き込みを許可しません。複数候補は `AMBIGUOUS_TARGET` で拒否されます。

接続先が決まった後、別のインスタンスに切り替えるには `pid:<PID>` または `id:acad-<year>-<PID>` を指定します。稼働中の確認済みターゲットから離れる場合は `rebind=true` も必要です。同じプロセスを `verify=false` で再選択すると、書き込み確認は解除されます。以前の期待する図面のライフタイムIDは保持されますが、`rebind=true` でリセットするか、明示的な `expected_document_id` で置き換えることができます。図面を開く・切り替える操作の後は `dwg_health().target` を確認してください。別の図面を確認するには、その図面に一致する `expected_document_id` または `rebind=true` が必要です。検証失敗時は以前の接続先を保持し、古い接続先で待機中の呼び出しは送信前に拒否されます。書き込みには gateway と plugin の両方を更新する必要があります。

`--read-only` では `dwg_switch_target` はカタログに含まれず、直接呼び出しも拒否されます。`--read-only --target pid:<PID>`（または `--target id:acad-<year>-<PID>`）で gateway を起動し、health と現在のターゲットを確認してください。`TARGET_LOST` または `TARGET_CHANGED` の場合は `dwg_list_available_targets` を呼び、意図した明示的な `--target` で gateway を再起動します。接続先を失っても別のインスタンスには自動的に切り替わりません。

PID、またはインスタンスが一つだけの4桁年で初期ターゲットを選択:

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

`--read-only` で書き込み toolset を外します。`--toolsets all`、または必要な既定を**含めた**明示リストを使ってください（カスタムリストは既定セットを**置き換え**ます。例: `query,modify,meta,view,annotation`）。環境変数: `BIMWRIGHT_DWG_TOOLSETS=…`。

`dwg_send_code` は `meta` でデフォルト登録されます。`--enable-send-code` / `--disable-send-code` でサーバー側を切り替えます（JSON `enableSendCode`、環境変数 `BIMWRIGHT_DWG_ENABLE_SEND_CODE`）。無効な場合、C# を含むバッチは送信前に全体を拒否します。`MCPDISABLECODE` / `MCPENABLECODE` は AutoCAD セッション側だけを切り替え、サーバーの無効設定を上書きしません。`dwg_run_lisp` は互換性のため登録を維持しますが、サーバーとプラグインの両方ですべての要求を拒否します。このビルドには LISP の隔離実行環境やスクリプトの信頼を確立する仕組みがありません。`--read-only` は両ツールを除外します。

`dwg_send_code` はインラインの同期スニペットのみを受け付け、`async`/`await` と `#load` ディレクティブは実行前に拒否します。AutoCAD API は呼び出し元スレッドで使い、`Task.Run` や別スレッドへ移さないでください。戻り値は JSON DTO に限定し、ネストした AutoCAD/COM オブジェクトも拒否します。`dwg_inspect_lisp` は静的解析に利用できます。2,000,000 文字を超えるソースは完全検査不能として危険判定になり、200 件の表示上限に達しても危険度の検出は続きます。正規表現にはタイムアウトがあり、時間切れの検査は不完全かつ危険と報告し、`clean` は返しません。

---

コールログは**デフォルト無効**です。`--enable-call-log` / `--disable-call-log`（JSON `enableCallLog`、環境変数 `BIMWRIGHT_DWG_ENABLE_CALL_LOG`）で切り替えます。優先順位は JSON → 環境変数 → CLI、同じペアでは最後のフラグが優先されます。無効時はコールログ・ジャーナルを作成・追記せず、既存ログも削除しません。有効時の `%LOCALAPPDATA%\Bimwright\dwg-mcp\dwg-mcp-calls.jsonl` はマスク済みパラメーターと時間・成否メタデータのみを記録し、応答は記録しません。入れ子バッチを含む code フィールドは文字数と元の UTF-8 本文の SHA-256 のみ、ソースを含み得るコード関連エラー本文は省略します。コード本文キャッシュ・本文ジャーナルは未実装です。ToolBaker の適応的使用記録、診断ログ、メモリ内 History は別機能で、プラグインには永続コールログ writer がありません。

## アクティビティ通知と History

通知は既定で有効です。キーボードフォーカスを奪わない1枚のカードに `dwg-mcp <年>`、最新ツール名、**Success · Failed · Capture** を表示します。数字はオドメーター式に動き、画像は固定フレームの中央に表示され、最低5秒保持してクロスフェードします。Windows の **Animation effects** が無効なら即時更新します。ホバー中は閉じず、アイドル時間は10/20/30/60秒（既定20秒）。AutoCAD が最小化中またはモーダル状態ならカウントを保持して後で表示します。`MCPTOAST` で切り替え、`MCPSETTINGS` で設定します。Branding は既定で無効、ホバー時のみ表示し、再起動後も設定を保持します。保存失敗を表示してもセッション内の選択は有効です。

カード本体をクリックすると **History** を開く／フォーカスしてカードを閉じます。×は閉じるだけ、サムネイルは許可された画像を開いてカードを閉じます。`MCPHISTORY` も同じモデルレスウィンドウを開きます。プラグイン処理済み呼び出しの最新1,000件の簡潔な結果をメモリに保持し、通知が無効でも検索、Success/Failed・Read/Write フィルター、実行時間、選択詳細を利用できます。カードを閉じても履歴は残り、**New session** で消去します。永続ログを追加せず、生の入力、コード本文、JSON 結果は保持しません。サーバーのみのツール通知はまだ含みません。設定キー `enableToast`、`toastIdleSeconds`、`showBranding` は `%LOCALAPPDATA%\Bimwright\dwg-mcp\settings.json` に保存します。

---

2.1.2 では `dwg_send_code(code, data?, compile_only=false)` が JSON 入力をソースと分離します。`data` は Newtonsoft `JToken` で、object/array/scalar を受け付けます（UTF-8 最大 512 KiB、深さ 64、重複キーと非有限数は禁止、null は省略扱い）。日付文字列は文字列のままです。`compile_only=true` は文を実行せず、stdout も生成しません。確認済みターゲットとアクティブな図面は必要です。内部の `result.ok`、`compiled`、`executed`、`diagnostics`（元ソースの行/列は 1 始まり）と任意の runtime `location` を確認してください。対応する gateway/plugin を同時に更新し、data/compile_only 付きの呼び出しは batch/baked tool 外で直接実行します。旧 plugin はコード送信前に拒否され、code のみの呼び出しは従来どおり利用できます。入力値は call log に保存されず、History には長さと SHA-256 のみが残ります。

## Health と更新確認（v2.1.1）

`dwg_health()` は既定でオフライン・読み取り専用です。セッション開始時に対象を選び、`dwg_health(check_updates=true)` を一度実行します。図面を要求・ロックせず、host/document 状態とロード済み server/plugin の version/build/alignment を返します。接続先がなくても gateway identity を保持します。旧 plugin の `HEALTH_UNSUPPORTED` や不足した identity は未確認であり、破損とはみなしません。UI がビジーでも plugin identity は保持されます。操作 toast や drawing batch の対象外です。

GitHub 確認は同じ stable release/tag に両 component を照合し、共有 5 秒・metadata サイズ制限を使います。`checked` は lookup の成功だけを示し、artifact の信頼性を証明しません。disk SHA-256 は capture 時点の観測で、ロード済み memory の証明ではありません。欠けた根拠は `unknown` のままです。

両 `update.components`、notification 全文、`lookup_reason`、`protected_component_reasons`、`unobserved_components` を確認します。`UPDATE_CHECK_UNAVAILABLE` は外部 lookup の失敗であり runtime 障害ではありません。`LOCAL_VERSION_AHEAD` は情報通知で、downgrade しません。`prepare_update` と `bundle_update_proposal_allowed=true` の場合だけ更新提案を行い、インストールには承認が必要です。health は package の取得・インストール、client config の変更、図面の save/close、process restart を行いません。

reconnect 後、同じ接続の新しい initialize と `tools/list` の `_meta["bimwright/dwg-mcp/runtime"]`・schema・annotations を `client_verification` と比較します。server 自体は client cache refresh を証明できません。read-only でも利用可能で、GitHub option のため `openWorldHint=true` です。[agent guide](AGENTS.md)。v2.1.1 で利用できます。gateway と plugin は同じリリースから更新してください。CI は AutoCAD 2022–2027 の全6プラグインをビルド済みです。検証範囲はリリースノートを参照してください。

## ツール

2.1.2 のデフォルト起動では45のツール（クエリ（`dwg_get_drawing_profile` と `dwg_inspect_lisp` 含む）、変更、メタ（`dwg_send_code`/`dwg_run_lisp` 含む）、ビュー、およびデフォルト有効の `dwg_capture_view_image`）が公開されます。オプショナルのToolBaker、注釈、ブロック、寸法、エクスポート、作図ツールセットは `--toolsets` で有効にでき、MCPサーフェス全体は69ツールになります。 登録数には拒否のみを返す `dwg_run_lisp` が含まれます。スキャン判定は実行を許可しません。

一般的なCADツールは、選択されたAutoCADターゲットの現在アクティブなドキュメントに対して動作します。エンティティ入力と返されるエンティティIDは、`7F5AD` のようなAutoCAD 16進ハンドルを使用します。作成、コピー、オフセット、および変更の応答は、生成または変更されたエンティティを16進ハンドルで識別します。

従来のquery/countパラメータだけの呼び出しはモデル空間を走査します。従来queryのパラメータ順、返却フィールド、列挙順、`include_geometry` 出力は維持され、limitは既定500、1–5000にクランプされます。`dwg_select_by_layer` と `dwg_select_by_type` は引き続きモデル空間のハンドル一覧を返し、pickfirst選択は変更しません。四つのtypeフィルターは実際のmanaged短縮名・完全名を大文字小文字を区別せず照合し、`TEXT` → `DBText`、`INSERT` → `BlockReference`、`LWPOLYLINE` → `Polyline` を追加します。`POLYLINE` から追加のDXF型を推測しません。

大きな図面の読み取りは `dwg_get_drawing_profile()`（`section="summary"`、`offset=0`）から始めます。既定ではモデル空間と全レイアウトの最上位エンティティを走査し、ブロック定義やxrefの内容は展開しません。ブロック参照一つを一つのエンティティとして数えます。任意の第5引数 `space` は `all`（既定）、レイアウト定義を読まない `model`、または一つの正確な名前を指定する `layout:<name>` を選びます。合計を使う前にsource、scope、完全性を確認してください。typeは正確なランタイム完全名で分類し、layerとspaceも個別に集計します。観測済み・不完全な件数や取得不能値は明示するか `null` を返し、完全な合計とは扱いません。

`output.next_calls` に従い `types`、`layers`、`spaces` をページ取得します。提案には `section`、`offset`、`expected_snapshot`、`max_output_bytes`、`space` が含まれます。最初のページでは `expected_snapshot` は省略可能ですが、`offset>0` では必須です。snapshotが変わると `PROFILE_CHANGED` を返すのでsummaryからやり直します。別の `document_id` も結果と一緒に保持してください。各ページで図面を再走査し、キャッシュは使いません。`max_output_bytes` の既定は16384（4096–65536）で、グローバルresponse budgetとともに最終MCP結果のシリアライズ後サイズを制限します。走査時間の制限ではありません。snapshotはscopeを固定し、modelのquery/count/profileはviewportだけの変更を無視しますが、all・名前付きlayoutのprofileとlayout queryはそれも検出します。scopeを変える場合は古い `expected_snapshot` を省略してsummaryを再取得します。scope指定のprofile応答には `scope.requested_space` が必要で、scopeを無視する古いpluginは `PROFILE_SCOPE_UNSUPPORTED` で拒否されます。

| ツール | 目的 |
|------|---------|
| `dwg_health` | host/runtime identity と任意の stable release 確認。既定はオフライン・読み取り専用 |
| `dwg_get_drawing_info` | 現在の図面名、現在の画層、現在のスペース/レイアウト、単位スカラーを読み取る |
| `dwg_get_drawing_profile` | all（既定）・modelのみ・名前付きlayoutの最上位entityを読み取り専用で集計。scope付きsnapshotと最終responseバイト上限 |
| `dwg_get_entity_properties` | AutoCAD 16進ハンドルで識別されるエンティティのプロパティを読み取る |
| `dwg_list_layers` | 現在の図面の画層を色と状態フラグとともに一覧表示 |
| `dwg_query_entities` | 従来のモデル空間query、または範囲・フィールドを指定したページ取得。署名付き継続と最終応答のバイト上限 |
| `dwg_count_entities` | type/layer/color、任意のWCS extentsと名前付きspaceで集計し、判定不能のcoverageを明示 |
| `dwg_select_by_layer` | 1つの画層のモデル空間エンティティハンドルリストを返す（ピックファースト選択は変更しない） |
| `dwg_select_by_type` | 1つのエンティティタイプのモデル空間エンティティハンドルリストを返す（ピックファースト選択は変更しない） |
| `dwg_get_selected_texts` | 従来pickfirst選択の書き戻し用グループ、またはhandles/bboxソースから読み取り専用クラスターをページ取得 |
| `dwg_update_texts` | 1つのトランザクションでハンドル指定による新しいテキストを書き込む |
| `dwg_create_layer` | 既存の画層のプロパティを上書きせずに画層を作成する |
| `dwg_create_line` | 現在の図面空間に1本の線を作成 |
| `dwg_create_circle` | 現在の図面空間に1つの円を作成 |
| `dwg_attach_image` | リンクされたラスター画像（PNG/JPG/TIFF/BMP）を正確な位置とサイズで貼り付け |
| `dwg_create_point` | 1つの点を作成し、その16進ハンドルを返す |
| `dwg_create_polyline` | 頂点から軽量ポリラインを作成し、その16進ハンドルを返す |
| `dwg_create_rectangle` | 長方形ポリラインを作成し、その16進ハンドルを返す |
| `dwg_create_arc` | 1つの円弧を作成し、その16進ハンドルを返す |
| `dwg_create_ellipse` | 1つの楕円を作成し、その16進ハンドルを返す |
| `dwg_change_layer` | 16進ハンドルで識別されるエンティティを別の画層に移動 |
| `dwg_change_color` | AutoCADカラーインデックスでエンティティの色を変更 |
| `dwg_move_entities` | 16進ハンドルで識別されるエンティティを変位ベクトルで移動 |
| `dwg_rotate_entities` | 16進ハンドルで識別されるエンティティを基点の周りで回転 |
| `dwg_scale_entities` | 16進ハンドルで識別されるエンティティを基点の周りで拡大縮小 |
| `dwg_copy_entities` | 16進ハンドルで識別されるエンティティをコピーし、コピーされたハンドルを返す |
| `dwg_erase_entities` | 16進ハンドルで識別されるエンティティを削除 |
| `dwg_offset_entities` | 曲線エンティティをオフセットし、生成されたハンドルを返す |
| `dwg_translate_and_rewrite` | **推奨。** 翻訳テキストを書き戻す: アンカー、削除、MText、フォント、高さ |
| `dwg_apply_unicode_style` | `Bimwright_Unicode` スタイルが存在することを確認し、ターゲットに適用 |
| `dwg_collapse_and_rewrite` | 明示的な形状制御による低レベル書き換えプリミティブ |
| `dwg_list_available_targets` | PID、ターゲットID、プロセス開始時刻付きで各インスタンスを表示 |
| `dwg_get_current_target` | バインド状態、世代、PID、確認済み図面を表示 |
| `dwg_switch_target` | インスタンスを選択・確認。別のインスタンスには `pid:`/`id:`、稼働中の確認済みターゲットから離れるには `rebind=true` も必要。読み取り専用モードでは除外 |
| `dwg_list_recent_drawings` | 接続中の AutoCAD の最近の DWG を最大50件取得、名前で絞り込み可能；図面不要 |
| `dwg_open_drawing` | 絶対パスの DWG を開く、読み取り専用を指定可能；既存図面を再利用し保存・閉じる操作なし；既定600秒（[詳細](README.md#drawing-session-tools)） |
| `dwg_report_task_result` | タスク報告（結果、要約、`dwg:<handle>` エンティティリンク付き Markdown 本文）を AutoCAD ユーザー向けのローカルファイルとして保存；図面は変更しない |
| `dwg_batch_execute` | 複数の内部ワイヤーコマンドを論理バッチとして実行 |
| `dwg_send_code` | AutoCAD .NET API でC#スニペットを実行（globals `doc`/`db`/`ed`、`return` 値 + stdout キャプチャ、30秒の協調キャンセル。`MCPDISABLECODE`/`MCPENABLECODE` でセッション単位に切替） |
| `dwg_run_lisp` | 互換性用エンドポイント。すべての `file`/`code`/`command` を `lisp_execution_blocked` で拒否します。ファイル読込、プラグインへの送信、実行は行いません。静的解析には `dwg_inspect_lisp` を使用してください。 |
| `dwg_inspect_lisp` | `.lsp` ファイルやインライン AutoLISP の静的スキャン — プロセス実行（`startapp`/`shell`）、危険な COM（`WScript.Shell`/`XMLHTTP`）、永続化（`acaddoc.lsp`、レジストリ書込）、ファイル削除、二段階 `(load …)`、難読化（`eval`/`read`）を検出。`verdict` clean/caution/dangerous + findings を返す。純粋なローカル解析 — 実行せず AutoCAD 不要、`--read-only` でも利用可。 `clean` は安全性を保証しません。レポートは `execution_authorized=false` と `safety_assured=false` を返します。 |
| `dwg_zoom_extents` | 図面ビューポートの範囲にズーム |
| `dwg_zoom_window` | 2つのコーナー点で定義されたウィンドウにビューポートをズーム |
| `dwg_zoom_to_entity` | ハンドルで識別される特定の図面エンティティの範囲にビューポートをズーム |
| `dwg_capture_view_image` | AutoCAD内で図面グラフィックスをキャプチャし、インライン画像・画像パス・実サイズ・SHA-256ハッシュ・図面/ビューポート/カメラのメタデータを返す（既定オン；パスポリシー適用） |
| `dwg_inspect_view_region` | キャプチャ上の正規化矩形を指定し、ズーム・再生成後の新しい画像と親画像IDを返す |
| `dwg_restore_view` | 以前のキャプチャのカメラへ戻り、新しい画像を取得する |

図面の読み取り: キャプチャ → 画像を確認 → 領域を選択 → `dwg_inspect_view_region` → 詳細を確認。全体へ戻るには親IDを保存して `dwg_restore_view` を使用します。履歴はAutoCADプロセス内で最大64件・30分です。初期対応はモデル空間の単一ビューポート、真上からの正投影、回転なしに限定されます。古い画像や非対応ビューは拒否されます。応答には既存JSONに加えてMCP画像ブロック（最大8 MiB）が含まれます。カメラの変更と画像保存を行うため `--read-only` では利用できませんが、図形編集やDWG保存は行いません。インストール済みホストでの受入検証は未完了です。上記のワークフローを参照。

**範囲指定とページ化されたentity読み取り**は既存の `dwg_query_entities` と `dwg_count_entities` の拡張で、新しいMCPツールは増えません。`bbox`、`bbox_mode`、`space`、`fields`、`cursor`、`max_output_bytes` のいずれかを指定するとページ化されます。フィルターなしなら `paged=true` を使います。`paged=false` だけなら従来動作を維持します。従来の五つのパラメータは先頭に維持されます。新オプションと `include_geometry=true` は併用せず、`fields` で選びます。

| 末尾に追加するqueryオプション | 意味 |
|---|---|
| `paged=false` | trueにして他の新オプションなしでページ化を有効化 |
| `bbox=null` | 有限でmin ≤ maxのWCS座標を含むJSON文字列 `{"min":[x,y,z],"max":[x,y,z]}` |
| `bbox_mode=null` | box指定時は既定 `intersects`、または `contains`。`bbox` が必要 |
| `space=null` | 既定 `model`、または `layout:<表示LayoutName>` を大文字小文字を区別せず完全一致。profileの名前を使用 |
| `fields=null` | JSON配列文字列。既定 `handle`、`type`、`layer`。許可項目は `handle`、`type`、`runtime_type`、`layer`、`color_index`、`text`、`position`、`extents`。`handle` は常に含む |
| `cursor=null` | 前のページから受け取る継続トークン |
| `max_output_bytes=null` | 最終MCP結果の上限。既定16384バイト、4096–65536。小さいグローバルbudgetも適用 |

`bbox` は接触境界を含むWCS軸平行3D `GeometricExtents` の比較で、厳密な図形交差判定ではありません。座標は図面単位で、INSUNITS換算は行いません。結果にINSUNITSコード・名前を含み、コード0はunitless/unknownのままです。選んだモデル空間またはレイアウトの最上位entityだけを走査し、block/xref/viewportの表示内容を展開せず、別spaceへフォールバックしません。`dwg_count_entities` は従来三つのパラメータに `bbox`、`bbox_mode`、`space` だけを追加し、範囲metadataと判定不能coverageを返します。範囲内のハンドル一覧はqueryの `fields="[\"handle\"]"` を使い、selectの契約は維持します。

ページはAutoCADハンドルの数値順です。`result.output.next_calls` のfilter/fieldsを保って呼び出してください。署名付きhost-session cursorは図面ライフタイム、revision、最後に**返却された**ハンドルに結び付きます。`limit` とbyte capは変更可能ですが、filter/fieldsを変える場合はcursorを省略して最初から始めます。不正・改変・図面変更後のcursorは拒否します。各ページでspaceを再走査し、キャッシュは使いません。byte capは出力量だけを制限し、走査時間は制限しません。`result.output` はbudget、`omitted_matching`、`has_more`、`next_cursor`、`next_calls`、行が入らない場合の案内を返します。読み取り・filter・必要extentsの失敗はcoverageに数え、`exact_match_count=null` と観測済み `matched_count` を返し、不明ハンドルを飛ばす継続は発行しません。entityが対応しないpropertyは `null` と `unavailable_fields` で示します。拡張query/countは直接呼び出しのみで、gatewayとpluginのbatch事前検査が前の項目を実行する前に全体を拒否します。従来パラメータだけのquery/count batchは維持し、baked queryは引き続き許可しません。拡張読み取りにはgatewayとpluginの両方の対応が必要で、未対応の古いpluginに対してgatewayが `QUERY_FEATURE_UNSUPPORTED` を返し、従来形式の出力を破棄しますが、従来パラメータだけの呼び出しは利用できます。

**手動選択なしのテキスト読み取り**は既存の `dwg_get_selected_texts` に明示的なソースを指定します。元の `groupingStrength="normal"`、`includeEntities=false` は先頭に維持され、この二つだけなら従来の選択・書き戻しフローです。新オプションを使う場合は `handles` と `bbox` のどちらか一つが必須で、選択ソースへ暗黙に戻りません。

| 追加するtext-readオプション | 意味 |
|---|---|
| `handles=null` | 16進ハンドルのJSON配列文字列。入力は最大5000件で重複を除去、`"[]"` は有効な空ソース |
| `bbox=null` | JSONオブジェクト文字列 `{"min":[x,y,z],"max":[x,y,z]}`。空間queryと同じWCS・図面単位のextents条件 |
| `bbox_mode=null` | box指定時は既定 `intersects`、または `contains`。`bbox` が必要 |
| `space=null` | 既定 `model`、または大文字小文字を区別せず完全一致の `layout:<name>`。別spaceへ戻らない |
| `cursor=null` | `output.next_calls` から受け取る署名付きtext-cluster継続 |
| `max_output_bytes=null` | 最終MCP結果の上限。既定16384バイト、4096–65536、グローバルbudgetも適用 |

新ソースは最上位で直接見つかるDBText、MText、MLeader、属性参照・定数属性定義だけを読み、BlockReference、入れ子block、xrefは展開しません。従来選択の一段block処理は維持します。modelソースのsnapshotはviewportだけの変更を無視し、layoutソースは検出します。完全ソースは最大5000テキストrecord・合計8 MiBのUTF-8 textです。不完全なソースはcoverageを示し、クラスターやcursorを返しません。`TEXT_SOURCE_TOO_LARGE` ではソースを絞ります。既存の空間クラスタリングは変更せず、完全な受理ソース全体を処理してから安定順にクラスター単位でページ化します。

`result.output.next_calls` のMCP camelCase引数 `groupingStrength`、`includeEntities` を保持してください。cursorはdocument/scope revision、ソース、grouping設定とtext専用のクラスター序数に結び付き、entity handleではありません。byte fittingはクラスターを丸ごと外し、返却済みクラスターまでしか進めません。`omitted_clusters`、blockedの説明、継続情報を確認してください。大きすぎるクラスターはcapを最大65536まで上げ（制限中のglobal budgetも調整）、または `includeEntities=false`、弱いgrouping、狭いソースを使います。source/grouping/includeEntitiesを変える場合はcursorなしでやり直します。

新ソース結果は `read_only=true`、`writeback_ready=false` で、従来の数値IDや `rewrite_mode` の代わりにopaqueな `cluster_key` を返します。新ソース読み取りは既存のrewriteグループを保持し、CAD形状や選択も変えません。エンティティ単位で翻訳する場合は `includeEntities=true` で読み取り、handle指定の `dwg_update_texts` で書き込みます。`dwg_translate_and_rewrite` には引き続き**手動選択**読み取りが必要で、この段階では新ソースページからクラスタ単位で書き戻せません。新ソース読み取りは直接呼び出しのみで、gateway/pluginがbatch全体と拡張baked呼び出しを実行前に拒否します。従来選択batchは維持します。古いpluginがソースを扱えない場合、gatewayは `TEXT_FEATURE_UNSUPPORTED` を返して選択payloadを破棄し、選択へフォールバックしません。

オプショナルのToolBakerツールは、`toolbaker` ツールセットが有効な場合に公開されます:

| ツール | 目的 |
|------|---------|
| `dwg_list_baked_tools` | サーバー管理のSQLiteレジストリから承認済みベイクドツールを一覧表示 |
| `dwg_run_baked_tool` | 名前を指定して承認済みベイクドツールを実行 |
| `dwg_list_bake_suggestions` | 検出された繰り返しワークフローの提案を一覧表示 |
| `dwg_accept_bake_suggestion` | 提案を検証、スモークテスト、承認 |
| `dwg_dismiss_bake_suggestion` | 提案を却下または抑制 |
| `dwg_create_bake_issue_draft` | 提案のGitHub Issue下書きを生成（送信はしない） |

オプショナルの注釈ツールは、`annotation` ツールセットが有効な場合に公開されます:

| ツール | 目的 |
|------|---------|
| `dwg_create_text` | ターゲット高さ、回転、プロパティを指定して単行テキスト（DBText）を作成 |
| `dwg_create_mtext` | 書式と幅を指定して複数行テキスト（MText）を作成 |
| `dwg_create_leader` | マルチリーダー（MLeader）をオプションのリーダーテキスト付きで作成 |
| `dwg_create_table` | 指定された行/列のテキスト内容でAutoCADテーブルを作成 |

オプショナルのブロックツールは、`block` ツールセットが有効な場合に公開されます:

| ツール | 目的 |
|------|---------|
| `dwg_list_blocks` | 現在の図面のブロック定義を一覧表示（読み取り専用で安全） |
| `dwg_get_block_attributes` | ハンドルでブロック参照の属性を読み取る（読み取り専用で安全） |
| `dwg_insert_block` | ブロック参照を挿入、オプションで外部DWGからインポート |
| `dwg_set_block_attributes` | ハンドルでブロック参照の属性を設定 |
| `dwg_explode_block` | ブロック参照を分解し、生成されたパーツのハンドルを返す |

オプショナルの寸法ツールは、`dimension` ツールセットが有効な場合に公開されます:

| ツール | 目的 |
|------|---------|
| `dwg_create_linear_dimension` | 回転角度指定で回転 linear 寸法を作成 |
| `dwg_create_aligned_dimension` | 2点間の平行寸法を作成 |
| `dwg_create_radial_dimension` | 円または円弧の半径寸法を作成 |
| `dwg_create_diameter_dimension` | 円または円弧の直径寸法を作成 |

オプショナルのエクスポートツールは、`export` ツールセットが有効な場合に公開されます:

| ツール | 目的 |
|------|---------|
| `dwg_export_dxf` | 図面をDXFファイルにエクスポート（出力パスポリシーで保護） |

オプショナルの作図ツールは、`drawing` ツールセットが有効な場合に公開されます:

| ツール | 目的 |
|------|---------|
| `dwg_get_variables` | 図面システム変数の現在値を読み取る |
| `dwg_set_system_variable` | 図面システム変数の値を設定 |
| `dwg_save_drawing` | 現在の図面をファイルに保存（confirm=true が必要） |
| `dwg_purge_drawing` | 未使用の名前付きオブジェクト（ブロック、画層、スタイル）をパージ（dry_run=true 対応、実際のパージには confirm=true が必要） |

### 出力パスポリシー
すべてのエクスポート操作は、以下のポリシーによって厳格に保護されます:
- 出力パスは絶対パスである必要があります。
- ファイル拡張子は特定のツールと一致する必要があります（例: DXFエクスポートには `.dxf`）。
- `overwrite_existing=true` が明示的に指定されない限り、既存ファイルは上書きされません。
- `allow_repo_output=true` が設定されていない限り、リポジトリルートディレクトリへの書き込みは拒否されます。

### オプショナルツールセットと読み取り専用動作

デフォルトでは、`query`、`modify`、`meta`、`view` ツールセットのみが有効です。`--toolsets` フラグ（例: `--toolsets all` または `--toolsets query,modify,meta,view,annotation,block,dimension,export,drawing`）を使用して他のツールセットをオプトインできます。

- **読み取り専用モード（`--read-only`）**: 読み取り専用モードがアクティブな場合、書き込み可能なすべてのツールセット（`modify`、`code`、`annotation`、`dimension`、`export`、`drawing` の書き込みツール）は完全に無効化されます。
- **ブロックツールセットの分割**: `block` ツールセットは読み取り専用ツールと書き込み可能ツールに分割されます。`--read-only` がアクティブな場合、`dwg_list_blocks` と `dwg_get_block_attributes` は引き続き使用可能ですが（安全な読み取り検査）、変更/作成ツール（`dwg_insert_block`、`dwg_set_block_attributes`、`dwg_explode_block`）は削除されます。
- **ビューと読み取り専用**: 読み取り専用では一時的なズーム操作のみ残ります。`dwg_capture_view_image`、`dwg_inspect_view_region`、`dwg_restore_view` は画像ファイルを作成するため除外されます。
- **作図操作と読み取り専用**: `drawing` ツールセットは、読み取り専用モードで `dwg_get_variables` を維持しますが、`dwg_set_system_variable`、`dwg_save_drawing`、`dwg_purge_drawing` は削除されます。
- **延期された角度寸法**: 現在サポートされているのは linear、平行、半径、直径寸法タイプのみです。角度寸法は延期されており、まだ実装されていません。
- **ネイティブビューキャプチャ**: `dwg_capture_view_image` は AutoCAD の `Document.CapturePreviewImage` API を使用し、デスクトップ自動化や DWG 保存を行いません。コマンド実行中やキャプチャ中の図面/カメラ変更は拒否されます。オプションの `expected_document_fingerprint` で後続キャプチャを既存結果にバインド可能。読み取りアトラス用に返されたメタデータは画像と共に保存してください。ツールが書き込むのは画像のみです。実際の出力サイズは要求値とわずかに異なる場合があり、カメラメタデータはアクティブビューポートを示します（タイルドビューやペーパーレイアウトの汎用ピクセル/ワールド変換ではありません）。上記のワークフロー を参照。
- **延期されたファイルエクスポートツール**: `dwg_export_pdf` と `dwg_export_image` は延期されたままです。ビューポートキャプチャはプロットの設定・検証を行いません。

### 手動スモークチェックリスト

スクラッチDWGで:

1. `dwg_get_drawing_info` を実行。
2. `dwg_list_layers` を実行。
3. `dwg_create_layer` で `BIMWRIGHT_TEST` を作成。
4. `dwg_create_point`、`dwg_create_polyline`、`dwg_create_rectangle`、`dwg_create_arc`、`dwg_create_ellipse` で `BIMWRIGHT_TEST` 上に点、ポリライン、長方形、円弧、楕円を作成。返された16進ハンドルを記録し、1つの曲線（円弧または楕円など）を色とオフセットの確認用に予約。
5. `dwg_query_entities`、`dwg_count_entities`、`dwg_select_by_layer`、`dwg_select_by_type` でそれらのエンティティを画層とタイプでクエリ、カウント、選択。選択ツールがハンドルリストを返し、ピックファースト選択を変更しないことを確認。
6. `dwg_move_entities`、`dwg_rotate_entities`、`dwg_scale_entities` で予約外のスクラッチエンティティを移動、回転、拡大縮小。
7. `dwg_copy_entities` で予約外のスクラッチエンティティを1つコピーし、その後 `dwg_erase_entities` でその使い捨てのコピー一時エンティティのみを削除。
8. `dwg_change_color` で予約済み曲線の色を変更し、`dwg_offset_entities` でその曲線をオフセットし、返された生成ハンドルが16進ハンドルであることを確認。
9. 既存のテキスト翻訳ワークフローが引き続き機能することを確認: スクラッチテキストを選択、`dwg_get_selected_texts` を実行、`dwg_translate_and_rewrite` で書き換え。

### 任意スモーク — annotation / block / dimension

先にオプション toolset を有効化（`--toolsets …` または `--toolsets all`）。スクラッチ DWG で:

1. `dwg_create_text`、`dwg_create_mtext`、`dwg_create_leader`、`dwg_create_table` でスクラッチDWGにテキスト、mtext、リーダー、テーブルを作成。
2. `dwg_list_blocks` でブロック定義を一覧表示。
3. `dwg_insert_block` で図面から、または絶対外部DWGパスから既知のブロックを挿入。
4. `dwg_get_block_attributes` と `dwg_set_block_attributes` でブロック属性を取得・設定。
5. `dwg_explode_block` でブロック参照を分解。
6. `dwg_create_linear_dimension`、`dwg_create_aligned_dimension`、`dwg_create_radial_dimension`、`dwg_create_diameter_dimension` で linear、平行、半径、直径寸法を作成し、linear投影距離の検証が期待通りに成功/失敗することを確認。

### 任意スモーク — view / export / drawing

`view` / `export` / `drawing` toolset を必要に応じて有効化したうえで:

1. `dwg_zoom_extents` を実行。
2. 座標を指定して `dwg_zoom_window` を実行。
3. 記録した16進ハンドルを使用して `dwg_zoom_to_entity` でエンティティにズーム。
4. `dwg_get_variables` で作図変数を読み取り。
5. `dwg_export_dxf` で図面をDXFにエクスポート。
6. `dry_run=true` で `dwg_purge_drawing` を実行し、その後 `confirm=true` で実行（コピーした使い捨てDWGのみ）。
7. `confirm=true` で `dwg_save_drawing` を実行（コピーした使い捨てDWGのみ）。

### 0.1.x ツール名からの移行

MCPツール名は現在 `dwg_` プレフィックスを使用しています。Rawプラグインコマンド名は内部ワイヤーコマンドのままです。

| 0.1.x MCP名 | 1.0 MCP名 |
|----------------|--------------|
| `get_selected_texts` | `dwg_get_selected_texts` |
| `update_texts` | `dwg_update_texts` |
| `translate_and_rewrite` | `dwg_translate_and_rewrite` |
| `apply_unicode_style` | `dwg_apply_unicode_style` |
| `collapse_and_rewrite` | `dwg_collapse_and_rewrite` |
| `send_code` | `dwg_send_code` |
| `run_lisp` | `dwg_run_lisp` |

---

## 標準ワークフロー

この書き戻しフローは手動選択と元の `groupingStrength`/`includeEntities` だけを使います。読み取り専用ソースの `cluster_key` は数値IDの代わりに使えません。

```
1. ユーザーがAutoCADでテキストエンティティを選択
2. エージェントが dwg_get_selected_texts を呼び出す → クラスタリングされたテキストグループを受信
3. エージェントが各クラスターを翻訳
4. エージェントが dwg_translate_and_rewrite([{id, new_text}, ...]) を呼び出す
   ツールが処理: アンカー、削除、MText、フォントスタイル、高さ。完了。
5. 必要に応じてユーザーが REGEN を実行
```

rewrite成功時の `final_text_height` は、Unicode正規化と最終scaleの後、rewrite transaction内で `new_handle` のentityから読み取った実際の高さpropertyです。取得できない場合は `null` です。`style_only` はanchorの値であり、他のグループ要素の高さは異なる場合があります。block definition内のrewriteはdefinitionのローカル単位で高さを返し、world表示上の高さではありません。

エージェントの視点からは、読み取り、書き込みの2ステップです。

---

## サポート対象AutoCADバージョン

| バージョン | ObjectARX リリース | プラグイン TFM | ステータス |
|---------|-------------------|------------|--------|
| AutoCAD 2022 | 24.1 | `net48` | シェル足場済み; Autodeskの`AutoCAD.NET`パッケージでビルド |
| AutoCAD 2023 | 24.2 | `net48` | シェル足場済み; Autodeskの`AutoCAD.NET`パッケージでビルド |
| AutoCAD 2024 | 24.3 | `net48` | デフォルトサポート対象シェルおよび通常のソリューションビルド |
| AutoCAD 2025 | 25.0 | `net8.0-windows` | シェル足場済み; Autodeskの`AutoCAD.NET`パッケージでビルド |
| AutoCAD 2026 | 25.1 | `net8.0-windows` | シェル足場済み; 2025とバイナリ互換だが独立したシェルとしてビルド |
| AutoCAD 2027 | 26.0 | `net10.0-windows` | シェル足場済み; 2025/2026とはバイナリ非互換 |

すべてのシェルはnuget.org上のAutodeskの`AutoCAD.NET`参照パッケージに対してコンパイルされるため、.NET 8と.NET 10のSDKがあるマシンなら、AutoCADをインストールせずに6年分すべてをビルドできます。パッケージはコンパイル時のみ使用され、AutodeskのDLLがプラグインの出力やSetup ZIPにコピーされることはありません。ビルドが成功しても、その年のAutoCADでプラグインが動作することの証明にはなりません。

---

## セキュリティ

`dwg_send_code` は AutoCAD とファイルシステムへの完全な権限で C# を実行します。信頼できるエージェント専用であり、サンドボックスではありません。`dwg_run_lisp` は入力、判定、`MCPENABLECODE` に関係なく拒否されます。ラッパーの作成やキューへの登録は行わず、trusted paths や SECURELOAD も変更しません。C#、batch、ToolBaker で拒否を迂回しないでください。`dwg_inspect_lisp` は静的解析であり、ウイルス対策やマシン全体の保護ではありません。`clean` は既知のパターンが未検出という意味です。全レポートは `execution_authorized=false` と `safety_assured=false` を返します。適用にはサーバーとプラグインの更新および再起動が必要です。旧バイナリの動作は変わりません。

セキュリティモデルは以下に依存しています:

- **ローカルのみのトランスポート** — AutoCAD 2022–2024 は127.0.0.1上のTCP、2025–2027 はループバック名前付きパイプ、リモートアクセス不可。
- **セッションごとの認証トークン** — プラグイン起動ごとにローテーションされ、リクエストごとに検証。
- **セッションキルスイッチ** — `MCPDISABLECODE` / `MCPENABLECODE` は `dwg_send_code` を制御します。コードを有効化しても、リスナーを再起動しても LISP は拒否されます。
- **タイムアウト境界** — スクリプトはドキュメントロックを保持するスレッド上でインライン実行され、30秒の協調キャンセルが適用されます。
- **信頼できるエージェントの前提** — 自分が制御するMCPクライアントとのみ使用してください。

プラグインポートをネットワークに公開しないでください。

---

## プロジェクト構造

```
dwg-mcp/
├── src/
│   ├── Bimwright.Dwg.sln
│   ├── server/            # .NET 8 MCP サーバー (グローバルツール)
│   ├── shared/            # ハンドラー、クラスタリング、書き換え、Unicode
│   ├── plugin-acad22/     # AutoCAD 2022 シェル (.NET 4.8)
│   ├── plugin-acad23/     # AutoCAD 2023 シェル (.NET 4.8)
│   ├── plugin-acad24/     # AutoCAD 2024 シェル (.NET 4.8)
│   ├── plugin-acad25/     # AutoCAD 2025 シェル (.NET 8)
│   ├── plugin-acad26/     # AutoCAD 2026 シェル (.NET 8)
│   └── plugin-acad27/     # AutoCAD 2027 シェル (.NET 10)
├── tests/                 # xUnit
├── scripts/               # インストール/アンインストール PowerShell
├── lib/acad24/            # 注釈のみ; Autodesk DLLは決してコミットされない
└── .github/workflows/     # CI
```

---

## bimwright ファミリー

AI アシスタントと BIM・CAD アプリケーションをつなぐオープンソースのツール。

**bimwright** は **BIM** と **wright** を組み合わせた名前です。wright は、ものを作る人や建てる人を表す古い英語で、*shipwright*（船大工）などに使われます。

- [**rvt-mcp**](https://github.com/bimwright/rvt-mcp) — Autodesk® Revit®
- [**dwg-mcp**](https://github.com/bimwright/dwg-mcp) — Autodesk® AutoCAD®
- [**nwd-mcp**](https://github.com/bimwright/nwd-mcp) — Autodesk® Navisworks®
- [**ipt-mcp**](https://github.com/bimwright/ipt-mcp) — Autodesk® Inventor®
- [**bim-wiki**](https://github.com/bimwright/bim-wiki) — ベトナム語ファーストのBIM知識ベース

---

## 免責事項

AutoCAD および Autodesk は Autodesk, Inc. の登録商標です。bimwright は独立したオープンソースプロジェクトであり、Autodesk, Inc. とは提携、スポンサー、または推奨関係にありません。

---

## ライセンス

[Apache License 2.0](LICENSE)

サードパーティ通知: [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)

## 権限と自動モード

`dwg_send_code` 以外の各ツールは read-only、destructive、idempotent、open-world の各属性を明示します。通常の全ツールモードは69ツール、既定の読み取り専用モードは17ツール、`--toolsets all --read-only` は以下の23読み取り専用ツールです。ズームは一時的なUI状態を変更できます。ターゲット切り替えは読み取り専用モードから除外されます。キャプチャ、領域検査、ビュー復元も画像ファイルを作成するため除外されます。`dwg_health` は読み取り専用ですが、明示的な更新チェックではGitHubに接続できます。

許可するツールだけに正確なルールを設定してください。`mcp__dwg-mcp__*` のワイルドカードは図面編集、ファイル書き込み、コード実行も許可するため避けてください。`dwg_send_code` は annotations を持たず、この読み取り専用リストには含まれません。継続的な実行許可はMCPクライアントで明示的に選択してください。

<!-- read-only-allowlist:start -->
```text
mcp__dwg-mcp__dwg_count_entities
mcp__dwg-mcp__dwg_create_bake_issue_draft
mcp__dwg-mcp__dwg_get_block_attributes
mcp__dwg-mcp__dwg_get_current_target
mcp__dwg-mcp__dwg_get_drawing_info
mcp__dwg-mcp__dwg_get_drawing_profile
mcp__dwg-mcp__dwg_get_entity_properties
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
mcp__dwg-mcp__dwg_zoom_extents
mcp__dwg-mcp__dwg_zoom_to_entity
mcp__dwg-mcp__dwg_zoom_window
```
<!-- read-only-allowlist:end -->
