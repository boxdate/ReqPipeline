using ReqPipeline.Core.Domain;
using ReqPipeline.Core.Validation;

namespace ReqPipeline.UnitTests;

public class GraphLinterTests
{
    private static List<BehaviorDefinition> CreateStandardBehaviorCatalog() =>
    [
        new BehaviorDefinition
        {
            BehaviorDefId = "BEH-CREATE",
            CanonicalLabel = "create",
            JapaneseLabel = "作成",
            Effect = SemanticEffect.ManageEntity,
            AllowedTargetTypes = [ConceptType.DomainObject],
            JapaneseTemplate = "{target}を作成すること",
            EnglishTemplate = "create {target}"
        },
        new BehaviorDefinition
        {
            BehaviorDefId = "BEH-DELETE",
            CanonicalLabel = "delete",
            JapaneseLabel = "削除",
            Effect = SemanticEffect.ManageEntity,
            AllowedTargetTypes = [ConceptType.DomainObject],
            JapaneseTemplate = "{target}を削除すること",
            EnglishTemplate = "delete {target}"
        },
        new BehaviorDefinition
        {
            BehaviorDefId = "BEH-SET",
            CanonicalLabel = "set",
            JapaneseLabel = "設定",
            Effect = SemanticEffect.MutateState,
            AllowedTargetTypes = [ConceptType.StateVariable, ConceptType.LifecycleState],
            RequiresFromValue = false,
            RequiresToValue = true,
            JapaneseTemplate = "{target}を{to}に設定すること",
            EnglishTemplate = "set {target} to {to}"
        },
        new BehaviorDefinition
        {
            BehaviorDefId = "BEH-TRANSITION",
            CanonicalLabel = "transition",
            JapaneseLabel = "遷移",
            Effect = SemanticEffect.MutateState,
            AllowedTargetTypes = [ConceptType.LifecycleState],
            RequiresFromValue = true,
            RequiresToValue = true,
            JapaneseTemplate = "{target}を{from}から{to}へ遷移すること",
            EnglishTemplate = "transition {target} from {from} to {to}"
        },
        new BehaviorDefinition
        {
            BehaviorDefId = "BEH-DISPLAY",
            CanonicalLabel = "display",
            JapaneseLabel = "表示",
            Effect = SemanticEffect.EmitAction,
            AllowedTargetTypes = [ConceptType.DomainObject],
            JapaneseTemplate = "{target}を表示すること",
            EnglishTemplate = "display {target}"
        },
        new BehaviorDefinition
        {
            BehaviorDefId = "BEH-PROHIBIT",
            CanonicalLabel = "prohibit",
            JapaneseLabel = "禁止",
            Effect = SemanticEffect.Prohibit,
            AllowedTargetTypes = [ConceptType.DomainObject, ConceptType.LifecycleState, ConceptType.StateVariable],
            JapaneseTemplate = "{target}の{prohibitedAction}を禁止すること",
            EnglishTemplate = "prohibit {prohibitedAction} on {target}"
        }
    ];

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
        BehaviorCatalog = CreateStandardBehaviorCatalog(),
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
                    BehaviorDefRef = "BEH-CREATE",
                    TargetTermRef = "OBJ-1"
                }
            }
        ]
    };

    [Fact]
    public void BaseDocument_HasNoErrors()
    {
        var doc = CreateValidBaseDocument();
        var diagnostics = GraphLinter.Lint(doc);
        Assert.DoesNotContain(diagnostics, d => d.Severity == LintSeverity.Error);
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
        child.Behavior!.BehaviorDefRef = "BEH-MISSING";
        child.Behavior.ProhibitedBehaviorDefRef = "BEH-PROHIBIT-MISSING";
        child.Behavior.TargetTermRef = "TARGET-MISSING";

        var diagnostics = GraphLinter.Lint(doc);
        var lint001 = diagnostics.Where(d => d.RuleId == GraphLinter.RuleMissingReference).ToList();

        Assert.Equal(8, lint001.Count);
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
        child.Behavior!.TargetTermRef = "SV-BOOL";     // StateVariable instead of DomainObject for BEH-CREATE

        var diagnostics = GraphLinter.Lint(doc);
        var lint002 = diagnostics.Where(d => d.RuleId == GraphLinter.RuleConceptTypeMismatch).ToList();

        Assert.Equal(5, lint002.Count);
        Assert.All(lint002, d => Assert.Equal(LintSeverity.Error, d.Severity));
    }

    [Fact]
    public void Lint003_DetectsLogicalDomainClosureViolations()
    {
        var doc = CreateValidBaseDocument();
        var child = doc.Requirements[1];
        // Invalid boolean value ("yes" is not "true"/"false")
        child.Preconditions[0].ExpectedValues = ["yes"];
        // Invalid enum values on BEH-TRANSITION
        child.Behavior = new BehaviorNode
        {
            BehaviorDefRef = "BEH-TRANSITION",
            TargetTermRef = "LC-1",
            FromValue = "InvalidFrom",
            ToValue = "InvalidTo"
        };

        // Also add a BEH-SET with invalid value
        doc.Requirements.Add(new CanonicalRequirementNode
        {
            Id = "REQ-1-2",
            Title = "不正なSET",
            NodeKind = RequirementNodeKind.Atomic,
            ParentRequirementId = "REQ-1",
            SubjectSystemRef = "SYS-1",
            Behavior = new BehaviorNode
            {
                BehaviorDefRef = "BEH-SET",
                TargetTermRef = "SV-BOOL",
                ToValue = "maybe"
            }
        });

        // Also add a BEH-PROHIBIT with invalid ProhibitedToValue
        doc.Requirements.Add(new CanonicalRequirementNode
        {
            Id = "REQ-1-3",
            Title = "不正なProhibitedToValue",
            NodeKind = RequirementNodeKind.Atomic,
            ParentRequirementId = "REQ-1",
            SubjectSystemRef = "SYS-1",
            Behavior = new BehaviorNode
            {
                BehaviorDefRef = "BEH-PROHIBIT",
                TargetTermRef = "LC-1",
                ProhibitedBehaviorDefRef = "BEH-TRANSITION",
                ProhibitedToValue = "NonExistentState"
            }
        });

        var diagnostics = GraphLinter.Lint(doc);
        var lint003 = diagnostics.Where(d => d.RuleId == GraphLinter.RuleLogicalDomainViolation).ToList();

        Assert.Equal(5, lint003.Count);
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
                BehaviorDefRef = "BEH-TRANSITION",
                TargetTermRef = "LC-1",
                FromValue = "Draft",
                ToValue = null
            }
        });
        // TRANSITION missing FromValue
        doc.Requirements.Add(new CanonicalRequirementNode
        {
            Id = "REQ-BAD-TRANS-FROM",
            Title = "遷移元欠落",
            NodeKind = RequirementNodeKind.Atomic,
            ParentRequirementId = "REQ-1",
            SubjectSystemRef = "SYS-1",
            Behavior = new BehaviorNode
            {
                BehaviorDefRef = "BEH-TRANSITION",
                TargetTermRef = "LC-1",
                FromValue = null,
                ToValue = "Published"
            }
        });
        // SET missing ToValue
        doc.Requirements.Add(new CanonicalRequirementNode
        {
            Id = "REQ-BAD-SET",
            Title = "設定値欠落",
            NodeKind = RequirementNodeKind.Atomic,
            ParentRequirementId = "REQ-1",
            SubjectSystemRef = "SYS-1",
            Behavior = new BehaviorNode
            {
                BehaviorDefRef = "BEH-SET",
                TargetTermRef = "SV-BOOL",
                ToValue = ""
            }
        });
        // PROHIBIT referencing another PROHIBIT behavior (self-prohibition / prohibiting prohibition)
        doc.Requirements.Add(new CanonicalRequirementNode
        {
            Id = "REQ-BAD-PROHIBIT",
            Title = "禁止の禁止",
            NodeKind = RequirementNodeKind.Atomic,
            ParentRequirementId = "REQ-1",
            SubjectSystemRef = "SYS-1",
            Behavior = new BehaviorNode
            {
                BehaviorDefRef = "BEH-PROHIBIT",
                TargetTermRef = "OBJ-1",
                ProhibitedBehaviorDefRef = "BEH-PROHIBIT"
            }
        });

        var diagnostics = GraphLinter.Lint(doc);
        var lint005 = diagnostics.Where(d => d.RuleId == GraphLinter.RuleBehaviorFieldIntegrity).ToList();

        Assert.Equal(4, lint005.Count);
        Assert.Contains(lint005, d => d.TargetId == "REQ-BAD-TRANS");
        Assert.Contains(lint005, d => d.TargetId == "REQ-BAD-TRANS-FROM");
        Assert.Contains(lint005, d => d.TargetId == "REQ-BAD-SET");
        Assert.Contains(lint005, d => d.TargetId == "REQ-BAD-PROHIBIT");
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

        Assert.DoesNotContain(diagnostics, d => d.RuleId == GraphLinter.RulePendingEscapeRecord);
        Assert.DoesNotContain(diagnostics, d => d.Severity == LintSeverity.Error);
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
                BehaviorDefRef = "BEH-PROHIBIT",
                TargetTermRef = "OBJ-1",
                ProhibitedBehaviorDefRef = "BEH-DELETE"
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
                BehaviorDefRef = "BEH-DELETE",
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
                BehaviorDefRef = "BEH-PROHIBIT",
                TargetTermRef = "OBJ-1",
                ProhibitedBehaviorDefRef = "BEH-DELETE"
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
                BehaviorDefRef = "BEH-DELETE",
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
                BehaviorDefRef = "BEH-TRANSITION",
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
                BehaviorDefRef = "BEH-TRANSITION",
                TargetTermRef = "LC-SCREEN",
                FromValue = "作成",
                ToValue = "詳細"
            }
        });

        var diagnostics = GraphLinter.Lint(doc);
        var lint008 = diagnostics.Where(d => d.RuleId == GraphLinter.RuleRequirementConflict).ToList();

        Assert.Equal(2, lint008.Count);
        Assert.All(lint008, d => Assert.Equal(LintSeverity.Error, d.Severity));
        Assert.Contains(lint008, d => d.TargetId == "REQ-TRANS-A" && d.Message.Contains("REQ-TRANS-B") && d.Message.Contains("conflicting state mutation targets"));
        Assert.Contains(lint008, d => d.TargetId == "REQ-TRANS-B" && d.Message.Contains("REQ-TRANS-A") && d.Message.Contains("conflicting state mutation targets"));
    }

    [Fact]
    public void Lint008_ProhibitedToValue_DetectsConflictOnlyWhenToValueMatches()
    {
        var doc = CreateValidBaseDocument();

        // LC-1 への Archived 遷移のみを禁止
        doc.Requirements.Add(new CanonicalRequirementNode
        {
            Id = "REQ-PROHIBIT-ARCHIVE",
            Title = "アーカイブ遷移禁止",
            NodeKind = RequirementNodeKind.Atomic,
            ParentRequirementId = "REQ-1",
            EarsPattern = EarsEntryPattern.EventDriven,
            SubjectSystemRef = "SYS-1",
            TriggerEventRef = "EVT-1",
            Behavior = new BehaviorNode
            {
                BehaviorDefRef = "BEH-PROHIBIT",
                TargetTermRef = "LC-1",
                ProhibitedBehaviorDefRef = "BEH-TRANSITION",
                ProhibitedToValue = "Archived"
            }
        });

        // Published への遷移は ProhibitedToValue と一致しないので衝突しない
        doc.Requirements.Add(new CanonicalRequirementNode
        {
            Id = "REQ-TRANS-PUBLISH",
            Title = "公開へ遷移",
            NodeKind = RequirementNodeKind.Atomic,
            ParentRequirementId = "REQ-1",
            EarsPattern = EarsEntryPattern.EventDriven,
            SubjectSystemRef = "SYS-1",
            TriggerEventRef = "EVT-1",
            Behavior = new BehaviorNode
            {
                BehaviorDefRef = "BEH-TRANSITION",
                TargetTermRef = "LC-1",
                FromValue = "Draft",
                ToValue = "Published"
            }
        });

        var noConflictDiagnostics = GraphLinter.Lint(doc);
        Assert.DoesNotContain(noConflictDiagnostics, d => d.RuleId == GraphLinter.RuleRequirementConflict);

        // Archived への遷移を追加すると衝突検知される
        doc.Requirements.Add(new CanonicalRequirementNode
        {
            Id = "REQ-TRANS-ARCHIVE",
            Title = "アーカイブへ遷移",
            NodeKind = RequirementNodeKind.Atomic,
            ParentRequirementId = "REQ-1",
            EarsPattern = EarsEntryPattern.EventDriven,
            SubjectSystemRef = "SYS-1",
            TriggerEventRef = "EVT-1",
            Behavior = new BehaviorNode
            {
                BehaviorDefRef = "BEH-TRANSITION",
                TargetTermRef = "LC-1",
                FromValue = "Published",
                ToValue = "Archived"
            }
        });

        var conflictDiagnostics = GraphLinter.Lint(doc);
        var lint008 = conflictDiagnostics.Where(d => d.RuleId == GraphLinter.RuleRequirementConflict).ToList();
        Assert.Equal(2, lint008.Count);
        Assert.Contains(lint008, d => d.TargetId == "REQ-PROHIBIT-ARCHIVE" && d.Message.Contains("REQ-TRANS-ARCHIVE"));
        Assert.Contains(lint008, d => d.TargetId == "REQ-TRANS-ARCHIVE" && d.Message.Contains("REQ-PROHIBIT-ARCHIVE"));
    }

    [Fact]
    public void Lint008_DetectsMutateStateConflict_BetweenSetAndTransition()
    {
        var doc = CreateValidBaseDocument();

        doc.Requirements.Add(new CanonicalRequirementNode
        {
            Id = "REQ-SET-PUBLISHED",
            Title = "公開設定",
            NodeKind = RequirementNodeKind.Atomic,
            ParentRequirementId = "REQ-1",
            EarsPattern = EarsEntryPattern.EventDriven,
            SubjectSystemRef = "SYS-1",
            TriggerEventRef = "EVT-1",
            Behavior = new BehaviorNode
            {
                BehaviorDefRef = "BEH-SET",
                TargetTermRef = "LC-1",
                ToValue = "Published"
            }
        });

        doc.Requirements.Add(new CanonicalRequirementNode
        {
            Id = "REQ-TRANS-ARCHIVE",
            Title = "アーカイブ遷移",
            NodeKind = RequirementNodeKind.Atomic,
            ParentRequirementId = "REQ-1",
            EarsPattern = EarsEntryPattern.EventDriven,
            SubjectSystemRef = "SYS-1",
            TriggerEventRef = "EVT-1",
            Behavior = new BehaviorNode
            {
                BehaviorDefRef = "BEH-TRANSITION",
                TargetTermRef = "LC-1",
                FromValue = "Draft",
                ToValue = "Archived"
            }
        });

        var diagnostics = GraphLinter.Lint(doc);
        var lint008 = diagnostics.Where(d => d.RuleId == GraphLinter.RuleRequirementConflict).ToList();
        Assert.Equal(2, lint008.Count);
        Assert.Contains(lint008, d => d.TargetId == "REQ-SET-PUBLISHED" && d.Message.Contains("conflicting state mutation targets"));
        Assert.Contains(lint008, d => d.TargetId == "REQ-TRANS-ARCHIVE" && d.Message.Contains("conflicting state mutation targets"));
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

    [Fact]
    public void Lint012_FsmCompleteness_WarnsOnUnreachableAndDeadEndStates()
    {
        var doc = CreateValidBaseDocument();
        doc.Requirements.Add(new CanonicalRequirementNode
        {
            Id = "REQ-1-2",
            Title = "公開遷移",
            NodeKind = RequirementNodeKind.Atomic,
            ParentRequirementId = "REQ-1",
            RationaleRefs = ["RAT-1"],
            EarsPattern = EarsEntryPattern.EventDriven,
            SubjectSystemRef = "SYS-1",
            ActorRef = "ACT-1",
            TriggerEventRef = "EVT-1",
            Behavior = new BehaviorNode
            {
                BehaviorDefRef = "BEH-TRANSITION",
                TargetTermRef = "LC-1",
                FromValue = "Draft",
                ToValue = "Published"
            }
        });

        var diagnostics = GraphLinter.Lint(doc);
        var fsmWarnings = diagnostics.Where(d => d.RuleId == GraphLinter.RuleFsmCompleteness).ToList();

        Assert.NotEmpty(fsmWarnings);
        Assert.All(fsmWarnings, d => Assert.Equal(LintSeverity.Warning, d.Severity));
        Assert.Contains(fsmWarnings, d => d.TargetId == "LC-1" && d.Message.Contains("Published") && d.Message.Contains("dead-end"));
        Assert.Contains(fsmWarnings, d => d.TargetId == "LC-1" && d.Message.Contains("Archived") && d.Message.Contains("unreachable"));
    }

    [Fact]
    public void Lint012_FsmCompleteness_NoWarningWhenAllStatesReachableAndCyclic()
    {
        var doc = CreateValidBaseDocument();
        doc.Requirements.Add(new CanonicalRequirementNode
        {
            Id = "REQ-1-2",
            Title = "Draft to Published",
            NodeKind = RequirementNodeKind.Atomic,
            ParentRequirementId = "REQ-1",
            RationaleRefs = ["RAT-1"],
            SubjectSystemRef = "SYS-1",
            Behavior = new BehaviorNode { BehaviorDefRef = "BEH-TRANSITION", TargetTermRef = "LC-1", FromValue = "Draft", ToValue = "Published" }
        });
        doc.Requirements.Add(new CanonicalRequirementNode
        {
            Id = "REQ-1-3",
            Title = "Published to Archived",
            NodeKind = RequirementNodeKind.Atomic,
            ParentRequirementId = "REQ-1",
            RationaleRefs = ["RAT-1"],
            SubjectSystemRef = "SYS-1",
            Behavior = new BehaviorNode { BehaviorDefRef = "BEH-TRANSITION", TargetTermRef = "LC-1", FromValue = "Published", ToValue = "Archived" }
        });
        doc.Requirements.Add(new CanonicalRequirementNode
        {
            Id = "REQ-1-4",
            Title = "Archived to Draft",
            NodeKind = RequirementNodeKind.Atomic,
            ParentRequirementId = "REQ-1",
            RationaleRefs = ["RAT-1"],
            SubjectSystemRef = "SYS-1",
            Behavior = new BehaviorNode { BehaviorDefRef = "BEH-TRANSITION", TargetTermRef = "LC-1", FromValue = "Archived", ToValue = "Draft" }
        });

        var diagnostics = GraphLinter.Lint(doc);
        var fsmWarnings = diagnostics.Where(d => d.RuleId == GraphLinter.RuleFsmCompleteness).ToList();

        Assert.Empty(fsmWarnings);
    }

    [Fact]
    public void Lint013_UnusedGlossaryTerm_WarnsWhenUnreferencedAndClearWhenReferenced()
    {
        var doc = CreateValidBaseDocument();

        // LC-1 is not referenced in BaseDocument
        var diagnostics = GraphLinter.Lint(doc);
        var unusedWarnings = diagnostics.Where(d => d.RuleId == GraphLinter.RuleUnusedGlossaryTerm).ToList();
        var single = Assert.Single(unusedWarnings);
        Assert.Equal(LintSeverity.Warning, single.Severity);
        Assert.Equal("LC-1", single.TargetId);

        // Reference LC-1 in a requirement
        doc.Requirements.Add(new CanonicalRequirementNode
        {
            Id = "REQ-1-2",
            Title = "遷移",
            NodeKind = RequirementNodeKind.Atomic,
            ParentRequirementId = "REQ-1",
            RationaleRefs = ["RAT-1"],
            SubjectSystemRef = "SYS-1",
            Behavior = new BehaviorNode { BehaviorDefRef = "BEH-TRANSITION", TargetTermRef = "LC-1", FromValue = "Draft", ToValue = "Published" }
        });

        var afterDiagnostics = GraphLinter.Lint(doc);
        Assert.DoesNotContain(afterDiagnostics, d => d.RuleId == GraphLinter.RuleUnusedGlossaryTerm);
    }
}
