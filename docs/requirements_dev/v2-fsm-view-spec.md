# V2 FSM View & 状態遷移完全性チェック 実装仕様書

## 1. 目的と前提
* **目的:** 同一の正本AST（`KnowledgeGraphDocument`）から、`LifecycleState` に基づく **有限状態機械（FSM）View** を決定論的に生成し、状態遷移の抜け漏れ（未到達状態・行き止まり状態）および未使用語彙を静的検証できるようにする。
* **将来接続:** 本仕様で作成する `FsmGraph` モデルは、次期機能「カスタマージャーニーの答え合わせ（イマジナリー自販機：ユーザー行動ステップの遷移トレース）」の直接の土台として利用する。
* **技術スタック:** .NET 10.0 / C# / Blazor (`src/ReqPipeline.Web`) / xUnit (`tests/ReqPipeline.UnitTests`)
* **設計原則:**
  * コア（FSM解析・MermaidノードID・Linterメッセージ）は **英語シンボル（`CanonicalName` / `AllowedValues`）正本** とする。
  * Web UI および Mermaid の表示ラベルには日本語表示名（`GetDisplayName()` / `LogicalType.GetValueLabel()`）を併用する。
  * LLM や外部検証器は使わず、100% 決定論的に動作させる。

---

## 2. Core 実装 (`src/ReqPipeline.Core/`)

### 2.1 FSM 解析モデル (`src/ReqPipeline.Core/Analysis/FsmGraphBuilder.cs`)
`KnowledgeGraphDocument` を受け取り、`List<FsmModel>` を返す純粋関数 `FsmGraphBuilder.Build(KnowledgeGraphDocument document)` を実装する。

* **対象:** `document.Glossary` のうち `ConceptType == ConceptType.LifecycleState` かつ `LogicalType.Kind == LogicalTypeKind.Enum` の語彙。
* **モデル構造:**
  * `FsmModel`:
    * `StateTermId` (`string`), `CanonicalName` (`string`), `DisplayName` (`string`)
    * `InitialState` (`string?`): `AllowedValues` の先頭要素（空なら `null`）
    * `States` (`List<FsmStateInfo>`): 各 `AllowedValues` ごとに以下を算出
      * `Value` (`string`: 英語シンボル), `Label` (`string`: `LogicalType.GetValueLabel(value)`)
      * `IsInitial` (`bool`)
      * `IsReachable` (`bool`: `InitialState` からの BFS で到達可能か)
      * `IsSink` (`bool`: 出発する遷移 `FromValue == Value` が 0 件か)
      * `IncomingCount` (`int`), `OutgoingCount` (`int`)
    * `Transitions` (`List<FsmTransition>`)
  * `FsmTransition`:
    * `RequirementId` (`string`), `RequirementTitle` (`string`)
    * `FromValue` (`string`), `ToValue` (`string`)
    * `TriggerEventRef` (`string?`), `TriggerCanonicalName` (`string?`), `TriggerDisplayName` (`string?`)
    * `ActorDisplayName` (`string?`)
* **エッジ抽出ルール:**
  * `NodeKind == Atomic` かつ `Behavior?.Kind == BehaviorKind.TRANSITION` かつ `Behavior.TargetTermRef == stateTerm.Id` の要求を対象とする。
  * `FromValue` と `ToValue` がともに `AllowedValues` に含まれるものを有効遷移として登録する。

### 2.2 Mermaid レンダラー (`src/ReqPipeline.Core/Rendering/FsmMermaidRenderer.cs`)
`FsmModel` を受け取り、`stateDiagram-v2` 形式の Mermaid テキストを決定論的に出力する `FsmMermaidRenderer.Render(FsmModel model, bool useJapaneseLabels = true)` を実装する。

* **出力イメージ:**
  ```mermaid
  stateDiagram-v2
      [*] --> List
      state "一覧 (List)" as List
      state "詳細 (Detail)" as Detail
      state "作成 (Create)" as Create
      List --> Detail : 顧客選択操作 [REQ-001-3]
  ```
* `useJapaneseLabels == false` の場合は英語正本ラベル（`TriggerCanonicalName` 等）で出力する。
* 状態ID（`List`, `Detail` 等）にスペースや特殊文字が含まれる場合は安全にサニタイズする。

### 2.3 Linter 拡張 (`src/ReqPipeline.Core/Validation/GraphLinter.cs`)
既存の `LINT-001` 〜 `LINT-008` に加え、以下の **2ルール（いずれも `Severity: Warning`）** を追加する。

| RuleId | 検証内容 | TargetId | 判定 |
| --- | --- | --- | --- |
| `LINT-009` | `FsmGraphBuilder.Build(document)` の結果において、各 `LifecycleState` に **未到達状態（`!IsReachable`）** または **行き止まり状態（`IsSink` かつ遷移が1件以上存在するFSM内）** がある場合に警告する | `StateTermId` | Warning |
| `LINT-010` | `document.Glossary` に登録されている語彙のうち、どの要求（`SubjectSystemRef`, `ActorRef`, `TriggerEventRef`, `Preconditions[*].StateTermRef`, `Behavior.TargetTermRef`）からも一度も参照されていない **未使用語彙** がある場合に警告する | `term.Id` | Warning |

* **注意:** `Severity` は `Warning` とする（ドラフト編集や既存サンプルの `Error == 0` テストを壊さないため）。
* エラー/警告メッセージは英語正本とする（例: `State 'Detail' in LifecycleState 'ScreenState' is a dead-end (no outgoing transitions).`）。

---

## 3. Web UI 実装 (`src/ReqPipeline.Web/`)

### 3.1 ナビゲーション & Mermaid JS 連携
* `MainLayout.razor` のヘッダーに **`FSM View`** タブ（`/fsm`）を追加する。
* `App.razor` に Mermaid.js（CDN: `mermaid@11` 等）を読み込み、`wwwroot/js/v2-interop.js` に指定コンテナへ Mermaid SVG を安全に描画する関数 `renderMermaid(elementId, code)` を追加する。

### 3.2 `FsmView.razor` (`/fsm`)
* **LifecycleState 選択バー:** 複数の `LifecycleState` がある場合にチップ/タブで切り替え可能。
* **表示モード切替:** 日本語ラベル / 英語正本ラベルのトグル。
* **メインエリア:**
  1. **状態遷移図（Mermaid ライブ描画）** ＋ Mermaidソース表示・コピー・`.mmd` ダウンロードボタン。
  2. **状態ヘルス一覧:** 各状態について `Initial` / `Reachable` / `⚠ Unreachable (LINT-009)` / `⚠ Dead-end (LINT-009)` バッジを表示。
  3. **遷移テーブル:** `From` → `Trigger` → `To` → `[REQ-ID]`（クリックすると Workbench `/` へ遷移し、該当の `Atomic` 要求を選択状態にする）。

---

## 4. ユニットテスト (`tests/ReqPipeline.UnitTests/`)
1. `FsmGraphBuilderTests.cs`（新規）:
   * `crud-sample.v2.json` から `ScreenState` の FSM が構築され、`List`（初期・到達可能）、`Detail`（到達可能・行き止まり）、`Create`（未到達・行き止まり）が正しく判定されること。
   * `Detail -> List` の戻り遷移を追加すると `Detail` の `IsSink` が `false` になること。
   * `FsmMermaidRenderer.Render` が有効な `stateDiagram-v2` を出力すること（日本語ラベル／英語ラベル両方）。
2. `GraphLinterTests.cs`（追記）:
   * `LINT-009`: 未到達状態・行き止まり状態で `Warning` が出ること、全状態が循環・到達可能なら出ないこと。
   * `LINT-010`: 未使用語彙で `Warning` が出ること、全語彙が参照されていれば出ないこと。
   * ※ 既存テスト（`CrudSampleJson_PassesGraphLinterWithZeroErrors...`）が `Error` 0件を期待している場合、`Warning` 追加との整合性を確認・調整すること。

---

## 5. 完了条件
* `dotnet build src/ReqPipeline.Web/ReqPipeline.Web.csproj` が 0 エラーで通ること。
* `dotnet test` が全件パスすること。
* `dotnet run --project src/ReqPipeline.Web` で `/fsm` 画面が開き、`crud-sample.v2.json` の状態遷移図（Mermaid）と状態ヘルス・遷移テーブルがリアルタイム描画されること。
