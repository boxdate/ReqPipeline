using System.Text.Json;
using System.Text.Json.Serialization;

namespace ReqPipeline.Core.Domain;

public sealed class KnowledgeGraphDocument
{
    public const string CurrentSchemaVersion = "2.1";
    public static readonly IReadOnlyList<string> SupportedSchemaVersions = ["2.0", "2.1"];

    public string SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string DocumentId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }

    public List<RationaleNode> Rationales { get; set; } = [];
    public List<ApprovedGlossaryTerm> Glossary { get; set; } = [];
    public List<BehaviorDefinition> BehaviorCatalog { get; set; } = [];
    public List<CanonicalRequirementNode> Requirements { get; set; } = [];
    public List<FreeTextEscapeRecord> EscapeRecords { get; set; } = [];
    public List<UserJourney> Journeys { get; set; } = [];
    public List<AchievementCriterion> AchievementCriteria { get; set; } = [];

    public static bool IsSupportedSchemaVersion(string? schemaVersion) =>
        !string.IsNullOrWhiteSpace(schemaVersion)
        && SupportedSchemaVersions.Contains(schemaVersion, StringComparer.Ordinal);

    public static KnowledgeGraphDocument CreateEmpty(
        string title,
        string description = "",
        bool includeDefaultCrudBehaviors = false) => new()
    {
        SchemaVersion = CurrentSchemaVersion,
        DocumentId = $"DOC-NEW-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
        Title = title ?? string.Empty,
        Description = description ?? string.Empty,
        Rationales = [],
        Glossary = [],
        BehaviorCatalog = includeDefaultCrudBehaviors ? CreateDefaultCrudBehaviors() : [],
        Requirements = [],
        EscapeRecords = [],
        Journeys = [],
        AchievementCriteria = []
    };

    public static List<BehaviorDefinition> CreateDefaultCrudBehaviors() =>
    [
        new BehaviorDefinition
        {
            BehaviorDefId = "BEH-CREATE",
            CanonicalLabel = "create",
            JapaneseLabel = "作成",
            Description = "指定したドメインオブジェクトを新規作成する。",
            Effect = SemanticEffect.ManageEntity,
            AllowedTargetTypes = [ConceptType.DomainObject],
            RequiresFromValue = false,
            RequiresToValue = false,
            JapaneseTemplate = "{target}を作成すること",
            EnglishTemplate = "create {target}"
        },
        new BehaviorDefinition
        {
            BehaviorDefId = "BEH-DELETE",
            CanonicalLabel = "delete",
            JapaneseLabel = "削除",
            Description = "指定したドメインオブジェクトを削除する。",
            Effect = SemanticEffect.ManageEntity,
            AllowedTargetTypes = [ConceptType.DomainObject],
            RequiresFromValue = false,
            RequiresToValue = false,
            JapaneseTemplate = "{target}を削除すること",
            EnglishTemplate = "delete {target}"
        },
        new BehaviorDefinition
        {
            BehaviorDefId = "BEH-SET",
            CanonicalLabel = "set",
            JapaneseLabel = "設定",
            Description = "状態変数またはライフサイクル状態の値を設定する。",
            Effect = SemanticEffect.MutateState,
            AllowedTargetTypes = [ConceptType.StateVariable, ConceptType.LifecycleState],
            RequiresFromValue = false,
            RequiresToValue = true,
            JapaneseTemplate = "{target}を{to}に設定すること",
            EnglishTemplate = "set {target} to {to}"
        },
        new BehaviorDefinition
        {
            BehaviorDefId = "BEH-TRANSITION",
            CanonicalLabel = "transition",
            JapaneseLabel = "遷移",
            Description = "ライフサイクル状態を遷移元から遷移先へ遷移する。",
            Effect = SemanticEffect.MutateState,
            AllowedTargetTypes = [ConceptType.LifecycleState],
            RequiresFromValue = true,
            RequiresToValue = true,
            JapaneseTemplate = "{target}を{from}から{to}へ遷移すること",
            EnglishTemplate = "transition {target} from {from} to {to}"
        },
        new BehaviorDefinition
        {
            BehaviorDefId = "BEH-DISPLAY",
            CanonicalLabel = "display",
            JapaneseLabel = "表示",
            Description = "指定したドメインオブジェクトを表示・出力する。",
            Effect = SemanticEffect.EmitAction,
            AllowedTargetTypes = [ConceptType.DomainObject],
            RequiresFromValue = false,
            RequiresToValue = false,
            JapaneseTemplate = "{target}を表示すること",
            EnglishTemplate = "display {target}"
        },
        new BehaviorDefinition
        {
            BehaviorDefId = "BEH-PROHIBIT",
            CanonicalLabel = "prohibit",
            JapaneseLabel = "禁止",
            Description = "対象に対する特定の振る舞いまたは操作全般を禁止する。",
            Effect = SemanticEffect.Prohibit,
            AllowedTargetTypes = [ConceptType.DomainObject, ConceptType.LifecycleState, ConceptType.StateVariable],
            RequiresFromValue = false,
            RequiresToValue = false,
            JapaneseTemplate = "{target}の{prohibitedAction}を禁止すること",
            EnglishTemplate = "prohibit {prohibitedAction} on {target}"
        }
    ];

    public static JsonSerializerOptions CreateJsonSerializerOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public string ToJson() => JsonSerializer.Serialize(this, CreateJsonSerializerOptions());

    public static KnowledgeGraphDocument FromJson(string json)
    {
        var doc = JsonSerializer.Deserialize<KnowledgeGraphDocument>(json, CreateJsonSerializerOptions())
            ?? throw new JsonException("Failed to deserialize KnowledgeGraphDocument.");

        if (!string.IsNullOrWhiteSpace(doc.SchemaVersion) && !IsSupportedSchemaVersion(doc.SchemaVersion))
        {
            throw new JsonException(
                $"Unsupported schemaVersion '{doc.SchemaVersion}'. Supported versions: {string.Join(", ", SupportedSchemaVersions)}.");
        }

        doc.Rationales ??= [];
        doc.Glossary ??= [];
        doc.BehaviorCatalog ??= [];
        doc.Requirements ??= [];
        doc.EscapeRecords ??= [];
        doc.Journeys ??= [];
        doc.AchievementCriteria ??= [];

        foreach (var ac in doc.AchievementCriteria)
        {
            ac.LinkedCompositeIds ??= [];
            ac.LinkedJourneyIds ??= [];
            ac.ViewpointTags ??= [];
        }

        return doc;
    }
}
