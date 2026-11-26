using System.Text;

namespace ReqPipeline.Core.Testing;

/// <summary>
/// TestSpecificationDocument から Gherkin (.feature) 形式のテスト仕様を出力するエクスポーター。
/// 日本語 (# language: ja) と英語の両方をサポートし、用語・状態値を "..." で括って自動テスト互換にする。
/// </summary>
public static class GherkinTestExporter
{
    public static string Export(
        TestSpecificationDocument spec,
        TestExportLanguage language = TestExportLanguage.Japanese,
        TestLevel? filterLevel = null)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var isJa = language == TestExportLanguage.Japanese;
        var kw = new GherkinKeywords(isJa);
        var sb = new StringBuilder();

        if (isJa)
        {
            sb.AppendLine("# language: ja");
        }

        var hasOutput = false;

        // 1. RequirementTree (CompositeRequirement -> Feature, AtomicRequirement -> Rule)
        if (filterLevel is null or TestLevel.Unit)
        {
            var reqContainers = spec.ViewpointContainers
                .Where(c => c.Kind == TestViewpointKind.RequirementTree && c.TestCases.Count > 0)
                .ToList();

            foreach (var container in reqContainers)
            {
                if (hasOutput) sb.AppendLine();
                hasOutput = true;

                var featureTags = new List<string>();
                if (!string.IsNullOrWhiteSpace(container.RationaleId))
                {
                    featureTags.Add($"@{container.RationaleId}");
                }
                featureTags.Add($"@{container.Id}");
                featureTags.Add("@level:component");

                sb.AppendLine(string.Join(" ", featureTags));
                sb.AppendLine($"{kw.Feature}: {(isJa ? container.Title : container.TitleEn)}");

                var groupedByReq = container.TestCases
                    .GroupBy(tc => tc.SourceRef, StringComparer.Ordinal)
                    .OrderBy(g => g.Key, StringComparer.Ordinal);

                foreach (var group in groupedByReq)
                {
                    sb.AppendLine();
                    var cases = group
                        .OrderBy(TestSpecificationGenerator.GetCaseSortPriority)
                        .ThenBy(c => c.TestCaseId, StringComparer.Ordinal)
                        .ToList();
                    var techniques = cases.Select(c => c.Technique).Distinct().ToList();
                    var techniqueTag = techniques.Count > 1
                        ? "condition-matrix"
                        : TestSpecificationGenerator.FormatTechniqueTag(techniques[0]);

                    var ruleTags = new List<string>
                    {
                        $"@{group.Key}",
                        "@level:unit",
                        $"@technique:{techniqueTag}"
                    };
                    if (cases.Any(c => c.Tags.Contains("@safety", StringComparer.Ordinal)))
                    {
                        ruleTags.Add("@safety");
                    }

                    sb.AppendLine($"  {string.Join(" ", ruleTags)}");
                    var ruleTitle = ExtractRuleTitle(cases[0], isJa);
                    sb.AppendLine($"  {kw.Rule}: {ruleTitle}");

                    // セクション 7.3: 前提条件 0 個かつケース 1 件のみの場合は通常の Scenario で出力
                    var allConditionTermIds = cases
                        .SelectMany(c => c.GivenConditions)
                        .Select(g => g.TermId)
                        .Distinct(StringComparer.Ordinal)
                        .ToList();

                    if (cases.Count == 1 && allConditionTermIds.Count == 0)
                    {
                        AppendSingleScenario(sb, cases[0], kw, isJa, indent: "    ");
                    }
                    else
                    {
                        AppendScenarioOutline(sb, cases, allConditionTermIds, kw, isJa);
                    }
                }
            }
        }

        // 2. FSM (LifecycleState -> Feature)
        if (filterLevel is null or TestLevel.Component)
        {
            var fsmContainers = spec.ViewpointContainers
                .Where(c => c.Kind == TestViewpointKind.FsmMatrix && c.TestCases.Count > 0)
                .ToList();

            foreach (var container in fsmContainers)
            {
                if (hasOutput) sb.AppendLine();
                hasOutput = true;

                sb.AppendLine($"@{container.Id} @level:component @technique:state-transition");
                sb.AppendLine($"{kw.Feature}: {(isJa ? container.Title : container.TitleEn)}");

                foreach (var tc in container.TestCases)
                {
                    sb.AppendLine();
                    var scenarioTags = tc.Tags.Where(t => !string.Equals(t, $"@{container.Id}", StringComparison.Ordinal)).ToList();
                    if (scenarioTags.Count > 0)
                    {
                        sb.AppendLine($"  {string.Join(" ", scenarioTags)}");
                    }
                    AppendSingleScenario(sb, tc, kw, isJa, indent: "  ");
                }
            }
        }

        // 3. E2E (UserJourney -> Feature)
        if (filterLevel is null or TestLevel.E2E)
        {
            var journeyContainers = spec.ViewpointContainers
                .Where(c => c.Kind == TestViewpointKind.Journey && c.TestCases.Count > 0)
                .ToList();

            foreach (var container in journeyContainers)
            {
                if (hasOutput) sb.AppendLine();
                hasOutput = true;

                var featureTags = new List<string> { $"@{container.Id}", "@level:e2e", "@technique:journey-trace" };
                if (container.HasBlockedSteps)
                {
                    featureTags.Add("@safety");
                }

                sb.AppendLine(string.Join(" ", featureTags));
                sb.AppendLine($"{kw.Feature}: {(isJa ? container.Title : container.TitleEn)}");

                foreach (var tc in container.TestCases)
                {
                    sb.AppendLine();
                    AppendSingleScenario(sb, tc, kw, isJa, indent: "  ");
                }
            }
        }

        return sb.ToString();
    }

    private static void AppendSingleScenario(
        StringBuilder sb,
        DerivedTestCase tc,
        GherkinKeywords kw,
        bool isJa,
        string indent)
    {
        var title = isJa ? tc.Title : tc.TitleEn;
        sb.AppendLine($"{indent}{kw.Scenario}: [{tc.TestCaseId}] {title}");

        for (var i = 0; i < tc.GivenConditions.Count; i++)
        {
            var cond = tc.GivenConditions[i];
            var stepKw = i == 0 ? kw.Given : kw.And;
            var line = isJa
                ? $"{indent}  {stepKw} \"{cond.LabelJa}\" が \"{cond.ValueLabelJa}\" である"
                : $"{indent}  {stepKw} \"{cond.LabelEn}\" is \"{cond.ValueLabelEn}\"";
            sb.AppendLine(line);
        }

        for (var i = 0; i < tc.WhenSteps.Count; i++)
        {
            var step = tc.WhenSteps[i];
            var stepKw = i == 0 ? kw.When : kw.And;
            var actionText = isJa ? step.ActionTextJa : step.ActionTextEn;
            sb.AppendLine($"{indent}  {stepKw} {actionText}");
        }

        var thenText = isJa ? tc.ThenTextJa : tc.ThenTextEn;
        var thenLines = thenText.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (thenLines.Length == 0)
        {
            sb.AppendLine($"{indent}  {kw.Then} {thenText}");
        }
        else
        {
            for (var i = 0; i < thenLines.Length; i++)
            {
                var stepKw = i == 0 ? kw.Then : kw.And;
                sb.AppendLine($"{indent}  {stepKw} {thenLines[i]}");
            }
        }
    }

    private static void AppendScenarioOutline(
        StringBuilder sb,
        List<DerivedTestCase> cases,
        List<string> conditionTermIds,
        GherkinKeywords kw,
        bool isJa)
    {
        const string indent = "    ";
        var primary = cases[0];
        var baseTitle = ExtractRuleTitle(primary, isJa);
        var outlineTitle = isJa
            ? $"{baseTitle} の条件網羅検証"
            : $"Condition matrix verification for {baseTitle}";

        sb.AppendLine($"{indent}{kw.ScenarioOutline}: {outlineTitle}");

        // 列ヘッダ名マップ (TermId -> Label)
        var termLabels = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var termId in conditionTermIds)
        {
            var sampleCond = cases
                .SelectMany(c => c.GivenConditions)
                .First(g => string.Equals(g.TermId, termId, StringComparison.Ordinal));
            termLabels[termId] = isJa ? sampleCond.LabelJa : sampleCond.LabelEn;
        }

        for (var i = 0; i < conditionTermIds.Count; i++)
        {
            var termId = conditionTermIds[i];
            var label = termLabels[termId];
            var stepKw = i == 0 ? kw.Given : kw.And;
            var line = isJa
                ? $"{indent}  {stepKw} \"{label}\" が \"<{label}>\" である"
                : $"{indent}  {stepKw} \"{label}\" is \"<{label}>\"";
            sb.AppendLine(line);
        }

        for (var i = 0; i < primary.WhenSteps.Count; i++)
        {
            var step = primary.WhenSteps[i];
            var stepKw = i == 0 ? kw.When : kw.And;
            var actionText = isJa ? step.ActionTextJa : step.ActionTextEn;
            sb.AppendLine($"{indent}  {stepKw} {actionText}");
        }

        var expectedColName = isJa ? "期待結果" : "Expected";
        var caseIdColName = isJa ? "ケースID" : "CaseId";
        sb.AppendLine($"{indent}  {kw.Then} <{expectedColName}>");
        sb.AppendLine();
        sb.AppendLine($"{indent}  {kw.Examples}:");

        var headers = new List<string> { caseIdColName };
        headers.AddRange(conditionTermIds.Select(id => termLabels[id]));
        headers.Add(expectedColName);

        sb.AppendLine($"{indent}    | {string.Join(" | ", headers.Select(EscapeTableCell))} |");

        foreach (var tc in cases)
        {
            var row = new List<string> { tc.TestCaseId };
            foreach (var termId in conditionTermIds)
            {
                var cond = tc.GivenConditions.FirstOrDefault(g => string.Equals(g.TermId, termId, StringComparison.Ordinal));
                var val = cond is null
                    ? "-"
                    : (isJa ? cond.ValueLabelJa : cond.ValueLabelEn);
                row.Add(val);
            }
            row.Add(isJa ? tc.ThenTextJa : tc.ThenTextEn);

            sb.AppendLine($"{indent}    | {string.Join(" | ", row.Select(EscapeTableCell))} |");
        }
    }

    private static string ExtractRuleTitle(DerivedTestCase tc, bool isJa)
    {
        var raw = isJa ? tc.Title : tc.TitleEn;
        var parenIdx = isJa ? raw.IndexOf('（') : raw.IndexOf(" (", StringComparison.Ordinal);
        return parenIdx > 0 ? raw[..parenIdx] : raw;
    }

    private static string EscapeTableCell(string text) =>
        text.Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);

    private readonly struct GherkinKeywords(bool isJa)
    {
        public string Feature { get; } = isJa ? "機能" : "Feature";
        public string Rule { get; } = isJa ? "ルール" : "Rule";
        public string ScenarioOutline { get; } = isJa ? "シナリオアウトライン" : "Scenario Outline";
        public string Scenario { get; } = isJa ? "シナリオ" : "Scenario";
        public string Given { get; } = isJa ? "前提" : "Given";
        public string And { get; } = isJa ? "かつ" : "And";
        public string When { get; } = isJa ? "もし" : "When";
        public string Then { get; } = isJa ? "ならば" : "Then";
        public string Examples { get; } = isJa ? "例" : "Examples";
    }
}
