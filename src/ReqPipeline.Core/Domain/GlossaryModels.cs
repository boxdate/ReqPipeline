using System.Text.Json.Serialization;

namespace ReqPipeline.Core.Domain;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ConceptType
{
    System,
    Actor,
    DomainObject,
    Attribute,
    Event,
    StateVariable,
    LifecycleState
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LogicalTypeKind
{
    None,
    Boolean,
    Enum
}

public sealed class LogicalTypeDefinition
{
    public LogicalTypeKind Kind { get; set; } = LogicalTypeKind.None;
    public List<string> AllowedValues { get; set; } = [];

    public bool IsValidValue(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        return Kind switch
        {
            LogicalTypeKind.Boolean => value is "true" or "false",
            LogicalTypeKind.Enum => AllowedValues.Contains(value, StringComparer.Ordinal),
            _ => false
        };
    }
}

public sealed class ApprovedGlossaryTerm
{
    public string Id { get; set; } = string.Empty;
    public string CanonicalName { get; set; } = string.Empty;
    public ConceptType ConceptType { get; set; }
    public string? Namespace { get; set; }
    public string Description { get; set; } = string.Empty;
    public List<string> Aliases { get; set; } = [];
    public LogicalTypeDefinition LogicalType { get; set; } = new();
}
