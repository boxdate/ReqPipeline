using ReqPipeline.Core.Domain;
using ReqPipeline.Core.Validation;

namespace ReqPipeline.UnitTests;

public class GraphLinterTests
{
    private static KnowledgeGraphDocument CreateValidBaseDocument() => new()
    {
        SchemaVersion = "2.0",
        DocumentId = "DOC-TEST",
        Title = "テストドキュメント",
        Rationales =
        [
            new RationaleNode { Id = "RAT-1", Title = "理由1", Description = "テスト用理由" }
        ],
        Glossary =
        [
            new ApprovedGlossaryTerm { Id = "SYS-1", CanonicalName = "TargetSystem", DisplayName = "対象システム", ConceptType = ConceptType.System },
            new ApprovedGlossaryTerm { Id = "ACT-1", CanonicalName = "User", DisplayName = "利用者", ConceptType = ConceptType.Actor },
            new ApprovedGlossaryTerm { Id = "OBJ-1", CanonicalName = "Customer", DisplayName = "顧客", ConceptType = ConceptType.DomainObject },
            new ApprovedGlossaryTerm { Id = "EVT-1", CanonicalName = "RegisterAction", DisplayName = "登録操作", ConceptType = ConceptType.Event },
            new ApprovedGlossaryTerm
            {
                Id = "SV-BOOL",
                CanonicalName = "IsAuthenticated",
                DisplayName = "認証済み",
                ConceptType = ConceptType.StateVariable,
                LogicalType = new LogicalTypeDefinition { Kind = LogicalTypeKind.Boolean }
            },
            new ApprovedGlossaryTerm
            {
                Id = "LC-1",
                CanonicalName = "Status",
                DisplayName = "ステータス",
                ConceptType = ConceptType.LifecycleState,
                LogicalType = new LogicalTypeDefinition
                {
                    Kind = LogicalTypeKind.Enum,
                    AllowedValues = ["Draft", "Published", "Archived"]
                }
            }
        ],
        Requirements =
        [
            new CanonicalRequirementNode
            {
                Id = "REQ-1",
                Title = "親要求",
                NodeKind = RequirementNodeKind.Composite,
                RationaleRefs = ["RAT-1"],
                SubjectSystemRef = "SYS-1"
            },
            new CanonicalRequirementNode
            {
                Id = "REQ-1-1",
                Title = "子要求",
                NodeKind = RequirementNodeKind.Atomic,
                ParentRequirementId = "REQ-1",
                RationaleRefs = ["RAT-1"],
                EarsPattern = EarsEntryPattern.EventDriven,
                SubjectSystemRef = "SYS-1",
                ActorRef = "ACT-1",
                TriggerEventRef = "EVT-1",
                Preconditions =
                [
                    new PreconditionClause
                    {
                        StateTermRef = "SV-BOOL",
                        Operator = ComparisonOperator.EQ,
                        ExpectedValues = ["true"]
                    }
                ],
                Behavior = new BehaviorNode
                {
                    Kind = BehaviorKind.CREATE,
                    TargetTermRef = "OBJ-1"
                }
            }
        ]
    };

    [Fact]
    public void BaseDocument_HasNoDiagnostics()
    {
        var doc = CreateValidBaseDocument();
        var diagnostics = GraphLinter.Lint(doc);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Lint001_DetectsMissingReferences()
    {
        var doc = CreateValidBaseDocument();
        var child = doc.Requirements[1];
        child.RationaleRefs = ["RAT-MISSING"];
        child.SubjectSystemRef = "SYS-MISSING";
        child.ActorRef = "ACT-MISSING";
        child.TriggerEventRef = "EVT-MISSING";
        child.Preconditions[0].StateTermRef = "STATE-MISSING";
        child.Behavior!.TargetTermRef = "TARGET-MISSING";

        var diagnostics = GraphLinter.Lint(doc);
        var lint001 = diagnostics.Where(d => d.RuleId == GraphLinter.RuleMissingReference).ToList();

        Assert.Equal(6, lint001.Count);
        Assert.All(lint001, d => Assert.Equal(LintSeverity.Error, d.Severity));
    }

    [Fact]
    public void Lint002_DetectsConceptTypeMismatches()
    {
        var doc = CreateValidBaseDocument();
        var child = doc.Requirements[1];
        child.SubjectSystemRef = "ACT-1";        // Actor instead of System
        child.ActorRef = "SYS-1";                // System instead of Actor
        child.TriggerEventRef = "OBJ-1";         // DomainObject instead of Event
        child.Preconditions[0].StateTermRef = "EVT-1"; // Event instead of StateVariable/LifecycleState

        var diagnostics = GraphLinter.Lint(doc);
        var lint002 = diagnostics.Where(d => d.RuleId == GraphLinter.RuleConceptTypeMismatch).ToList();

        Assert.Equal(4, lint002.Count);
        Assert.All(lint002, d => Assert.Equal(LintSeverity.Error, d.Severity));
    }

    [Fact]
    public void Lint003_DetectsLogicalDomainClosureViolations()
    {
        var doc = CreateValidBaseDocument();
        var child = doc.Requirements[1];
        // Invalid boolean value ("yes" is not "true"/"false")
        child.Preconditions[0].ExpectedValues = ["yes"];
        // Invalid enum values on TRANSITION
        child.Behavior = new BehaviorNode
        {
            Kind = BehaviorKind.TRANSITION,
            TargetTermRef = "LC-1",
            FromValue = "InvalidFrom",
            ToValue = "InvalidTo"
        };

        // Also add a SET with invalid value
        doc.Requirements.Add(new CanonicalRequirementNode
        {
            Id = "REQ-1-2",
            Title = "不正なSET",
            NodeKind = RequirementNodeKind.Atomic,
            ParentRequirementId = "REQ-1",
            SubjectSystemRef = "SYS-1",
            Behavior = new BehaviorNode
            {
                Kind = BehaviorKind.SET,
                TargetTermRef = "SV-BOOL",
                SetValue = "maybe"
            }
        });

        var diagnostics = GraphLinter.Lint(doc);
        var lint003 = diagnostics.Where(d => d.RuleId == GraphLinter.RuleLogicalDomainViolation).ToList();

        Assert.Equal(4, lint003.Count);
        Assert.All(lint003, d => Assert.Equal(LintSeverity.Error, d.Severity));
    }

    [Fact]
    public void Lint004_DetectsCompositeWithoutChildrenAndAtomicWithoutBehaviorOrPendingEscape()
    {
        var doc = CreateValidBaseDocument();
        // Add empty composite
        doc.Requirements.Add(new CanonicalRequirementNode
        {
            Id = "REQ-EMPTY-COMP",
            Title = "子なしComposite",
            NodeKind = RequirementNodeKind.Composite,
            RationaleRefs = ["RAT-1"],
            SubjectSystemRef = "SYS-1"
        });
        // Add atomic with null Behavior and no escape record
        doc.Requirements.Add(new CanonicalRequirementNode
        {
            Id = "REQ-EMPTY-ATOMIC",
            Title = "BehaviorなしAtomic",
            NodeKind = RequirementNodeKind.Atomic,
            ParentRequirementId = "REQ-1",
            SubjectSystemRef = "SYS-1",
            Behavior = null
        });

        var diagnostics = GraphLinter.Lint(doc);
        var lint004 = diagnostics.Where(d => d.RuleId == GraphLinter.RuleNodeStructureViolation).ToList();

        Assert.Equal(2, lint004.Count);
        Assert.Contains(lint004, d => d.TargetId == "REQ-EMPTY-COMP");
        Assert.Contains(lint004, d => d.TargetId == "REQ-EMPTY-ATOMIC");
    }

    [Fact]
    public void Lint004AndLint007_AtomicWithPendingEscapePassesLint004ButEmitsLint007Warning()
    {
        var doc = CreateValidBaseDocument();
        doc.Requirements.Add(new CanonicalRequirementNode
        {
            Id = "REQ-ESC-1",
            Title = "エスケープ要求",
            NodeKind = RequirementNodeKind.Atomic,
            ParentRequirementId = "REQ-1",
            SubjectSystemRef = "SYS-1",
            Behavior = null
        });
        doc.EscapeRecords.Add(new FreeTextEscapeRecord
        {
            Id = "ESC-001",
            RequirementId = "REQ-ESC-1",
            Reason = EscapeReason.ComplexTemporalConstraint,
            FreeText = "応答は99パーセンタイルで200ms以内に返すこと。",
            Status = EscapeRecordStatus.PENDING
        });

        var diagnostics = GraphLinter.Lint(doc);

        Assert.DoesNotContain(diagnostics, d => d.RuleId == GraphLinter.RuleNodeStructureViolation);
        var warning = Assert.Single(diagnostics, d => d.RuleId == GraphLinter.RulePendingEscapeRecord);
        Assert.Equal(LintSeverity.Warning, warning.Severity);
        Assert.Equal("ESC-001", warning.TargetId);
    }

    [Fact]
    public void Lint005_DetectsBehaviorFieldIntegrityViolations()
    {
        var doc = CreateValidBaseDocument();
        // TRANSITION missing ToValue
        doc.Requirements.Add(new CanonicalRequirementNode
        {
            Id = "REQ-BAD-TRANS",
            Title = "遷移先欠落",
            NodeKind = RequirementNodeKind.Atomic,
            ParentRequirementId = "REQ-1",
            SubjectSystemRef = "SYS-1",
            Behavior = new BehaviorNode
            {
                Kind = BehaviorKind.TRANSITION,
                TargetTermRef = "LC-1",
                FromValue = "Draft",
                ToValue = null
            }
        });
        // SET missing SetValue
        doc.Requirements.Add(new CanonicalRequirementNode
        {
            Id = "REQ-BAD-SET",
            Title = "設定値欠落",
            NodeKind = RequirementNodeKind.Atomic,
            ParentRequirementId = "REQ-1",
            SubjectSystemRef = "SYS-1",
            Behavior = new BehaviorNode
            {
                Kind = BehaviorKind.SET,
                TargetTermRef = "SV-BOOL",
                SetValue = ""
            }
        });
        // CREATE targeting non-DomainObject (StateVariable)
        doc.Requirements.Add(new CanonicalRequirementNode
        {
            Id = "REQ-BAD-CREATE",
            Title = "非DomainObjectのCREATE",
            NodeKind = RequirementNodeKind.Atomic,
            ParentRequirementId = "REQ-1",
            SubjectSystemRef = "SYS-1",
            Behavior = new BehaviorNode
            {
                Kind = BehaviorKind.CREATE,
                TargetTermRef = "SV-BOOL"
            }
        });
        // DELETE targeting non-DomainObject (Event)
        doc.Requirements.Add(new CanonicalRequirementNode
        {
            Id = "REQ-BAD-DELETE",
            Title = "非DomainObjectのDELETE",
            NodeKind = RequirementNodeKind.Atomic,
            ParentRequirementId = "REQ-1",
            SubjectSystemRef = "SYS-1",
            Behavior = new BehaviorNode
            {
                Kind = BehaviorKind.DELETE,
                TargetTermRef = "EVT-1"
            }
        });

        var diagnostics = GraphLinter.Lint(doc);
        var lint005 = diagnostics.Where(d => d.RuleId == GraphLinter.RuleBehaviorFieldIntegrity).ToList();

        Assert.Equal(4, lint005.Count);
        Assert.Contains(lint005, d => d.TargetId == "REQ-BAD-TRANS");
        Assert.Contains(lint005, d => d.TargetId == "REQ-BAD-SET");
        Assert.Contains(lint005, d => d.TargetId == "REQ-BAD-CREATE");
        Assert.Contains(lint005, d => d.TargetId == "REQ-BAD-DELETE");
    }

    [Fact]
    public void Lint006_DetectsTopLevelRequirementWithoutValidRationale()
    {
        var doc = CreateValidBaseDocument();
        doc.Requirements[0].RationaleRefs.Clear();

        var diagnostics = GraphLinter.Lint(doc);
        var lint006 = Assert.Single(diagnostics, d => d.RuleId == GraphLinter.RuleTopLevelRationaleMissing);

        Assert.Equal(LintSeverity.Error, lint006.Severity);
        Assert.Equal("REQ-1", lint006.TargetId);
    }

    [Fact]
    public void Lint007_DoesNotWarnForResolvedEscapeRecords()
    {
        var doc = CreateValidBaseDocument();
        doc.EscapeRecords.Add(new FreeTextEscapeRecord
        {
            Id = "ESC-RESOLVED",
            RequirementId = "REQ-1-1",
            Reason = EscapeReason.MissingDomainVocabulary,
            FreeText = "解消済みメモ",
            Status = EscapeRecordStatus.RESOLVED,
            ResolutionNote = "グロッサリーへ追加済み"
        });

        var diagnostics = GraphLinter.Lint(doc);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Lint008_DetectsAllowVsProhibitConflict_WhenPreconditionsOverlap()
    {
        var doc = CreateValidBaseDocument();
        doc.Glossary.Add(new ApprovedGlossaryTerm
        {
            Id = "SV-ROLE",
            CanonicalName = "ユーザーロール",
            ConceptType = ConceptType.StateVariable,
            LogicalType = new LogicalTypeDefinition
            {
                Kind = LogicalTypeKind.Enum,
                AllowedValues = ["Admin", "User", "Guest"]
            }
        });
        doc.Glossary.Add(new ApprovedGlossaryTerm
        {
            Id = "EVT-DEL",
            CanonicalName = "顧客削除要求",
            ConceptType = ConceptType.Event
        });

        // 管理者以外は顧客削除禁止 (NEQ Admin + PROHIBIT DELETE)
        doc.Requirements.Add(new CanonicalRequirementNode
        {
            Id = "REQ-DEL-PROHIBIT",
            Title = "管理者以外の削除禁止",
            NodeKind = RequirementNodeKind.Atomic,
            ParentRequirementId = "REQ-1",
            EarsPattern = EarsEntryPattern.UnwantedBehavior,
            SubjectSystemRef = "SYS-1",
            TriggerEventRef = "EVT-DEL",
            Preconditions =
            [
                new PreconditionClause
                {
                    StateTermRef = "SV-ROLE",
                    Operator = ComparisonOperator.NEQ,
                    ExpectedValues = ["Admin"]
                }
            ],
            Behavior = new BehaviorNode
            {
                Kind = BehaviorKind.PROHIBIT,
                TargetTermRef = "OBJ-1",
                ProhibitedAction = BehaviorKind.DELETE
            }
        });

        // 条件なし（全ロール許容）で顧客を削除する (DELETE) -> User/Guest で重複し衝突
        doc.Requirements.Add(new CanonicalRequirementNode
        {
            Id = "REQ-DEL-ALLOW",
            Title = "顧客削除",
            NodeKind = RequirementNodeKind.Atomic,
            ParentRequirementId = "REQ-1",
            EarsPattern = EarsEntryPattern.EventDriven,
            SubjectSystemRef = "SYS-1",
            TriggerEventRef = "EVT-DEL",
            Behavior = new BehaviorNode
            {
                Kind = BehaviorKind.DELETE,
                TargetTermRef = "OBJ-1"
            }
        });

        var diagnostics = GraphLinter.Lint(doc);
        var lint008 = diagnostics.Where(d => d.RuleId == GraphLinter.RuleRequirementConflict).ToList();

        Assert.Equal(2, lint008.Count);
        Assert.All(lint008, d => Assert.Equal(LintSeverity.Error, d.Severity));
        Assert.Contains(lint008, d => d.TargetId == "REQ-DEL-PROHIBIT" && d.Message.Contains("REQ-DEL-ALLOW") && d.Message.Contains("allow vs prohibit contradiction"));
        Assert.Contains(lint008, d => d.TargetId == "REQ-DEL-ALLOW" && d.Message.Contains("REQ-DEL-PROHIBIT") && d.Message.Contains("allow vs prohibit contradiction"));
    }

    [Fact]
    public void Lint008_DoesNotTrigger_WhenPreconditionsAreMutuallyExclusive()
    {
        var doc = CreateValidBaseDocument();
        doc.Glossary.Add(new ApprovedGlossaryTerm
        {
            Id = "SV-ROLE",
            CanonicalName = "ユーザーロール",
            ConceptType = ConceptType.StateVariable,
            LogicalType = new LogicalTypeDefinition
            {
                Kind = LogicalTypeKind.Enum,
                AllowedValues = ["Admin", "User", "Guest"]
            }
        });
        doc.Glossary.Add(new ApprovedGlossaryTerm
        {
            Id = "EVT-DEL",
            CanonicalName = "顧客削除要求",
            ConceptType = ConceptType.Event
        });

        // 管理者以外は顧客削除禁止 (NEQ Admin)
        doc.Requirements.Add(new CanonicalRequirementNode
        {
            Id = "REQ-DEL-PROHIBIT",
            Title = "管理者以外の削除禁止",
            NodeKind = RequirementNodeKind.Atomic,
            ParentRequirementId = "REQ-1",
            EarsPattern = EarsEntryPattern.UnwantedBehavior,
            SubjectSystemRef = "SYS-1",
            TriggerEventRef = "EVT-DEL",
            Preconditions =
            [
                new PreconditionClause
                {
                    StateTermRef = "SV-ROLE",
                    Operator = ComparisonOperator.NEQ,
                    ExpectedValues = ["Admin"]
                }
            ],
            Behavior = new BehaviorNode
            {
                Kind = BehaviorKind.PROHIBIT,
                TargetTermRef = "OBJ-1",
                ProhibitedAction = BehaviorKind.DELETE
            }
        });

        // 管理者は顧客を削除する (EQ Admin) -> 積集合が空なので衝突なし
        doc.Requirements.Add(new CanonicalRequirementNode
        {
            Id = "REQ-DEL-ADMIN",
            Title = "管理者の顧客削除",
            NodeKind = RequirementNodeKind.Atomic,
            ParentRequirementId = "REQ-1",
            EarsPattern = EarsEntryPattern.EventDriven,
            SubjectSystemRef = "SYS-1",
            TriggerEventRef = "EVT-DEL",
            Preconditions =
            [
                new PreconditionClause
                {
                    StateTermRef = "SV-ROLE",
                    Operator = ComparisonOperator.EQ,
                    ExpectedValues = ["Admin"]
                }
            ],
            Behavior = new BehaviorNode
            {
                Kind = BehaviorKind.DELETE,
                TargetTermRef = "OBJ-1"
            }
        });

        var diagnostics = GraphLinter.Lint(doc);

        Assert.DoesNotContain(diagnostics, d => d.RuleId == GraphLinter.RuleRequirementConflict);
    }

    [Fact]
    public void Lint008_DetectsTransitionTargetConflict_ForSameTriggerAndFromValue()
    {
        var doc = CreateValidBaseDocument();
        doc.Glossary.Add(new ApprovedGlossaryTerm
        {
            Id = "LC-SCREEN",
            CanonicalName = "画面状態",
            ConceptType = ConceptType.LifecycleState,
            LogicalType = new LogicalTypeDefinition
            {
                Kind = LogicalTypeKind.Enum,
                AllowedValues = ["一覧", "詳細", "作成"]
            }
        });

        // 同じトリガー (EVT-1)・同じ遷移元 ("作成") から "一覧" と "詳細" へ別々に遷移
        doc.Requirements.Add(new CanonicalRequirementNode
        {
            Id = "REQ-TRANS-A",
            Title = "一覧へ遷移",
            NodeKind = RequirementNodeKind.Atomic,
            ParentRequirementId = "REQ-1",
            EarsPattern = EarsEntryPattern.EventDriven,
            SubjectSystemRef = "SYS-1",
            TriggerEventRef = "EVT-1",
            Behavior = new BehaviorNode
            {
                Kind = BehaviorKind.TRANSITION,
                TargetTermRef = "LC-SCREEN",
                FromValue = "作成",
                ToValue = "一覧"
            }
        });

        doc.Requirements.Add(new CanonicalRequirementNode
        {
            Id = "REQ-TRANS-B",
            Title = "詳細へ遷移",
            NodeKind = RequirementNodeKind.Atomic,
            ParentRequirementId = "REQ-1",
            EarsPattern = EarsEntryPattern.EventDriven,
            SubjectSystemRef = "SYS-1",
            TriggerEventRef = "EVT-1",
            Behavior = new BehaviorNode
            {
                Kind = BehaviorKind.TRANSITION,
                TargetTermRef = "LC-SCREEN",
                FromValue = "作成",
                ToValue = "詳細"
            }
        });

        var diagnostics = GraphLinter.Lint(doc);
        var lint008 = diagnostics.Where(d => d.RuleId == GraphLinter.RuleRequirementConflict).ToList();

        Assert.Equal(2, lint008.Count);
        Assert.All(lint008, d => Assert.Equal(LintSeverity.Error, d.Severity));
        Assert.Contains(lint008, d => d.TargetId == "REQ-TRANS-A" && d.Message.Contains("REQ-TRANS-B") && d.Message.Contains("conflicting transition targets"));
        Assert.Contains(lint008, d => d.TargetId == "REQ-TRANS-B" && d.Message.Contains("REQ-TRANS-A") && d.Message.Contains("conflicting transition targets"));
    }

    [Fact]
    public void Lint009_DetectsInvalidOrDuplicateGlossarySymbols()
    {
        var doc = CreateValidBaseDocument();
        doc.Glossary.Add(new ApprovedGlossaryTerm
        {
            Id = "SV-BAD-NAME",
            CanonicalName = "車速",
            DisplayName = "車速",
            ConceptType = ConceptType.StateVariable,
            LogicalType = new LogicalTypeDefinition
            {
                Kind = LogicalTypeKind.Enum,
                AllowedValues = ["高速", "LowSpeed", "LowSpeed"]
            }
        });
        doc.Glossary.Add(new ApprovedGlossaryTerm
        {
            Id = "SYS-DUP",
            CanonicalName = "TargetSystem",
            DisplayName = "重複システム",
            ConceptType = ConceptType.System
        });

        var diagnostics = GraphLinter.Lint(doc);
        var lint009 = diagnostics.Where(d => d.RuleId == GraphLinter.RuleGlossarySymbolConvention).ToList();

        Assert.Equal(4, lint009.Count);
        Assert.All(lint009, d => Assert.Equal(LintSeverity.Error, d.Severity));
        Assert.Contains(lint009, d => d.TargetId == "SV-BAD-NAME" && d.Message.Contains("invalid CanonicalName"));
        Assert.Contains(lint009, d => d.TargetId == "SV-BAD-NAME" && d.Message.Contains("invalid AllowedValue '高速'"));
        Assert.Contains(lint009, d => d.TargetId == "SV-BAD-NAME" && d.Message.Contains("duplicate AllowedValue 'LowSpeed'"));
        Assert.Contains(lint009, d => d.TargetId == "SYS-DUP" && d.Message.Contains("duplicate CanonicalName 'TargetSystem'"));
    }

    [Fact]
    public void Lint010_DetectsOrphanValueLabelsKeys()
    {
        var doc = CreateValidBaseDocument();
        doc.Glossary.Add(new ApprovedGlossaryTerm
        {
            Id = "SV-SPEED",
            CanonicalName = "VehicleSpeed",
            DisplayName = "車速",
            ConceptType = ConceptType.StateVariable,
            Unit = "km/h",
            AbstractionNote = "車速 >= 30 km/h を HighSpeed として抽象化",
            LogicalType = new LogicalTypeDefinition
            {
                Kind = LogicalTypeKind.Enum,
                AllowedValues = ["LowSpeed", "HighSpeed"],
                ValueLabels = new Dictionary<string, string>
                {
                    ["LowSpeed"] = "低速",
                    ["UnknownSpeed"] = "不明"
                }
            }
        });
        doc.Glossary.Add(new ApprovedGlossaryTerm
        {
            Id = "SV-FLAG",
            CanonicalName = "BrakeActive",
            DisplayName = "ブレーキ作動",
            ConceptType = ConceptType.StateVariable,
            LogicalType = new LogicalTypeDefinition
            {
                Kind = LogicalTypeKind.Boolean,
                ValueLabels = new Dictionary<string, string>
                {
                    ["true"] = "作動中",
                    ["maybe"] = "不明"
                }
            }
        });

        var diagnostics = GraphLinter.Lint(doc);
        var lint010 = diagnostics.Where(d => d.RuleId == GraphLinter.RuleGlossaryValueLabelsIntegrity).ToList();

        Assert.Equal(2, lint010.Count);
        Assert.All(lint010, d => Assert.Equal(LintSeverity.Error, d.Severity));
        Assert.Contains(lint010, d => d.TargetId == "SV-SPEED" && d.Message.Contains("UnknownSpeed"));
        Assert.Contains(lint010, d => d.TargetId == "SV-FLAG" && d.Message.Contains("maybe"));
    }

    [Fact]
    public void Lint011_DetectsDeprecatedLabelUsageAcrossDocument()
    {
        var doc = CreateValidBaseDocument();
        doc.Glossary.Add(new ApprovedGlossaryTerm
        {
            Id = "SV-SPEED",
            CanonicalName = "VehicleSpeed",
            DisplayName = "車速",
            ConceptType = ConceptType.StateVariable,
            Unit = "km/h",
            DeprecatedLabels = ["スピード"],
            LogicalType = new LogicalTypeDefinition
            {
                Kind = LogicalTypeKind.Enum,
                AllowedValues = ["LowSpeed", "HighSpeed"]
            }
        });
        doc.Requirements[0].Title = "スピード監視要求";
        doc.EscapeRecords.Add(new FreeTextEscapeRecord
        {
            Id = "ESC-DEP",
            RequirementId = "REQ-1-1",
            Reason = EscapeReason.QuantitativeCalculation,
            FreeText = "スピードが30km/h以上の場合",
            Status = EscapeRecordStatus.RESOLVED
        });

        var diagnostics = GraphLinter.Lint(doc);
        var lint011 = diagnostics.Where(d => d.RuleId == GraphLinter.RuleDeprecatedLabelUsage).ToList();

        Assert.Equal(2, lint011.Count);
        Assert.All(lint011, d => Assert.Equal(LintSeverity.Error, d.Severity));
        Assert.Contains(lint011, d => d.TargetId == "REQ-1" && d.Message.Contains("スピード"));
        Assert.Contains(lint011, d => d.TargetId == "ESC-DEP" && d.Message.Contains("スピード"));
    }
}
