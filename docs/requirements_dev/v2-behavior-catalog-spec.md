# V2 実装仕様書：4意味プリミティブ (`SemanticEffect`) ＋ ユーザー定義 `BehaviorCatalog` ＆ 多ドメイン検証

## 1. 概要と前提
* **背景:** [4_behaviorcatalog.md](./4_behaviorcatalog.md) に基づき、CRUD固定の `BehaviorKind`（6種）を廃止し、**Core固定の4意味プリミティブ (`SemanticEffect`)** と **ユーザー定義 `BehaviorCatalog` (`BehaviorDefinition`)** の2層アーキテクチャへリファクタリングする。
* **スキーマバージョン:** 未リリースの V2 開発中のため `SchemaVersion = "2.0"` のまま維持し、`samples/crud-sample.v2.json` を新スキーマへ一括移行する。
* **フェーズ分割:** 手戻りを防ぐため、**Phase 1（Core & UnitTests）→ Phase 2（Blazor UI）→ Phase 3（`doorlock-sample.v2.json` & サンプル切替）** の3段階で実装する。

---

## Phase 1: Core AST・検証エンジン・Renderer リファクタリング & ユニットテスト

### 1.1 ドメインモデル (`src/ReqPipeline.Core/Domain/`)
1. **`BehaviorCatalogModels.cs`（新規）**
   * `SemanticEffect` enum（`JsonStringEnumConverter` 付き）:
     * `MutateState` / `ManageEntity` / `EmitAction` / `Prohibit`
   * `BehaviorDefinition` クラス:
     * `BehaviorDefId` (`string`, 例: `BEH-TRANSITION`, `BEH-LOCK`)
     * `CanonicalLabel` (`string`, 英語正本名)
     * `JapaneseLabel` (`string`, 日本語表示名)
     * `Description` (`string`)
     * `Effect` (`SemanticEffect`)
     * `AllowedTargetTypes` (`List<ConceptType>`)
     * `RequiresFromValue` (`bool`)
     * `RequiresToValue` (`bool`)
     * `JapaneseTemplate` (`string`, プレースホルダ: `{target}`, `{from}`, `{to}`, `{prohibitedAction}`)
     * `EnglishTemplate` (`string`, 同上)
2. **`KnowledgeGraphDocument.cs`（更新）**
   * `public List<BehaviorDefinition> BehaviorCatalog { get; set; } = [];` を追加。
3. **`RequirementAstModels.cs`（更新）**
   * 旧 `BehaviorKind` enum を廃止する。
   * `BehaviorNode` を以下へ更新：
     * `BehaviorDefRef` (`string`)
     * `TargetTermRef` (`string`)
     * `FromValue` (`string?`)
     * `ToValue` (`string?`) — 旧 `SetValue` を統合
     * `ProhibitedBehaviorDefRef` (`string?`) — 旧 `ProhibitedAction` をカタログID参照へ型安全化（`null` は対象への全操作禁止）
     * `ProhibitedToValue` (`string?`) — 特定値への状態遷移/設定のみ禁止する場合に指定
     * `Timing` (`TimingKind`)
     * `Notes` (`string?`)

### 1.2 `GraphLinter` のデータ駆動化 (`src/ReqPipeline.Core/Validation/GraphLinter.cs`)
| RuleId | 更新後の検証内容 | 判定 |
| --- | --- | --- |
| `LINT-001` | `Behavior.BehaviorDefRef` および `Behavior.ProhibitedBehaviorDefRef`（非null時）が `BehaviorCatalog` に存在すること | Error |
| `LINT-002` | `Behavior.TargetTermRef` の `ConceptType` が、`BehaviorDef.AllowedTargetTypes` に含まれていること | Error |
| `LINT-003` | `FromValue` / `ToValue` / `ProhibitedToValue`（非null時）が、対象語彙の `LogicalType`（`Boolean` / `Enum`）に閉包していること | Error |
| `LINT-005` | ① `BehaviorDef.RequiresFromValue == true` なら `FromValue` 必須、② `BehaviorDef.RequiresToValue == true` なら `ToValue` 必須、③ `BehaviorDef.Effect == Prohibit` のとき、`ProhibitedBehaviorDefRef` の参照先 `Effect` が `Prohibit` ではないこと（自己禁止・禁止の禁止を防止） | Error |
| `LINT-008` | 条件①（主語・トリガー重複）＆条件②（前提条件の同時成立）を満たす `(A, B)` に対し、`TargetTermRef` 一致かつ以下をエラー検知：<br>① **`Prohibit` vs 他プリミティブ:** `ProhibitedBehaviorDefRef == null` または `ProhibitedBehaviorDefRef == other.BehaviorDefRef`、かつ `ProhibitedToValue == null` または `ProhibitedToValue == other.ToValue`。<br>② **`MutateState` 同士の競合:** `FromValue` が重複（一致、または一方が `null`）かつ `ToValue` が異なる。 | Error |

### 1.3 `FsmGraphBuilder` & `JourneySimulator` (`src/ReqPipeline.Core/Analysis/`)
* **`FsmGraphBuilder`:**
  * `BehaviorDef.Effect == SemanticEffect.MutateState` かつ `TargetTerm.ConceptType == ConceptType.LifecycleState` かつ `FromValue != null && ToValue != null` の要求を遷移エッジとして抽出する（`FromValue == null` の `SET` 相当は FSM エッジに含めない）。
* **`JourneySimulator`:**
  * **`MutateState`:**
    * `FromValue != null` の場合：`currentState` に `TargetTermRef` が存在し、かつ現在値が `FromValue` と一致すれば `ToValue` へ更新して `Passed`。現在値が未設定または不一致なら **`StateMismatch` (FAIL)**。
    * `FromValue == null` の場合：前提条件を満たしていれば現在値に関係なく `ToValue` へ更新して `Passed`。
  * **`ManageEntity` / `EmitAction`:** 前提条件を満たせば `Passed`（状態変更なし）。
  * **`Prohibit`:** 前提条件を満たせば `Blocked`（状態変更なし）。

### 1.4 Renderer のテンプレート展開化 (`src/ReqPipeline.Core/Rendering/`)
* `JapaneseRequirementRenderer` / `EnglishRequirementRenderer` は、`BehaviorDefinition.JapaneseTemplate` / `EnglishTemplate` を用いて以下を展開する：
  * `{target}`: 日本語は `targetTerm.GetDisplayName()`、英語は `targetTerm.CanonicalName`
  * `{from}` / `{to}`: 日本語は `targetTerm.LogicalType.GetValueLabel(...)`、英語は正規値そのまま
  * `{prohibitedAction}`: `ProhibitedBehaviorDefRef` が指定されていれば参照先 `BehaviorDefinition` の `JapaneseLabel` / `CanonicalLabel`。`null` の場合は日本語 `"操作"`、英語 `"any action"` へフォールバック。
* **`ProhibitedToValue` の扱い:**
  * 標準プリセットの `BEH-PROHIBIT` テンプレートには `{to}` を含めない（`ProhibitedToValue == null` 時に日本語助詞が崩れるのを防ぐため）。
  * テンプレートに `{to}` が含まれる場合は `ProhibitedToValue` を展開し、含まれない場合でも `ProhibitedToValue != null` なら末尾に `（遷移先: {to}）` / ` (to {to})` を自動付記する。

### 1.5 `samples/crud-sample.v2.json` の移行 & ユニットテスト
* 6種の標準プリセット（`BEH-CREATE`, `BEH-DELETE`, `BEH-SET`, `BEH-TRANSITION`, `BEH-DISPLAY`, `BEH-PROHIBIT`）を `behaviorCatalog` へ定義し、全 `requirements` を新スキーマへ移行。
* `GraphLinterTests`, `V2CrudSampleAndRenderingTests`, `FsmGraphBuilderTests`, `JourneySimulatorTests` を新スキーマへ更新し、`ProhibitedToValue` 衝突や `MutateState` の `FromValue == null` / 現在値未設定ケースのテストを追加。
* **Phase 1 完了条件:** `dotnet test` が全件パスすること。

---

## Phase 2: Blazor UI の動的化 (`src/ReqPipeline.Web/`)

1. **Behavior Catalog 管理画面（`/behaviors`）の新設**
   * `MainLayout.razor` ヘッダーに **`Behaviors`** タブを追加。
   * `BehaviorDefinition` の一覧・新規追加・編集・削除。
   * `SemanticEffect` 選択時に推奨設定（`AllowedTargetTypes`, `RequiresFromValue`, `RequiresToValue`, 日英テンプレート雛形）を自動補完し、ダミー語彙でのリアルタイムプレビューを表示。
2. **Workbench (`Home.razor`) の動的スロット化**
   * `BehaviorKind` の固定チップを廃止し、`Workspace.Document.BehaviorCatalog` から動詞を動的選択できるようにする。
   * 選択した `BehaviorDefinition` の `AllowedTargetTypes` で `TargetTermRef` 候補を動的フィルタ。
   * `RequiresFromValue` / `RequiresToValue` に応じて `FromValue` / `ToValue` スロットを表示。
   * `Effect == Prohibit` のとき、`ProhibitedBehaviorDefRef`（`Effect != Prohibit` のカタログのみ候補表示）と `ProhibitedToValue`（対象が `Enum`/`Boolean` のときのみ表示）を編集可能にする。
* **Phase 2 完了条件:** `dotnet build src/ReqPipeline.Web/ReqPipeline.Web.csproj` が 0 エラーで通り、CRUD サンプルの編集・プレビュー・Linter・FSM・Journey がすべて正常動作すること。

---

## Phase 3: 多ドメイン検証 (`samples/doorlock-sample.v2.json` & サンプル切替 UI)

1. **`samples/doorlock-sample.v2.json` の作成**
   * 英語正本シンボル＋日本語表示名で車両ドアロック＆警告制御システムを定義。
   * 5つの制御ドメイン固有動詞（`BEH-LOCK`, `BEH-UNLOCK`, `BEH-CHIME`, `BEH-RECORD-DTC`, `BEH-INTERLOCK`）を定義（CRUD動詞は不使用）。
   * 検証シナリオを網羅する要求群と、2本のプリセット `Journeys`（`JRN-001`: 正常施錠＋アンサーバック鳴動、`JRN-002`: 走行中解錠インターロック `BLOCKED`）を同梱。
2. **サンプル切替 UI**
   * `WorkspaceStateService` にサンプル切替メソッドを追加し、`MainLayout.razor` ヘッダーから **`CRUD`** / **`DoorLock`** をワンクリックで切り替え可能にする。
3. **ユニットテスト & 実機検証**
   * `doorlock-sample.v2.json` の読み込み・`GraphLinter`（Error 0件、条件外し時の `LINT-008` 発火）・FSM（`Unlocked ⇄ Locked`）・Journey（`JRN-001` PASS / `JRN-002` BLOCKED）をテスト＆ブラウザで確認。
