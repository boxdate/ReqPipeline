using System.Text.Json;
using ReqPipeline.Core.Analysis;
using ReqPipeline.Core.Domain;
using ReqPipeline.Core.Testing;
using ReqPipeline.Core.Validation;

namespace ReqPipeline.UnitTests;

public class AchievementCriteriaTests
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

    private static KnowledgeGraphDocument CreateBaseAcDocument() => new()
    {
        SchemaVersion = KnowledgeGraphDocument.CurrentSchemaVersion,
        DocumentId = "DOC-AC-TEST",
        Title = "ACテストプロジェクト",
        Rationales =
        [
            new RationaleNode { Id = "RAT-1", Title = "理由1", Description = "根拠1" },
            new RationaleNode { Id = "RAT-2", Title = "理由2", Description = "根拠2" }
        ],
        Glossary =
        [
            new ApprovedGlossaryTerm { Id = "SYS-1", CanonicalName = "CoreSystem", DisplayName = "コアシステム", ConceptType = ConceptType.System },
            new ApprovedGlossaryTerm { Id = "ACT-1", CanonicalName = "Operator", DisplayName = "操作者", ConceptType = ConceptType.Actor },
            new ApprovedGlossaryTerm { Id = "OBJ-A", CanonicalName = "Order", DisplayName = "注文", ConceptType = ConceptType.DomainObject },
            new ApprovedGlossaryTerm { Id = "OBJ-B", CanonicalName = "Invoice", DisplayName = "請求書", ConceptType = ConceptType.DomainObject },
            new ApprovedGlossaryTerm { Id = "EVT-1", CanonicalName = "SubmitOrder", DisplayName = "注文送信", ConceptType = ConceptType.Event },
            new ApprovedGlossaryTerm { Id = "EVT-2", CanonicalName = "CancelOrder", DisplayName = "注文取消", ConceptType = ConceptType.Event },
            new ApprovedGlossaryTerm
            {
                Id = "LC-1",
                CanonicalName = "OrderStatus",
                DisplayName = "注文ステータス",
                ConceptType = ConceptType.LifecycleState,
                LogicalType = new LogicalTypeDefinition
                {
                    Kind = LogicalTypeKind.Enum,
                    AllowedValues = ["Open", "Closed"]
                }
            }
        ],
        BehaviorCatalog = KnowledgeGraphDocument.CreateDefaultCrudBehaviors(),
        Requirements =
        [
            new CanonicalRequirementNode
            {
                Id = "REQ-1",
                Title = "注文登録親要求",
                NodeKind = RequirementNodeKind.Composite,
                RationaleRefs = ["RAT-1"],
                SubjectSystemRef = "SYS-1"
            },
            new CanonicalRequirementNode
            {
                Id = "REQ-1-1",
                Title = "注文作成",
                NodeKind = RequirementNodeKind.Atomic,
                ParentRequirementId = "REQ-1",
                RationaleRefs = ["RAT-1"],
                EarsPattern = EarsEntryPattern.EventDriven,
                SubjectSystemRef = "SYS-1",
                ActorRef = "ACT-1",
                TriggerEventRef = "EVT-1",
                Behavior = new BehaviorNode
                {
                    BehaviorDefRef = "BEH-CREATE",
                    TargetTermRef = "OBJ-A"
                }
            },
            new CanonicalRequirementNode
            {
                Id = "REQ-2",
                Title = "請求書発行親要求",
                NodeKind = RequirementNodeKind.Composite,
                RationaleRefs = ["RAT-2"],
                SubjectSystemRef = "SYS-1"
            },
            new CanonicalRequirementNode
            {
                Id = "REQ-2-1",
                Title = "請求書作成",
                NodeKind = RequirementNodeKind.Atomic,
                ParentRequirementId = "REQ-2",
                RationaleRefs = ["RAT-2"],
                EarsPattern = EarsEntryPattern.EventDriven,
                SubjectSystemRef = "SYS-1",
                ActorRef = "ACT-1",
                TriggerEventRef = "EVT-2",
                Behavior = new BehaviorNode
                {
                    BehaviorDefRef = "BEH-CREATE",
                    TargetTermRef = "OBJ-B"
                }
            }
        ],
        Journeys =
        [
            new UserJourney
            {
                Id = "JRN-1",
                Title = "注文登録ジャーニー",
                InitialStates = new Dictionary<string, string> { ["LC-1"] = "Open" },
                Steps = [new JourneyStep { StepId = "STEP-1", UserAction = "注文する", RequirementRef = "REQ-1-1" }]
            },
            new UserJourney
            {
                Id = "JRN-2",
                Title = "請求書発行ジャーニー",
                Steps = [new JourneyStep { StepId = "STEP-1", UserAction = "請求書を作る", RequirementRef = "REQ-2-1" }]
            }
        ]
    };

    [Fact]
    public void CreateEmpty_InitializesSchema21WithEmptyCollectionsAndOptionalCrudBehaviors()
    {
        var empty = KnowledgeGraphDocument.CreateEmpty("エレベーター開閉制御仕様", "ドア開閉制御のゼロベース設計");

        Assert.Equal("2.1", empty.SchemaVersion);
        Assert.StartsWith("DOC-NEW-", empty.DocumentId);
        Assert.Equal("エレベーター開閉制御仕様", empty.Title);
        Assert.Equal("ドア開閉制御のゼロベース設計", empty.Description);
        Assert.Empty(empty.Rationales);
        Assert.Empty(empty.Glossary);
        Assert.Empty(empty.BehaviorCatalog);
        Assert.Empty(empty.Requirements);
        Assert.Empty(empty.EscapeRecords);
        Assert.Empty(empty.Journeys);
        Assert.Empty(empty.AchievementCriteria);

        Assert.Empty(GraphLinter.Lint(empty));
        Assert.Empty(FsmGraphBuilder.Build(empty));
        var spec = TestSpecificationGenerator.Generate(empty);
        Assert.Empty(spec.AllTestCases);
        Assert.Equal(0, spec.Summary.TotalTestCases);
        Assert.False(double.IsNaN(spec.Summary.AtomicRequirementCoveragePercent));

        var withCrud = KnowledgeGraphDocument.CreateEmpty("業務アプリ", includeDefaultCrudBehaviors: true);
        Assert.Equal("2.1", withCrud.SchemaVersion);
        Assert.NotEqual(empty.DocumentId, withCrud.DocumentId);
        Assert.Equal(6, withCrud.BehaviorCatalog.Count);
        Assert.Contains(withCrud.BehaviorCatalog, b => b.BehaviorDefId == "BEH-CREATE");
        Assert.Contains(withCrud.BehaviorCatalog, b => b.BehaviorDefId == "BEH-PROHIBIT");
    }

    [Fact]
    public void FromJson_SupportsV20BackwardCompatibilityWithoutAchievementCriteriaProperty()
    {
        const string legacyV20Json = """
        {
          "schemaVersion": "2.0",
          "documentId": "DOC-LEGACY-20",
          "title": "V2.0互換テスト",
          "rationales": [
            { "id": "RAT-1", "title": "理由1", "description": "説明1" }
          ],
          "glossary": [
            { "id": "SYS-1", "canonicalName": "LegacySystem", "displayName": "旧システム", "conceptType": "System" },
            { "id": "OBJ-1", "canonicalName": "Item", "displayName": "項目", "conceptType": "DomainObject" }
          ],
          "behaviorCatalog": [
            {
              "behaviorDefId": "BEH-CREATE",
              "canonicalLabel": "create",
              "japaneseLabel": "作成",
              "effect": "ManageEntity",
              "allowedTargetTypes": ["DomainObject"],
              "japaneseTemplate": "{target}を作成すること",
              "englishTemplate": "create {target}"
            }
          ],
          "requirements": [
            {
              "id": "REQ-1",
              "title": "親要求",
              "nodeKind": "Composite",
              "rationaleRefs": ["RAT-1"],
              "subjectSystemRef": "SYS-1"
            },
            {
              "id": "REQ-1-1",
              "title": "子要求",
              "nodeKind": "Atomic",
              "parentRequirementId": "REQ-1",
              "rationaleRefs": ["RAT-1"],
              "subjectSystemRef": "SYS-1",
              "behavior": {
                "behaviorDefRef": "BEH-CREATE",
                "targetTermRef": "OBJ-1"
              }
            }
          ],
          "escapeRecords": [],
          "journeys": []
        }
        """;

        var doc = KnowledgeGraphDocument.FromJson(legacyV20Json);

        Assert.Equal("2.0", doc.SchemaVersion);
        Assert.NotNull(doc.AchievementCriteria);
        Assert.Empty(doc.AchievementCriteria);

        var diagnostics = GraphLinter.Lint(doc);
        Assert.DoesNotContain(diagnostics, d => d.RuleId == GraphLinter.RuleCompositeUnassignedToAc);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void FromJson_RejectsUnsupportedSchemaVersion()
    {
        const string unsupportedJson = """
        {
          "schemaVersion": "9.9",
          "documentId": "DOC-FUTURE",
          "title": "未知バージョン"
        }
        """;

        Assert.Throws<JsonException>(() => KnowledgeGraphDocument.FromJson(unsupportedJson));
    }

    [Fact]
    public void AchievementCriterion_RoundTripsThroughJson()
    {
        var doc = CreateBaseAcDocument();
        doc.AchievementCriteria.Add(new AchievementCriterion
        {
            Id = "AC-001",
            RationaleId = "RAT-1",
            Title = "注文登録の達成基準",
            Description = "注文が正常に登録されること",
            LinkedCompositeIds = ["REQ-1"],
            LinkedJourneyIds = ["JRN-1"],
            ViewpointTags = ["正", "業務"],
            ManualVerificationNote = "二重クリック抑止を手動確認する"
        });

        var json = doc.ToJson();
        var restored = KnowledgeGraphDocument.FromJson(json);

        Assert.Equal("2.1", restored.SchemaVersion);
        var ac = Assert.Single(restored.AchievementCriteria);
        Assert.Equal("AC-001", ac.Id);
        Assert.Equal("RAT-1", ac.RationaleId);
        Assert.Equal("注文登録の達成基準", ac.Title);
        Assert.Equal("注文が正常に登録されること", ac.Description);
        Assert.Equal(["REQ-1"], ac.LinkedCompositeIds);
        Assert.Equal(["JRN-1"], ac.LinkedJourneyIds);
        Assert.Equal(["正", "業務"], ac.ViewpointTags);
        Assert.Equal("二重クリック抑止を手動確認する", ac.ManualVerificationNote);
    }

    [Fact]
    public void GraphLinter_AcRules_DetectsAllErrorsAndWarnings()
    {
        var doc = CreateBaseAcDocument();
        doc.AchievementCriteria.AddRange(
        [
            new AchievementCriterion
            {
                Id = "AC-001",
                RationaleId = "RAT-MISSING",
                Title = "",
                LinkedCompositeIds = ["REQ-MISSING", "REQ-1-1"], // missing + non-Composite (Atomic)
                LinkedJourneyIds = ["JRN-MISSING"]
            },
            new AchievementCriterion
            {
                Id = "AC-001", // duplicate Id
                RationaleId = "RAT-1",
                Title = "実現漏れ・検証漏れAC",
                LinkedCompositeIds = [],
                LinkedJourneyIds = [],
                ManualVerificationNote = null
            }
        ]);

        var diagnostics = GraphLinter.Lint(doc);

        Assert.Contains(diagnostics, d =>
            d.Severity == LintSeverity.Error
            && d.RuleId == GraphLinter.RuleAcMissingRationale
            && d.TargetId == "AC-001");

        var err002 = diagnostics.Where(d => d.RuleId == GraphLinter.RuleAcMissingComposite).ToList();
        Assert.Equal(2, err002.Count);
        Assert.All(err002, d => Assert.Equal(LintSeverity.Error, d.Severity));

        Assert.Contains(diagnostics, d =>
            d.Severity == LintSeverity.Error
            && d.RuleId == GraphLinter.RuleAcMissingJourney
            && d.TargetId == "AC-001");

        var err004 = diagnostics.Where(d => d.RuleId == GraphLinter.RuleAcInvalidIdentityOrTitle).ToList();
        Assert.Equal(2, err004.Count); // empty title + duplicate id
        Assert.All(err004, d => Assert.Equal(LintSeverity.Error, d.Severity));

        Assert.Contains(diagnostics, d =>
            d.Severity == LintSeverity.Warning
            && d.RuleId == GraphLinter.RuleAcNoLinkedComposite
            && d.TargetId == "AC-001");

        Assert.Contains(diagnostics, d =>
            d.Severity == LintSeverity.Warning
            && d.RuleId == GraphLinter.RuleAcNoVerification
            && d.TargetId == "AC-001");

        // REQ-1 and REQ-2 are unassigned to any valid AC
        var warn003 = diagnostics.Where(d => d.RuleId == GraphLinter.RuleCompositeUnassignedToAc).ToList();
        Assert.Equal(2, warn003.Count);
        Assert.Contains(warn003, d => d.TargetId == "REQ-1");
        Assert.Contains(warn003, d => d.TargetId == "REQ-2");
    }

    [Fact]
    public void GraphLinter_AcManyToManyAndManualVerificationNote_ProducesNoAcDiagnostics()
    {
        var doc = CreateBaseAcDocument();
        doc.AchievementCriteria.AddRange(
        [
            new AchievementCriterion
            {
                Id = "AC-001",
                RationaleId = "RAT-1",
                Title = "注文登録",
                LinkedCompositeIds = ["REQ-1"],
                LinkedJourneyIds = ["JRN-1"]
            },
            new AchievementCriterion
            {
                Id = "AC-002",
                RationaleId = "RAT-2",
                Title = "注文と請求書の整合（多対多＋手動確認）",
                LinkedCompositeIds = ["REQ-1", "REQ-2"],
                LinkedJourneyIds = [],
                ManualVerificationNote = "請求金額と注文金額の一致を手動検証する"
            }
        ]);

        var diagnostics = GraphLinter.Lint(doc);

        Assert.DoesNotContain(diagnostics, d => d.RuleId.StartsWith("AC-", StringComparison.Ordinal));
    }

    [Fact]
    public void AcTraceabilityAnalyzer_ExcludesSystemAndActorFromLateralImpact_AndDetectsSharedDomainTerms()
    {
        var doc = CreateBaseAcDocument();

        // AC-1 uses OBJ-A, EVT-1, LC-1 (via JRN-1), SYS-1, ACT-1
        // AC-2 uses OBJ-B, EVT-2, SYS-1, ACT-1 (only System & Actor overlap -> no lateral impact!)
        doc.AchievementCriteria.AddRange(
        [
            new AchievementCriterion
            {
                Id = "AC-1",
                RationaleId = "RAT-1",
                Title = "注文登録",
                LinkedCompositeIds = ["REQ-1"],
                LinkedJourneyIds = ["JRN-1"]
            },
            new AchievementCriterion
            {
                Id = "AC-2",
                RationaleId = "RAT-2",
                Title = "請求書発行",
                LinkedCompositeIds = ["REQ-2"],
                LinkedJourneyIds = ["JRN-2"]
            }
        ]);

        var res1 = AcTraceabilityAnalyzer.Analyze(doc, "AC-1");
        Assert.True(res1.Found);
        Assert.Equal(["REQ-1-1"], res1.ResolvedAtomicIds);
        Assert.Contains("SYS-1", res1.ReferencedTermIds);
        Assert.Contains("ACT-1", res1.ReferencedTermIds);
        Assert.Contains("OBJ-A", res1.ReferencedTermIds);
        Assert.Contains("LC-1", res1.ReferencedTermIds);
        Assert.Empty(res1.LateralImpactAcLinks);

        // Now add LC-1 to JRN-2 initial states -> AC-1 and AC-2 share LC-1 (LifecycleState)
        doc.Journeys.Single(j => j.Id == "JRN-2").InitialStates["LC-1"] = "Open";

        var res1After = AcTraceabilityAnalyzer.Analyze(doc, "AC-1");
        var link = Assert.Single(res1After.LateralImpactAcLinks);
        Assert.Equal("AC-2", link.TargetAcId);
        Assert.Equal("請求書発行", link.TargetAcTitle);
        Assert.Equal("RAT-2", link.TargetRationaleId);
        var sharedTerm = Assert.Single(link.SharedTerms);
        Assert.Equal("LC-1", sharedTerm.TermId);
        Assert.Equal("OrderStatus", sharedTerm.CanonicalName);
        Assert.Equal("注文ステータス", sharedTerm.DisplayName);
        Assert.Equal(ConceptType.LifecycleState, sharedTerm.ConceptType);

        // Unknown AC returns Found == false
        var missing = AcTraceabilityAnalyzer.Analyze(doc, "AC-UNKNOWN");
        Assert.False(missing.Found);
        Assert.Empty(missing.ResolvedAtomicIds);
        Assert.Empty(missing.ReferencedTermIds);
        Assert.Empty(missing.LateralImpactAcLinks);
    }

    [Fact]
    public void AcTraceabilityAnalyzer_DetectsExpectedLateralImpactsInSamples()
    {
        var doorLockDoc = KnowledgeGraphDocument.FromJson(File.ReadAllText(GetSamplePath("doorlock-sample.v2.json")));
        var doorLockAll = AcTraceabilityAnalyzer.AnalyzeAll(doorLockDoc);

        Assert.Equal(3, doorLockAll.Count);
        var dlAc1 = doorLockAll["AC-001"];
        Assert.Equal(["REQ-001-1", "REQ-001-2", "REQ-001-3"], dlAc1.ResolvedAtomicIds);
        var dlLinkToAc2 = Assert.Single(dlAc1.LateralImpactAcLinks, l => l.TargetAcId == "AC-002");
        Assert.Contains(dlLinkToAc2.SharedTerms, t => t.TermId == "TERM-LC-001" && t.ConceptType == ConceptType.LifecycleState);
        Assert.DoesNotContain(dlLinkToAc2.SharedTerms, t => t.ConceptType is ConceptType.System or ConceptType.Actor);

        var dlAc2 = doorLockAll["AC-002"];
        // Many-to-many: AC-002 links REQ-002 and REQ-001 -> resolves all 5 atomics sorted ascending
        Assert.Equal(["REQ-001-1", "REQ-001-2", "REQ-001-3", "REQ-002-1", "REQ-002-2"], dlAc2.ResolvedAtomicIds);

        // AC-003 only shares System with AC-001/AC-002 -> no lateral impact
        Assert.Empty(doorLockAll["AC-003"].LateralImpactAcLinks);

        var crudDoc = KnowledgeGraphDocument.FromJson(File.ReadAllText(GetSamplePath("crud-sample.v2.json")));
        var crudAc1 = AcTraceabilityAnalyzer.Analyze(crudDoc, "AC-001");
        var crudLink = Assert.Single(crudAc1.LateralImpactAcLinks);
        Assert.Equal("AC-002", crudLink.TargetAcId);
        Assert.Contains(crudLink.SharedTerms, t => t.TermId == "TERM-OBJ-001" && t.CanonicalName == "Customer");
        Assert.Contains(crudLink.SharedTerms, t => t.TermId == "TERM-LC-001" && t.CanonicalName == "ScreenState");
        Assert.DoesNotContain(crudLink.SharedTerms, t => t.ConceptType is ConceptType.System or ConceptType.Actor);
    }
}
