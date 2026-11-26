using System.Text;
using System.Text.Json;
using ReqPipeline.Core.Domain;
using ReqPipeline.Core.Testing;

namespace ReqPipeline.UnitTests;

public class TestSpecificationTests
{
    private static string GetSamplePath(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "samples", fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not locate samples/{fileName} from test output directory.");
    }

    private static KnowledgeGraphDocument LoadSample(string fileName) =>
        KnowledgeGraphDocument.FromJson(File.ReadAllText(GetSamplePath(fileName)));

    [Theory]
    [InlineData("crud-sample.v2.json")]
    [InlineData("doorlock-sample.v2.json")]
    public void Generate_IsDeterministicAcrossRepeatedRuns(string sampleFile)
    {
        var doc1 = LoadSample(sampleFile);
        var doc2 = LoadSample(sampleFile);

        var spec1 = TestSpecificationGenerator.Generate(doc1);
        var spec2 = TestSpecificationGenerator.Generate(doc2);

        var jsonOpts = KnowledgeGraphDocument.CreateJsonSerializerOptions();
        var json1 = JsonSerializer.Serialize(spec1, jsonOpts);
        var json2 = JsonSerializer.Serialize(spec2, jsonOpts);

        Assert.Equal(json1, json2);
        Assert.NotEmpty(spec1.AllTestCases);
        Assert.Equal(100.0, spec1.Summary.AtomicRequirementCoveragePercent);
        Assert.Equal(spec1.AllTestCases.Count, spec1.Summary.TotalTestCases);
        Assert.Equal(
            spec1.Summary.UnitTestCaseCount + spec1.Summary.ComponentTestCaseCount + spec1.Summary.E2ETestCaseCount,
            spec1.Summary.TotalTestCases);

        // 固定ソート順の検証: Unit -> Component -> E2E
        var levels = spec1.AllTestCases.Select(c => c.TestLevel).ToList();
        Assert.Equal(levels.OrderBy(l => l).ToList(), levels);
    }

    [Fact]
    public void Generate_InsertingNewRequirementInMiddle_DoesNotShiftExistingTestCaseIds()
    {
        var baseDoc = LoadSample("doorlock-sample.v2.json");
        var baseSpec = TestSpecificationGenerator.Generate(baseDoc);
        var baseUnitIds = baseSpec.AllTestCases
            .Where(c => c.TestLevel == TestLevel.Unit)
            .Select(c => c.TestCaseId)
            .ToHashSet(StringComparer.Ordinal);
        var baseE2eIds = baseSpec.AllTestCases
            .Where(c => c.TestLevel == TestLevel.E2E)
            .Select(c => c.TestCaseId)
            .ToHashSet(StringComparer.Ordinal);

        // 途中に新しい要求を挿入
        var modifiedDoc = LoadSample("doorlock-sample.v2.json");
        modifiedDoc.Requirements.Insert(2, new CanonicalRequirementNode
        {
            Id = "REQ-001-99",
            Title = "追加テスト要求",
            NodeKind = RequirementNodeKind.Atomic,
            ParentRequirementId = "REQ-001",
            RationaleRefs = ["RAT-001"],
            EarsPattern = EarsEntryPattern.EventDriven,
            SubjectSystemRef = "TERM-SYS-001",
            TriggerEventRef = "TERM-EVT-004",
            Preconditions = [],
            Behavior = new BehaviorNode
            {
                BehaviorDefRef = "BEH-CHIME",
                TargetTermRef = "TERM-OBJ-001",
                Timing = TimingKind.IMMEDIATE
            }
        });

        var modifiedSpec = TestSpecificationGenerator.Generate(modifiedDoc);
        var modifiedIds = modifiedSpec.AllTestCases
            .Select(c => c.TestCaseId)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var id in baseUnitIds)
        {
            Assert.Contains(id, modifiedIds);
        }
        foreach (var id in baseE2eIds)
        {
            Assert.Contains(id, modifiedIds);
        }
        Assert.Contains("TC-REQ-001-99-POS-01", modifiedIds);
    }

    [Fact]
    public void DoorLock_HalfAjarNegationOnNormalLock_AutoResolvesToBlockedByReq002_1()
    {
        var doc = LoadSample("doorlock-sample.v2.json");
        var spec = TestSpecificationGenerator.Generate(doc);

        var req001_1Cases = spec.AllTestCases
            .Where(c => c.TestLevel == TestLevel.Unit && c.SourceRef == "REQ-001-1")
            .ToList();

        // POS-01 (Unlocked & Closed -> Locked)
        var pos = Assert.Single(req001_1Cases, c => c.TestCaseId == "TC-REQ-001-1-POS-01");
        Assert.Equal(TestTechnique.ConditionNormal, pos.Technique);
        Assert.Equal(ExpectedTestOutcome.StateChanged, pos.ExpectedOutcome);

        // NEG ケースのうち DoorAjarState=Ajar へ反転したケースが REQ-002-1 により Blocked へ自動解決されること
        var ajarNeg = Assert.Single(req001_1Cases, c =>
            c.Technique == TestTechnique.ConditionNegation
            && c.GivenConditions.Any(g => g.TermId == "TERM-SV-001" && g.RawValue == "Ajar"));

        Assert.Equal(ExpectedTestOutcome.Blocked, ajarNeg.ExpectedOutcome);
        Assert.Contains("REQ-002-1", ajarNeg.ThenTextJa);
        Assert.Contains("REQ-002-1", ajarNeg.ThenTextEn);
        Assert.Contains("@safety", ajarNeg.Tags);

        // REQ-002-1 の禁止解除ケース (UNB: DoorAjarState=Closed) は REQ-001-1 が代替発火すること
        var req002_1Unb = Assert.Single(spec.AllTestCases, c => c.TestCaseId == "TC-REQ-002-1-UNB-01");
        Assert.Equal(TestTechnique.SafetyInterlock, req002_1Unb.Technique);
        Assert.Equal(ExpectedTestOutcome.AlternativeFired, req002_1Unb.ExpectedOutcome);
        Assert.Contains("REQ-001-1", req002_1Unb.ThenTextJa);
    }

    [Fact]
    public void DoorLock_FsmMatrix_ExcludesUnrelatedEventAndClassifiesCellsCorrectly()
    {
        var doc = LoadSample("doorlock-sample.v2.json");
        var spec = TestSpecificationGenerator.Generate(doc);

        var matrix = Assert.Single(spec.TransitionMatrices);
        Assert.Equal("TERM-LC-001", matrix.LifecycleTermId);
        Assert.Equal(["Unlocked", "Locked"], matrix.States);

        // TERM-EVT-004 (LockFaultDetected) は DoorLockState を参照しないため除外されること
        var eventIds = matrix.Events.Select(e => e.EventTermId).ToList();
        Assert.Equal(["TERM-EVT-001", "TERM-EVT-002", "TERM-EVT-003"], eventIds);
        Assert.DoesNotContain("TERM-EVT-004", eventIds);

        // (Unlocked, EVT-001): 条件付き施錠 + 半ドアインターロック共存
        var unlockedLock = matrix.GetCell("Unlocked", "TERM-EVT-001");
        Assert.NotNull(unlockedLock);
        Assert.Equal(MatrixCellKind.ConditionalTransition, unlockedLock.Classification);
        Assert.True(unlockedLock.HasProhibit);
        Assert.Contains("REQ-001-1", unlockedLock.RelatedRequirementIds);
        Assert.Contains("REQ-002-1", unlockedLock.RelatedRequirementIds);
        Assert.Contains(unlockedLock.TestCases, tc => tc.ExpectedOutcome == ExpectedTestOutcome.StateChanged);
        Assert.Contains(unlockedLock.TestCases, tc => tc.ExpectedOutcome == ExpectedTestOutcome.Blocked);

        // (Locked, EVT-002): 条件付き解錠 + 走行中インターロック共存
        var lockedUnlock = matrix.GetCell("Locked", "TERM-EVT-002");
        Assert.NotNull(lockedUnlock);
        Assert.Equal(MatrixCellKind.ConditionalTransition, lockedUnlock.Classification);
        Assert.True(lockedUnlock.HasProhibit);

        // (Locked, EVT-001): 禁止要求のみ -> ExplicitProhibit
        var lockedLock = matrix.GetCell("Locked", "TERM-EVT-001");
        Assert.NotNull(lockedLock);
        Assert.Equal(MatrixCellKind.ExplicitProhibit, lockedLock.Classification);
        Assert.True(lockedLock.HasProhibit);

        // (Unlocked, EVT-003): 純粋な空白セル -> UndefinedNoOp + ReviewNote
        var unlockedCompleted = matrix.GetCell("Unlocked", "TERM-EVT-003");
        Assert.NotNull(unlockedCompleted);
        Assert.Equal(MatrixCellKind.UndefinedNoOp, unlockedCompleted.Classification);
        Assert.False(unlockedCompleted.HasProhibit);
        var undefCase = Assert.Single(unlockedCompleted.TestCases);
        Assert.Equal(ExpectedTestOutcome.NoOp, undefCase.ExpectedOutcome);
        Assert.Equal(TestTechnique.InvalidTransition, undefCase.Technique);
        Assert.NotNull(undefCase.ReviewNoteJa);
        Assert.Contains("未定義セル", undefCase.ReviewNoteJa);

        // (Locked, EVT-003): EmitAction (アンサーバック鳴動) -> ConditionalTransition
        var lockedCompleted = matrix.GetCell("Locked", "TERM-EVT-003");
        Assert.NotNull(lockedCompleted);
        Assert.Equal(MatrixCellKind.ConditionalTransition, lockedCompleted.Classification);
        Assert.False(lockedCompleted.HasProhibit);
    }

    [Fact]
    public void CrudSample_FsmMatrix_HasValidTransitionsAndExcludesCrudNonFsmEvents()
    {
        var doc = LoadSample("crud-sample.v2.json");
        var spec = TestSpecificationGenerator.Generate(doc);

        var matrix = Assert.Single(spec.TransitionMatrices);
        Assert.Equal("TERM-LC-001", matrix.LifecycleTermId);

        var eventIds = matrix.Events.Select(e => e.EventTermId).ToList();
        Assert.DoesNotContain("TERM-EVT-001", eventIds);
        Assert.DoesNotContain("TERM-EVT-002", eventIds);
        Assert.DoesNotContain("TERM-EVT-004", eventIds);
        Assert.Contains("TERM-EVT-003", eventIds);
        Assert.Contains("TERM-EVT-005", eventIds);

        // (List, EVT-005): 無条件の有効遷移 (List -> Create)
        var listToCreate = matrix.GetCell("List", "TERM-EVT-005");
        Assert.NotNull(listToCreate);
        Assert.Equal(MatrixCellKind.ValidTransition, listToCreate.Classification);
        Assert.False(listToCreate.HasProhibit);
        Assert.Equal(["Create"], listToCreate.TargetStates);
        var tc = Assert.Single(listToCreate.TestCases);
        Assert.Equal("TC-FSM-LC001-List-EVT005", tc.TestCaseId);
        Assert.Equal(TestTechnique.ValidTransition, tc.Technique);
        Assert.Equal(ExpectedTestOutcome.StateChanged, tc.ExpectedOutcome);

        // E2E ジャーニー: JRN-002 はブロックあり -> @safety タグ & Blocked
        var jrn2 = Assert.Single(spec.AllTestCases, c => c.TestCaseId == "TC-JRN-002");
        Assert.Equal(TestLevel.E2E, jrn2.TestLevel);
        Assert.Equal(ExpectedTestOutcome.Blocked, jrn2.ExpectedOutcome);
        Assert.Contains("@safety", jrn2.Tags);
    }

    [Fact]
    public void EvaluateAllRequirements_RespectsUnboundVariableRuleAndProhibitPrecedence()
    {
        var doc = LoadSample("doorlock-sample.v2.json");

        // 1. 未束縛変数 (TERM-SV-001 を渡さない) では REQ-001-1 も REQ-002-1 も誤発火しないこと
        var unboundEval = TestSpecificationGenerator.EvaluateAllRequirements(
            doc,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["TERM-LC-001"] = "Unlocked" },
            "TERM-EVT-001");
        Assert.Equal(ExpectedTestOutcome.NoOp, unboundEval.ExpectedOutcome);
        Assert.Empty(unboundEval.MatchedRequirementIds);

        // 2. 複数ヒット時に Prohibit と通常要求が同時に一致する場合、Blocked が優先されること
        var normalLock = doc.Requirements.Single(r => r.Id == "REQ-001-1");
        normalLock.Preconditions.Clear(); // 意図的に競合させる
        var conflictEval = TestSpecificationGenerator.EvaluateAllRequirements(
            doc,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["TERM-LC-001"] = "Unlocked",
                ["TERM-SV-001"] = "Ajar"
            },
            "TERM-EVT-001");
        Assert.Equal(ExpectedTestOutcome.Blocked, conflictEval.ExpectedOutcome);
        Assert.Equal(["REQ-002-1"], conflictEval.MatchedRequirementIds);
    }

    [Fact]
    public void Generate_WithEscapeRecord_TracksManualReviewAndExcludesFromAutoCoverageDenominator()
    {
        var doc = LoadSample("doorlock-sample.v2.json");
        doc.Requirements.Add(new CanonicalRequirementNode
        {
            Id = "REQ-003-2",
            Title = "複雑な時相制約の故障判定",
            NodeKind = RequirementNodeKind.Atomic,
            ParentRequirementId = "REQ-003",
            RationaleRefs = ["RAT-003"],
            Behavior = null
        });
        doc.EscapeRecords.Add(new FreeTextEscapeRecord
        {
            Id = "ESC-001",
            RequirementId = "REQ-003-2",
            Reason = EscapeReason.ComplexTemporalConstraint,
            FreeText = "3回連続で500ms以内に固着を検知した場合にフェイルセーフへ移行する。",
            Status = EscapeRecordStatus.PENDING
        });

        var spec = TestSpecificationGenerator.Generate(doc);

        Assert.Equal(1, spec.Summary.EscapeManualReviewCount);
        Assert.Equal(100.0, spec.Summary.AtomicRequirementCoveragePercent);

        var req003Container = Assert.Single(spec.ViewpointContainers, c => c.Id == "REQ-003");
        Assert.True(req003Container.HasEscapeRecords);
        Assert.Single(req003Container.EscapeRecords, e => e.Id == "ESC-001");

        var md = MarkdownTestExporter.Export(spec, TestExportLanguage.Japanese);
        Assert.Contains("ESC-001", md);
        Assert.Contains("手動テスト設計要", md);
    }

    [Fact]
    public void CsvExporter_ProducesRfc4180CompliantUtf8BomOutputWithOptionalExecutionColumns()
    {
        var doc = LoadSample("doorlock-sample.v2.json");
        var spec = TestSpecificationGenerator.Generate(doc);

        // UTF-8 BOM チェック
        var bytes = CsvTestExporter.ExportUtf8BomBytes(spec, includeExecutionColumns: false);
        Assert.True(bytes.Length > 3);
        Assert.Equal(0xEF, bytes[0]);
        Assert.Equal(0xBB, bytes[1]);
        Assert.Equal(0xBF, bytes[2]);

        var csvDefault = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        var defaultRows = ParseRfc4180Csv(csvDefault);

        // ヘッダー 13 列固定
        Assert.Equal(
            [
                "TestCaseId", "TestLevel", "Technique", "RationaleId", "CompositeId",
                "SourceRef", "ViewpointTitle", "Title", "Given", "When", "Then",
                "ExpectedOutcome", "ReviewNote"
            ],
            defaultRows[0]);
        Assert.Equal(spec.AllTestCases.Count + 1, defaultRows.Count);
        Assert.All(defaultRows, row => Assert.Equal(13, row.Count));

        // 複数条件を持つケースは Given セル内に \n 改行を含む
        var posRow = defaultRows.Single(r => r[0] == "TC-REQ-001-1-POS-01");
        Assert.Contains('\n', posRow[8]);

        // 実行記録列オプション ON -> 17 列
        var csvWithExec = CsvTestExporter.Export(spec, includeExecutionColumns: true);
        var execRows = ParseRfc4180Csv(csvWithExec);
        Assert.Equal(17, execRows[0].Count);
        Assert.Equal(["Result", "Tester", "ExecutedAt", "Comment"], execRows[0].Skip(13).ToList());
        Assert.All(execRows.Skip(1), row =>
        {
            Assert.Equal(17, row.Count);
            Assert.Equal(["", "", "", ""], row.Skip(13).ToList());
        });
    }

    [Fact]
    public void GherkinExporter_SupportsJapaneseAndEnglishAndZeroPreconditionScenario()
    {
        var doc = LoadSample("doorlock-sample.v2.json");
        var spec = TestSpecificationGenerator.Generate(doc);

        // 日本語出力
        var gherkinJa = GherkinTestExporter.Export(spec, TestExportLanguage.Japanese);
        Assert.StartsWith("# language: ja", gherkinJa);
        Assert.Contains("機能:", gherkinJa);
        Assert.Contains("ルール:", gherkinJa);
        Assert.Contains("シナリオアウトライン:", gherkinJa);
        Assert.Contains("例:", gherkinJa);
        Assert.Contains("@RAT-001", gherkinJa);
        Assert.Contains("@REQ-001-1", gherkinJa);
        Assert.Contains("@safety", gherkinJa);
        Assert.Contains("\"ドアロック状態\"", gherkinJa);

        // セクション 7.3: 前提条件 0 個かつケース 1 件のみの REQ-003-1 は Scenario Outline ではなく Scenario (シナリオ) で出力
        var req003RuleIdx = gherkinJa.IndexOf("@REQ-003-1", StringComparison.Ordinal);
        Assert.True(req003RuleIdx >= 0);
        var nextFeatureIdx = gherkinJa.IndexOf("機能:", req003RuleIdx, StringComparison.Ordinal);
        var req003Block = nextFeatureIdx > req003RuleIdx
            ? gherkinJa[req003RuleIdx..nextFeatureIdx]
            : gherkinJa[req003RuleIdx..];
        Assert.Contains("シナリオ: [TC-REQ-003-1-POS-01]", req003Block);
        Assert.DoesNotContain("シナリオアウトライン:", req003Block);

        // 英語出力
        var gherkinEn = GherkinTestExporter.Export(spec, TestExportLanguage.English);
        Assert.DoesNotContain("# language: ja", gherkinEn);
        Assert.Contains("Feature:", gherkinEn);
        Assert.Contains("Rule:", gherkinEn);
        Assert.Contains("Scenario Outline:", gherkinEn);
        Assert.Contains("Examples:", gherkinEn);
        Assert.Contains("\"DoorLockState\"", gherkinEn);

        // E2E フィルタ
        var e2eOnly = GherkinTestExporter.Export(spec, TestExportLanguage.English, TestLevel.E2E);
        Assert.Contains("@JRN-001", e2eOnly);
        Assert.DoesNotContain("@REQ-001-1", e2eOnly);
    }

    [Fact]
    public void MarkdownExporter_OutputsSummaryViewpointsAndTransitionMatrix()
    {
        var doc = LoadSample("doorlock-sample.v2.json");
        var spec = TestSpecificationGenerator.Generate(doc);

        var mdJa = MarkdownTestExporter.Export(spec, TestExportLanguage.Japanese);
        Assert.Contains("## 1. サマリー", mdJa);
        Assert.Contains("## 2. 観点コンテナ別テストケース一覧", mdJa);
        Assert.Contains("## 3. 状態遷移マトリクス", mdJa);
        Assert.Contains("TC-REQ-001-1-POS-01", mdJa);
        Assert.Contains("⛔ ブロック", mdJa);
        Assert.Contains("— 未定義", mdJa);

        var mdEn = MarkdownTestExporter.Export(spec, TestExportLanguage.English);
        Assert.Contains("## 1. Summary", mdEn);
        Assert.Contains("## 2. Test Cases by Viewpoint Container", mdEn);
        Assert.Contains("## 3. State Transition Matrices", mdEn);
    }

    [Fact]
    public void UnitCases_AreSortedWithPosBeforeNegAndBlkBeforeUnbAcrossAllOutputs()
    {
        var crudSpec = TestSpecificationGenerator.Generate(LoadSample("crud-sample.v2.json"));
        var req001_3Ids = crudSpec.AllTestCases
            .Where(c => c.TestLevel == TestLevel.Unit && c.SourceRef == "REQ-001-3")
            .Select(c => c.TestCaseId)
            .ToList();

        Assert.Equal("TC-REQ-001-3-POS-01", req001_3Ids[0]);
        Assert.Equal("TC-REQ-001-3-NEG-01", req001_3Ids[1]);

        // Gherkin / CSV / Markdown でも POS が NEG より先頭に出力されること
        var crudGherkin = GherkinTestExporter.Export(crudSpec, TestExportLanguage.Japanese);
        Assert.True(
            crudGherkin.IndexOf("TC-REQ-001-3-POS-01", StringComparison.Ordinal)
            < crudGherkin.IndexOf("TC-REQ-001-3-NEG-01", StringComparison.Ordinal));

        var crudCsv = CsvTestExporter.Export(crudSpec);
        Assert.True(
            crudCsv.IndexOf("TC-REQ-001-3-POS-01", StringComparison.Ordinal)
            < crudCsv.IndexOf("TC-REQ-001-3-NEG-01", StringComparison.Ordinal));

        var crudMd = MarkdownTestExporter.Export(crudSpec);
        Assert.True(
            crudMd.IndexOf("TC-REQ-001-3-POS-01", StringComparison.Ordinal)
            < crudMd.IndexOf("TC-REQ-001-3-NEG-01", StringComparison.Ordinal));

        // DoorLock: POS -> NEG および BLK -> UNB の順序
        var doorSpec = TestSpecificationGenerator.Generate(LoadSample("doorlock-sample.v2.json"));
        var lockIds = doorSpec.AllTestCases
            .Where(c => c.TestLevel == TestLevel.Unit && c.SourceRef == "REQ-001-1")
            .Select(c => c.TestCaseId)
            .ToList();
        Assert.Equal("TC-REQ-001-1-POS-01", lockIds[0]);
        Assert.StartsWith("TC-REQ-001-1-NEG-", lockIds[1]);

        var interlockIds = doorSpec.AllTestCases
            .Where(c => c.TestLevel == TestLevel.Unit && c.SourceRef == "REQ-002-1")
            .Select(c => c.TestCaseId)
            .ToList();
        Assert.Equal(["TC-REQ-002-1-BLK-01", "TC-REQ-002-1-UNB-01"], interlockIds);
    }

    [Fact]
    public void JourneyTestCase_ThenIsMultilineAndLocalizedForJaAndEn()
    {
        var crudSpec = TestSpecificationGenerator.Generate(LoadSample("crud-sample.v2.json"));
        var jrn1 = Assert.Single(crudSpec.AllTestCases, c => c.TestCaseId == "TC-JRN-001");

        // 日本語: 改行区切り＋日本語ラベル（英語 Canonical 名が混在しない）
        var jaLines = jrn1.ThenTextJa.Split('\n');
        Assert.Equal(9, jaLines.Length); // 8 steps + 最終状態行
        Assert.Equal("1. (REQ-001-4) 成功 [画面状態: 一覧 → 作成]", jaLines[0]);
        Assert.Equal("2. (REQ-001-1) 成功 [顧客: 作成]", jaLines[1]);
        Assert.StartsWith("最終状態: ", jaLines[^1]);
        Assert.DoesNotContain("ScreenState", jrn1.ThenTextJa);

        // 英語: 改行区切り＋Canonical 名
        var enLines = jrn1.ThenTextEn.Split('\n');
        Assert.Equal(9, enLines.Length);
        Assert.Equal("1. (REQ-001-4) Passed [ScreenState: List -> Create]", enLines[0]);
        Assert.Equal("2. (REQ-001-1) Passed [Customer: create]", enLines[1]);
        Assert.StartsWith("Final states: ", enLines[^1]);

        // DoorLock ブロックありジャーニーの日本語表記および 3 形式エクスポート整合性
        var doorSpec = TestSpecificationGenerator.Generate(LoadSample("doorlock-sample.v2.json"));
        var jrn2 = Assert.Single(doorSpec.AllTestCases, c => c.TestCaseId == "TC-JRN-002");
        var doorJaLines = jrn2.ThenTextJa.Split('\n');
        Assert.Equal("1. (REQ-002-2) ブロック [ドアロック状態の解錠を禁止]", doorJaLines[0]);
        Assert.StartsWith("最終状態: ", doorJaLines[^1]);

        // CSV セル内改行・Markdown <br>・Gherkin ならば/かつ 分割
        var csvRows = ParseRfc4180Csv(CsvTestExporter.Export(doorSpec));
        var jrn2Row = csvRows.Single(r => r[0] == "TC-JRN-002");
        Assert.Contains('\n', jrn2Row[10]);

        var md = MarkdownTestExporter.Export(doorSpec, TestExportLanguage.Japanese);
        Assert.Contains("1. (REQ-002-2) ブロック [ドアロック状態の解錠を禁止]<br>最終状態:", md);

        var gherkinE2e = GherkinTestExporter.Export(doorSpec, TestExportLanguage.Japanese, TestLevel.E2E);
        Assert.Contains("ならば 1. (REQ-002-2) ブロック [ドアロック状態の解錠を禁止]", gherkinE2e);
        Assert.Contains("かつ 最終状態:", gherkinE2e);
    }

    /// <summary>
    /// テスト検証用の簡易 RFC 4180 CSV パーサー（セル内改行・ダブルクォートエスケープ対応）。
    /// </summary>
    private static List<List<string>> ParseRfc4180Csv(string csv)
    {
        var rows = new List<List<string>>();
        var currentRow = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < csv.Length; i++)
        {
            var ch = csv[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < csv.Length && csv[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(ch);
                }
            }
            else
            {
                if (ch == '"')
                {
                    inQuotes = true;
                }
                else if (ch == ',')
                {
                    currentRow.Add(field.ToString());
                    field.Clear();
                }
                else if (ch == '\r' || ch == '\n')
                {
                    if (ch == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n')
                    {
                        i++;
                    }
                    currentRow.Add(field.ToString());
                    field.Clear();
                    rows.Add(currentRow);
                    currentRow = [];
                }
                else
                {
                    field.Append(ch);
                }
            }
        }

        if (field.Length > 0 || currentRow.Count > 0)
        {
            currentRow.Add(field.ToString());
            rows.Add(currentRow);
        }

        return rows;
    }
}
