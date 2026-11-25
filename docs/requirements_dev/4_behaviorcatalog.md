# ReqPipeline V2 設計メモ：4意味プリミティブ ＋ ユーザー定義 BehaviorCatalog と多ドメイン検証計画

## 1. 背景と設計哲学

### 1.1 課題：CRUD依存のハードコードからの脱却
現在の ReqPipeline V2（MVP）では、要求の振る舞いが CRUD に寄せた6種類（`CREATE`, `DELETE`, `SET`, `TRANSITION`, `DISPLAY`, `PROHIBIT`）として C# の `enum` にハードコードされている。  
このままでは、車載制御・設備制御・在庫引当・承認ワークフローといった別ドメインへ適用しようとした際に、適切な動詞がなく Escape Hatch（自由記述）へ逃げざるを得なくなる。かといって、あらゆる業界の動詞を Core 側に無数に追加し続けることも現実的ではない。

### 1.2 基本思想：論理空間と物理空間の分離
ReqPipeline V2 は、システムズエンジニアリング（RFLP）における**論理空間（状態・遷移・規範）の探索**に特化する。

* **スコープ内（論理空間・決定論100%）：** `Bool / Enum` の有限状態、イベント駆動・状態駆動の遷移、許可と禁止の矛盾検知（Linter）、状態空間の完全性（FSM）、目的達成までの経路検証（Journey）。
* **スコープ外（物理空間・専用ツールへ委譲）：** 時相論理（タイマー・継続時間）、連続値・複雑な数値計算、UIの色やレイアウトといった実装詳細（How）。これらは意図的に割り切り、必要に応じて Escape Hatch へ記録するか、専用ツール・実装フェーズに委ねる。

### 1.3 人間・AI・決定論エンジンの責務分離
ツール全体の運用において、それぞれの強みに合わせて役割を明確に分離する。

| 担当 | 領域 | 具体的な役割 |
| :--- | :--- | :--- |
| **AI（確率論）** | **WDD（Why Driven Development）** | 目的・背景・ステークホルダーの価値観を壁打ちし、要求の種を発散・言語化する。Escape に退避した曖昧な文の分解ヒントを出す。 |
| **人間（設計者）** | **情報モデル・動詞カタログ設計** | ドメインの本質を見抜き、`Glossary`（状態・イベント）と `BehaviorCatalog`（動詞）を適切な抽象度で削り出す。 |
| **ReqPipeline（決定論）** | **整合性・完全性・疎通の検証** | 確率を一切挟まず、仕様の矛盾（`LINT-008`）、孤立・行き止まり状態（FSM）、シナリオのしりとり断絶（Journey）を即座に検知する。 |

---

## 2. 2層アーキテクチャ：最小命令セット × ユーザー定義カタログ

検証エンジンのシンプルさとドメイン汎用性を両立するため、振る舞いを**「Core固定の意味プリミティブ（4種）」**と**「ユーザー定義の振る舞いカタログ」**の2層に分離する。

### 2.1 エンジン層：4つの意味プリミティブ (`SemanticEffect`)
計算機科学（有限状態機械）と規範論理（Deontic Logic）の観点から、離散状態の決定論的検証に必要な作用は以下の4つに収束する。

| `SemanticEffect` | 役割（システムへの作用） | 検証エンジンでの扱い |
| :--- | :--- | :--- |
| **`MutateState`** | 内部状態（`StateVariable` / `LifecycleState`）の値を書き換える（旧 `SET` / `TRANSITION`） | FSMのエッジ抽出対象。Journeyでは状態辞書を更新し、Linterで遷移先の競合を検知する。 |
| **`ManageEntity`** | ドメインオブジェクトを生成・消滅・記録する（旧 `CREATE` / `DELETE`） | 前提条件を満たせば `PASS`。`Prohibit` との衝突検知対象。 |
| **`EmitAction`** | 内部状態を変えず外部へ出力・通知・実行する（旧 `DISPLAY`、送信、鳴動など） | 状態辞書を変更せず `PASS`。`Prohibit` との衝突検知対象。 |
| **`Prohibit`** | 特定の振る舞いや状態遷移を禁止・ブロックする（旧 `PROHIBIT`、インターロックなど） | Journeyでは `BLOCKED` として評価。他プリミティブとの矛盾を `LINT-008` で検知する。 |

> **シンプルさ優先の方針（現フェーズ）：**  
> `ManageEntity` 同士の相反関係（例：「作成」と「削除」の同時発生）の検知は初期段階では導入せず、まずは **「`Prohibit` vs 他プリミティブの衝突」** と **「`MutateState` 同士の遷移先競合」** のみに絞って運用する。実運用で不足を感じた場合のみ、カタログ側への排他定義追加を検討する。

### 2.2 ドメイン層：振る舞いカタログ (`BehaviorDefinition`)
要求定義者がプロジェクトの正本（JSON）内に独自の動詞を定義・拡張できる層。すべてのカスタム動詞はいずれかの `SemanticEffect` に紐づくため、ユーザーが増やした動詞であっても Linter・FSM・Journey がそのまま機能する。

---

## 3. 論理空間モデリングの実践指針

4つのプリミティブと `Bool / Enum` で実務の要求を綺麗に捌くための設計パターンを整理する。

### 3.1 述語抽象化（Predicate Abstraction）による数値・集合の扱い
件数や大小比較、所有権チェックといった条件は、振る舞いが分岐する論理的境界だけを `Bool` または少数 `Enum` へ抽象化する。

* **件数制約の例：** 「明細が1件以上あること」 ➔ `明細存在状態 (Bool: true=1件以上 / false=0件)`
* **数量比較の例：** 「在庫数が注文数以上であること」 ➔ `在庫引当可能 (Bool)`
* **所有権の例：** 「自分が作成したデータのみ編集可」 ➔ `作成者本人 (Bool)`

**追加と削除の非対称性への対処：**
`Bool` のみでは「削除後に0件になるか、1件以上残るか」が区別できない。論理空間では以下のいずれかで割り切る。
1. **観測状態（入力条件）として割り切る（推奨）：** 状態遷移の対象にはせず、Journey の `InitialStates` チップを切り替えて各分岐（0件時の禁止／1件以上時の確定）を検証する。
2. **イベントまたは状態を分ける：** 「最終明細削除イベント」を分けるか、3値 `Enum`（`0件 / 1件 / 2件以上`）にする。

### 3.2 ワンショット出力 vs 保持出力の切り分け
* **鳴って終わる出力（ブザー、通知送信、ログ出力）：** 別の要求から現在状態を参照しないため **`EmitAction`** を使う。
* **解除が必要な保持出力（警告灯点灯、モーダル表示、ロック保持）：** 後続の要求で「点灯中なら消灯する」と参照するため **`MutateState`**（例：`警告灯状態: OFF -> ON`）としてモデル化する。これにより、消し忘れが FSM View で `Out = 0` として自動検出される。

---

## 4. リファクタリング詳細仕様

### 4.1 Core AST の変更 (`src/ReqPipeline.Core`)

1. **`SemanticEffect` enum の導入**（旧 `BehaviorKind` を廃止）
   - `MutateState`, `ManageEntity`, `EmitAction`, `Prohibit`
2. **`BehaviorDefinition` の追加**（`KnowledgeGraphDocument.BehaviorCatalog` として保持）
   - `BehaviorDefId` (string): 一意ID（例：`BEH-TRANSITION`, `BEH-LOCK`）
   - `CanonicalLabel` (string) / `JapaneseLabel` (string) / `Description` (string)
   - `Effect` (`SemanticEffect`)
   - `AllowedTargetTypes` (`List<ConceptType>`): 対象スロットに許可する概念型
   - `RequiresFromValue` (bool) / `RequiresToValue` (bool): `MutateState` 用スロット制御
   - `JapaneseTemplate` / `EnglishTemplate` (string): `{target}`, `{from}`, `{to}`, `{prohibitedAction}` を展開
3. **`BehaviorNode` の型安全化**
   - `BehaviorDefRef` (string): `BehaviorCatalog` へのID参照
   - `TargetTermRef` (string): `Glossary` へのID参照
   - `FromValue` (string?) / `ToValue` (string?): 旧 `SetValue` を `ToValue` に統合
   - `ProhibitedBehaviorDefRef` (string?): 旧自由文字列 `ProhibitedAction` を廃止し、カタログID参照で型安全化（`null` は対象への全操作禁止）
   - `ProhibitedToValue` (string?): 特定値への状態遷移のみ禁止する場合に指定

### 4.2 検証エンジンと Renderer の更新
* **GraphLinter:**
  - `BehaviorDefRef` 存在チェック、`AllowedTargetTypes` 型一致チェック、`MutateState` の `FromValue`/`ToValue` 閉包チェック、`Prohibit` の自己禁止防止を追加。
  - **`LINT-008`（要求衝突検知）：** 前提条件とトリガーが重複するペア `(A, B)` に対し、① `Prohibit` との衝突（`TargetTermRef` 一致、かつ `ProhibitedBehaviorDefRef == null` またはID一致・`ProhibitedToValue` 一致）、② `MutateState` 同士の競合（`TargetTermRef` 一致、`FromValue` 重複、`ToValue` 不一致）をエラー検知。
* **FsmAnalyzer:**
  - `Effect == MutateState` かつ `TargetTerm.ConceptType == LifecycleState` かつ `FromValue != null && ToValue != null` の要求を遷移エッジとして抽出。
* **JourneyEvaluator:**
  - `MutateState`: `FromValue` が現在値と一致すれば `PASS` ＆ 状態更新（不一致なら `FAIL`）。
  - `ManageEntity` / `EmitAction`: 前提条件一致で `PASS`（状態変更なし）。
  - `Prohibit`: 前提条件一致で `BLOCKED`（状態変更なし）。
* **Renderer:**
  - `BehaviorDefinition` の日英テンプレートにプレースホルダを差し込む汎用処理へ統一。

### 4.3 CRUD標準プリセット（後方互換）
`samples/crud-sample.v2.json` を新スキーマへ移行し、以下の6件を初期カタログとして定義する。

| ID | 日本語名 | `Effect` | `AllowedTargetTypes` | `RequiresFrom` | 日本語テンプレート |
| :--- | :--- | :--- | :--- | :---: | :--- |
| `BEH-CREATE` | 作成する | `ManageEntity` | `DomainObject` | `false` | `{target}を作成すること` |
| `BEH-DELETE` | 削除する | `ManageEntity` | `DomainObject` | `false` | `{target}を削除すること` |
| `BEH-SET` | 設定する | `MutateState` | `StateVariable`, `LifecycleState` | `false` | `{target}を{to}に設定すること` |
| `BEH-TRANSITION` | 遷移する | `MutateState` | `LifecycleState` | `true` | `{target}を{from}から{to}へ遷移すること` |
| `BEH-DISPLAY` | 表示する | `EmitAction` | `DomainObject` | `false` | `{target}を表示すること` |
| `BEH-PROHIBIT` | 禁止する | `Prohibit` | `DomainObject`, `LifecycleState`, `StateVariable` | `false` | `{target}の{prohibitedAction}を禁止すること` |

### 4.4 Blazor UI の更新方針 (`src/ReqPipeline.Web`)
個人開発での保守性を優先し、テキストDSL＋LSPによるエディタ二重管理は行わず、**Blazor Workbench を主戦場**として拡張する。

1. **Behavior Catalog 管理画面の追加:** 動詞の追加・編集・削除。`SemanticEffect` 選択時に推奨設定とテンプレート雛形を自動補完し、ダミー語彙でプレビュー表示。
2. **Workbench スロットの動的化:** `BehaviorCatalog` から動詞ドロップダウンを動的生成し、選択した動詞の `Effect` / `RequiresFromValue` に応じて入力スロットと候補語彙を動的にフィルタリング。
3. **サンプル切替UI:** ヘッダー等から `CRUDサンプル` と `車載ドアロックサンプル` をワンクリック切替可能にする。

---

## 5. 多ドメイン検証計画：`doorlock-sample.v2.json`

CRUD以外のドメイン（イベント駆動・離散制御）において、**検証エンジンのコードを1行も変えずに**矛盾検知・FSM・Journeyが成立することを実証する。

### 5.1 題材：車両ドアロック＆警告制御システム
* **主な用語（Glossary）:**
  - `System`: ドアロック制御ECU
  - `Actor`: 運転者
  - `LifecycleState`: `ドアロック状態`（`解錠`, `施錠`）
  - `StateVariable`: `ドア開閉状態`（`全閉`, `半ドア`）、`走行状態`（`停車中`, `走行中`）
  - `Event`: `施錠スイッチ押下`, `解錠スイッチ押下`, `施錠完了イベント`, `ロック異常検知`
  - `DomainObject`: `アンサーバックブザー`, `故障コード(DTC)`

### 5.2 自作動詞カタログ (`BehaviorCatalog`)
CRUD用の動詞を一切使用せず、制御ドメイン固有の5動詞を定義する。

| ID | 日本語名 | `Effect` | 対象型 | 日本語テンプレート |
| :--- | :--- | :--- | :--- | :--- |
| `BEH-LOCK` | 施錠する | `MutateState` | `LifecycleState` | `{target}を{from}から{to}へ施錠すること` |
| `BEH-UNLOCK` | 解錠する | `MutateState` | `LifecycleState` | `{target}を{from}から{to}へ解錠すること` |
| `BEH-CHIME` | 鳴動させる | `EmitAction` | `DomainObject` | `{target}を鳴動させること` |
| `BEH-RECORD-DTC` | 記録する | `ManageEntity` | `DomainObject` | `{target}を記録すること` |
| `BEH-INTERLOCK` | インターロックする | `Prohibit` | `LifecycleState`, `DomainObject` | `{target}の{prohibitedAction}をインターロックすること` |

### 5.3 検証シナリオ（確認ポイント）
1. **静的衝突検知（`LINT-008`）:**
   - 「`走行状態 == 走行中` で `解錠スイッチ押下` ➔ `ドアロック状態` の `BEH-UNLOCK` をインターロックする（`Prohibit`）」と、「`走行状態 == 停車中` での通常解錠」が衝突しないことを確認。
   - 意図的に通常解錠の `走行状態 == 停車中` 条件を外した瞬間、`LINT-008` が赤く点灯することを確認。
   - 「`ドア開閉状態 == 半ドア` の間（状態駆動・トリガーなし）、`BEH-LOCK` をインターロックする」が、半ドア除外を忘れた施錠要求を正しく検知できることを確認。
2. **FSM View:**
   - `ドアロック状態` の `解錠 ⇄ 施錠` が Mermaid でライブ描画され、両状態が `Reachable` かつ `In/Out` 健全と判定されることを確認。
3. **Journey ウォークスルー:**
   - **JRN-001（正常系）：** `ドア開閉状態=全閉` / `ドアロック状態=解錠` から施錠し、アンサーバック鳴動まで `All Steps Connected` となること。
   - **JRN-002（異常系）：** `走行状態=走行中` / `ドアロック状態=施錠` の状態で解錠スイッチを押し、`BLOCKED` となって施錠状態が維持されること。

---

## 6. 実装ロードマップ

1. **Phase 1（Core & UnitTests）:**
   - `SemanticEffect` / `BehaviorDefinition` 導入、各検証エンジン・Rendererの移行、`crud-sample.v2.json` 更新、ユニットテスト全件パス。
2. **Phase 2（Blazor UI）:**
   - Behavior Catalog 管理UI追加、Workbench のスロット動的化。
3. **Phase 3（多ドメイン検証）:**
   - `doorlock-sample.v2.json` 作成とサンプル切替UI実装、ブラウザ上で衝突検知・FSM・Journeyの動作検証を完了する。