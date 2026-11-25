using ReqPipeline.Core.Domain;
using ReqPipeline.Core.Rendering;
using ReqPipeline.Core.Validation;

namespace ReqPipeline.UnitTests;

public class V2CrudSampleAndRenderingTests
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

    [Fact]
    public void CrudSampleJson_DeserializesAndRoundTripsWithStringEnums()
    {
        var rawJson = File.ReadAllText(GetSamplePath());
        var doc = KnowledgeGraphDocument.FromJson(rawJson);

        Assert.Equal("2.0", doc.SchemaVersion);
        Assert.Equal("DOC-CRUD-001", doc.DocumentId);
        Assert.Equal(2, doc.Rationales.Count);
        Assert.Equal(10, doc.Glossary.Count);
        Assert.Equal(6, doc.Requirements.Count);

        var serialized = doc.ToJson();
        Assert.Contains("\"conceptType\": \"LifecycleState\"", serialized);
        Assert.Contains("\"earsPattern\": \"EventDriven\"", serialized);
        Assert.Contains("\"earsPattern\": \"UnwantedBehavior\"", serialized);
        Assert.Contains("\"kind\": \"TRANSITION\"", serialized);
        Assert.Contains("\"kind\": \"PROHIBIT\"", serialized);
        Assert.Contains("\"timing\": \"IMMEDIATE\"", serialized);

        var roundTripped = KnowledgeGraphDocument.FromJson(serialized);
        Assert.Equal(doc.Requirements.Count, roundTripped.Requirements.Count);
    }

    [Fact]
    public void CrudSampleJson_PassesGraphLinterWithZeroErrorsAndZeroWarnings()
    {
        var doc = KnowledgeGraphDocument.FromJson(File.ReadAllText(GetSamplePath()));

        var diagnostics = GraphLinter.Lint(doc);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void CrudSampleJson_RendersExpectedJapaneseEarsSentences()
    {
        var doc = KnowledgeGraphDocument.FromJson(File.ReadAllText(GetSamplePath()));
        var reqById = doc.Requirements.ToDictionary(r => r.Id);

        Assert.Equal(
            "利用者による顧客登録要求が発生したとき、顧客管理システムは、即座に顧客を作成すること。",
            JapaneseRequirementRenderer.Render(reqById["REQ-001-1"], doc.Glossary));

        Assert.Equal(
            "利用者による顧客一覧画面表示要求が発生したとき、顧客管理システムは、顧客一覧を表示すること。",
            JapaneseRequirementRenderer.Render(reqById["REQ-001-2"], doc.Glossary));

        Assert.Equal(
            "画面状態が一覧の状態で、利用者による顧客選択操作が発生したとき、顧客管理システムは、画面状態を一覧から詳細へ遷移すること。",
            JapaneseRequirementRenderer.Render(reqById["REQ-001-3"], doc.Glossary));

        Assert.Equal(
            "もしユーザーロールが管理者以外であり、利用者による顧客削除要求が発生したならば、顧客管理システムは、顧客の削除を禁止すること。",
            JapaneseRequirementRenderer.Render(reqById["REQ-002-1"], doc.Glossary));
    }

    [Fact]
    public void CrudSampleJson_RendersExpectedUsdmMarkdownHierarchy()
    {
        var doc = KnowledgeGraphDocument.FromJson(File.ReadAllText(GetSamplePath()));

        var markdown = UsdmMarkdownRenderer.Render(doc);

        Assert.Contains("# 顧客管理CRUDアプリケーション要件仕様", markdown);
        Assert.Contains("## [RAT-001] 顧客情報の一元管理と業務効率化", markdown);
        Assert.Contains("### [REQ-001] 顧客の登録から一覧確認・詳細閲覧までの一連操作", markdown);
        Assert.Contains("利用者が顧客管理システム上で新規顧客を登録し、登録済みの顧客一覧を表示して、任意の顧客を選択することで詳細画面へ遷移できること。", markdown);
        Assert.Contains("1. **[REQ-001-1] 顧客の新規作成** (`EventDriven`): 利用者による顧客登録要求が発生したとき、顧客管理システムは、即座に顧客を作成すること。", markdown);
        Assert.Contains("2. **[REQ-001-2] 顧客一覧の表示** (`EventDriven`): 利用者による顧客一覧画面表示要求が発生したとき、顧客管理システムは、顧客一覧を表示すること。", markdown);
        Assert.Contains("3. **[REQ-001-3] 顧客詳細画面への遷移** (`EventDriven`): 画面状態が一覧の状態で、利用者による顧客選択操作が発生したとき、顧客管理システムは、画面状態を一覧から詳細へ遷移すること。", markdown);
        Assert.Contains("## [RAT-002] 誤削除防止とアクセス制御の徹底", markdown);
        Assert.Contains("### [REQ-002] 管理者以外の利用者による顧客削除の防止", markdown);
        Assert.Contains("管理者権限（Admin）を持たない利用者が顧客の削除を試みた場合、顧客管理システムはその削除操作を禁止すること。", markdown);
        Assert.Contains("1. **[REQ-002-1] 管理者以外の顧客削除禁止** (`UnwantedBehavior`): もしユーザーロールが管理者以外であり、利用者による顧客削除要求が発生したならば、顧客管理システムは、顧客の削除を禁止すること。", markdown);
        Assert.Contains("*Canonical EARS:* `If UserRole is not Admin and User triggers CustomerDeleteRequested, then the CustomerManagementSystem shall prohibit DELETE on Customer.`", markdown);
    }

    [Fact]
    public void CrudSampleJson_RendersExpectedEnglishEarsSentences()
    {
        var doc = KnowledgeGraphDocument.FromJson(File.ReadAllText(GetSamplePath()));
        var reqById = doc.Requirements.ToDictionary(r => r.Id);

        Assert.Equal(
            "When User triggers CustomerCreateRequested, the CustomerManagementSystem shall immediately create Customer.",
            EnglishRequirementRenderer.Render(reqById["REQ-001-1"], doc.Glossary));

        Assert.Equal(
            "When User triggers CustomerListViewRequested, the CustomerManagementSystem shall display CustomerList.",
            EnglishRequirementRenderer.Render(reqById["REQ-001-2"], doc.Glossary));

        Assert.Equal(
            "While ScreenState is List, when User triggers CustomerSelected, the CustomerManagementSystem shall transition ScreenState from List to Detail.",
            EnglishRequirementRenderer.Render(reqById["REQ-001-3"], doc.Glossary));

        Assert.Equal(
            "If UserRole is not Admin and User triggers CustomerDeleteRequested, then the CustomerManagementSystem shall prohibit DELETE on Customer.",
            EnglishRequirementRenderer.Render(reqById["REQ-002-1"], doc.Glossary));
    }

    [Fact]
    public void JapaneseRequirementRenderer_SupportsAllFiveEarsPatternsAndBehaviors()
    {
        var glossary = new List<ApprovedGlossaryTerm>
        {
            new() { Id = "SYS", CanonicalName = "注文システム", ConceptType = ConceptType.System },
            new() { Id = "OBJ", CanonicalName = "注文", ConceptType = ConceptType.DomainObject },
            new()
            {
                Id = "SV-MAINT",
                CanonicalName = "メンテナンスモード",
                ConceptType = ConceptType.StateVariable,
                LogicalType = new LogicalTypeDefinition { Kind = LogicalTypeKind.Boolean }
            },
            new()
            {
                Id = "SV-AUDIT",
                CanonicalName = "監査ログ機能",
                ConceptType = ConceptType.StateVariable,
                LogicalType = new LogicalTypeDefinition { Kind = LogicalTypeKind.Boolean }
            },
            new()
            {
                Id = "SV-STATUS",
                CanonicalName = "処理状態",
                ConceptType = ConceptType.StateVariable,
                LogicalType = new LogicalTypeDefinition { Kind = LogicalTypeKind.Enum, AllowedValues = ["待機", "完了"] }
            }
        };

        var ubiquitous = new CanonicalRequirementNode
        {
            Id = "R1",
            NodeKind = RequirementNodeKind.Atomic,
            EarsPattern = EarsEntryPattern.Ubiquitous,
            SubjectSystemRef = "SYS",
            Behavior = new BehaviorNode { Kind = BehaviorKind.DISPLAY, TargetTermRef = "OBJ" }
        };
        Assert.Equal("注文システムは、注文を表示すること。", JapaneseRequirementRenderer.Render(ubiquitous, glossary));

        var stateDriven = new CanonicalRequirementNode
        {
            Id = "R2",
            NodeKind = RequirementNodeKind.Atomic,
            EarsPattern = EarsEntryPattern.StateDriven,
            SubjectSystemRef = "SYS",
            Preconditions =
            [
                new PreconditionClause { StateTermRef = "SV-MAINT", Operator = ComparisonOperator.EQ, ExpectedValues = ["true"] }
            ],
            Behavior = new BehaviorNode { Kind = BehaviorKind.SET, TargetTermRef = "SV-STATUS", SetValue = "待機", Timing = TimingKind.EVENTUAL }
        };
        Assert.Equal("メンテナンスモードがtrueの間、注文システムは、最終的に処理状態を待機に設定すること。", JapaneseRequirementRenderer.Render(stateDriven, glossary));

        var optionalFeature = new CanonicalRequirementNode
        {
            Id = "R3",
            NodeKind = RequirementNodeKind.Atomic,
            EarsPattern = EarsEntryPattern.OptionalFeature,
            SubjectSystemRef = "SYS",
            Preconditions =
            [
                new PreconditionClause { StateTermRef = "SV-AUDIT", Operator = ComparisonOperator.EQ, ExpectedValues = ["true"] }
            ],
            Behavior = new BehaviorNode { Kind = BehaviorKind.DELETE, TargetTermRef = "OBJ" }
        };
        Assert.Equal("監査ログ機能がtrueの場合、注文システムは、注文を削除すること。", JapaneseRequirementRenderer.Render(optionalFeature, glossary));
    }

    [Fact]
    public void GlossaryMetadata_BooleanAndEnumValueLabels_RenderAndRoundTrip()
    {
        var doc = new KnowledgeGraphDocument
        {
            DocumentId = "DOC-ADAS-001",
            Title = "車速制御仕様",
            Rationales = [new RationaleNode { Id = "RAT-1", Title = "安全走行", Description = "高速走行時の安全確保" }],
            Glossary =
            [
                new ApprovedGlossaryTerm
                {
                    Id = "SYS-ADAS",
                    CanonicalName = "AdasController",
                    DisplayName = "ADASコントローラ",
                    ConceptType = ConceptType.System
                },
                new ApprovedGlossaryTerm
                {
                    Id = "SV-SPEED",
                    CanonicalName = "VehicleSpeed",
                    DisplayName = "車速",
                    ConceptType = ConceptType.StateVariable,
                    Unit = "km/h",
                    AbstractionNote = "車速 >= 30 km/h を HighSpeed として抽象化",
                    DeprecatedLabels = ["スピード"],
                    LogicalType = new LogicalTypeDefinition
                    {
                        Kind = LogicalTypeKind.Enum,
                        AllowedValues = ["LowSpeed", "HighSpeed"],
                        ValueLabels = new Dictionary<string, string>
                        {
                            ["LowSpeed"] = "低速",
                            ["HighSpeed"] = "高速"
                        }
                    }
                },
                new ApprovedGlossaryTerm
                {
                    Id = "SV-LOCK",
                    CanonicalName = "DoorLocked",
                    DisplayName = "ドアロック状態",
                    ConceptType = ConceptType.StateVariable,
                    LogicalType = new LogicalTypeDefinition
                    {
                        Kind = LogicalTypeKind.Boolean,
                        ValueLabels = new Dictionary<string, string>
                        {
                            ["true"] = "施錠",
                            ["false"] = "解錠"
                        }
                    }
                }
            ],
            Requirements =
            [
                new CanonicalRequirementNode
                {
                    Id = "REQ-1",
                    Title = "高速走行時の自動ドアロック",
                    NodeKind = RequirementNodeKind.Atomic,
                    RationaleRefs = ["RAT-1"],
                    EarsPattern = EarsEntryPattern.StateDriven,
                    SubjectSystemRef = "SYS-ADAS",
                    Preconditions =
                    [
                        new PreconditionClause { StateTermRef = "SV-SPEED", Operator = ComparisonOperator.EQ, ExpectedValues = ["HighSpeed"] }
                    ],
                    Behavior = new BehaviorNode
                    {
                        Kind = BehaviorKind.SET,
                        TargetTermRef = "SV-LOCK",
                        SetValue = "true",
                        Timing = TimingKind.IMMEDIATE
                    }
                }
            ]
        };

        var json = doc.ToJson();
        var restored = KnowledgeGraphDocument.FromJson(json);

        Assert.Empty(GraphLinter.Lint(restored));

        var speedTerm = restored.Glossary.Single(t => t.Id == "SV-SPEED");
        Assert.Equal("km/h", speedTerm.Unit);
        Assert.Equal("車速 >= 30 km/h を HighSpeed として抽象化", speedTerm.AbstractionNote);
        Assert.Equal(["スピード"], speedTerm.DeprecatedLabels);

        var sentence = JapaneseRequirementRenderer.Render(restored.Requirements[0], restored.Glossary);
        Assert.Equal("車速が高速の間、ADASコントローラは、即座にドアロック状態を施錠に設定すること。", sentence);
    }
}
