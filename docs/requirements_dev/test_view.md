# ReqPipeline V2.0.0 設計仕様書：Test View（テスト観点コンテナ ➔ テストケース決定論的導出）

## 1. 概要・位置づけ・スコープ境界

### 1.1 目的
`ReqPipeline V2.0.0` の最終ピースとして、構造化された単一の正本（`KnowledgeGraphDocument`）から **テスト観点コンテナの整理 ➔ テスト技法に基づくテストケース展開 ➔ 3形式エクスポート** までを 100% 決定論的（AI・確率論なし）に一気通貫させる `Test View`（`/tests`）を実装する。

### 1.2 スコープ境界（V2.0.0 で厳守するルール）
* **正本スキーマ非破壊（`SchemaVersion = "2.0"` 維持）：** `USDM View` / `FSM View` と同じく、正本 JSON から決定論的に自動導出する **Read-Only 派生ビュー** とする。テスト実行結果の保存やテストケース側の手動編集（テスト管理ツール化）は行わない。
* **全件展開ポリシー：** 実プロジェクトの要求規模が未知の段階で早すぎる最適化（件数キャップ等）を行うと仕様漏れを隠蔽するリスクがあるため、V2.0.0 では素直に**全件展開**し、UI 側のフィルタとトグルで見せ方を制御する。

### 1.3 WDD・VSTeP・漢字曼荼羅との責務分離
ツールが「テスト観点そのものを自動発明する」という無理な主張を避け、テスト工学的に筋の通った責務分離を採用する。

| 層 / プロセス | 主体 | 役割と範囲 |
| :--- | :--- | :--- |
| **上流発散（WDD / テスト要求分析）** | 人間（3 Amigos / AI壁打ち補助） | 受け入れ（達成）基準や QA の *What if*、**漢字曼荼羅の物理・時相・環境領域（`連 / 並 / 遅 / 断 / 超 / 満` 等）**から観点を洗い出す。 |
| **観点コンテナ（What / Why）** | 正本（`Rationale` ➔ `CompositeRequirement`） | WDD の達成基準から導出された親要求群を、VSTeP におけるテスト観点の束（テストコンテナ）として扱う。 |
| **テスト詳細設計（How / ケース展開）** | **ReqPipeline V2（決定論エンジン）** | 定義済みの観点コンテナおよび横断モデル（FSM / Journey）に対し、標準テスト技法を適用して **論理空間（`正 / 禁 / 無 / 逆 / 重 / 権`）** のテストケースを漏れなく自動展開する。 |
| **モデル外・曖昧領域（Escape Hatch）** | 人間（手動テスト設計へ委譲） | 親要求配下に `EscapeRecord` が存在する場合、自動展開不可の領域として UI および `ReviewNote` で「手動テスト設計要」と明示する。 |

---

## 2. テスト階層（V字モデル）と導出マッピング

要求モデルの構造をそのまま V 字モデルのテスト階層へ対応づける。

| テスト階層 (`TestLevel`) | 導出元（ReqPipeline AST） | 適用するテスト技法 (`Technique`) | Gherkin マッピング |
| :--- | :--- | :--- | :--- |
| **`Unit`**<br>（単体・ロジック） | `AtomicRequirement` | `ConditionNormal`（正常発火）<br>`ConditionNegation`（1条件反転 / MC/DCライク）<br>`SafetyInterlock`（`Prohibit` ブロック＆解除対照） | 親要求 `Feature:` 配下の **`Rule:`** ＋ `Scenario Outline` (`Examples`) |
| **`Component`**<br>（機能・状態統合） | `CompositeRequirement`<br>`LifecycleState`（FSM） | `ValidTransition`（有効遷移 / 0-Switch）<br>`ConditionalTransition`（ガード付き遷移）<br>`ExplicitProhibit`（明示的禁止セル）<br>`InvalidTransition`（純粋な空白 / 無効遷移） | `CompositeRequirement` ➔ **`Feature:`**<br>`LifecycleState` ➔ 状態遷移用 **`Feature:`** |
| **`E2E`**<br>（システム・受け入れ） | `CustomerJourney` | `JourneyTrace`（正常フロー / 異常ブロックフロー通し検証） | Journey ごとに独立した **`Feature:`** ＋ 連続ステップの `Scenario` |

---

## 3. Core 導出エンジン仕様 (`src/ReqPipeline.Core`)

### 3.1 新設モデル（`Testing` 名前空間）
`KnowledgeGraphDocument`、`FsmGraphBuilder`、`JourneySimulator` を入力とし、`TestSpecificationGenerator.Generate(document)` で以下を決定論的に生成する。

* **`TestSpecificationDocument`**
  * `Summary`: 総観点コンテナ数、総テストケース数、階層別件数、`Atomic` 要求カバレッジ率（自動ケース紐付け済み要求数 / 非Escape要求数）、未定義セル総数、`Escape` 要手動確認件数。
  * `ViewpointContainers`: `List<TestViewpointContainer>`（`Rationale ➔ CompositeRequirement` ツリー、`LifecycleState` 群、`Journey` 群）。
  * `TransitionMatrices`: `List<StateTransitionMatrix>`（`LifecycleState` ごとの 2 次元マトリクス）。
  * `AllTestCases`: `List<DerivedTestCase>`（固定順序でソート済み）。

* **`StateTransitionMatrix` & `MatrixCell`**
  * `LifecycleTermId`, `LifecycleLabel`, `LifecycleCanonicalLabel`
  * `States`: `List<string>`（対象 `LifecycleState` の `AllowedValues`）
  * `Events`: `List<MatrixEventColumn>`（**絞り込みルール:** 当該 `LifecycleState` を `TargetTermRef` または `Preconditions` に参照する要求群に登場する `Event` のみを列に含める）
  * `Cells`: 2 次元または `(State, EventId)` マップ。各 `MatrixCell` は以下を保持：
    * `Classification` (`MatrixCellKind`):
      1. **`ValidTransition`**: 無条件（または遷移元のみ）の `MutateState` が定義済み。
      2. **`ConditionalTransition`**: 他の `StateVariable` 等のガード条件付き `MutateState`（または `EmitAction` / `ManageEntity`）が定義済み。
      3. **`ExplicitProhibit`**: `Prohibit` 要求が定義済み（同一セルに条件付き許可と禁止が共存する場合は、UI バッジで複合表示または条件付き＋禁止として明示）。
      4. **`UndefinedNoOp`**: 該当する要求が 0 件の純粋な空白セル。
    * `RelatedRequirementIds`: 該当セルに関係する `Atomic` 要求 ID 一覧。
    * `TestCases`: 当該セルから導出された `List<DerivedTestCase>`。

* **`DerivedTestCase`**
  * `TestCaseId` (string)
  * `TestLevel` (`Unit` / `Component` / `E2E`)
  * `Technique` (`ConditionNormal`, `ConditionNegation`, `SafetyInterlock`, `ValidTransition`, `ConditionalTransition`, `ExplicitProhibit`, `InvalidTransition`, `JourneyTrace`)
  * `RationaleId` (string?)
  * `CompositeId` (string?)
  * `SourceRef` (string: `REQ-xxx`, `TERM-LC-xxx`, `JRN-xxx`)
  * `ViewpointTitle` / `ViewpointTitleEn` (string)
  * `Title` / `TitleEn` (string)
  * `GivenConditions`: `List<TestConditionEntry>`（語彙ID、日本語/英語ラベル、値）
  * `WhenSteps`: `List<TestActionEntry>`（アクター、イベント/行動、ステップ番号）
  * `ThenTextJa` / `ThenTextEn` (string)
  * `ExpectedOutcome` (`StateChanged`, `ActionEmitted`, `Blocked`, `NoOp`, `AlternativeFired`)
  * `ReviewNoteJa` / `ReviewNoteEn` (string?)
  * `Tags`: `List<string>`（例：`@RAT-001`, `@COMP-001`, `@REQ-001-1`, `@safety`）

### 3.2 決定論的テストケース ID 採番ルール（ID ズレ防止）
単純連番（`TC-001`）は途中の要求追加で全行の ID がズレて差分・トレースが壊れるため、以下の自然キー＋スコープ内連番を採用する。

| 階層 | フォーマット | 例 | 備考 |
| :--- | :--- | :--- | :--- |
| **`Unit`** | `TC-{ReqId}-{Kind}-{Seq:D2}` | `TC-REQ-001-1-POS-01`<br>`TC-REQ-001-1-NEG-01`<br>`TC-REQ-002-1-BLK-01`<br>`TC-REQ-002-1-UNB-01` | `POS`: 正常発火 / `NEG`: 1条件反転 / `BLK`: 禁止ブロック / `UNB`: 禁止解除 |
| **`Component` (FSM)** | `TC-FSM-{ShortTermId}-{FromValue}-{ShortEventId}`<br>*(同一セル複数ケース時は末尾に `-{Seq:D2}`)* | `TC-FSM-LC001-Locked-EV001` | `TERM-` プレフィックスを除去して短縮。セルと 1 対 1 で対応 |
| **`E2E`** | `TC-{JourneyId}` | `TC-JRN-001`<br>`TC-JRN-002` | Journey ID と 1 対 1 |

**固定ソート順（Git diff 安定化）：**
`TestLevel` (`Unit` ➔ `Component` ➔ `E2E`) ➔ `RationaleId` ➔ `CompositeId` ➔ `SourceRef` ➔ `TestCaseId`

### 3.3 テストケース導出アルゴリズムと「全要求再評価」

#### (A) Unit 層（`AtomicRequirement` ごと）
1. **通常要求（`Effect != Prohibit`）：**
   * **`POS`（正常発火）：** 全 `Preconditions`（および `MutateState` の `FromValue`）を満たす状態辞書を構築し、当該要求のトリガーで期待効果を確認。
   * **`NEG`（1条件反転）：** 各 `Precondition`（および `FromValue`）を 1 つずつ非成立値へ反転させる（`Bool` は反対値、`Enum` は `AllowedValues` 内の非一致値。複数ある場合は定義順に全件展開）。他の条件は成立値のまま固定し、後述の「全要求再評価」で期待結果を決定する。
2. **禁止要求（`Effect == Prohibit`）：**
   * **`BLK`（禁止ブロック）：** 禁止条件を満たす状態＋トリガーで `ExpectedOutcome = Blocked` を確認（`StateDriven` でトリガー未指定の場合は、禁止対象 `TargetTermRef` かつ `ProhibitedBehaviorDefRef` に対応する通常要求のトリガーを補完して検証）。
   * **`UNB`（禁止解除）：** 禁止の前提条件を 1 つずつ崩した状態で「全要求再評価」を実行し、ブロックされずに正常系（`AlternativeFired`）または `NoOp` になることを確認。

#### (B) Component 層（`LifecycleState` ごとの 2 次元状態遷移マトリクス）
* 行（`States`）× 列（関連 `Events`）の全交点セルについて、`LifecycleState = FromState` ＋ `Trigger = Event` に該当する要求を抽出する。
* **ガード条件がない場合：** 単一の有効遷移（`ValidTransition`）、明示的禁止（`ExplicitProhibit`）、または未定義セル（`UndefinedNoOp`）として 1 ケース生成。
* **ガード条件がある場合（`ConditionalTransition`）：** 関連する他 `StateVariable` の条件成立パターン（有効遷移／禁止）と、条件を崩したパターンをそれぞれ生成し、全要求再評価で期待結果を解決する。
* **`UndefinedNoOp`（純粋な空白セル）：** `ExpectedOutcome = NoOp` とし、`ReviewNote` に「未定義セル：意図的な無視（No-op）か仕様漏れか要確認」を自動付与する。

#### (C) E2E 層（`CustomerJourney` ごと）
* 既存の `JourneySimulator` の評価結果を取り込み、`InitialStates` を `Given`、各ステップの操作列を番号付き `When`、各ステップの遷移・ブロック結果および `FinalStates` を `Then` として展開する。
* `BlockedCount > 0` のフローには `@safety` タグを付与し、`ExpectedOutcome` は最終評価に応じて設定する。

#### (D) 期待結果の全要求再評価ロジック（重要）
条件反転（`NEG` / `UNB`）・ガード不成立・無効遷移ケースでは、雑に「発火しないこと」とせず、構築した `(StateDictionary, TriggerEvent)` に対してドキュメント内の**全 `AtomicRequirement` を決定論的に再評価**する。

1. **いずれかの `Prohibit` 要求が条件一致した場合：**
   * `ExpectedOutcome = Blocked`
   * `Then`：「`REQ-xxx` により操作がブロックされ、状態が維持されること」
2. **別の通常要求（`MutateState` / `ManageEntity` / `EmitAction`）が条件一致した場合：**
   * `ExpectedOutcome = AlternativeFired`
   * `Then`：「代替要求 `REQ-xxx` が発火し、`[該当要求の期待動作]` となること」
3. **どの要求も条件一致しない場合：**
   * `ExpectedOutcome = NoOp`
   * `Then`：「該当する要求が存在しないため、対象状態は変化しないこと」

> **検証イメージ（`DoorLock` サンプル）：**
> 通常施錠要求（`ドア開閉状態 == 全閉`）の 1 条件反転（`ドア開閉状態 = 半ドア`）を再評価すると、自動で半ドア施錠インターロック要求（`Prohibit`）にヒットし、`ExpectedOutcome = Blocked` として正確な期待結果が導出される。

---

## 4. エクスポート仕様（3形式）

### 4.1 Gherkin (`.feature`) エクスポーター
* **言語切替：** 日本語（`# language: ja`：`機能 / ルール / シナリオアウトライン / シナリオ / 前提 / かつ / もし / ならば / 例`）と英語（`Feature / Rule / Scenario Outline / Scenario / Given / And / When / Then / Examples`）を切替可能。
* **自動化互換記法：** 用語名および状態値は `"..."` で囲み、Step Definition（Reqnroll / SpecFlow / Cucumber 等）へそのままバインド可能にする。
* **タグ付け：** `@RAT-xxx`, `@COMP-xxx`, `@REQ-xxx`, `@JRN-xxx`, `@level:component|unit|e2e`, `@technique:...`, `@safety` を付与。
* **テスト階層別構造：**
  * **Component + Unit：** `CompositeRequirement` を `Feature:` とし、配下の `AtomicRequirement` ごとに `Rule:` ＋ `Scenario Outline`（`Examples` 表に `ケースID`・前提変数列・`期待結果`）を出力。
  * **FSM：** `LifecycleState` ごとに `Feature:` を切り、状態遷移ケースを出力。
  * **E2E：** `CustomerJourney` ごとに独立した `Feature:` を切り、連続ステップの `Scenario` を出力。

```gherkin
# language: ja
@RAT-001 @COMP-001 @level:component
機能: ドアロック施錠および安全インターロック制御

  @REQ-001-1 @level:unit @technique:condition-matrix
  ルール: 通常施錠（全閉時の施錠スイッチ押下）
    シナリオアウトライン: 施錠スイッチ押下による施錠と1条件反転の確認
      前提 "ドアロック状態" が "<ドアロック状態>" である
      かつ "ドア開閉状態" が "<ドア開閉状態>" である
      もし "運転者" が "施錠スイッチ押下" を実行する
      ならば <期待結果>

      例:
        | ケースID            | ドアロック状態 | ドア開閉状態 | 期待結果 |
        | TC-REQ-001-1-POS-01 | 解錠           | 全閉         | "ドアロック状態" が "施錠" になること |
        | TC-REQ-001-1-NEG-01 | 解錠           | 半ドア       | "REQ-002-1" により操作がブロックされ状態が変わらないこと |
```

### 4.2 CSV エクスポーター（RFC 4180 準拠・UTF-8 BOM 付き）
Excel での文字化け防止のため **UTF-8（BOM付き）** で出力し、複数条件・複数ステップは `"` で囲んだセル内改行（`\n`）とする。

**基本 13 列構成（順序固定）：**

| # | 列名 | 内容 |
| :-: | :--- | :--- |
| 1 | `TestCaseId` | 決定論的 ID（Upsert キー） |
| 2 | `TestLevel` | `Unit` / `Component` / `E2E` |
| 3 | `Technique` | 適用テスト技法名 |
| 4 | `RationaleId` | 最上位の根拠 ID（なければ空） |
| 5 | `CompositeId` | 親要求 ID（観点コンテナ ID、なければ空） |
| 6 | `SourceRef` | 直接の導出元 ID（`REQ-xxx`, `TERM-LC-xxx`, `JRN-xxx`） |
| 7 | `ViewpointTitle` | 観点コンテナ名 |
| 8 | `Title` | テストケースの 1 行要約 |
| 9 | `Given` | 前提条件・初期状態（複数行はセル内改行） |
| 10 | `When` | 入力・トリガー（E2E は番号付き手順） |
| 11 | `Then` | 期待結果（全要求再評価済み） |
| 12 | `ExpectedOutcome` | `StateChanged` / `ActionEmitted` / `Blocked` / `NoOp` / `AlternativeFired` |
| 13 | `ReviewNote` | 未定義セル確認・Escape 手動設計要などの自動注記 |

* **任意オプション（UI チェックボックス、デフォルト OFF）：** 「実行記録用の空列を含める」を ON にした場合のみ、末尾に `Result`, `Tester`, `ExecutedAt`, `Comment` の 4 列を追加する。

### 4.3 Markdown エクスポーター
レビュー資料や GitHub PR 添付向けに、1 ファイルで以下を出力する。
1. サマリー（総数、要求カバレッジ率、未定義セル件数、Escape 手動確認件数）
2. 観点コンテナ別テストケース一覧（`Rationale ➔ CompositeRequirement` / `FSM` / `Journey`）
3. `LifecycleState` ごとの状態遷移マトリクス表（Markdown テーブル）

---

## 5. Blazor UI 仕様 (`src/ReqPipeline.Web`)

### 5.1 ナビゲーションと全体構成
* ヘッダーナビゲーションに 7 つ目のタブ **`Test View`**（ルート: `/tests`）を追加。
* **上部サマリー＆エクスポートバー：**
  * バッジ表示：`観点コンテナ数`、`総ケース数`、`要求カバレッジ（%）`、`未定義セル数`、`Escape 要手動確認数`。
  * 言語切替（日本語 / English）トグル。
  * エクスポートボタン群：**Markdown（コピー / DL）**、**CSV（DL + 実行列オプション）**、**Gherkin（コピー / DL + レベル選択）**。

### 5.2 左ペイン：テスト観点コンテナツリー
1. **`すべて表示`（全テストケース一覧）**
2. **要求・達成基準軸（WDD 由来コンテナ）：** `Rationale` ➔ `CompositeRequirement` の 2 階層ツリー。各親要求にケース数バッジ、配下に `EscapeRecord` がある場合はオレンジの「手動設計要」アイコンを表示。
3. **横断モデル軸：**
   * `FSM 状態遷移マトリクス`（`LifecycleState` ごとに項目を配置、未定義セル数をバッジ表示）
   * `E2E シナリオ`（`Journey` ごとに項目を配置、正常／ブロック種別を表示）

### 5.3 右ペイン：詳細ビュー＆インタラクティブ 2 次元状態遷移マトリクス
* **共通フィルタバー：** `TestLevel` / `Technique` / `ExpectedOutcome` フィルタ、および **`「未定義セル（No-op）ケースを表示」`** トグル。
* **FSM 状態遷移マトリクス選択時（外部ライブラリ不要の純粋 HTML テーブル）：**
  * 横スクロール対応（`overflow-x-auto`）＋ 左端の状態列を `sticky left-0` 固定。
  * セルの 4 分類色分けバッジ：
    * **有効遷移 (`ValidTransition`):** 青系（例：`➔ 施錠`）
    * **条件付き遷移 (`ConditionalTransition`):** インディゴ／シアン系（例：`➔ 施錠 (条件あり)`）
    * **明示的禁止 (`ExplicitProhibit`):** オレンジ／赤系（例：`⛔ ブロック`）
    * **純粋な空白 (`UndefinedNoOp`):** グレー系（例：`— 未定義`）
  * **`「未定義セルのみハイライト」` トグル：** ON にすると空白セル以外を淡くし、仕様漏れレビューを視覚的に補助。
  * **セルクリック展開：** 任意のセルをクリック（`@onclick`）するとテーブル下部に詳細パネルが展開され、該当セルの全テストケース（`Given / When / Then`、`ReviewNote`、該当 `REQ-xxx` への `Workbench で開く →` リンク）を表示する。
* **要求コンテナ／E2E／すべて表示選択時：**
  * 親要求配下に `EscapeRecord` がある場合、先頭に「手動テスト設計が必要な Escape 要求」の注意バナーを表示。
  * テストケースをカードまたはコンパクトテーブルで表示し、各ケースから元の `REQ-xxx` / `JRN-xxx` へ `Workbench で開く →` / `Journey で開く →` のリンクで遷移可能にする。

---

## 6. 実装フェーズと完了条件

### Phase 1: Core 導出エンジン・エクスポーター・単体テスト (`ReqPipeline.Core` & `ReqPipeline.UnitTests`)
1. `TestSpecificationGenerator` および期待結果の全要求再評価ロジックを実装。
2. 決定論的 ID 採番・固定ソート・3 形式エクスポーター（Gherkin / CSV［UTF-8 BOM・RFC4180］ / Markdown）を実装。
3. ユニットテストを追加し、以下を検証して `dotnet test` を全件グリーンにする：
   * `crud-sample.v2.json` および `doorlock-sample.v2.json` 両方での導出結果の決定論性（2 回実行して完全一致）。
   * 途中への要求追加時に他要求の `TestCaseId` がズレないこと。
   * `DoorLock` の半ドア 1 条件反転ケースが `Blocked`（`REQ-002-1` ヒット）へ自動解決されること。
   * FSM マトリクスで無関係イベントが除外され、4 分類が正しく判定されること。
   * CSV の RFC 4180 準拠（セル内改行・クォート）と Gherkin の日英出力・タグ・`"..."` 括り。

### Phase 2: Blazor UI 実装 (`ReqPipeline.Web`)
1. `/tests` ページ（左ペイン観点ツリー、右ペイン詳細＆インタラクティブ 2 次元状態遷移マトリクス、フィルタ・未定義セルハイライト）を実装。
2. Markdown / CSV / Gherkin のクリップボードコピーおよびファイルダウンロード機能を実装。

### Phase 3: クロスドメイン実機確認 & V2.0.0 リリース仕上げ
1. `dotnet run --project src/ReqPipeline.Web` 上で `CRUD` ⇄ `DoorLock` サンプルを切り替えながら、`Test View` のマトリクス・セルクリック展開・3 形式ダウンロードを確認。
2. `README.md` / `README_ja.md` に `Test View` の説明（および WDD ➔ Glossary ➔ ReqPipeline V2 ➔ Test View の全体像）を追記し、`V2.0.0` リリース準備を完了する。

---

## 7. 実装時のエッジケース補足ルール

1. **全要求再評価（3.3 D）における未指定変数・複数ヒット時の扱い:**
   * 再評価対象の要求 `R` がヒットするのは、`R` のすべての前提条件（および `MutateState` の `FromValue`）の変数が現在の `StateDictionary` に**存在し、かつ値を満たす場合のみ**とする（未束縛変数を必要とする要求は誤発火させない）。
   * 複数の要求が同時にヒットした場合、`Effect == Prohibit` の要求が 1 件でも含まれていれば **`ExpectedOutcome = Blocked`** を優先する。`Prohibit` が含まれない場合は、ヒットした要求 ID と期待動作をすべて列挙して `AlternativeFired` とする。
2. **FSM マトリクスセルの共存表現（3.1 / 5.3）:**
   * `MatrixCell` に `HasProhibit` (`bool`) を持たせる。
   * 同一セルに条件付き遷移（`ConditionalTransition`）と禁止（`Prohibit`）が共存する場合は、UI 上で複合バッジ（例：`➔ 解錠 (条件あり) / ⛔ ブロック`）として明示する。
3. **前提条件 0 個の `Atomic` 要求の Gherkin 出力（4.1）:**
   * `Preconditions` および `FromValue` がなく変数列が 0・ケースが 1 件のみの `Rule:` は、`Scenario Outline` ではなく通常の **`Scenario:`（シナリオ）** として出力する。