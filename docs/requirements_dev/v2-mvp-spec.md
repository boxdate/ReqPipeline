## 1. 実装の前提とガードレール（Antigravityへの制約）

* **技術スタック:** .NET 10.0 / C# / Blazor (`src/ReqPipeline.Web`) / xUnit (`tests/ReqPipeline.UnitTests`) / `System.Text.Json`
* **後方互換:** V1の `requirements.json` / `glossary.json` との互換性は維持しない。V2の正本スキーマ（`SchemaVersion = "2.0"`）で新規構築する。
* **スコープ境界（今回やらないこと）:**
* ローカルLLM（Ollama）連携、JEV候補生成、Alloy/nuXmv連携は**今回のMVPスコープ外**とする。
* まずは「決定論的なAST・Linter・Renderer・構造化スロットUI」を100%動作させることに集中する。


* **データ保存:** ローカルの単一JSONファイル（`KnowledgeGraphDocument`）の読み書きで完結させる（DB不要）。

---

## 2. 実装フェーズと詳細仕様

Antigravityに一括で投げるとUIとロジックが混ざって手戻りが起きやすいため、**Part A（Core＋テスト）を `dotnet test` で全件パスさせてから、Part B（Blazor UI）へ進む2段階**に分割します。

### Part A: Coreエンジンと決定論的検証（UIなし・テスト駆動）

#### 1. ドメインモデル (`src/ReqPipeline.Core/Domain/`)

* `KnowledgeGraphDocument.cs` / `RationaleNode.cs`: 正本ルートと理由ノード。
* `GlossaryModels.cs`: `ApprovedGlossaryTerm`、`ConceptType`（7種）、`LogicalTypeDefinition`（`None` / `Boolean` / `Enum`）。
* `RequirementAstModels.cs`: `CanonicalRequirementNode`（`Composite` / `Atomic`）、`ParentRequirementId`、`EarsEntryPattern`（5種）、`ComparisonOperator`（`EQ`, `NEQ`, `IN`）、`BehaviorNode`（`CREATE`, `SET`, `DELETE`, `TRANSITION`, `DISPLAY`, `PROHIBIT` の6種）、`TimingKind`（`UNSPECIFIED`, `IMMEDIATE`, `EVENTUAL`）。
* `EscapeRecordModels.cs`: `FreeTextEscapeRecord`、`EscapeReason`（6種）。
* **JSONシリアライズ要件:** Enumは文字列としてシリアライズ/デシリアライズすること（`JsonStringEnumConverter`）。

#### 2. 決定論的Linter (`src/ReqPipeline.Core/Validation/GraphLinter.cs`)

`KnowledgeGraphDocument` を受け取り、`List<LintDiagnostic>`（`Severity: Error | Warning`, `RuleId`, `TargetId`, `Message`）を返す純粋関数として実装する。

| RuleId | 検証内容 | 判定 |
| --- | --- | --- |
| `LINT-001` | すべての参照ID（`RationaleRefs`, `SubjectSystemRef`, `ActorRef`, `TriggerEventRef`, `StateTermRef`, `TargetTermRef`）が正本内に存在すること | Error |
| `LINT-002` | 参照先グロッサリーの `ConceptType` がスロットの型と一致すること（例：`SubjectSystemRef` ➔ `System`、`TriggerEventRef` ➔ `Event`、`StateTermRef` ➔ `StateVariable` または `LifecycleState`） | Error |
| `LINT-003` | `Preconditions` の `ExpectedValues`、および `Behavior` の `FromValue` / `ToValue` / `SetValue` が、参照先グロッサリーの `LogicalType`（`Boolean` の `"true"/"false"` または `Enum` の `AllowedValues`）に閉包していること | Error |
| `LINT-004` | `NodeKind == Composite` のノードは1つ以上の子要求を持ち、`NodeKind == Atomic` のノードは `Behavior != null` または `PENDING` の `FreeTextEscapeRecord` を持つこと | Error |
| `LINT-005` | `BehaviorKind` ごとの必須フィールド整合性（`TRANSITION` なら `FromValue` と `ToValue` 必須、`SET` なら `SetValue` 必須、`CREATE`/`DELETE` なら対象が `DomainObject` であること） | Error |
| `LINT-006` | 最上位要求（`ParentRequirementId == null`）に有効な `RationaleRefs` が1つ以上紐づいていること | Error |
| `LINT-007` | `PENDING` 状態の `FreeTextEscapeRecord` が残っていること（承認ゲート用） | Warning |

#### 3. Renderer (`src/ReqPipeline.Core/Rendering/`)

* `JapaneseRequirementRenderer.cs`: `CanonicalRequirementNode`（`Atomic`）と `Glossary` を受け取り、EARS 5パターンに応じた自然な日本語文を決定論的に生成する。
* `UsdmMarkdownRenderer.cs`: `KnowledgeGraphDocument` から `Rationale ➔ 親要求(Composite) ➔ 子要求(Atomic)` の階層Markdown（USDM View）を出力する。

#### 4. CRUDサンプルとユニットテスト (`tests/ReqPipeline.UnitTests/`)

* 顧客管理CRUDアプリ（作成・一覧表示・詳細遷移・管理者以外の削除禁止）のサンプルJSON（`samples/crud-sample.v2.json`）を作成。
* 正常系テスト：サンプルJSONが `GraphLinter` で Error 0件になり、期待通りの日本語EARS文とUSDM Markdownが生成されること。
* 異常系テスト：`LINT-001` 〜 `LINT-007` の各ルールが意図通りエラー/警告を検出すること。

---

### Part B: Blazor 構造化スロット・チップ型Workbench (`src/ReqPipeline.Web/`)

Part A のテストがすべて通った後に着手する最小UI仕様です。

1. **Glossary管理タブ（食材の登録）**
* `ApprovedGlossaryTerm` の追加・編集・一覧表示（`ConceptType` と `Bool/Enum` の値リストを編集可能）。


2. **Visual Formulation Workbench（調理画面）**
* 左ペイン：Rationale ➔ 親要求（Composite）➔ 子要求（Atomic）のツリー表示。
* 右ペイン（スロット編集）：
* 入口として **EARS 5パターン** を選択すると、必要なスロット（`Context`, `Trigger`, `Subject`, `Behavior`）のみが表示される。
* 各スロットは**型フィルタ済みのドロップダウン／チップ選択**とする（例：`Trigger` スロットには `ConceptType == Event` の語彙しか出ない、`FromValue`/`ToValue` には対象Stateの `AllowedValues` しか出ない）。
* **親子分割アクション:** 親要求に対して「子要求（動作）を追加」ボタンを押すと、親の `RationaleRefs` や共通の `Context`/`Trigger` を引き継いだ `Atomic` 子要求を作成できる。
* **Escape Hatchボタン:** スロットで表現できない場合、`EscapeReason` を選んで自由記述レコードを記録できる。




3. **リアルタイムPreview & Lintパネル**
* 編集中に `JapaneseRequirementRenderer` の生成文と `GraphLinter` のエラー・警告を即時表示する。
* JSONのエクスポート／インポート機能。
