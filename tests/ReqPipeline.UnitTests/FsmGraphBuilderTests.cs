using ReqPipeline.Core.Analysis;
using ReqPipeline.Core.Domain;
using ReqPipeline.Core.Rendering;

namespace ReqPipeline.UnitTests;

public class FsmGraphBuilderTests
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
    public void Build_FromCrudSample_IdentifiesInitialReachableUnreachableAndSinkStates()
    {
        var doc = KnowledgeGraphDocument.FromJson(File.ReadAllText(GetSamplePath()));

        var models = FsmGraphBuilder.Build(doc);

        var fsm = Assert.Single(models);
        Assert.Equal("TERM-LC-001", fsm.StateTermId);
        Assert.Equal("ScreenState", fsm.CanonicalName);
        Assert.Equal("画面状態", fsm.DisplayName);
        Assert.Equal("List", fsm.InitialState);

        var byValue = fsm.States.ToDictionary(s => s.Value);

        var list = byValue["List"];
        Assert.True(list.IsInitial);
        Assert.True(list.IsReachable);
        Assert.False(list.IsSink);
        Assert.Equal(0, list.IncomingCount);
        Assert.Equal(1, list.OutgoingCount);
        Assert.Equal("一覧", list.Label);

        var detail = byValue["Detail"];
        Assert.False(detail.IsInitial);
        Assert.True(detail.IsReachable);
        Assert.True(detail.IsSink);
        Assert.Equal(1, detail.IncomingCount);
        Assert.Equal(0, detail.OutgoingCount);
        Assert.Equal("詳細", detail.Label);

        var create = byValue["Create"];
        Assert.False(create.IsInitial);
        Assert.False(create.IsReachable);
        Assert.True(create.IsSink);
        Assert.Equal(0, create.IncomingCount);
        Assert.Equal(0, create.OutgoingCount);
        Assert.Equal("作成", create.Label);

        var transition = Assert.Single(fsm.Transitions);
        Assert.Equal("REQ-001-3", transition.RequirementId);
        Assert.Equal("List", transition.FromValue);
        Assert.Equal("Detail", transition.ToValue);
        Assert.Equal("TERM-EVT-003", transition.TriggerEventRef);
        Assert.Equal("CustomerSelected", transition.TriggerCanonicalName);
        Assert.Equal("顧客選択操作", transition.TriggerDisplayName);
        Assert.Equal("利用者", transition.ActorDisplayName);
    }

    [Fact]
    public void Build_AddingReturnTransitionFromDetailToList_ClearsDetailSinkFlag()
    {
        var doc = KnowledgeGraphDocument.FromJson(File.ReadAllText(GetSamplePath()));
        doc.Requirements.Add(new CanonicalRequirementNode
        {
            Id = "REQ-001-4",
            Title = "一覧画面へ戻る",
            NodeKind = RequirementNodeKind.Atomic,
            ParentRequirementId = "REQ-001",
            RationaleRefs = ["RAT-001"],
            EarsPattern = EarsEntryPattern.EventDriven,
            SubjectSystemRef = "TERM-SYS-001",
            ActorRef = "TERM-ACT-001",
            TriggerEventRef = "TERM-EVT-002",
            Behavior = new BehaviorNode
            {
                Kind = BehaviorKind.TRANSITION,
                TargetTermRef = "TERM-LC-001",
                FromValue = "Detail",
                ToValue = "List"
            }
        });

        var fsm = Assert.Single(FsmGraphBuilder.Build(doc));
        var detail = fsm.States.Single(s => s.Value == "Detail");
        var list = fsm.States.Single(s => s.Value == "List");

        Assert.False(detail.IsSink);
        Assert.Equal(1, detail.OutgoingCount);
        Assert.Equal(1, list.IncomingCount);
        Assert.Equal(2, fsm.Transitions.Count);
    }

    [Fact]
    public void FsmMermaidRenderer_RendersJapaneseAndEnglishStateDiagrams()
    {
        var doc = KnowledgeGraphDocument.FromJson(File.ReadAllText(GetSamplePath()));
        var fsm = Assert.Single(FsmGraphBuilder.Build(doc));

        var ja = FsmMermaidRenderer.Render(fsm, useJapaneseLabels: true);
        Assert.StartsWith("stateDiagram-v2", ja);
        Assert.Contains("[*] --> List", ja);
        Assert.Contains("state \"一覧 (List)\" as List", ja);
        Assert.Contains("state \"詳細 (Detail)\" as Detail", ja);
        Assert.Contains("state \"作成 (Create)\" as Create", ja);
        Assert.Contains("List --> Detail : 顧客選択操作 [REQ-001-3]", ja);

        var en = FsmMermaidRenderer.Render(fsm, useJapaneseLabels: false);
        Assert.StartsWith("stateDiagram-v2", en);
        Assert.Contains("[*] --> List", en);
        Assert.Contains("state \"List\" as List", en);
        Assert.Contains("state \"Detail\" as Detail", en);
        Assert.Contains("List --> Detail : CustomerSelected [REQ-001-3]", en);
    }
}
