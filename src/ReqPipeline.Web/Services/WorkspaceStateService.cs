using ReqPipeline.Core.Domain;
using ReqPipeline.Core.Rendering;
using ReqPipeline.Core.Validation;

namespace ReqPipeline.Web.Services;

public sealed class WorkspaceStateService
{
    private readonly string? _samplePath;

    public KnowledgeGraphDocument Document { get; private set; } = new();
    public IReadOnlyList<LintDiagnostic> Diagnostics { get; private set; } = [];
    public string? SelectedRequirementId { get; private set; }
    public string? SelectedRationaleId { get; private set; }
    public string? LastErrorMessage { get; private set; }

    public int ErrorCount => Diagnostics.Count(d => d.Severity == LintSeverity.Error);
    public int WarningCount => Diagnostics.Count(d => d.Severity == LintSeverity.Warning);

    public event Action? OnChange;

    public WorkspaceStateService(IWebHostEnvironment env)
    {
        _samplePath = FindSampleFile(env.ContentRootPath) ?? FindSampleFile(AppContext.BaseDirectory);
        LoadSampleOrDefault();
    }

    public void LoadSampleOrDefault()
    {
        LastErrorMessage = null;
        if (_samplePath is not null && File.Exists(_samplePath))
        {
            try
            {
                var json = File.ReadAllText(_samplePath);
                Document = KnowledgeGraphDocument.FromJson(json);
            }
            catch (Exception ex)
            {
                LastErrorMessage = $"サンプルJSONの読み込みに失敗しました: {ex.Message}";
                Document = CreateEmptyDocument();
            }
        }
        else
        {
            Document = CreateEmptyDocument();
        }

        SelectedRequirementId = Document.Requirements
            .FirstOrDefault(r => r.NodeKind == RequirementNodeKind.Atomic)?.Id
            ?? Document.Requirements.FirstOrDefault()?.Id;

        RefreshLintAndNotify();
    }

    public void SelectRequirement(string? requirementId)
    {
        SelectedRequirementId = requirementId;
        SelectedRationaleId = null;
        OnChange?.Invoke();
    }

    public void SelectRationale(string? rationaleId)
    {
        SelectedRationaleId = rationaleId;
        SelectedRequirementId = null;
        OnChange?.Invoke();
    }

    public CanonicalRequirementNode? GetSelectedRequirement() =>
        SelectedRequirementId is null
            ? null
            : Document.Requirements.FirstOrDefault(r => string.Equals(r.Id, SelectedRequirementId, StringComparison.Ordinal));

    public RationaleNode? GetSelectedRationale() =>
        SelectedRationaleId is null
            ? null
            : Document.Rationales.FirstOrDefault(r => string.Equals(r.Id, SelectedRationaleId, StringComparison.Ordinal));

    public RationaleNode AddRationale()
    {
        var nextIndex = 1;
        while (Document.Rationales.Any(r => r.Id == $"RAT-{nextIndex:000}"))
        {
            nextIndex++;
        }

        var rationale = new RationaleNode
        {
            Id = $"RAT-{nextIndex:000}",
            Title = $"新規理由 {nextIndex}",
            Description = string.Empty
        };

        Document.Rationales.Add(rationale);
        SelectedRationaleId = rationale.Id;
        SelectedRequirementId = null;
        RefreshLintAndNotify();
        return rationale;
    }

    public void DeleteRationale(string rationaleId)
    {
        Document.Rationales.RemoveAll(r => string.Equals(r.Id, rationaleId, StringComparison.Ordinal));
        if (string.Equals(SelectedRationaleId, rationaleId, StringComparison.Ordinal))
        {
            SelectedRationaleId = null;
            SelectedRequirementId = Document.Requirements.FirstOrDefault()?.Id;
        }
        RefreshLintAndNotify();
    }

    public void NotifyChanged()
    {
        LastErrorMessage = null;
        RefreshLintAndNotify();
    }

    public bool TryImportJson(string json, out string? error)
    {
        try
        {
            var imported = KnowledgeGraphDocument.FromJson(json);
            Document = imported;
            SelectedRequirementId = Document.Requirements
                .FirstOrDefault(r => r.NodeKind == RequirementNodeKind.Atomic)?.Id
                ?? Document.Requirements.FirstOrDefault()?.Id;
            LastErrorMessage = null;
            error = null;
            RefreshLintAndNotify();
            return true;
        }
        catch (Exception ex)
        {
            error = $"JSONの読み込みに失敗しました: {ex.Message}";
            LastErrorMessage = error;
            OnChange?.Invoke();
            return false;
        }
    }

    public string ExportJson() => Document.ToJson();

    public string RenderUsdmMarkdown() => UsdmMarkdownRenderer.Render(Document);

    public string RenderJapanesePreview(CanonicalRequirementNode node)
    {
        if (node.NodeKind != RequirementNodeKind.Atomic)
        {
            return string.Empty;
        }

        return JapaneseRequirementRenderer.Render(node, Document.Glossary, Document.EscapeRecords);
    }

    public string RenderEnglishPreview(CanonicalRequirementNode node)
    {
        if (node.NodeKind != RequirementNodeKind.Atomic)
        {
            return string.Empty;
        }

        return EnglishRequirementRenderer.Render(node, Document.Glossary, Document.EscapeRecords);
    }

    public IReadOnlyList<LintDiagnostic> GetDiagnosticsForRequirement(string requirementId)
    {
        var relatedEscapeIds = Document.EscapeRecords
            .Where(e => string.Equals(e.RequirementId, requirementId, StringComparison.Ordinal))
            .Select(e => e.Id)
            .ToHashSet(StringComparer.Ordinal);

        return Diagnostics
            .Where(d => string.Equals(d.TargetId, requirementId, StringComparison.Ordinal)
                        || relatedEscapeIds.Contains(d.TargetId))
            .ToList();
    }

    public IReadOnlyList<ApprovedGlossaryTerm> GetGlossaryByTypes(params ConceptType[] types) =>
        Document.Glossary.Where(t => types.Contains(t.ConceptType)).ToList();

    public IReadOnlyList<string> GetAllowedValues(string? termRef)
    {
        if (string.IsNullOrWhiteSpace(termRef))
        {
            return [];
        }

        var term = Document.Glossary.FirstOrDefault(t => string.Equals(t.Id, termRef, StringComparison.Ordinal));
        if (term is null)
        {
            return [];
        }

        return term.LogicalType.Kind switch
        {
            LogicalTypeKind.Boolean => ["true", "false"],
            LogicalTypeKind.Enum => term.LogicalType.AllowedValues,
            _ => []
        };
    }

    public CanonicalRequirementNode AddCompositeRequirement(string? rationaleId = null)
    {
        var nextIndex = 1;
        while (Document.Requirements.Any(r => r.Id == $"REQ-{nextIndex:000}"))
        {
            nextIndex++;
        }

        var defaultSystem = Document.Glossary.FirstOrDefault(g => g.ConceptType == ConceptType.System)?.Id;
        var rationaleRefs = new List<string>();
        if (!string.IsNullOrWhiteSpace(rationaleId))
        {
            rationaleRefs.Add(rationaleId);
        }
        else if (Document.Rationales.Count > 0)
        {
            rationaleRefs.Add(Document.Rationales[0].Id);
        }

        var composite = new CanonicalRequirementNode
        {
            Id = $"REQ-{nextIndex:000}",
            Title = $"新規親要求 {nextIndex}",
            NodeKind = RequirementNodeKind.Composite,
            RationaleRefs = rationaleRefs,
            SubjectSystemRef = defaultSystem,
            EarsPattern = EarsEntryPattern.Ubiquitous
        };

        Document.Requirements.Add(composite);
        SelectedRequirementId = composite.Id;
        SelectedRationaleId = null;
        RefreshLintAndNotify();
        return composite;
    }

    public CanonicalRequirementNode AddAtomicChild(CanonicalRequirementNode parent)
    {
        var existingChildren = Document.Requirements
            .Count(r => string.Equals(r.ParentRequirementId, parent.Id, StringComparison.Ordinal));

        var suffix = existingChildren + 1;
        var newId = $"{parent.Id}-{suffix}";
        while (Document.Requirements.Any(r => r.Id == newId))
        {
            suffix++;
            newId = $"{parent.Id}-{suffix}";
        }

        var defaultSystem = parent.SubjectSystemRef
            ?? Document.Glossary.FirstOrDefault(g => g.ConceptType == ConceptType.System)?.Id;
        var defaultDomainObj = Document.Glossary.FirstOrDefault(g => g.ConceptType == ConceptType.DomainObject)?.Id
            ?? Document.Glossary.FirstOrDefault()?.Id
            ?? string.Empty;

        var child = new CanonicalRequirementNode
        {
            Id = newId,
            Title = $"{parent.Title} - ステップ {suffix}",
            Namespace = parent.Namespace,
            NodeKind = RequirementNodeKind.Atomic,
            ParentRequirementId = parent.Id,
            RationaleRefs = [.. parent.RationaleRefs],
            EarsPattern = parent.EarsPattern,
            SubjectSystemRef = defaultSystem,
            ActorRef = parent.ActorRef,
            TriggerEventRef = parent.TriggerEventRef,
            Preconditions = parent.Preconditions
                .Select(p => new PreconditionClause
                {
                    StateTermRef = p.StateTermRef,
                    Operator = p.Operator,
                    ExpectedValues = [.. p.ExpectedValues]
                })
                .ToList(),
            Behavior = new BehaviorNode
            {
                Kind = BehaviorKind.DISPLAY,
                TargetTermRef = defaultDomainObj,
                Timing = TimingKind.UNSPECIFIED
            }
        };

        Document.Requirements.Add(child);
        SelectedRequirementId = child.Id;
        SelectedRationaleId = null;
        RefreshLintAndNotify();
        return child;
    }

    public void MoveChildRequirement(string parentId, string childId, int delta)
    {
        var childIndices = new List<int>();
        for (var i = 0; i < Document.Requirements.Count; i++)
        {
            if (string.Equals(Document.Requirements[i].ParentRequirementId, parentId, StringComparison.Ordinal))
            {
                childIndices.Add(i);
            }
        }

        var pos = childIndices.FindIndex(idx => string.Equals(Document.Requirements[idx].Id, childId, StringComparison.Ordinal));
        var targetPos = pos + delta;
        if (pos < 0 || targetPos < 0 || targetPos >= childIndices.Count)
        {
            return;
        }

        var i1 = childIndices[pos];
        var i2 = childIndices[targetPos];
        (Document.Requirements[i1], Document.Requirements[i2]) = (Document.Requirements[i2], Document.Requirements[i1]);
        RefreshLintAndNotify();
    }

    public void DeleteRequirement(string requirementId)
    {
        var toRemove = new HashSet<string>(StringComparer.Ordinal) { requirementId };
        foreach (var child in Document.Requirements.Where(r => string.Equals(r.ParentRequirementId, requirementId, StringComparison.Ordinal)))
        {
            toRemove.Add(child.Id);
        }

        Document.Requirements.RemoveAll(r => toRemove.Contains(r.Id));
        Document.EscapeRecords.RemoveAll(e => toRemove.Contains(e.RequirementId));

        if (SelectedRequirementId is not null && toRemove.Contains(SelectedRequirementId))
        {
            SelectedRequirementId = Document.Requirements.FirstOrDefault()?.Id;
        }

        RefreshLintAndNotify();
    }

    public FreeTextEscapeRecord? GetPendingEscape(string requirementId) =>
        Document.EscapeRecords.FirstOrDefault(
            e => string.Equals(e.RequirementId, requirementId, StringComparison.Ordinal)
                 && e.Status == EscapeRecordStatus.PENDING);

    public FreeTextEscapeRecord EnableEscapeHatch(CanonicalRequirementNode node, EscapeReason reason, string freeText)
    {
        node.Behavior = null;

        var existing = GetPendingEscape(node.Id);
        if (existing is not null)
        {
            existing.Reason = reason;
            existing.FreeText = freeText;
            RefreshLintAndNotify();
            return existing;
        }

        var nextIdx = Document.EscapeRecords.Count + 1;
        var escId = $"ESC-{nextIdx:000}";
        while (Document.EscapeRecords.Any(e => e.Id == escId))
        {
            nextIdx++;
            escId = $"ESC-{nextIdx:000}";
        }

        var record = new FreeTextEscapeRecord
        {
            Id = escId,
            RequirementId = node.Id,
            Reason = reason,
            FreeText = freeText,
            Status = EscapeRecordStatus.PENDING
        };

        Document.EscapeRecords.Add(record);
        RefreshLintAndNotify();
        return record;
    }

    public void ReturnToStructuredSlot(CanonicalRequirementNode node)
    {
        foreach (var esc in Document.EscapeRecords.Where(
            e => string.Equals(e.RequirementId, node.Id, StringComparison.Ordinal)
                 && e.Status == EscapeRecordStatus.PENDING))
        {
            esc.Status = EscapeRecordStatus.RESOLVED;
            esc.ResolutionNote = "構造化スロットへ再変換済み";
        }

        var defaultTarget = Document.Glossary.FirstOrDefault(g => g.ConceptType == ConceptType.DomainObject)?.Id
            ?? Document.Glossary.FirstOrDefault()?.Id
            ?? string.Empty;

        node.Behavior = new BehaviorNode
        {
            Kind = BehaviorKind.DISPLAY,
            TargetTermRef = defaultTarget,
            Timing = TimingKind.UNSPECIFIED
        };

        RefreshLintAndNotify();
    }

    public void AddGlossaryTerm(ApprovedGlossaryTerm term)
    {
        Document.Glossary.Add(term);
        RefreshLintAndNotify();
    }

    public void DeleteGlossaryTerm(string termId)
    {
        Document.Glossary.RemoveAll(t => string.Equals(t.Id, termId, StringComparison.Ordinal));
        RefreshLintAndNotify();
    }

    private void RefreshLintAndNotify()
    {
        Diagnostics = GraphLinter.Lint(Document);
        OnChange?.Invoke();
    }

    private static string? FindSampleFile(string startDir)
    {
        var dir = new DirectoryInfo(startDir);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "samples", "crud-sample.v2.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }

        return null;
    }

    private static KnowledgeGraphDocument CreateEmptyDocument() => new()
    {
        SchemaVersion = "2.0",
        DocumentId = "DOC-NEW-001",
        Title = "新規要件仕様書"
    };
}
