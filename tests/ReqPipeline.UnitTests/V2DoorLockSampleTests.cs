using ReqPipeline.Core.Analysis;
using ReqPipeline.Core.Domain;
using ReqPipeline.Core.Rendering;
using ReqPipeline.Core.Validation;

namespace ReqPipeline.UnitTests;

public class V2DoorLockSampleTests
{
    private static string GetSamplePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "samples", "doorlock-sample.v2.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }

        throw new FileNotFoundException("Could not locate samples/doorlock-sample.v2.json from test output directory.");
    }

    [Fact]
    public void DoorLockSampleJson_DeserializesWithFiveControlVerbsAndZeroCrudVerbs()
    {
        var doc = KnowledgeGraphDocument.FromJson(File.ReadAllText(GetSamplePath()));

        Assert.Equal("2.0", doc.SchemaVersion);
        Assert.Equal("DOC-DOORLOCK-001", doc.DocumentId);
        Assert.Equal(3, doc.Rationales.Count);
        Assert.Equal(11, doc.Glossary.Count);
        Assert.Equal(5, doc.BehaviorCatalog.Count);
        Assert.Equal(9, doc.Requirements.Count);
        Assert.Equal(2, doc.Journeys.Count);

        var verbIds = doc.BehaviorCatalog.Select(b => b.BehaviorDefId).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("BEH-LOCK", verbIds);
        Assert.Contains("BEH-UNLOCK", verbIds);
        Assert.Contains("BEH-CHIME", verbIds);
        Assert.Contains("BEH-RECORD-DTC", verbIds);
        Assert.Contains("BEH-INTERLOCK", verbIds);

        Assert.DoesNotContain("BEH-CREATE", verbIds);
        Assert.DoesNotContain("BEH-DELETE", verbIds);
        Assert.DoesNotContain("BEH-SET", verbIds);
        Assert.DoesNotContain("BEH-TRANSITION", verbIds);
        Assert.DoesNotContain("BEH-DISPLAY", verbIds);
    }

    [Fact]
    public void DoorLockSampleJson_PassesGraphLinterWithZeroDiagnostics()
    {
        var doc = KnowledgeGraphDocument.FromJson(File.ReadAllText(GetSamplePath()));

        var diagnostics = GraphLinter.Lint(doc);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void DoorLockSampleJson_RemovingDrivingPreconditionFromUnlock_TriggersLint008()
    {
        var doc = KnowledgeGraphDocument.FromJson(File.ReadAllText(GetSamplePath()));
        var normalUnlock = doc.Requirements.Single(r => r.Id == "REQ-001-2");
        normalUnlock.Preconditions.Clear();

        var diagnostics = GraphLinter.Lint(doc);
        var lint008 = diagnostics.Where(d => d.RuleId == GraphLinter.RuleRequirementConflict).ToList();

        Assert.Equal(2, lint008.Count);
        Assert.All(lint008, d => Assert.Equal(LintSeverity.Error, d.Severity));
        Assert.Contains(lint008, d => d.TargetId == "REQ-001-2" && d.Message.Contains("REQ-002-2"));
        Assert.Contains(lint008, d => d.TargetId == "REQ-002-2" && d.Message.Contains("REQ-001-2"));
    }

    [Fact]
    public void DoorLockSampleJson_RemovingDoorClosedPreconditionFromLock_TriggersLint008AgainstStateDrivenInterlock()
    {
        var doc = KnowledgeGraphDocument.FromJson(File.ReadAllText(GetSamplePath()));
        var normalLock = doc.Requirements.Single(r => r.Id == "REQ-001-1");
        normalLock.Preconditions.Clear();

        var diagnostics = GraphLinter.Lint(doc);
        var lint008 = diagnostics.Where(d => d.RuleId == GraphLinter.RuleRequirementConflict).ToList();

        Assert.Equal(2, lint008.Count);
        Assert.Contains(lint008, d => d.TargetId == "REQ-001-1" && d.Message.Contains("REQ-002-1"));
        Assert.Contains(lint008, d => d.TargetId == "REQ-002-1" && d.Message.Contains("REQ-001-1"));
    }

    [Fact]
    public void DoorLockSampleJson_BuildsCyclicTwoStateFsmForDoorLockState()
    {
        var doc = KnowledgeGraphDocument.FromJson(File.ReadAllText(GetSamplePath()));

        var fsm = Assert.Single(FsmGraphBuilder.Build(doc));
        Assert.Equal("TERM-LC-001", fsm.StateTermId);
        Assert.Equal("DoorLockState", fsm.CanonicalName);
        Assert.Equal("Unlocked", fsm.InitialState);
        Assert.Equal(2, fsm.States.Count);
        Assert.Equal(2, fsm.Transitions.Count);

        Assert.All(fsm.States, s =>
        {
            Assert.True(s.IsReachable);
            Assert.False(s.IsSink);
            Assert.Equal(1, s.IncomingCount);
            Assert.Equal(1, s.OutgoingCount);
        });

        var mermaid = FsmMermaidRenderer.Render(fsm, useJapaneseLabels: true);
        Assert.Contains("Unlocked --> Locked : 施錠スイッチ押下 [REQ-001-1]", mermaid);
        Assert.Contains("Locked --> Unlocked : 解錠スイッチ押下 [REQ-001-2]", mermaid);
    }

    [Fact]
    public void DoorLockSampleJson_PresetJourneysBehaveAsExpected()
    {
        var doc = KnowledgeGraphDocument.FromJson(File.ReadAllText(GetSamplePath()));

        // JRN-001: 正常施錠＋アンサーバック鳴動
        var jrn1 = doc.Journeys.Single(j => j.Id == "JRN-001");
        var res1 = JourneySimulator.Simulate(doc, jrn1);
        Assert.True(res1.IsAllPassed);
        Assert.Equal(2, res1.PassedStepCount);
        Assert.Equal(0, res1.BlockedStepCount);
        Assert.Equal(0, res1.FailedStepCount);
        Assert.Equal("Locked", res1.FinalStates["TERM-LC-001"]);

        // JRN-002: 走行中解錠インターロック -> Blocked & Locked maintained
        var jrn2 = doc.Journeys.Single(j => j.Id == "JRN-002");
        var res2 = JourneySimulator.Simulate(doc, jrn2);
        Assert.True(res2.IsAllPassed);
        Assert.Equal(0, res2.PassedStepCount);
        Assert.Equal(1, res2.BlockedStepCount);
        Assert.Equal(0, res2.FailedStepCount);
        Assert.Equal(JourneyStepStatus.Blocked, res2.Steps[0].Status);
        Assert.Equal("Locked", res2.FinalStates["TERM-LC-001"]);
    }

    [Fact]
    public void DoorLockSampleJson_RendersExpectedJapaneseAndEnglishEarsSentences()
    {
        var doc = KnowledgeGraphDocument.FromJson(File.ReadAllText(GetSamplePath()));
        var reqById = doc.Requirements.ToDictionary(r => r.Id);

        Assert.Equal(
            "ドア開閉状態が全閉の状態で、運転者による施錠スイッチ押下が発生したとき、ドアロック制御ECUは、即座にドアロック状態を解錠から施錠へ施錠すること。",
            JapaneseRequirementRenderer.Render(reqById["REQ-001-1"], doc.Glossary, doc.EscapeRecords, doc.BehaviorCatalog));

        Assert.Equal(
            "もし走行状態が走行中であり、運転者による解錠スイッチ押下が発生したならば、ドアロック制御ECUは、即座にドアロック状態の解錠をインターロックすること。",
            JapaneseRequirementRenderer.Render(reqById["REQ-002-2"], doc.Glossary, doc.EscapeRecords, doc.BehaviorCatalog));

        Assert.Equal(
            "If DrivingState is Driving and Driver triggers UnlockSwitchPressed, then the DoorLockECU shall immediately interlock unlock on DoorLockState.",
            EnglishRequirementRenderer.Render(reqById["REQ-002-2"], doc.Glossary, doc.EscapeRecords, doc.BehaviorCatalog));
    }
}
