# ReqPipeline V2.1.0 Phase 1 実装仕様書
**WDD 達成基準（AC）モデル導入 ＋ 空プロジェクト新規作成基盤**

- **対象バージョン:** `v2.1.0`（Phase 1 / 全 3 Phase）
- **前提ベース:** `v2.0.0`（7 ビュー構成、ウォーム・ドキュメント × CUD デザイントークン、`UiIcon.razor` 適用済み）
- **後続フェーズとの接続:**
  - Phase 2: USDM View の対となる SysML 要求図（`«verify»` 破線で AC と親要求を接続）
  - Phase 3: 3DEXPERIENCE 風ナレッジグラフビュー（Cytoscape.js、AC を中央ハブに上流・下流・共有 Glossary 経由の横飛び火を可視化）

---

## 1. 目的と全体スコープ

V2.0.0 はサンプルデータ（`DoorLock` / `CRUD`）で決定論的検証を体験するショーケースとして完成した。V2.1.0 Phase 1 では、ReqPipeline を「ゼロから実務仕様を設計・検証できる実用ツール」へ引き上げるため、以下の 2 本柱を実装する。

1. **WDD（Why Driven Development）の達成基準（AC: Achievement Criterion）を独立エンティティとして導入**
   - 「US（`Rationale`） ➔ `AC` ➔ `[検証側: E2E (Journey) / 実現側: 親要求 (Composite)]`」の二股構造を正本モデルに組み込む。
   - `AC` ↔ `Composite` および `AC` ↔ `Journey` は **多対多（M:N）** を許容する。
2. **空プロジェクト新規作成と 0 件耐性（Empty State / 未保存ガード / ドラフト自動保存）**
   - 全 7 ビューが 0 件データで例外なく動作し、迷わず最初の 1 件を作成できる導線を整える。
   - ヘッダーのサンプル切替をプロジェクトスイッチャーへ統合し、Phase 3 で 8 タブ目（`Graph`）が増えても 1280px 幅に収まる土台を作る。

### スコープ外（Phase 1 では実装しない）
- SysML 要求図の描画（Phase 2）
- Cytoscape.js による `/graph` ビューの追加（Phase 3）
- サーバーサイド DB・ユーザー認証（引き続きクライアント完結・JSON 正本）

---

## 2. ドメインモデル設計（`ReqPipeline.Core`）

### 2.1 WDD 二股トレーサビリティ構造

```text
  Rationale (US / Why)
         │
         ▼
  AchievementCriterion (AC: 達成基準・テスト観点のハブ)
         │
         ├──▶ [検証側] LinkedJourneyIds (E2E シナリオ / 多対多)
         │             + ManualVerificationNote (離散論理外の手動観点)
         │
         └──▶ [実現側] LinkedCompositeIds (親要求 Composite / 多対多)
                        │
                        └──▶ Atomic 要求 (子要求)
                               ├──▶ Unit / 条件反転テスト・FSM 状態遷移
                               └──▶ 参照する Glossary 語彙
                                     └─▶ (自動導出) 同一 Glossary を共有する別 AC への横飛び火
```

### 2.2 `AchievementCriterion` エンティティ定義

プロジェクトルートモデル（`RequirementProject` 等）に以下を追加する。

```csharp
public sealed class AchievementCriterion
{
    public string Id { get; set; } = string.Empty;                      // 例: "AC-001"
    public string RationaleId { get; set; } = string.Empty;             // 親となる Rationale ID（例: "RAT-001"）
    public string Title { get; set; } = string.Empty;                   // 達成基準・観点タイトル（必須）
    public string Description { get; set; } = string.Empty;             // WDD で合意した達成基準本文
    public List<string> LinkedCompositeIds { get; set; } = new();       // 実現側: 親要求 ID（多対多）
    public List<string> LinkedJourneyIds { get; set; } = new();         // 検証側: E2E Journey ID（多対多）
    public List<string> ViewpointTags { get; set; } = new();            // 任意タグ（例: "正", "禁", "物理:連打"）
    public string? ManualVerificationNote { get; set; }                 // 論理モデル外の手動検証観点メモ（任意）
}
```

プロジェクトルートモデルへの追加プロパティ：
```csharp
public List<AchievementCriterion> AchievementCriteria { get; set; } = new();
```

### 2.3 JSON スキーマと後方互換性
- 新規保存・エクスポート時の `SchemaVersion` は `"2.1"` とする。
- インポート時は `"2.0"` および `"2.1"` の両方を受け入れる。
- `"2.0"` JSON（`achievementCriteria` プロパティが存在しない場合）を読み込んだ際は `AchievementCriteria` を空リストとして正常にデシリアライズし、エラーにしないこと。

### 2.4 共有 `Glossary` 経由の横飛び火・トレーサビリティ解決ヘルパー（Phase 3 への布石）

`ReqPipeline.Core` に決定論的なトレース解析サービス（例：`AcTraceabilityAnalyzer`）を用意し、UI や後続 Phase から共通利用できる形にする。

- **入力:** プロジェクト全体、対象 `acId`
- **自動導出結果:**
  1. `ResolvedAtomicIds`: `LinkedCompositeIds` 配下の全 `Atomic` 要求 ID（重複除去・ID 昇順）
  2. `ReferencedTermIds`: `ResolvedAtomicIds` および `LinkedJourneyIds` が参照している `Glossary` 語彙 ID 集合
  3. `LateralImpactAcLinks`: 同一 `Glossary` 語彙 ID を共有している **他の AC** と、共有している語彙 ID リストのペア一覧（自 AC は除外）

---

## 3. 静的検証（Linter）ルール拡張（`ReqPipeline.Core`）

既存の Linter パイプラインに、AC 向けの決定論的チェックを追加する。既存の V2.0 単体テストを壊さないよう、整合性違反以外は `Warning` とし、既存チェックの挙動は変更しないこと。

| ルール ID（例） | 重大度 | 検知条件 | メッセージ意図 |
| :--- | :--- | :--- | :--- |
| `AC-ERR-001` | **Error** | `RationaleId` が空、または存在しない `Rationale` を参照している | AC の親となる根拠（US）が見つかりません |
| `AC-ERR-002` | **Error** | `LinkedCompositeIds` に存在しない親要求 ID が含まれる | AC が存在しない親要求を参照しています |
| `AC-ERR-003` | **Error** | `LinkedJourneyIds` に存在しない `Journey` ID が含まれる | AC が存在しない E2E シナリオを参照しています |
| `AC-ERR-004` | **Error** | `AchievementCriteria` 内で `Id` が重複している、または `Title` が空 | AC の ID 重複またはタイトル未入力 |
| `AC-WARN-001` | **Warning** | `LinkedCompositeIds` が 0 件 | **実現漏れ:** 達成基準に対応する親要求が未設定です |
| `AC-WARN-002` | **Warning** | `LinkedJourneyIds` が 0 件 **かつ** `ManualVerificationNote` が空 | **検証漏れ:** 達成基準を確認する E2E または手動観点が未設定です |
| `AC-WARN-003` | **Warning** | `AchievementCriteria.Count > 0` のプロジェクトにおいて、どの AC からも参照されていない `Composite` がある | **観点未割当:** どの AC にも紐づいていない親要求があります |

> **重要:** `AC-WARN-003` は `AchievementCriteria` が 1 件以上登録されているプロジェクトでのみ評価する。これにより、AC を持たない V2.0 互換 JSON や既存ユニットテストへの副作用を防ぐ。

---

## 4. 既存サンプルデータへの AC 組み込み

`DoorLock` および `CRUD` サンプルに、WDD の二股構造および「AC ↔ 親要求の多対多」が体験できるお手本 AC を 2〜3 件ずつ追加する。

- **`DoorLock` サンプル（例）:**
  - `AC-001`: 通常施錠・解錠とアンサーバック通知（実現: 通常施錠/解錠の親要求群、検証: 正常系 `Journey`、タグ: `正`, `通知`）
  - `AC-002`: 半ドア・走行中の誤操作に対する安全インターロック（実現: 安全保護の親要求＋施錠/解錠の親要求と一部共有して多対多を体現、検証: インターロック `Journey`、手動観点: チャタリング・同時押し確認、タグ: `禁`, `安全`, `物理:連打`）
  - `AC-003`: 故障検知時のフェールセーフ・診断記録（実現: 診断系親要求、手動観点または対応 `Journey`、タグ: `異`, `保守`）
- **`CRUD` サンプル（例）:**
  - 正常系登録・更新の達成基準と、権限・バリデーション保護の達成基準の 2 件を追加。
- **テスト保全:** サンプル変更によって件数アサーションに影響が出る既存ユニットテストがある場合は、新仕様に合わせて期待値を更新しつつ、既存ロジックのデグレードがないことを検証する。

---

## 5. 空プロジェクト新規作成とデータ保護（`ReqPipeline.Core` / `ReqPipeline.Web`）

### 5.1 空プロジェクトファクトリ（`ReqPipeline.Core`）
```csharp
public static RequirementProject CreateEmpty(string name, string description = "")
```
- `SchemaVersion = "2.1"`、`ProjectName = name`、`Description = description`
- `GlossaryTerms`, `Behaviors`, `Rationales`, `Composites`, `Atomics`, `Journeys`, `AchievementCriteria` をすべて空リストで初期化。

### 5.2 ヘッダーのプロジェクトスイッチャー化（`MainLayout`）
- 現在の `CRUD | DoorLock` セグメントコントロールを、ドロップダウン型の **プロジェクトスイッチャー**（例：`DoorLock ▾`）へ統合する。
  - 表示内容：現在のプロジェクト名（長い場合は `max-width` で省略表示）＋未保存ドット（`IsDirty == true` 時のみアンバーの極小ドット）
  - ドロップダウン項目：
    - `+ 新規プロジェクトを作成...`（ウォーム強調アクション）
    - 区切り線
    - サンプル: `DoorLock（組込み安全系）`
    - サンプル: `CRUD（Web業務系）`
    - （ドラフト保存がある場合）`ローカル下書きを復元`
- `JSON Import` / `JSON Export` は既存どおりコンパクトなアウトラインボタンを維持し、1280px 幅で 1 行に綺麗に収まること。

### 5.3 新規プロジェクト作成モーダル
- 入力項目：
  - プロジェクト名（必須・例：`エレベーター開閉制御仕様`）
  - 概要・目的（任意・テキストエリア）
- 作成確定で空モデルを現在プロジェクトへセットし、`/workbench`（または現在画面）を更新、`IsDirty = false` で開始する。

### 5.4 未保存変更ガード（`IsDirty`）
- プロジェクト内のエンティティ追加・編集・削除が発生したら `AppState.IsDirty = true` とする。
- `JSON Export` 完了時に `IsDirty = false` へ戻す。
- `IsDirty == true` の状態で「別サンプル切替」「新規プロジェクト作成」「JSON Import」を実行しようとした場合、確認モーダルを表示する：
  - **「キャンセル」**
  - **「JSON Export して続行」**（安全退避してから切替）
  - **「変更を破棄して続行」**（バーミリオン系アウトライン）

### 5.5 ブラウザ `localStorage` への自動ドラフト保存
- JS Interop（`v2-interop.js`）経由で、編集のたびにデバウンス（例：800ms）して現在プロジェクトの JSON を `localStorage`（キー例：`reqpipeline.v2.draft`）へ保存する。
- アプリ起動時に有効なドラフトが存在する場合は、初期ロードで自動復元するか、画面上部に控えめなウォームバナー（「前回の編集下書きを復元できます [復元] [破棄]」）を表示する（**推奨: バナー方式**。起動直後にサンプルを確認したいユーザー体験を邪魔しないため）。
- `localStorage` 内の JSON が破損・旧形式エラーの場合は安全に握りつぶしてキーを削除し、起動不能にならないこと。

### 5.6 共通 `<EmptyState>` コンポーネントと全 7 ビューの 0 件耐性
- 共通コンポーネント `EmptyState.razor`（ウォーム紙面調・1px 破線枠・`UiIcon`・タイトル・簡潔なガイド文・1〜2 個のアクションボタン）を作成する。
- **全 7 ビューの 0 件時挙動（例外・ゼロ除算・Mermaid クラッシュの完全防止）:**
  1. `Workbench`: 「まだ根拠（US / Rationale）がありません」➔ `[+ 最初の Rationale を作成]` / `[Glossary から語彙を定義]`
  2. `Glossary`: 「語彙・状態・イベントが未登録です」➔ `[+ 語彙を追加]`
  3. `Behaviors`: 「振る舞い定義がありません」➔ `[+ 振る舞いを追加]`
  4. `USDM`: 「要求が未登録です」➔ `[Workbench で要求を作成]`
  5. `FSM`: 状態変数（`LifecycleState`）が 0 件のときは Mermaid 描画を呼び出さず `<EmptyState>` を表示（`[Glossary で状態変数を定義]`）
  6. `Journey`: 「E2E シナリオがありません」➔ `[+ Journey を作成]`
  7. `Test View`: ケース数 0 件時はカバレッジを `—`（または `0.0%`、ゼロ除算禁止）で表示し、マトリクス・カード領域に `<EmptyState>` を表示。エクスポートボタンは無効化（`disabled`）。

---

## 6. UI / UX 設計（`ReqPipeline.Web`）

すべて V2.0.0 のウォーム・ドキュメント（`#FAF9F6` / `#FFFFFF` / `#F3F1EC`）× CUD デザイントークン × `UiIcon.razor` に統一し、絵文字や Bootstrap 原色クラスは使用しない。

### 6.1 `Workbench` 内の AC 編集 UI
- ヘッダーのタブ数は 7 のまま維持する。
- `Workbench` のメイン領域上部に、ウォームストーン枠のセグメント切替を追加する：
  - **`[ 要求 (USDM) | 達成基準 (AC) ]`**
- 左ペインの `Rationale` ツリーは両モードで共有する。選択中の `Rationale` に紐づく AC 一覧（または全 AC 一覧）を表示し、追加・編集・削除できる。
- **AC エディタフォーム構成:**
  - `ID`（自動採番 `AC-001`, `AC-002`… 編集可）、`タイトル`、`親 Rationale` 選択、`達成基準の説明`
  - **実現側（親要求 `Composite` 紐づけ）:** チェックボックス付きの複数選択リスト（ID＋タイトル表示、検索フィルタ付きだとなお良い）
  - **検証側（E2E `Journey` 紐づけ）:** チェックボックス付きの複数選択リスト（ID＋タイトル表示）
  - **観点タグ（`ViewpointTags`）:** カンマ区切り入力またはチップ追加・削除UI（無彩色ウォームアウトラインバッジで表示）
  - **手動検証観点メモ（`ManualVerificationNote`）:** 任意テキストエリア
  - **関連・飛び火プレビュー（読み取り専用サマリー）:** `AcTraceabilityAnalyzer` の結果を使い、この AC の親要求配下にある `Atomic` 件数と、「同じ `Glossary` を共有している他の AC（横飛び火候補）」をアンバー破線バッジで控えめに表示する。

### 6.2 `Test View`（`/tests`）の AC 軸への昇格
- **AC が 1 件以上存在するプロジェクト:**
  - 左ペインの「要求・達成基準軸」が正式な **AC コンテナ**（`AC-001` 等）でグルーピングされる。
  - 各 AC コンテナを選択すると、右ペインに以下を整理して表示：
    - AC のタイトル・説明・`ViewpointTags`・`ManualVerificationNote`（ある場合、控えめなウォームカードで表示）
    - **検証側（E2E）:** `LinkedJourneyIds` に対応する E2E テストケース群
    - **実現側（Unit / Component）:** `LinkedCompositeIds` 配下の `Atomic` 要求から導出されたテストケース群
    - **横飛び火（共有 Glossary 経由）:** 関連する別 AC へのクイックジャンプピル
  - どの AC にも紐づいていない親要求のケースが存在する場合は、左ペイン末尾に **「AC 未割当（要確認）」** コンテナをアンバーアクセントで表示する。
  - 多対多により 1 つの親要求が複数 AC に紐づく場合、各 AC コンテナ配下にそれぞれ表示してよいが、上部サマリーの「総ケース数」はテストケース ID ベースで重複カウントしないこと。
- **AC が 0 件のプロジェクト（V2.0 互換フォールバック）:**
  - 従来通り `Composite`（親要求）単位でグルーピングする。

---

## 7. 実装ステップ（推奨分割）

差分肥大化と手戻りを防ぐため、以下の順で実装・検証を行う。

1. **Step 1（Core 基盤）:** `AchievementCriterion` モデル追加、`SchemaVersion 2.1` 後方互換、`CreateEmpty()`、`AcTraceabilityAnalyzer`、Linter ルール追加、サンプルデータ更新、および `ReqPipeline.UnitTests` 追加・全パス確認。
2. **Step 2（Web 状態管理＆空プロジェクト基盤）:** プロジェクトスイッチャー、`IsDirty` ガード、新規作成モーダル、`localStorage` 下書き保存・復元バナー、共通 `<EmptyState>` と全 7 ビューの 0 件耐性実装。
3. **Step 3（AC UI ＆ Test View 統合）:** `Workbench` の `[ 要求 (USDM) | 達成基準 (AC) ]` 編集 UI 実装、`Test View` の AC 軸グルーピング・重複なし集計・未割当表示、全 7 ビュー ×（`DoorLock` / `CRUD` / 新規空プロジェクト）の動作確認。

---

## 8. 受け入れ基準（Definition of Done）

- [ ] `dotnet build` および `dotnet test` がエラー 0 で全パスする。
- [ ] V2.0.0 形式（`AchievementCriteria` なし）の JSON を問題なくインポートできる。
- [ ] 新規プロジェクト作成モーダルから空プロジェクトを生成でき、全 7 ビューを巡回してもコンソールエラー・画面クラッシュ・ゼロ除算が発生しない。
- [ ] 空プロジェクトから `Glossary` ➔ `Rationale` ➔ `Composite` / `Atomic` ➔ `Journey` ➔ `AC` の順に手動入力し、`Test View` で導出ケースが表示され、JSON Export / 再 Import で完全復元できる。
- [ ] 1 つの `Composite` を複数の `AC` に紐づけても（多対多）、Linter エラーにならず、`Test View` の総ケース数が重複カウントされない。
- [ ] 同じ `Glossary` を共有する別 AC が `AcTraceabilityAnalyzer` で自動検出され、`Workbench` / `Test View` で確認できる。
- [ ] 未保存の変更がある状態でサンプル切替・新規作成を押した際、未保存ガードの確認モーダルが表示される。
- [ ] 追加した全 UI がウォーム・ドキュメント × CUD デザイントークンと `UiIcon.razor` で統一されており、絵文字や Bootstrap 原色クラスが混入していない。

---

## 9. 実装時の補足・設計確定事項

1. **実コードのルートクラス名への適合:**
   - 仕様書本文の `RequirementProject` / `ProjectName` / `GlossaryTerms` / `Behaviors` / `Composites` / `Atomics` は、実コードの **`KnowledgeGraphDocument`**（`DocumentId`, `Title`, `Description`, `Rationales`, `Glossary`, `BehaviorCatalog`, `Requirements`, `EscapeRecords`, `Journeys`）へ読み替えて追加・実装する。
   - `KnowledgeGraphDocument.CreateEmpty(string title, string description = "", bool includeDefaultCrudBehaviors = false)` とし、`SchemaVersion = "2.1"` および一意な `DocumentId`（例：`DOC-NEW-yyyyMMddHHmmss` 等）を適切に初期化する。
2. **横飛び火（`LateralImpactAcLinks`）のノイズ除外とメタ情報:**
   - `AcTraceabilityAnalyzer` の横飛び火判定対象は **状態・データ・イベント系（`LifecycleState`, `StateVariable`, `DomainObject`, `Attribute`, `Event`）** に限定し、全要求で共有されやすい `ConceptType.System` および `ConceptType.Actor` は除外する。
   - 共有語彙リストは単なる ID 配列ではなく、UI や Phase 3（グラフビュー）でそのまま活用できるよう `TermId`, `CanonicalName`, `DisplayName`, `ConceptType` を保持するレコード（例：`SharedTermInfo`）として返す。
3. **空プロジェクト時の `BehaviorCatalog` 補助導線:**
   - デフォルトは完全空（`BehaviorCatalog = []`）を維持する。
   - 新規プロジェクト作成モーダルに任意チェックボックス（`[ ] 標準 CRUD 動詞プリセット（6種）を初期投入する`、デフォルト OFF）を設け、あわせて `Behaviors` 画面の `<EmptyState>` にも `[標準動詞プリセットを投入]` ボタンを配置する。
4. **Linter ルール ID と `"2.0"` 後方互換テスト:**
   - AC 向けルール ID は仕様書通り **`AC-ERR-001〜004` / `AC-WARN-001〜003`** を採用する。
   - サンプル JSON を `SchemaVersion = "2.1"` へ更新する際、既存ユニットテストの期待値を整合させるとともに、**`achievementCriteria` プロパティを持たない `"2.0"` 形式 JSON の後方互換インポートテスト** を明示的に追加する。