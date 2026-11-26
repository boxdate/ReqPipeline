using System.Text.Json.Serialization;
using ReqPipeline.Core.Domain;

namespace ReqPipeline.Core.Testing;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TestLevel
{
    Unit,
    Component,
    E2E
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TestTechnique
{
    ConditionNormal,
    ConditionNegation,
    SafetyInterlock,
    ValidTransition,
    ConditionalTransition,
    ExplicitProhibit,
    InvalidTransition,
    JourneyTrace
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ExpectedTestOutcome
{
    StateChanged,
    ActionEmitted,
    Blocked,
    NoOp,
    AlternativeFired
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MatrixCellKind
{
    ValidTransition,
    ConditionalTransition,
    ExplicitProhibit,
    UndefinedNoOp
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TestViewpointKind
{
    RequirementTree,
    FsmMatrix,
    Journey
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TestExportLanguage
{
    Japanese,
    English
}

public sealed class TestConditionEntry
{
    public string TermId { get; init; } = string.Empty;
    public string LabelJa { get; init; } = string.Empty;
    public string LabelEn { get; init; } = string.Empty;
    public string RawValue { get; init; } = string.Empty;
    public string ValueLabelJa { get; init; } = string.Empty;
    public string ValueLabelEn { get; init; } = string.Empty;
}

public sealed class TestActionEntry
{
    public int StepNumber { get; init; } = 1;
    public string? ActorTermId { get; init; }
    public string? ActorLabelJa { get; init; }
    public string? ActorLabelEn { get; init; }
    public string? EventTermId { get; init; }
    public string? EventLabelJa { get; init; }
    public string? EventLabelEn { get; init; }
    public string ActionTextJa { get; init; } = string.Empty;
    public string ActionTextEn { get; init; } = string.Empty;
    public string? RequirementRef { get; init; }
}

public sealed class DerivedTestCase
{
    public string TestCaseId { get; init; } = string.Empty;
    public TestLevel TestLevel { get; init; }
    public TestTechnique Technique { get; init; }
    public string? RationaleId { get; init; }
    public string? CompositeId { get; init; }
    public string SourceRef { get; init; } = string.Empty;
    public string ViewpointTitle { get; init; } = string.Empty;
    public string ViewpointTitleEn { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string TitleEn { get; init; } = string.Empty;
    public List<TestConditionEntry> GivenConditions { get; init; } = [];
    public List<TestActionEntry> WhenSteps { get; init; } = [];
    public string ThenTextJa { get; init; } = string.Empty;
    public string ThenTextEn { get; init; } = string.Empty;
    public ExpectedTestOutcome ExpectedOutcome { get; init; }
    public string? ReviewNoteJa { get; init; }
    public string? ReviewNoteEn { get; init; }
    public List<string> Tags { get; init; } = [];
}

public sealed class MatrixEventColumn
{
    public string EventTermId { get; init; } = string.Empty;
    public string CanonicalName { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
}

public sealed class MatrixCell
{
    public string FromState { get; init; } = string.Empty;
    public string FromStateLabel { get; init; } = string.Empty;
    public string EventTermId { get; init; } = string.Empty;
    public string EventCanonicalName { get; init; } = string.Empty;
    public string EventDisplayName { get; init; } = string.Empty;
    public MatrixCellKind Classification { get; init; }
    public bool HasProhibit { get; init; }
    public List<string> TargetStates { get; init; } = [];
    public List<string> TargetStateLabels { get; init; } = [];
    public List<string> RelatedRequirementIds { get; init; } = [];
    public List<DerivedTestCase> TestCases { get; init; } = [];
}

public sealed class StateTransitionMatrix
{
    public string LifecycleTermId { get; init; } = string.Empty;
    public string LifecycleLabel { get; init; } = string.Empty;
    public string LifecycleCanonicalLabel { get; init; } = string.Empty;
    public List<string> States { get; init; } = [];
    public Dictionary<string, string> StateLabels { get; init; } = new(StringComparer.Ordinal);
    public List<MatrixEventColumn> Events { get; init; } = [];
    public List<MatrixCell> Cells { get; init; } = [];

    public int UndefinedCellCount => Cells.Count(c => c.Classification == MatrixCellKind.UndefinedNoOp);

    public MatrixCell? GetCell(string state, string eventTermId) =>
        Cells.FirstOrDefault(c =>
            string.Equals(c.FromState, state, StringComparison.Ordinal)
            && string.Equals(c.EventTermId, eventTermId, StringComparison.Ordinal));
}

public sealed class TestViewpointContainer
{
    public string Id { get; init; } = string.Empty;
    public TestViewpointKind Kind { get; init; }
    public string? RationaleId { get; init; }
    public string? RationaleTitle { get; init; }
    public string Title { get; init; } = string.Empty;
    public string TitleEn { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string SourceRef { get; init; } = string.Empty;
    public List<DerivedTestCase> TestCases { get; init; } = [];
    public List<FreeTextEscapeRecord> EscapeRecords { get; init; } = [];
    public int UndefinedCellCount { get; init; }
    public bool HasBlockedSteps { get; init; }

    public bool HasEscapeRecords => EscapeRecords.Count > 0;
}

public sealed class TestSpecificationSummary
{
    public int TotalViewpointContainers { get; init; }
    public int TotalTestCases { get; init; }
    public int UnitTestCaseCount { get; init; }
    public int ComponentTestCaseCount { get; init; }
    public int E2ETestCaseCount { get; init; }
    public int TotalNonEscapeAtomicRequirements { get; init; }
    public int CoveredAtomicRequirements { get; init; }
    public double AtomicRequirementCoveragePercent { get; init; }
    public int TotalUndefinedCells { get; init; }
    public int EscapeManualReviewCount { get; init; }
}

public sealed class TestSpecificationDocument
{
    public string DocumentId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public TestSpecificationSummary Summary { get; init; } = new();
    public List<TestViewpointContainer> ViewpointContainers { get; init; } = [];
    public List<StateTransitionMatrix> TransitionMatrices { get; init; } = [];
    public List<DerivedTestCase> AllTestCases { get; init; } = [];
}
