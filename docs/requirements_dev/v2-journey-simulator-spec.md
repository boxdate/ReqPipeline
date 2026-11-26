# V2 カスタマージャーニー答え合わせ（Journey Simulator）＆ CRUDサンプル拡張 実装仕様書

## 1. 目的とコンセプト
* **背景（「イマジナリー自販機」メソッド）:**
  * 静的な情報モデル（Glossary）や個別の `Atomic` 要求だけでは、「実際のユーザーが最初から最後まで操作したときに、画面遷移や状態のバトンが途切れずに繋がっているか（戻り導線の抜け・前提条件の不整合など）」が見えにくい。
  * **カスタマージャーニー（ユーザー行動ステップ列）** を正本AST上で決定論的にシミュレーションし、多面的にヌケモレ・振る舞いの不連続を検出できるようにする。
* **技術スタック:** .NET 10.0 / C# / Blazor (`src/ReqPipeline.Web`) / xUnit (`tests/ReqPipeline.UnitTests`)
* **設計原則:**
  * 100% 決定論的（LLM・外部ツール不要）。
  * コア（`JourneySimulator`・診断メッセージ・AST）は英語シンボル正本、Web UI 上は日本語表示名（`GetDisplayName()` / `LogicalType.GetValueLabel()`）を併用する。
  * `KnowledgeGraphDocument` への追加はオプショナル（`Journeys = []`）とし、既存JSONと完全互換を保つ。

---

## 2. ドメインモデル拡張 (`src/ReqPipeline.Core/Domain/`)

### 2.1 `JourneyModels.cs`（新規）
```csharp
public sealed class UserJourney
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    /// <summary>開始時の状態（Glossary TermId -> 英語正規値）</summary>
    public Dictionary<string, string> InitialStates { get; set; } = new(StringComparer.Ordinal);
    public List<JourneyStep> Steps { get; set; } = [];
}

public sealed class JourneyStep
{
    public string StepId { get; set; } = string.Empty;
    /// <summary>ユーザー行動の自然な説明（例: 「新規登録ボタンを押して作成画面へ移動する」）</summary>
    public string UserAction { get; set; } = string.Empty;
    /// <summary>この行動で発火・実行される Atomic 要求の ID</summary>
    public string RequirementRef { get; set; } = string.Empty;
}
```

### 2.2 `KnowledgeGraphDocument.cs`（追記）
* `public List<UserJourney> Journeys { get; set; } = [];` を追加する。

---

## 3. 決定論的ジャーニー検証エンジン (`src/ReqPipeline.Core/Analysis/JourneySimulator.cs`)

純粋関数 `JourneySimulator.Simulate(KnowledgeGraphDocument document, UserJourney journey)` を実装し、`JourneySimulationResult` を返す。

### 3.1 状態初期化
* `currentState`（`Dictionary<string, string>`）を以下の順で初期化する：
  1. `document.Glossary` の各 `LifecycleState`（`Enum`）について、未指定なら `AllowedValues.FirstOrDefault()` を既定初期値として設定。
  2. `journey.InitialStates` の値で上書き。

### 3.2 ステップ評価ルール（先頭から順番にしりとり検証）
各 `JourneyStep` ごとに `JourneyStepResult`（`Status`, `StateBefore`, `StateAfter`, `TransitionSummary`, `Message`）を算出する：

1. **要求参照チェック:** `RequirementRef` が `document.Requirements` に存在し、`NodeKind == Atomic` かつ `Behavior != null` であること。
   * 満たさない場合 ➔ `JourneyStepStatus.InvalidRequirement`（失敗）
2. **前提条件チェック (`Preconditions`):**
   * 要求のすべての `PreconditionClause` について、`currentState[StateTermRef]` が `EQ` / `NEQ` / `IN` を満たすか判定する。
   * 未設定または不一致 ➔ `JourneyStepStatus.PreconditionFailed`（失敗、期待値と現在値を明示）
3. **動作と状態のバトンチェック (`Behavior`):**
   * **`TRANSITION`:**
     * `currentState[TargetTermRef]` が `Behavior.FromValue` と一致するか検証。
     * 一致 ➔ `currentState[TargetTermRef] = Behavior.ToValue` へ更新し、`JourneyStepStatus.Passed`。
     * 不一致 ➔ `JourneyStepStatus.StateMismatch`（失敗：状態のバトン断絶、`Expected FromValue 'X', but current is 'Y'`）。
   * **`SET`:**
     * `currentState[TargetTermRef] = Behavior.SetValue` へ更新し、`JourneyStepStatus.Passed`。
   * **`PROHIBIT`:**
     * 状態は変更せず、`JourneyStepStatus.Blocked`（禁止動作が発動したことを明示。異常系シナリオの確認に使える）。
   * **`CREATE` / `DELETE` / `DISPLAY`:**
     * 前提条件を満たしていれば `JourneyStepStatus.Passed`（状態維持）。

### 3.3 サマリー情報 (`JourneySimulationResult`)
* `IsAllPassed`: すべてのステップが `Passed`（または意図された `Blocked` を除く全失敗が0件）であるか。
* `FailedStepCount`: `InvalidRequirement` / `PreconditionFailed` / `StateMismatch` の件数。
* `FinalStates`: 全ステップ終了後の状態マップ（初期状態へ戻れているかの確認用）。

---

## 4. CRUD サンプルの拡張 (`samples/crud-sample.v2.json`)
既存の顧客管理CRUDサンプルを拡張し、FSM View と Journey Simulator の両方で実践的な遷移・戻り導線を体験できるようにする：

* **`ScreenState` (`LifecycleState` Enum) を 4 状態へ拡張:**
  * `List`（一覧） / `Create`（作成） / `Detail`（詳細） / `Edit`（編集）
* **追加する遷移・振る舞い（既存テストの整合性を維持しつつ追加）:**
  * `List -> Create`（新規作成画面へ遷移）
  * `Create -> List`（作成完了またはキャンセルで一覧へ戻る）
  * `List -> Detail`（既存：詳細画面へ遷移）
  * `Detail -> Edit`（編集画面へ遷移）
  * `Edit -> Detail`（更新保存またはキャンセルで詳細へ戻る）
  * `Detail -> List`（一覧へ戻る）
* **プリセット `Journeys` を2本同梱:**
  1. `JRN-001`: **新規登録〜詳細確認〜編集〜一覧復帰フロー**（全ステップ `Passed` で綺麗に `List` に戻る正常系）
  2. `JRN-002`: **管理者以外の削除ブロック確認フロー**（`UserRole = Member` で `Detail` から削除を試み、`Blocked` が発動することを確認する異常系）

---

## 5. Web UI 実装 (`src/ReqPipeline.Web/`)

### 5.1 ナビゲーション
* `MainLayout.razor` のヘッダーに **`Journey`** タブ（`/journey`）を追加する（`Workbench` / `Glossary` / `USDM View` / `FSM View` / `Journey`）。

### 5.2 `JourneyView.razor` (`/journey`)
* **左ペイン（ジャーニー一覧 & 初期状態設定）:**
  * ジャーニーの選択・新規追加（`+ ジャーニー追加`）・削除。
  * **初期状態（`InitialStates`）エディタ:** `LifecycleState` と `StateVariable`（`Boolean` / `Enum`）をチップで切り替え可能。値を変えた瞬間に右ペインのシミュレーション結果がリアルタイム更新される（例：`UserRole` を `Admin` ⇄ `Member` に切り替えて挙動差を即確認）。
* **右ペイン（しりとり検証タイムライン）:**
  * ヘッダーに全体判定バッジ（`✓ All Steps Connected` / `✗ N Step(s) Broken`）と最終状態を表示。
  * 各ステップをカードで縦に並べて矢印（`↓`）で接続：
    * ユーザー行動（`UserAction`）入力欄
    * 割り当てる `Atomic` 要求の選択ドロップダウン（選択すると日本語/英語プレビューも小表示）
    * 状態のバトン表示（例：`ScreenState: List ➔ Detail`）
    * ステップ判定バッジ（`✅ PASS` / `🛑 BLOCKED` / `❌ バトン断絶` / `❌ 前提不一致`）と失敗理由
    * `↑` / `↓` 並び替え・削除ボタン、`＋ ステップを追加` ボタン

---

## 6. ユニットテスト (`tests/ReqPipeline.UnitTests/`)
1. `JourneySimulatorTests.cs`（新規）:
   * 正常系：状態のバトンが連続して繋がるジャーニーで全ステップ `Passed` になり、最終状態が期待値になること。
   * 異常系（バトン断絶）：`FromValue` が現在状態と一致しないステップで `StateMismatch` が検出されること。
   * 異常系（前提条件不一致）：`Preconditions` を満たさない初期状態で `PreconditionFailed` が検出されること。
   * 禁止動作：`PROHIBIT` 要求を実行したステップで `Blocked` が返ること。
   * `crud-sample.v2.json` に同梱した `Journeys` のシミュレーションが期待通り動作すること。
2. 既存テスト（`V2CrudSampleAndRenderingTests.cs`, `FsmGraphBuilderTests.cs` 等）がすべてパスすること。

---

## 7. 完了条件
* `dotnet build src/ReqPipeline.Web/ReqPipeline.Web.csproj` が 0 エラーで通ること。
* `dotnet test` が全件パスすること。
* `dotnet run --project src/ReqPipeline.Web` で `/journey` 画面が開き、`crud-sample.v2.json` のジャーニー検証・初期状態切替・ステップ並び替えによるリアルタイム再検証が動作すること。
