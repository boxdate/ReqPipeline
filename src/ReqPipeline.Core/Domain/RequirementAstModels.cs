using System.Text.Json.Serialization;

namespace ReqPipeline.Core.Domain;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RequirementNodeKind
{
    Composite,
    Atomic
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EarsEntryPattern
{
    Ubiquitous,
    EventDriven,
    StateDriven,
    UnwantedBehavior,
    OptionalFeature
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ComparisonOperator
{
    EQ,
    NEQ,
    IN
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TimingKind
{
    UNSPECIFIED,
    IMMEDIATE,
    EVENTUAL
}

public sealed class PreconditionClause
{
    public string StateTermRef { get; set; } = string.Empty;
    public ComparisonOperator Operator { get; set; } = ComparisonOperator.EQ;
    public List<string> ExpectedValues { get; set; } = [];
}

public sealed class BehaviorNode
{
    public string BehaviorDefRef { get; set; } = string.Empty;
    public string TargetTermRef { get; set; } = string.Empty;
    public string? FromValue { get; set; }
    public string? ToValue { get; set; }
    public string? ProhibitedBehaviorDefRef { get; set; }
    public string? ProhibitedToValue { get; set; }
    public TimingKind Timing { get; set; } = TimingKind.UNSPECIFIED;
    public string? Notes { get; set; }
}

public sealed class CanonicalRequirementNode
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public string? Namespace { get; set; }
    public RequirementNodeKind NodeKind { get; set; } = RequirementNodeKind.Atomic;
    public string? ParentRequirementId { get; set; }
    public List<string> RationaleRefs { get; set; } = [];
    public EarsEntryPattern EarsPattern { get; set; } = EarsEntryPattern.Ubiquitous;
    public string? SubjectSystemRef { get; set; }
    public string? ActorRef { get; set; }
    public string? TriggerEventRef { get; set; }
    public List<PreconditionClause> Preconditions { get; set; } = [];
    public BehaviorNode? Behavior { get; set; }
}
