using System.Text.Json.Serialization;

namespace ReqPipeline.Core.Domain;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SemanticEffect
{
    MutateState,
    ManageEntity,
    EmitAction,
    Prohibit
}

public sealed class BehaviorDefinition
{
    public string BehaviorDefId { get; set; } = string.Empty;
    public string CanonicalLabel { get; set; } = string.Empty;
    public string JapaneseLabel { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public SemanticEffect Effect { get; set; }
    public List<ConceptType> AllowedTargetTypes { get; set; } = [];
    public bool RequiresFromValue { get; set; }
    public bool RequiresToValue { get; set; }
    public string JapaneseTemplate { get; set; } = string.Empty;
    public string EnglishTemplate { get; set; } = string.Empty;
}
