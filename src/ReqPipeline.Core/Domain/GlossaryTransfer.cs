using System.Text.Json;
using System.Text.Json.Serialization;

namespace ReqPipeline.Core.Domain;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GlossaryImportMode
{
    MergeOverwrite,
    MergeSkipExisting,
    ReplaceAll
}

public sealed class GlossarySetDocument
{
    public string SchemaVersion { get; set; } = "2.0";
    public List<ApprovedGlossaryTerm> Glossary { get; set; } = [];
}

public sealed record GlossaryImportResult(
    int AddedCount,
    int UpdatedCount,
    int SkippedCount,
    int TotalImportedCount);

public static class GlossaryTransfer
{
    public static string ExportJson(IReadOnlyList<ApprovedGlossaryTerm> glossary, string schemaVersion = "2.0")
    {
        ArgumentNullException.ThrowIfNull(glossary);

        var wrapper = new GlossarySetDocument
        {
            SchemaVersion = schemaVersion,
            Glossary = [.. glossary]
        };

        return JsonSerializer.Serialize(wrapper, KnowledgeGraphDocument.CreateJsonSerializerOptions());
    }

    public static List<ApprovedGlossaryTerm> ParseTermsFromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new JsonException("Glossary JSON is empty.");
        }

        var options = KnowledgeGraphDocument.CreateJsonSerializerOptions();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.ValueKind == JsonValueKind.Array)
        {
            return JsonSerializer.Deserialize<List<ApprovedGlossaryTerm>>(root.GetRawText(), options)
                ?? throw new JsonException("Failed to deserialize glossary array.");
        }

        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in root.EnumerateObject())
            {
                if (string.Equals(prop.Name, "glossary", StringComparison.OrdinalIgnoreCase)
                    && prop.Value.ValueKind == JsonValueKind.Array)
                {
                    return JsonSerializer.Deserialize<List<ApprovedGlossaryTerm>>(prop.Value.GetRawText(), options)
                        ?? throw new JsonException("Failed to deserialize 'glossary' property.");
                }
            }

            throw new JsonException("JSON object does not contain a 'glossary' array property.");
        }

        throw new JsonException("Expected a JSON object with 'glossary' array or a root JSON array of glossary terms.");
    }

    public static GlossaryImportResult ApplyImport(
        List<ApprovedGlossaryTerm> targetGlossary,
        IReadOnlyList<ApprovedGlossaryTerm> importedTerms,
        GlossaryImportMode mode)
    {
        ArgumentNullException.ThrowIfNull(targetGlossary);
        ArgumentNullException.ThrowIfNull(importedTerms);

        if (mode == GlossaryImportMode.ReplaceAll)
        {
            targetGlossary.Clear();
            targetGlossary.AddRange(importedTerms);
            return new GlossaryImportResult(
                AddedCount: importedTerms.Count,
                UpdatedCount: 0,
                SkippedCount: 0,
                TotalImportedCount: importedTerms.Count);
        }

        var added = 0;
        var updated = 0;
        var skipped = 0;

        foreach (var incoming in importedTerms)
        {
            var existingIdx = targetGlossary.FindIndex(t => string.Equals(t.Id, incoming.Id, StringComparison.Ordinal));
            if (existingIdx < 0)
            {
                targetGlossary.Add(incoming);
                added++;
            }
            else if (mode == GlossaryImportMode.MergeOverwrite)
            {
                targetGlossary[existingIdx] = incoming;
                updated++;
            }
            else
            {
                skipped++;
            }
        }

        return new GlossaryImportResult(added, updated, skipped, importedTerms.Count);
    }
}
