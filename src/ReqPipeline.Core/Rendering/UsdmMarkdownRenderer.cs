using System.Text;
using ReqPipeline.Core.Domain;

namespace ReqPipeline.Core.Rendering;

public sealed class UsdmMarkdownRenderer
{
    public static string Render(KnowledgeGraphDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var sb = new StringBuilder();
        var title = string.IsNullOrWhiteSpace(document.Title) ? "要件仕様書 (USDM View)" : document.Title;
        sb.AppendLine($"# {title}");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(document.Description))
        {
            sb.AppendLine(document.Description);
            sb.AppendLine();
        }

        var glossaryById = new Dictionary<string, ApprovedGlossaryTerm>(StringComparer.Ordinal);
        foreach (var term in document.Glossary)
        {
            if (!string.IsNullOrWhiteSpace(term.Id))
            {
                glossaryById.TryAdd(term.Id, term);
            }
        }

        var childrenByParent = document.Requirements
            .Where(r => !string.IsNullOrWhiteSpace(r.ParentRequirementId))
            .GroupBy(r => r.ParentRequirementId!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var topLevelRequirements = document.Requirements
            .Where(r => string.IsNullOrWhiteSpace(r.ParentRequirementId))
            .ToList();

        var renderedTopLevelIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var rationale in document.Rationales)
        {
            sb.AppendLine($"## [{rationale.Id}] {rationale.Title}");
            sb.AppendLine();
            sb.AppendLine($"> **理由 (Rationale):** {rationale.Description}");
            if (!string.IsNullOrWhiteSpace(rationale.Stakeholder))
            {
                sb.AppendLine($"> **ステークホルダー:** {rationale.Stakeholder}");
            }
            sb.AppendLine();

            var linkedTopLevels = topLevelRequirements
                .Where(r => r.RationaleRefs.Contains(rationale.Id, StringComparer.Ordinal))
                .ToList();

            foreach (var req in linkedTopLevels)
            {
                renderedTopLevelIds.Add(req.Id);
                RenderRequirementHierarchy(sb, req, childrenByParent, glossaryById, document.EscapeRecords);
            }
        }

        var orphanTopLevels = topLevelRequirements
            .Where(r => !renderedTopLevelIds.Contains(r.Id))
            .ToList();

        if (orphanTopLevels.Count > 0)
        {
            sb.AppendLine("## 未紐付け要求 (Unlinked Requirements)");
            sb.AppendLine();
            foreach (var req in orphanTopLevels)
            {
                RenderRequirementHierarchy(sb, req, childrenByParent, glossaryById, document.EscapeRecords);
            }
        }

        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    private static void RenderRequirementHierarchy(
        StringBuilder sb,
        CanonicalRequirementNode req,
        Dictionary<string, List<CanonicalRequirementNode>> childrenByParent,
        Dictionary<string, ApprovedGlossaryTerm> glossaryById,
        List<FreeTextEscapeRecord> escapeRecords)
    {
        if (req.NodeKind == RequirementNodeKind.Composite)
        {
            sb.AppendLine($"### [{req.Id}] {req.Title}");
            sb.AppendLine();

            if (!string.IsNullOrWhiteSpace(req.Summary))
            {
                sb.AppendLine(req.Summary);
                sb.AppendLine();
            }

            if (childrenByParent.TryGetValue(req.Id, out var children))
            {
                var step = 1;
                foreach (var child in children)
                {
                    if (child.NodeKind == RequirementNodeKind.Atomic)
                    {
                        var sentence = JapaneseRequirementRenderer.Render(child, glossaryById, escapeRecords);
                        sb.AppendLine($"{step}. **[{child.Id}] {child.Title}** (`{child.EarsPattern}`): {sentence}");
                        step++;
                    }
                    else
                    {
                        RenderRequirementHierarchy(sb, child, childrenByParent, glossaryById, escapeRecords);
                    }
                }
                sb.AppendLine();
            }
        }
        else
        {
            var sentence = JapaneseRequirementRenderer.Render(req, glossaryById, escapeRecords);
            sb.AppendLine($"- **[{req.Id}] {req.Title}** (`{req.EarsPattern}`): {sentence}");
            sb.AppendLine();
        }
    }
}
