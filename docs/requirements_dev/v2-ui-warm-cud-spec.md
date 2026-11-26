# ReqPipeline V2 UI 刷新仕様書：ウォーム・ドキュメント × CUD 準拠デザインシステム

## 1. 目的と制約
* **コンセプト:** ReqPipeline V2 の全 7 ビューを **「ウォーム・ドキュメント（Notion / 上質な紙の仕様書風）」×「CUD（カラーユニバーサルデザイン）準拠」** へ統一する。
* **制約:**
  * 外部 UI フレームワーク（MudBlazor / Fluent UI / Tailwind 等）は追加しない。
  * 純粋な CSS カスタムプロパティ（デザイントークン）と軽量インライン SVG（`UiIcon.razor`）で実装する。
  * **変更範囲は `src/ReqPipeline.Web/` のデザイン層（CSS / Layout / Markup / JS テーマ）のみ** とし、`src/ReqPipeline.Core/` および `tests/ReqPipeline.UnitTests/` の機能・出力文字列・テストは一切壊さない。
* **Bootstrap 5 との共存ルール:**
  * Bootstrap はグリッド・余白・フォーム骨格（`row`, `col-*`, `d-flex`, `gap-*`, `form-control`, `form-select`, `table`）のみ利用する。
  * 原色系クラス（`bg-primary`, `bg-success`, `bg-warning`, `bg-danger`, `btn-primary`, `alert-success` 等）はすべて自前トークンベースのクラス（`.v2-badge-*`, `.v2-btn-*`, `.v2-card`, `.v2-alert-*`）へ置き換える。
  * `.form-control:focus` / `.form-select:focus` の Bootstrap 標準グローを打ち消し、ウォーム／CUD トーンに馴染む控えめなフォーカスリング（`border-color: var(--cud-primary-accent); box-shadow: 0 0 0 2px rgba(29, 78, 216, 0.16);`）へ上書きする。

---

## 2. デザイントークン基盤 (`src/ReqPipeline.Web/wwwroot/css/design-tokens.css`)
`App.razor` で `bootstrap.min.css` の直後、`app.css` の直前に読み込む。

### 2.1 ウォーム紙面ベース（画面の 90%）
```css
:root {
    --surface-app: #FAF9F6;      /* 全体背景：ウォームアイボリー */
    --surface-card: #FFFFFF;     /* 仕様書シート・カード・ヘッダー */
    --surface-sidebar: #F3F1EC;  /* サイドバー：落ち着いたウォームストーン */
    --surface-subtle: #EFECE6;   /* ホバー・セグメント背景・補助ピル */
    --border-subtle: #E5E2DC;    /* 基本 1px ウォームボーダー */
    --border-strong: #D6D3CD;    /* 入力欄・強調枠 */
    --border-dashed: #C7C2B8;    /* 未定義セル用ウォーム破線 */
    --text-primary: #1C1917;     /* 本文・見出し：深いチャコールインク */
    --text-secondary: #57534E;   /* 補足・ラベル：ウォームトープ */
    --text-muted: #78716C;       /* ID・メタ情報 */

    /* CUD 準拠セマンティックカラー（赤と黄緑を並べない・WCAG AA 4.5:1 以上） */
    --cud-primary-fg: #1D4ED8;
    --cud-primary-accent: #2563EB;
    --cud-primary-bg: #EFF6FF;
    --cud-primary-border: #BFDBFE;

    --cud-success-fg: #0F766E;
    --cud-success-accent: #0D9488;
    --cud-success-bg: #F0FDFA;
    --cud-success-border: #99F6E4;

    --cud-warning-fg: #92400E;
    --cud-warning-accent: #D97706;
    --cud-warning-bg: #FFFBEB;
    --cud-warning-border: #FDE68A;

    --cud-danger-fg: #B91C1C;
    --cud-danger-accent: #DC2626;
    --cud-danger-bg: #FEF2F2;
    --cud-danger-border: #FECACA;

    --cud-neutral-fg: #57534E;
    --cud-neutral-bg: #F5F5F4;
    --cud-neutral-border: #D6D3CD;

    --font-sans: -apple-system, BlinkMacSystemFont, "Segoe UI", "Hiragino Kaku Gothic ProN", "Yu Gothic UI", Meiryo, sans-serif;
    --font-mono: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
    --radius-sm: 6px;
    --radius-md: 8px;
}
```

### 2.2 タイポグラフィ & バッジ使い分けルール
* **本文・EARS 文・`GIVEN / WHEN / THEN`:** `line-height: 1.65` で長文レビューの視認性を確保。
* **ID / コード値（`REQ-xxx`, `TERM-xxx`, `TC-xxx`, `JRN-xxx`）:** `var(--font-mono)`・小さめサイズ・`var(--text-muted)` で統一。
* **色の引き算（厳守）:**
  * 構造タグ（`Unit` / `Component` / `E2E`、技法名、`ConceptType`、`EarsPattern` 等）は **無彩色のウォームアウトラインバッジ（白背景＋`--border-strong`＋`--text-secondary`）** に統一。
  * CUD セマンティックカラーは **状態・安全性・エラー・結果（`Passed` / `Blocked` / `StateChanged` / `Errors` / `未定義`）のみ** に限定する。
* **カード:** 過度な影を廃し、`#FFFFFF` ＋ `1px solid var(--border-subtle)` ＋ `border-radius: 8px` のドキュメントシート調に統一。

---

## 3. 軽量 SVG アイコンコンポーネント (`src/ReqPipeline.Web/Components/Shared/UiIcon.razor`)
* UI 内の絵文字（`⛔`, `⚡`, `✓`, `⚠`, `🛑`, `✅`, `❌` 等）をすべて廃止し、`UiIcon.razor` へ置換する。
* 仕様: `Name` (`string`), `Size` (`int`, default `15`), `Class` (`string?`)。`stroke="currentColor"`, `stroke-width="1.6"`, `fill="none"`, `stroke-linecap="round"`, `stroke-linejoin="round"` の Lucide 風インライン SVG。
* 用意するアイコン: `arrow-right`, `git-branch`, `ban`, `zap`, `minus`, `check-circle`, `alert-triangle`, `x-circle`, `download`, `upload`, `copy`, `external-link`, `file-code`, `plus`, `trash`, `chevron-up`, `chevron-down`。

---

## 4. 共通ヘッダー (`MainLayout.razor` & `MainLayout.razor.css`)
* 濃紺ヘッダーを廃止し、**白ベース（`var(--surface-card)`）＋ 下部 `1px solid var(--border-subtle)`**（高さ 56px 固定）へ刷新。
* **7 タブナビゲーション:**
  * 短縮名で統一: `Workbench` / `Glossary` / `Behaviors` / `USDM` / `FSM` / `Journey` / `Tests`。
  * 塗りつぶし青ボタンを廃止し、**アクティブタブのみ `var(--text-primary)` 太字 ＋ 下部 `2px solid var(--text-primary)` インジケータ**。
  * 狭幅時は 2 段折り返しせず、`.v2-nav` の **横スクロール（`overflow-x: auto; white-space: nowrap;`）** でヘッダー高さを維持する。
* **右側コントロール群:**
  * `Errors: 0` は小さなディープティールのドット（エラー時はバーミリオン）＋ 落ち着いたピル。
  * サンプル切替（`CRUD` / `DoorLock`）は `var(--surface-subtle)` 枠内のセグメントコントロール（選択側のみ白背景＋細枠）。
  * `Import` / `Export` は `UiIcon` 付きの控えめなアウトラインボタン（`.v2-btn-outline`）。

---

## 5. Test View (`/tests`) の洗練と「非色手がかり」マトリクス
* **上部ツールバー:** カラフルな塗りつぶしピルをやめ、控えめなメトリクス表示（数値強調＋`--text-muted` ラベル）。`JA | EN` セグメント切替 ＋ アウトラインボタングループ（`MD` / `CSV` / `Gherkin`）。
* **2 次元状態遷移マトリクス（CUD × 形・線種での識別）:**
  * セル全体は淡い CUD ティント背景＋**左端 `3px solid` アクセント帯**＋`UiIcon`：
    * 有効遷移 (`ValidTransition`): ブルー帯 ＋ `arrow-right`
    * 条件付き遷移 (`ConditionalTransition`): アンバー帯 ＋ `git-branch`
    * 明示的禁止 (`ExplicitProhibit`): バーミリオン帯 ＋ `ban`（条件付き遷移との共存時は 2 行に分けて明瞭に表示）
    * アクション発火: ディープティール帯 ＋ `zap`
  * **未定義セル (`UndefinedNoOp`):** `1.5px dashed var(--border-dashed)` ＋ 極薄の斜線ハッチング背景 ＋ `minus` アイコン＋「未定義」。
  * **`未定義セルのみハイライト` ON 時:** 未定義セル以外を `opacity: 0.45` に落とし、未定義セルをアンバー破線枠（`var(--cud-warning-accent)`）で浮かび上がらせる。
  * **セル選択時:** `box-shadow: 0 0 0 2px var(--cud-primary-fg)` のフォーカスリングを表示し、下部にゆとりある `GIVEN / WHEN / THEN` 仕様カードを展開。

---

## 6. 2 段階実装フェーズと完了条件

### Phase 1: 基盤・ヘッダー・主役 2 画面（Workbench & Test View）
1. `wwwroot/css/design-tokens.css` 新設、`App.razor` 読み込み追加、`app.css` のトークン化＋Bootstrap フォーム `:focus` 上書き。
2. `Components/Shared/UiIcon.razor` 新設（`_Imports.razor` に `@using ReqPipeline.Web.Components.Shared` 追加）。
3. `MainLayout.razor` / `MainLayout.razor.css` のウォーム・ミニマル刷新（横スクロール対応含む）。
4. `Home.razor`（Workbench）および `TestView.razor`（Test View）の全クラス置換・絵文字廃止・非色手がかりマトリクス実装。
5. **確認:** `dotnet build src/ReqPipeline.Web/ReqPipeline.Web.csproj` 0 エラー & `dotnet test` 全件パス。

### Phase 2: 残り 5 画面・Mermaid テーマ統一・全画面実機検証
1. `GlossaryEditor.razor`, `BehaviorsEditor.razor`, `UsdmView.razor`, `FsmView.razor`, `JourneyView.razor` の全クラス置換・絵文字廃止・ウォームドキュメント統一。
2. `wwwroot/js/v2-interop.js` の `mermaid.initialize` に `theme: 'base'` ＋ ウォーム／CUD `themeVariables` を設定（到達不能・Dead-end ノードの `classDef` 等がある場合も既存テストを壊さない範囲で CUD トーンへ統一）。
3. **最終確認:** `dotnet build` / `dotnet test` 全件パス、および `dotnet run --project src/ReqPipeline.Web` 上で全 7 ビュー × 両サンプル（`CRUD` / `DoorLock`）の表示・コントラスト・レイアウト崩れなしを確認。
