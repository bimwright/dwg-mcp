<!-- mcp-name: io.github.bimwright/dwg-mcp -->

<p align="center">
  <img src="https://raw.githubusercontent.com/bimwright/.github/master/assets/logos/dwg-mcp.png" alt="dwg-mcp" width="180" />
</p>

<h1 align="center">dwg-mcp</h1>

<p align="center">
  <a href="https://github.com/bimwright/dwg-mcp/actions/workflows/build.yml"><img src="https://github.com/bimwright/dwg-mcp/actions/workflows/build.yml/badge.svg" alt="build" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-Apache%202.0-blue.svg" alt="license" /></a>
  <a href="#サポート対象autocadバージョン"><img src="https://img.shields.io/badge/AutoCAD-2022--2027-186BFF" alt="AutoCAD 2022-2027" /></a>
  <a href="#ツール"><img src="https://img.shields.io/badge/MCP-36%20default%20%2B%20optional-6C47FF" alt="MCP tools" /></a>
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

[GitHub Releases](https://github.com/bimwright/dwg-mcp/releases/latest) から setup ZIP（`DwgMcp.Setup-*-win-x64.zip`）を入手。v1.0.0 は AutoCAD **2024** と **2027** プラグイン入り。展開して `install.ps1`（全文は英語 README）。

`dotnet tool install -g Bimwright.Dwg.Server` は使わないでください。

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

4桁のターゲット年で特定のAutoCADインスタンスを固定:

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

`--read-only` で書き込み toolset を外します。`--toolsets all`、または必要な既定を**含めた**明示リストを使ってください（カスタムリストは既定セットを**置き換え**ます。例: `query,modify,meta,view,annotation`）。環境変数: `BIMWRIGHT_DWG_TOOLSETS=…`。

`dwg_send_code` はデフォルトのツール一覧からは非表示です。公開するには**両方**の側でオプトインしてください。サーバーを `--enable-send-code`（または `BIMWRIGHT_DWG_ENABLE_SEND_CODE=1`）で起動し、AutoCAD内でそのプラグインセッションに対して `MCPENABLECODE` を実行します（`MCPDISABLECODE` で取り消し）:

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

## ツール

デフォルト起動では36のツール（クエリ、変更、メタ、ビュー、およびデフォルト有効の `dwg_capture_view_image`）が公開されます。オプショナルのToolBaker、注釈、ブロック、寸法、エクスポート、作図ツールセットは `--toolsets` で有効にでき、`dwg_send_code` と合わせてMCPサーフェス全体は61ツールになります。

一般的なCADツールは、選択されたAutoCADターゲットの現在アクティブなドキュメントに対して動作します。エンティティ入力と返されるエンティティIDは、`7F5AD` のようなAutoCAD 16進ハンドルを使用します。作成、コピー、オフセット、および変更の応答は、生成または変更されたエンティティを16進ハンドルで識別します。

Plan 2 のクエリ拡張はモデル空間のみです。`dwg_query_entities`、`dwg_count_entities`、`dwg_select_by_layer`、`dwg_select_by_type` はペーパー空間/レイアウトエンティティではなくモデル空間をスキャンします。`dwg_select_by_layer` と `dwg_select_by_type` は呼び出し元にハンドルリストを返します。AutoCADのピックファースト選択は変更しません。

| ツール | 目的 |
|------|---------|
| `dwg_get_drawing_info` | 現在の図面名、現在の画層、現在のスペース/レイアウト、単位スカラーを読み取る |
| `dwg_get_entity_properties` | AutoCAD 16進ハンドルで識別されるエンティティのプロパティを読み取る |
| `dwg_list_layers` | 現在の図面の画層を色と状態フラグとともに一覧表示 |
| `dwg_query_entities` | オプションのタイプ、画層、色、制限、形状フラグでモデル空間エンティティをクエリ |
| `dwg_count_entities` | オプションのタイプ、画層、色フィルターでモデル空間エンティティをカウント |
| `dwg_select_by_layer` | 1つの画層のモデル空間エンティティハンドルリストを返す（ピックファースト選択は変更しない） |
| `dwg_select_by_type` | 1つのエンティティタイプのモデル空間エンティティハンドルリストを返す（ピックファースト選択は変更しない） |
| `dwg_get_selected_texts` | ピックファースト選択を読み取り、テキストエンティティを空間クラスタリングし、書き換えモードのヒント付きでグループ化テキストを返す |
| `dwg_update_texts` | 1つのトランザクションでハンドル指定による新しいテキストを書き込む |
| `dwg_create_layer` | 既存の画層のプロパティを上書きせずに画層を作成する |
| `dwg_create_line` | 現在の図面空間に1本の線を作成 |
| `dwg_create_circle` | 現在の図面空間に1つの円を作成 |
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
| `dwg_list_available_targets` | v2 JSONおよびレガシー2024検出ファイルから検出された実行中のAutoCADターゲットを一覧表示 |
| `dwg_get_current_target` | 固定されているターゲット年（あれば）を表示 |
| `dwg_switch_target` | このサーバープロセスをAutoCAD `2022`〜`2027` に固定 |
| `dwg_batch_execute` | 複数の内部ワイヤーコマンドを論理バッチとして実行 |
| `dwg_zoom_extents` | 図面ビューポートの範囲にズーム |
| `dwg_zoom_window` | 2つのコーナー点で定義されたウィンドウにビューポートをズーム |
| `dwg_zoom_to_entity` | ハンドルで識別される特定の図面エンティティの範囲にビューポートをズーム |
| `dwg_capture_view_image` | アクティブビューを画像ファイルへキャプチャ（既定オン；パスポリシー適用） |

`dwg_send_code` は上表に**含めない** — 両面オプトインのみ（インストール / セキュリティ参照）。

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
- **ビューと読み取り専用**: 読み取り専用でも `view` ツールセットは登録されたままです（ズームと `dwg_capture_view_image`）。Capture は MCP スキーマ上 read-only ですが、パスポリシーに従い画像ファイルを書き込みます — `--read-only` でも出力パスに注意。
- **作図操作と読み取り専用**: `drawing` ツールセットは、読み取り専用モードで `dwg_get_variables` を維持しますが、`dwg_set_system_variable`、`dwg_save_drawing`、`dwg_purge_drawing` は削除されます。
- **延期された角度寸法**: 現在サポートされているのは linear、平行、半径、直径寸法タイプのみです。角度寸法は延期されており、まだ実装されていません。
- **延期されたファイルエクスポートツール**: `dwg_export_pdf`、`dwg_export_image` ツールは延期されています。`dwg_capture_view_image` ツールはデフォルトで有効化されています。

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

---

## 標準ワークフロー

```
1. ユーザーがAutoCADでテキストエンティティを選択
2. エージェントが dwg_get_selected_texts を呼び出す → クラスタリングされたテキストグループを受信
3. エージェントが各クラスターを翻訳
4. エージェントが dwg_translate_and_rewrite([{id, new_text}, ...]) を呼び出す
   ツールが処理: アンカー、削除、MText、フォントスタイル、高さ。完了。
5. 必要に応じてユーザーが REGEN を実行
```

エージェントの視点からは、読み取り、書き込みの2ステップです。

---

## サポート対象AutoCADバージョン

| バージョン | ObjectARX リリース | プラグイン TFM | ステータス |
|---------|-------------------|------------|--------|
| AutoCAD 2022 | 24.1 | `net48` | シェル足場済み; リリースビルドにはローカルのAutodesk参照が必要 |
| AutoCAD 2023 | 24.2 | `net48` | シェル足場済み; リリースビルドにはローカルのAutodesk参照が必要 |
| AutoCAD 2024 | 24.3 | `net48` | デフォルトサポート対象シェルおよび通常のソリューションビルド |
| AutoCAD 2025 | 25.0 | `net8.0-windows` | シェル足場済み; リリースビルドにはローカルのAutodesk参照が必要 |
| AutoCAD 2026 | 25.1 | `net8.0-windows` | シェル足場済み; 2025とバイナリ互換だが独立したシェルとしてビルド |
| AutoCAD 2027 | 26.0 | `net10.0-windows` | シェル足場済み; 2025/2026とはバイナリ非互換 |

サーバーとテストは、すべてのAutoCADシェルがリリースビルドされていなくてもパスできます。AutoCADの年次バージョンを出荷するには、対応するAutodesk管理アセンブリがインストールされた準備済みマシンでそのシェルをビルドする必要があります。

---

## セキュリティ

`dwg_send_code` は、AutoCADプロセスおよびローカルファイルシステムへの完全なアクセス権を持つ任意のC#コードを実行します。デフォルトのMCPツールサーフェスには登録されていません。使用するには、サーバーを `--enable-send-code` または `BIMWRIGHT_DWG_ENABLE_SEND_CODE=1` で起動し、AutoCAD内で `MCPENABLECODE` を実行してそのセッションのプラグイン側同意を付与してください。

セキュリティモデルは以下に依存しています:

- **ローカルのみのトランスポート** — AutoCAD 2022–2024 は127.0.0.1上のTCP、2025–2027 はループバック名前付きパイプ、リモートアクセス不可。
- **セッションごとの認証トークン** — プラグイン起動ごとにローテーションされ、リクエストごとに検証。
- **両面コードオプトイン** — `dwg_send_code` は、サーバーが `--enable-send-code`（または `BIMWRIGHT_DWG_ENABLE_SEND_CODE=1`）で起動され **かつ** ユーザーがAutoCAD内でそのプラグインセッションに対して `MCPENABLECODE` を実行した場合にのみ登録されます。
- **タイムアウト境界** — スクリプト実行は専用スレッドで行われ、タイムアウト時にキャンセルおよび中止されます。
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

AECツールチェーンのための手鍛造MCPゲートウェイ — 単一のアーキテクチャ、予測可能/監査可能/可逆:

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
