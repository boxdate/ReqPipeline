using System.Text.Json;
using System.Text.Json.Serialization;

namespace ReqPipeline.Core.Domain;

public sealed class KnowledgeGraphDocument
{
    public string SchemaVersion { get; set; } = "2.0";
    public string DocumentId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }

    public List<RationaleNode> Rationales { get; set; } = [];
    public List<ApprovedGlossaryTerm> Glossary { get; set; } = [];
    public List<BehaviorDefinition> BehaviorCatalog { get; set; } = [];
    public List<CanonicalRequirementNode> Requirements { get; set; } = [];
    public List<FreeTextEscapeRecord> EscapeRecords { get; set; } = [];
    public List<UserJourney> Journeys { get; set; } = [];

    public static JsonSerializerOptions CreateJsonSerializerOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public string ToJson() => JsonSerializer.Serialize(this, CreateJsonSerializerOptions());

    public static KnowledgeGraphDocument FromJson(string json) =>
        JsonSerializer.Deserialize<KnowledgeGraphDocument>(json, CreateJsonSerializerOptions())
        ?? throw new JsonException("Failed to deserialize KnowledgeGraphDocument.");
}
