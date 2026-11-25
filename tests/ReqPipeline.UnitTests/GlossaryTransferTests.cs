using System.Text.Json;
using ReqPipeline.Core.Domain;

namespace ReqPipeline.UnitTests;

public class GlossaryTransferTests
{
    [Fact]
    public void ExportAndParse_GlossarySetDocument_RoundTripsAllFields()
    {
        var original = new List<ApprovedGlossaryTerm>
        {
            new()
            {
                Id = "SV-SPEED",
                CanonicalName = "VehicleSpeed",
                DisplayName = "車速",
                ConceptType = ConceptType.StateVariable,
                Unit = "km/h",
                AbstractionNote = "車速 >= 30 km/h を HighSpeed として抽象化",
                DeprecatedLabels = ["スピード"],
                LogicalType = new LogicalTypeDefinition
                {
                    Kind = LogicalTypeKind.Enum,
                    AllowedValues = ["LowSpeed", "HighSpeed"],
                    ValueLabels = new Dictionary<string, string>
                    {
                        ["LowSpeed"] = "低速",
                        ["HighSpeed"] = "高速"
                    }
                }
            }
        };

        var json = GlossaryTransfer.ExportJson(original);
        Assert.Contains("\"schemaVersion\": \"2.0\"", json);
        Assert.Contains("\"glossary\": [", json);

        var parsed = GlossaryTransfer.ParseTermsFromJson(json);
        Assert.Single(parsed);
        Assert.Equal("VehicleSpeed", parsed[0].CanonicalName);
        Assert.Equal("車速", parsed[0].DisplayName);
        Assert.Equal("km/h", parsed[0].Unit);
        Assert.Equal(["スピード"], parsed[0].DeprecatedLabels);
        Assert.Equal("高速", parsed[0].LogicalType.GetValueLabel("HighSpeed"));
    }

    [Fact]
    public void ParseTermsFromJson_AcceptsRootArrayAndFullKnowledgeGraphDocument()
    {
        var rootArrayJson = """
        [
          { "id": "TERM-1", "canonicalName": "Brake", "displayName": "ブレーキ", "conceptType": "DomainObject" }
        ]
        """;
        var fromArray = GlossaryTransfer.ParseTermsFromJson(rootArrayJson);
        Assert.Single(fromArray);
        Assert.Equal("TERM-1", fromArray[0].Id);

        var fullDoc = new KnowledgeGraphDocument
        {
            DocumentId = "DOC-1",
            Title = "Test",
            Glossary =
            [
                new ApprovedGlossaryTerm { Id = "TERM-2", CanonicalName = "Door", DisplayName = "ドア", ConceptType = ConceptType.DomainObject }
            ]
        };
        var fromFullDoc = GlossaryTransfer.ParseTermsFromJson(fullDoc.ToJson());
        Assert.Single(fromFullDoc);
        Assert.Equal("TERM-2", fromFullDoc[0].Id);
    }

    [Fact]
    public void ParseTermsFromJson_ThrowsOnInvalidJsonOrMissingGlossary()
    {
        Assert.Throws<JsonException>(() => GlossaryTransfer.ParseTermsFromJson(""));
        Assert.Throws<JsonException>(() => GlossaryTransfer.ParseTermsFromJson("{ \"foo\": [] }"));
    }

    [Fact]
    public void ApplyImport_SupportsMergeOverwrite_MergeSkipExisting_AndReplaceAll()
    {
        var incoming = new List<ApprovedGlossaryTerm>
        {
            new() { Id = "T1", CanonicalName = "UpdatedName", DisplayName = "更新後", ConceptType = ConceptType.DomainObject },
            new() { Id = "T3", CanonicalName = "NewItem", DisplayName = "新規", ConceptType = ConceptType.Event }
        };

        // 1. MergeOverwrite
        var target1 = CreateInitialGlossary();
        var res1 = GlossaryTransfer.ApplyImport(target1, incoming, GlossaryImportMode.MergeOverwrite);
        Assert.Equal(1, res1.AddedCount);
        Assert.Equal(1, res1.UpdatedCount);
        Assert.Equal(0, res1.SkippedCount);
        Assert.Equal(3, target1.Count);
        Assert.Equal("UpdatedName", target1.Single(t => t.Id == "T1").CanonicalName);

        // 2. MergeSkipExisting
        var target2 = CreateInitialGlossary();
        var res2 = GlossaryTransfer.ApplyImport(target2, incoming, GlossaryImportMode.MergeSkipExisting);
        Assert.Equal(1, res2.AddedCount);
        Assert.Equal(0, res2.UpdatedCount);
        Assert.Equal(1, res2.SkippedCount);
        Assert.Equal(3, target2.Count);
        Assert.Equal("OriginalName", target2.Single(t => t.Id == "T1").CanonicalName);

        // 3. ReplaceAll
        var target3 = CreateInitialGlossary();
        var res3 = GlossaryTransfer.ApplyImport(target3, incoming, GlossaryImportMode.ReplaceAll);
        Assert.Equal(2, res3.AddedCount);
        Assert.Equal(0, res3.UpdatedCount);
        Assert.Equal(0, res3.SkippedCount);
        Assert.Equal(2, target3.Count);
        Assert.DoesNotContain(target3, t => t.Id == "T2");
    }

    private static List<ApprovedGlossaryTerm> CreateInitialGlossary() =>
    [
        new ApprovedGlossaryTerm { Id = "T1", CanonicalName = "OriginalName", DisplayName = "元名称", ConceptType = ConceptType.DomainObject },
        new ApprovedGlossaryTerm { Id = "T2", CanonicalName = "ExistingOnly", DisplayName = "既存のみ", ConceptType = ConceptType.System }
    ];
}
