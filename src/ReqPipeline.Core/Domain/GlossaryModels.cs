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
    public Dictionary<string, string> ValueLabels { get; set; } = new(StringComparer.Ordinal);

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

    public string GetValueLabel(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (ValueLabels.TryGetValue(value, out var label) && !string.IsNullOrWhiteSpace(label))
        {
            return label;
        }

        return value;
    }
}

public sealed class ApprovedGlossaryTerm
{
    public string Id { get; set; } = string.Empty;
    public string CanonicalName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public ConceptType ConceptType { get; set; }
    public string? Namespace { get; set; }
    public string Description { get; set; } = string.Empty;
    public List<string> Aliases { get; set; } = [];
    public string? Unit { get; set; }
    public string? AbstractionNote { get; set; }
    public List<string> DeprecatedLabels { get; set; } = [];
    public LogicalTypeDefinition LogicalType { get; set; } = new();

    public string GetDisplayName() =>
        !string.IsNullOrWhiteSpace(DisplayName) ? DisplayName : CanonicalName;
}
