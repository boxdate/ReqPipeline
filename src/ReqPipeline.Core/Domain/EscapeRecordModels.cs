using System.Text.Json.Serialization;

namespace ReqPipeline.Core.Domain;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EscapeReason
{
    UnsupportedEarsPattern,
    UnsupportedBehaviorKind,
    ComplexTemporalConstraint,
    MissingDomainVocabulary,
    QuantitativeCalculation,
    ExternalSystemConstraint
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EscapeRecordStatus
{
    PENDING,
    RESOLVED,
    REJECTED
}

public sealed class FreeTextEscapeRecord
{
    public string Id { get; set; } = string.Empty;
    public string RequirementId { get; set; } = string.Empty;
    public EscapeReason Reason { get; set; }
    public string FreeText { get; set; } = string.Empty;
    public EscapeRecordStatus Status { get; set; } = EscapeRecordStatus.PENDING;
    public string? ResolutionNote { get; set; }
}
