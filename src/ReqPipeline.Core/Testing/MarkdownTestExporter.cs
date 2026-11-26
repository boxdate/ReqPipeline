using System.Text;

namespace ReqPipeline.Core.Testing;

/// <summary>
/// TestSpecificationDocument からレビュー資料・GitHub PR 添付向けの Markdown テスト仕様書を生成するエクスポーター。
/// 1. サマリー / 2. 観点コンテナ別テストケース一覧 / 3. 状態遷移マトリクス表 を 1 ファイルで出力する。
/// </summary>
public static class MarkdownTestExporter
{
    public static string Export(
        TestSpecificationDocument spec,
        TestExportLanguage language = TestExportLanguage.Japanese)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var isJa = language == TestExportLanguage.Japanese;
        var sb = new StringBuilder();

        // タイトル
        sb.AppendLine(isJa
            ? $"# テスト仕様書: {spec.Title} ({spec.DocumentId})"
            : $"# Test Specification: {spec.Title} ({spec.DocumentId})");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(spec.Description))
        {
            sb.AppendLine(spec.Description);
            sb.AppendLine();
        }

        // 1. サマリー
        AppendSummarySection(sb, spec.Summary, isJa);

        // 2. 観点コンテナ別テストケース一覧
        AppendViewpointContainersSection(sb, spec.ViewpointContainers, isJa);

        // 3. 状態遷移マトリクス表
        AppendTransitionMatricesSection(sb, spec.TransitionMatrices, isJa);

        return sb.ToString();
    }

    private static void AppendSummarySection(StringBuilder sb, TestSpecificationSummary summary, bool isJa)
    {
        sb.AppendLine(isJa ? "## 1. サマリー" : "## 1. Summary");
        sb.AppendLine();
        sb.AppendLine(isJa ? "| 指標 | 値 |" : "| Metric | Value |");
        sb.AppendLine("| :--- | ---: |");
        sb.AppendLine(isJa
            ? $"| 観点コンテナ数 | {summary.TotalViewpointContainers} |"
            : $"| Viewpoint Containers | {summary.TotalViewpointContainers} |");
        sb.AppendLine(isJa
            ? $"| 総テストケース数 | {summary.TotalTestCases} (Unit: {summary.UnitTestCaseCount}, Component: {summary.ComponentTestCaseCount}, E2E: {summary.E2ETestCaseCount}) |"
            : $"| Total Test Cases | {summary.TotalTestCases} (Unit: {summary.UnitTestCaseCount}, Component: {summary.ComponentTestCaseCount}, E2E: {summary.E2ETestCaseCount}) |");
        sb.AppendLine(isJa
            ? $"| Atomic 要求カバレッジ率 | {summary.AtomicRequirementCoveragePercent:0.0}% ({summary.CoveredAtomicRequirements} / {summary.TotalNonEscapeAtomicRequirements}) |"
            : $"| Atomic Requirement Coverage | {summary.AtomicRequirementCoveragePercent:0.0}% ({summary.CoveredAtomicRequirements} / {summary.TotalNonEscapeAtomicRequirements}) |");
        sb.AppendLine(isJa
            ? $"| 未定義セル総数 | {summary.TotalUndefinedCells} |"
            : $"| Undefined Matrix Cells | {summary.TotalUndefinedCells} |");
        sb.AppendLine(isJa
            ? $"| Escape 要手動確認件数 | {summary.EscapeManualReviewCount} |"
            : $"| Escape Manual Review Count | {summary.EscapeManualReviewCount} |");
        sb.AppendLine();
    }

    private static void AppendViewpointContainersSection(
        StringBuilder sb,
        List<TestViewpointContainer> containers,
        bool isJa)
    {
        sb.AppendLine(isJa ? "## 2. 観点コンテナ別テストケース一覧" : "## 2. Test Cases by Viewpoint Container");
        sb.AppendLine();

        foreach (var container in containers)
        {
            var title = isJa ? container.Title : container.TitleEn;
            var prefix = container.RationaleId is not null
                ? $"[{container.RationaleId} / {container.Id}]"
                : $"[{container.Id}]";

            sb.AppendLine($"### {prefix} {title}");
            sb.AppendLine();

            if (!string.IsNullOrWhiteSpace(container.Description))
            {
                sb.AppendLine(container.Description);
                sb.AppendLine();
            }

            if (container.HasEscapeRecords)
            {
                sb.AppendLine(isJa
                    ? "> [!WARNING] 手動テスト設計要（EscapeRecord あり）"
                    : "> [!WARNING] Manual Test Design Required (EscapeRecord present)");
                foreach (var esc in container.EscapeRecords)
                {
                    sb.AppendLine($"> - `{esc.Id}` (`{esc.RequirementId}` / `{esc.Reason}`): {esc.FreeText}");
                }
                sb.AppendLine();
            }

            if (container.TestCases.Count == 0)
            {
                sb.AppendLine(isJa ? "_自動導出されたテストケースはありません。_" : "_No automatically derived test cases._");
                sb.AppendLine();
                continue;
            }

            sb.AppendLine(isJa
                ? "| ケースID | 階層 | 技法 | 前提 (Given) | 操作 (When) | 期待結果 (Then) | 結果種別 | 注記 |"
                : "| Case ID | Level | Technique | Given | When | Then | Outcome | Review Note |");
            sb.AppendLine("| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |");

            foreach (var tc in container.TestCases)
            {
                var given = tc.GivenConditions.Count == 0
                    ? "-"
                    : string.Join("<br>", tc.GivenConditions.Select(c =>
                        isJa ? $"{EscapeMd(c.LabelJa)} = {EscapeMd(c.ValueLabelJa)}" : $"{EscapeMd(c.LabelEn)} = {EscapeMd(c.ValueLabelEn)}"));
                var when = tc.WhenSteps.Count == 0
                    ? "-"
                    : tc.WhenSteps.Count == 1
                        ? EscapeMd(isJa ? tc.WhenSteps[0].ActionTextJa : tc.WhenSteps[0].ActionTextEn)
                        : string.Join("<br>", tc.WhenSteps.Select(s =>
                            $"{s.StepNumber}. {EscapeMd(isJa ? s.ActionTextJa : s.ActionTextEn)}"));
                var then = EscapeMd(isJa ? tc.ThenTextJa : tc.ThenTextEn);
                var note = EscapeMd(isJa ? (tc.ReviewNoteJa ?? string.Empty) : (tc.ReviewNoteEn ?? string.Empty));

                sb.AppendLine($"| `{tc.TestCaseId}` | {tc.TestLevel} | `{tc.Technique}` | {given} | {when} | {then} | `{tc.ExpectedOutcome}` | {note} |");
            }

            sb.AppendLine();
        }
    }

    private static void AppendTransitionMatricesSection(
        StringBuilder sb,
        List<StateTransitionMatrix> matrices,
        bool isJa)
    {
        sb.AppendLine(isJa ? "## 3. 状態遷移マトリクス" : "## 3. State Transition Matrices");
        sb.AppendLine();

        if (matrices.Count == 0)
        {
            sb.AppendLine(isJa ? "_対象の LifecycleState はありません。_" : "_No LifecycleState found._");
            sb.AppendLine();
            return;
        }

        foreach (var matrix in matrices)
        {
            var title = isJa
                ? $"{matrix.LifecycleLabel} (`{matrix.LifecycleTermId}`)"
                : $"{matrix.LifecycleCanonicalLabel} (`{matrix.LifecycleTermId}`)";
            sb.AppendLine($"### {title}");
            sb.AppendLine();

            if (matrix.Events.Count == 0)
            {
                sb.AppendLine(isJa ? "_関連するイベントはありません。_" : "_No related events._");
                sb.AppendLine();
                continue;
            }

            var headerCols = new List<string> { isJa ? "状態 \\ イベント" : "State \\ Event" };
            headerCols.AddRange(matrix.Events.Select(e =>
                EscapeMd(isJa ? $"{e.DisplayName} (`{e.EventTermId}`)" : $"{e.CanonicalName} (`{e.EventTermId}`)")));

            sb.AppendLine($"| {string.Join(" | ", headerCols)} |");
            sb.AppendLine($"| :--- | {string.Join(" | ", matrix.Events.Select(_ => ":---"))} |");

            foreach (var state in matrix.States)
            {
                var stateLabel = isJa && matrix.StateLabels.TryGetValue(state, out var lbl)
                    ? $"{lbl} (`{state}`)"
                    : $"`{state}`";
                var row = new List<string> { EscapeMd(stateLabel) };

                foreach (var evt in matrix.Events)
                {
                    var cell = matrix.GetCell(state, evt.EventTermId);
                    row.Add(cell is null ? "-" : FormatMatrixCell(cell, isJa));
                }

                sb.AppendLine($"| {string.Join(" | ", row)} |");
            }

            sb.AppendLine();
        }
    }

    private static string FormatMatrixCell(MatrixCell cell, bool isJa)
    {
        var targets = isJa ? cell.TargetStateLabels : cell.TargetStates;
        var targetText = targets.Count > 0 ? $"➔ {string.Join(", ", targets)}" : (isJa ? "⚡ アクション" : "⚡ Action");

        return cell.Classification switch
        {
            MatrixCellKind.ValidTransition => EscapeMd(targetText),
            MatrixCellKind.ConditionalTransition => EscapeMd(
                cell.HasProhibit
                    ? (isJa ? $"{targetText} (条件あり) / ⛔ ブロック" : $"{targetText} (Conditional) / ⛔ Blocked")
                    : (isJa ? $"{targetText} (条件あり)" : $"{targetText} (Conditional)")),
            MatrixCellKind.ExplicitProhibit => isJa ? "⛔ ブロック" : "⛔ Blocked",
            MatrixCellKind.UndefinedNoOp => isJa ? "— 未定義" : "— Undefined",
            _ => "-"
        };
    }

    private static string EscapeMd(string text) =>
        text.Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("\r\n", "<br>", StringComparison.Ordinal)
            .Replace("\n", "<br>", StringComparison.Ordinal);
}
