using ReqPipeline.Core.Analysis;
using ReqPipeline.Core.Domain;

namespace ReqPipeline.UnitTests;

public class JourneySimulatorTests
{
    private static string GetSamplePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "samples", "crud-sample.v2.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }

        throw new FileNotFoundException("Could not locate samples/crud-sample.v2.json from test output directory.");
    }

    private static KnowledgeGraphDocument CreateTestDocument() => new()
    {
        SchemaVersion = "2.0",
        DocumentId = "DOC-JRN-TEST",
        Title = "Journey Simulator テスト文書",
        Rationales = [new RationaleNode { Id = "RAT-1", Title = "テスト理由", Description = "テスト用" }],
        Glossary =
        [
            new ApprovedGlossaryTerm { Id = "SYS-1", CanonicalName = "AppSystem", DisplayName = "アプリ", ConceptType = ConceptType.System },
            new ApprovedGlossaryTerm { Id = "ACT-1", CanonicalName = "User", DisplayName = "利用者", ConceptType = ConceptType.Actor },
            new ApprovedGlossaryTerm { Id = "OBJ-1", CanonicalName = "Order", DisplayName = "注文", ConceptType = ConceptType.DomainObject },
            new ApprovedGlossaryTerm
            {
                Id = "LC-1",
                CanonicalName = "OrderState",
                DisplayName = "注文状態",
                ConceptType = ConceptType.LifecycleState,
                LogicalType = new LogicalTypeDefinition
                {
                    Kind = LogicalTypeKind.Enum,
                    AllowedValues = ["Draft", "Submitted", "Approved"]
                }
            },
            new ApprovedGlossaryTerm
            {
                Id = "SV-AUTH",
                CanonicalName = "IsAuthenticated",
                DisplayName = "認証済み",
                ConceptType = ConceptType.StateVariable,
                LogicalType = new LogicalTypeDefinition
                {
                    Kind = LogicalTypeKind.Boolean
                }
            },
            new ApprovedGlossaryTerm
            {
                Id = "SV-ROLE",
                CanonicalName = "UserRole",
                DisplayName = "ユーザーロール",
                ConceptType = ConceptType.StateVariable,
                LogicalType = new LogicalTypeDefinition
                {
                    Kind = LogicalTypeKind.Enum,
                    AllowedValues = ["Admin", "Member", "Guest"]
                }
            }
        ],
        BehaviorCatalog =
        [
            new BehaviorDefinition
            {
                BehaviorDefId = "BEH-CREATE",
                CanonicalLabel = "create",
                JapaneseLabel = "作成",
                Effect = SemanticEffect.ManageEntity,
                AllowedTargetTypes = [ConceptType.DomainObject]
            },
            new BehaviorDefinition
            {
                BehaviorDefId = "BEH-DELETE",
                CanonicalLabel = "delete",
                JapaneseLabel = "削除",
                Effect = SemanticEffect.ManageEntity,
                AllowedTargetTypes = [ConceptType.DomainObject]
            },
            new BehaviorDefinition
            {
                BehaviorDefId = "BEH-SET",
                CanonicalLabel = "set",
                JapaneseLabel = "設定",
                Effect = SemanticEffect.MutateState,
                AllowedTargetTypes = [ConceptType.StateVariable, ConceptType.LifecycleState],
                RequiresFromValue = false,
                RequiresToValue = true
            },
            new BehaviorDefinition
            {
                BehaviorDefId = "BEH-TRANSITION",
                CanonicalLabel = "transition",
                JapaneseLabel = "遷移",
                Effect = SemanticEffect.MutateState,
                AllowedTargetTypes = [ConceptType.LifecycleState],
                RequiresFromValue = true,
                RequiresToValue = true
            },
            new BehaviorDefinition
            {
                BehaviorDefId = "BEH-PROHIBIT",
                CanonicalLabel = "prohibit",
                JapaneseLabel = "禁止",
                Effect = SemanticEffect.Prohibit,
                AllowedTargetTypes = [ConceptType.DomainObject, ConceptType.LifecycleState, ConceptType.StateVariable]
            }
        ],
        Requirements =
        [
            new CanonicalRequirementNode
            {
                Id = "REQ-COMP",
                Title = "親要求",
                NodeKind = RequirementNodeKind.Composite,
                RationaleRefs = ["RAT-1"],
                SubjectSystemRef = "SYS-1"
            },
            new CanonicalRequirementNode
            {
                Id = "REQ-LOGIN",
                Title = "ログイン設定",
                NodeKind = RequirementNodeKind.Atomic,
                ParentRequirementId = "REQ-COMP",
                SubjectSystemRef = "SYS-1",
                Behavior = new BehaviorNode
                {
                    BehaviorDefRef = "BEH-SET",
                    TargetTermRef = "SV-AUTH",
                    ToValue = "true"
                }
            },
            new CanonicalRequirementNode
            {
                Id = "REQ-CREATE",
                Title = "注文作成",
                NodeKind = RequirementNodeKind.Atomic,
                ParentRequirementId = "REQ-COMP",
                SubjectSystemRef = "SYS-1",
                Preconditions =
                [
                    new PreconditionClause { StateTermRef = "SV-AUTH", Operator = ComparisonOperator.EQ, ExpectedValues = ["true"] }
                ],
                Behavior = new BehaviorNode
                {
                    BehaviorDefRef = "BEH-CREATE",
                    TargetTermRef = "OBJ-1"
                }
            },
            new CanonicalRequirementNode
            {
                Id = "REQ-SUBMIT",
                Title = "注文申請",
                NodeKind = RequirementNodeKind.Atomic,
                ParentRequirementId = "REQ-COMP",
                SubjectSystemRef = "SYS-1",
                Preconditions =
                [
                    new PreconditionClause { StateTermRef = "SV-AUTH", Operator = ComparisonOperator.EQ, ExpectedValues = ["true"] }
                ],
                Behavior = new BehaviorNode
                {
                    BehaviorDefRef = "BEH-TRANSITION",
                    TargetTermRef = "LC-1",
                    FromValue = "Draft",
                    ToValue = "Submitted"
                }
            },
            new CanonicalRequirementNode
            {
                Id = "REQ-APPROVE",
                Title = "注文承認",
                NodeKind = RequirementNodeKind.Atomic,
                ParentRequirementId = "REQ-COMP",
                SubjectSystemRef = "SYS-1",
                Preconditions =
                [
                    new PreconditionClause { StateTermRef = "SV-ROLE", Operator = ComparisonOperator.IN, ExpectedValues = ["Admin"] }
                ],
                Behavior = new BehaviorNode
                {
                    BehaviorDefRef = "BEH-TRANSITION",
                    TargetTermRef = "LC-1",
                    FromValue = "Submitted",
                    ToValue = "Approved"
                }
            },
            new CanonicalRequirementNode
            {
                Id = "REQ-PROHIBIT-DELETE",
                Title = "一般ユーザーの削除禁止",
                NodeKind = RequirementNodeKind.Atomic,
                ParentRequirementId = "REQ-COMP",
                EarsPattern = EarsEntryPattern.UnwantedBehavior,
                SubjectSystemRef = "SYS-1",
                Preconditions =
                [
                    new PreconditionClause { StateTermRef = "SV-ROLE", Operator = ComparisonOperator.NEQ, ExpectedValues = ["Admin"] }
                ],
                Behavior = new BehaviorNode
                {
                    BehaviorDefRef = "BEH-PROHIBIT",
                    TargetTermRef = "OBJ-1",
                    ProhibitedBehaviorDefRef = "BEH-DELETE"
                }
            },
            new CanonicalRequirementNode
            {
                Id = "REQ-ESCAPE",
                Title = "Behavior未設定要求",
                NodeKind = RequirementNodeKind.Atomic,
                ParentRequirementId = "REQ-COMP",
                SubjectSystemRef = "SYS-1",
                Behavior = null
            }
        ]
    };

    [Fact]
    public void Simulate_ConnectedJourney_AllStepsPassedAndFinalStatesUpdated()
    {
        var doc = CreateTestDocument();
        var journey = new UserJourney
        {
            Id = "JRN-HAPPY",
            Title = "ログイン〜作成〜申請〜承認",
            InitialStates = new Dictionary<string, string>
            {
                ["SV-ROLE"] = "Admin"
            },
            Steps =
            [
                new JourneyStep { StepId = "S1", UserAction = "ログインする", RequirementRef = "REQ-LOGIN" },
                new JourneyStep { StepId = "S2", UserAction = "注文を作成する", RequirementRef = "REQ-CREATE" },
                new JourneyStep { StepId = "S3", UserAction = "注文を申請する", RequirementRef = "REQ-SUBMIT" },
                new JourneyStep { StepId = "S4", UserAction = "注文を承認する", RequirementRef = "REQ-APPROVE" }
            ]
        };

        var result = JourneySimulator.Simulate(doc, journey);

        Assert.True(result.IsAllPassed);
        Assert.Equal(0, result.FailedStepCount);
        Assert.Equal(4, result.PassedStepCount);
        Assert.Equal(0, result.BlockedStepCount);
        Assert.All(result.Steps, s => Assert.Equal(JourneyStepStatus.Passed, s.Status));

        // Initial LifecycleState defaulted to AllowedValues[0] ("Draft")
        Assert.Equal("Draft", result.InitialStates["LC-1"]);
        // Final states reflect SET and TRANSITION steps
        Assert.Equal("true", result.FinalStates["SV-AUTH"]);
        Assert.Equal("Approved", result.FinalStates["LC-1"]);
        Assert.Equal("Admin", result.FinalStates["SV-ROLE"]);
    }

    [Fact]
    public void Simulate_TransitionFromValueMismatch_DetectsStateMismatch()
    {
        var doc = CreateTestDocument();
        var journey = new UserJourney
        {
            Id = "JRN-MISMATCH",
            Title = "申請前に承認を試みる",
            InitialStates = new Dictionary<string, string>
            {
                ["SV-ROLE"] = "Admin"
            },
            Steps =
            [
                new JourneyStep { StepId = "S1", UserAction = "いきなり承認する", RequirementRef = "REQ-APPROVE" }
            ]
        };

        var result = JourneySimulator.Simulate(doc, journey);

        Assert.False(result.IsAllPassed);
        Assert.Equal(1, result.FailedStepCount);
        var step = Assert.Single(result.Steps);
        Assert.Equal(JourneyStepStatus.StateMismatch, step.Status);
        Assert.Contains("Expected FromValue 'Submitted'", step.Message);
        Assert.Contains("current is 'Draft'", step.Message);
        Assert.Equal("Draft", result.FinalStates["LC-1"]);
    }

    [Fact]
    public void Simulate_UnmetOrUnsetPrecondition_DetectsPreconditionFailed()
    {
        var doc = CreateTestDocument();
        var journey = new UserJourney
        {
            Id = "JRN-PRECOND",
            Title = "未認証または権限不足",
            InitialStates = new Dictionary<string, string>
            {
                // SV-AUTH is unset!
                ["SV-ROLE"] = "Member"
            },
            Steps =
            [
                new JourneyStep { StepId = "S1", UserAction = "未認証で注文申請", RequirementRef = "REQ-SUBMIT" },
                new JourneyStep { StepId = "S2", UserAction = "Member権限で承認", RequirementRef = "REQ-APPROVE" }
            ]
        };

        var result = JourneySimulator.Simulate(doc, journey);

        Assert.False(result.IsAllPassed);
        Assert.Equal(2, result.FailedStepCount);
        Assert.Equal(JourneyStepStatus.PreconditionFailed, result.Steps[0].Status);
        Assert.Contains("unset", result.Steps[0].Message);
        Assert.Equal(JourneyStepStatus.PreconditionFailed, result.Steps[1].Status);
        Assert.Contains("current is 'Member'", result.Steps[1].Message);
    }

    [Fact]
    public void Simulate_InvalidRequirementReferences_DetectsInvalidRequirement()
    {
        var doc = CreateTestDocument();
        var journey = new UserJourney
        {
            Id = "JRN-INVALID",
            Title = "不正要求参照",
            Steps =
            [
                new JourneyStep { StepId = "S1", UserAction = "存在しない要求", RequirementRef = "REQ-UNKNOWN" },
                new JourneyStep { StepId = "S2", UserAction = "Composite要求", RequirementRef = "REQ-COMP" },
                new JourneyStep { StepId = "S3", UserAction = "Behaviorなし要求", RequirementRef = "REQ-ESCAPE" }
            ]
        };

        var result = JourneySimulator.Simulate(doc, journey);

        Assert.False(result.IsAllPassed);
        Assert.Equal(3, result.FailedStepCount);
        Assert.All(result.Steps, s => Assert.Equal(JourneyStepStatus.InvalidRequirement, s.Status));
    }

    [Fact]
    public void Simulate_ProhibitBehavior_ReturnsBlockedWithoutFailingJourney()
    {
        var doc = CreateTestDocument();
        var journey = new UserJourney
        {
            Id = "JRN-PROHIBIT",
            Title = "禁止動作の確認",
            InitialStates = new Dictionary<string, string>
            {
                ["SV-ROLE"] = "Member"
            },
            Steps =
            [
                new JourneyStep { StepId = "S1", UserAction = "Memberが削除を試みる", RequirementRef = "REQ-PROHIBIT-DELETE" }
            ]
        };

        var result = JourneySimulator.Simulate(doc, journey);

        Assert.True(result.IsAllPassed);
        Assert.Equal(0, result.FailedStepCount);
        Assert.Equal(1, result.BlockedStepCount);
        var step = Assert.Single(result.Steps);
        Assert.Equal(JourneyStepStatus.Blocked, step.Status);
        Assert.Equal("Draft", result.FinalStates["LC-1"]);
    }

    [Fact]
    public void Simulate_CrudSamplePresetJourneys_BehaveAsExpected()
    {
        var doc = KnowledgeGraphDocument.FromJson(File.ReadAllText(GetSamplePath()));
        Assert.Equal(2, doc.Journeys.Count);

        // JRN-001: 新規登録〜詳細確認〜編集〜一覧復帰フロー (All Passed, returns to List)
        var jrn1 = doc.Journeys.Single(j => j.Id == "JRN-001");
        var res1 = JourneySimulator.Simulate(doc, jrn1);
        Assert.True(res1.IsAllPassed);
        Assert.Equal(0, res1.FailedStepCount);
        Assert.Equal(8, res1.PassedStepCount);
        Assert.All(res1.Steps, s => Assert.Equal(JourneyStepStatus.Passed, s.Status));
        Assert.Equal("List", res1.FinalStates["TERM-LC-001"]);

        // JRN-002: 管理者以外の削除ブロック確認フロー (Member -> Blocked, then Detail -> List Passed)
        var jrn2 = doc.Journeys.Single(j => j.Id == "JRN-002");
        var res2 = JourneySimulator.Simulate(doc, jrn2);
        Assert.True(res2.IsAllPassed);
        Assert.Equal(0, res2.FailedStepCount);
        Assert.Equal(1, res2.BlockedStepCount);
        Assert.Equal(1, res2.PassedStepCount);
        Assert.Equal(JourneyStepStatus.Blocked, res2.Steps[0].Status);
        Assert.Equal(JourneyStepStatus.Passed, res2.Steps[1].Status);
        Assert.Equal("List", res2.FinalStates["TERM-LC-001"]);

        // Switching UserRole to Admin causes REQ-002-1 precondition (NEQ Admin) to fail
        var jrn2AsAdmin = new UserJourney
        {
            Id = jrn2.Id,
            Title = jrn2.Title,
            InitialStates = new Dictionary<string, string>
            {
                ["TERM-LC-001"] = "Detail",
                ["TERM-SV-001"] = "Admin"
            },
            Steps = jrn2.Steps
        };
        var res2Admin = JourneySimulator.Simulate(doc, jrn2AsAdmin);
        Assert.False(res2Admin.IsAllPassed);
        Assert.Equal(1, res2Admin.FailedStepCount);
        Assert.Equal(JourneyStepStatus.PreconditionFailed, res2Admin.Steps[0].Status);
    }

    [Fact]
    public void Simulate_MutateStateWithoutFromValue_UpdatesRegardlessOfCurrentValue()
    {
        var doc = CreateTestDocument();
        doc.Requirements.Add(new CanonicalRequirementNode
        {
            Id = "REQ-FORCE-APPROVE",
            Title = "強制承認設定",
            NodeKind = RequirementNodeKind.Atomic,
            ParentRequirementId = "REQ-COMP",
            SubjectSystemRef = "SYS-1",
            Behavior = new BehaviorNode
            {
                BehaviorDefRef = "BEH-SET",
                TargetTermRef = "LC-1",
                FromValue = null,
                ToValue = "Approved"
            }
        });

        var journey = new UserJourney
        {
            Id = "JRN-SET-ANY",
            Title = "FromValueなしMutateStateテスト",
            Steps =
            [
                // SV-AUTH is initially unset; REQ-LOGIN sets it to true
                new JourneyStep { StepId = "S1", UserAction = "未設定状態からのSET", RequirementRef = "REQ-LOGIN" },
                // LC-1 is Draft; REQ-FORCE-APPROVE sets it to Approved without checking FromValue
                new JourneyStep { StepId = "S2", UserAction = "現在値に関わらずApprovedへ設定", RequirementRef = "REQ-FORCE-APPROVE" }
            ]
        };

        var result = JourneySimulator.Simulate(doc, journey);

        Assert.True(result.IsAllPassed);
        Assert.Equal(2, result.PassedStepCount);
        Assert.Equal("true", result.FinalStates["SV-AUTH"]);
        Assert.Equal("Approved", result.FinalStates["LC-1"]);
    }

    [Fact]
    public void Simulate_MutateStateWithFromValueWhenTargetUnset_DetectsStateMismatch()
    {
        var doc = CreateTestDocument();
        doc.Requirements.Add(new CanonicalRequirementNode
        {
            Id = "REQ-AUTH-TRANS",
            Title = "認証状態遷移",
            NodeKind = RequirementNodeKind.Atomic,
            ParentRequirementId = "REQ-COMP",
            SubjectSystemRef = "SYS-1",
            Behavior = new BehaviorNode
            {
                BehaviorDefRef = "BEH-TRANSITION",
                TargetTermRef = "SV-AUTH",
                FromValue = "false",
                ToValue = "true"
            }
        });

        var journey = new UserJourney
        {
            Id = "JRN-UNSET-MISMATCH",
            Title = "未設定ターゲットへのFromValue付き遷移",
            Steps =
            [
                // SV-AUTH is StateVariable and not in InitialStates -> unset!
                new JourneyStep { StepId = "S1", UserAction = "未設定のまま遷移を試みる", RequirementRef = "REQ-AUTH-TRANS" }
            ]
        };

        var result = JourneySimulator.Simulate(doc, journey);

        Assert.False(result.IsAllPassed);
        Assert.Equal(1, result.FailedStepCount);
        var step = Assert.Single(result.Steps);
        Assert.Equal(JourneyStepStatus.StateMismatch, step.Status);
        Assert.Contains("(unset)", step.Message);
    }
}
