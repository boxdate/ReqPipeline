using ReqPipeline.Core.Domain;
using ReqPipeline.Core.Rendering;
using ReqPipeline.Core.Validation;

namespace ReqPipeline.Web.Services;

public sealed class WorkspaceStateService
{
    private readonly string _contentRootPath;

    public KnowledgeGraphDocument Document { get; private set; } = new();
    public IReadOnlyList<LintDiagnostic> Diagnostics { get; private set; } = [];
    public string? SelectedRequirementId { get; private set; }
    public string? SelectedRationaleId { get; private set; }
    public string? SelectedJourneyId { get; private set; }
    public string? SelectedAcId { get; private set; }
    public string WorkbenchMode { get; private set; } = "usdm";
    public string? LastErrorMessage { get; private set; }
    public string ActiveSampleKey { get; private set; } = "crud";
    public bool IsDirty { get; private set; }

    public int ErrorCount => Diagnostics.Count(d => d.Severity == LintSeverity.Error);
    public int WarningCount => Diagnostics.Count(d => d.Severity == LintSeverity.Warning);

    public event Action? OnChange;
    public event Action? OnDocumentMutated;

    public WorkspaceStateService(IWebHostEnvironment env)
    {
        _contentRootPath = env.ContentRootPath;
        LoadSampleOrDefault();
    }

    public void LoadSampleOrDefault()
    {
        if (!TryLoadSample("crud", out _))
        {
            Document = KnowledgeGraphDocument.CreateEmpty("新規要件仕様書");
            ActiveSampleKey = string.Empty;
            SelectedRequirementId = null;
            SelectedRationaleId = null;
            SelectedJourneyId = null;
            SelectedAcId = null;
            IsDirty = false;
            RefreshLintAndNotify();
        }
    }

    public void CreateNewProject(string title, string description = "", bool includeDefaultCrudBehaviors = false)
    {
        var trimmedTitle = string.IsNullOrWhiteSpace(title) ? "新規プロジェクト" : title.Trim();
        var trimmedDescription = description?.Trim() ?? string.Empty;

        Document = KnowledgeGraphDocument.CreateEmpty(trimmedTitle, trimmedDescription, includeDefaultCrudBehaviors);
        ActiveSampleKey = string.Empty;
        SelectedRequirementId = null;
        SelectedRationaleId = null;
        SelectedJourneyId = null;
        SelectedAcId = null;
        LastErrorMessage = null;
        IsDirty = false;
        RefreshLintAndNotify();
    }

    public void MarkExported()
    {
        if (!IsDirty)
        {
            return;
        }

        IsDirty = false;
        OnChange?.Invoke();
    }

    public bool TryRestoreDraft(string json, out string? error)
    {
        try
        {
            var restored = KnowledgeGraphDocument.FromJson(json);
            Document = restored;
            ActiveSampleKey = string.Empty;
            SelectedRationaleId = null;
            SelectedJourneyId = Document.Journeys.FirstOrDefault()?.Id;
            SelectedAcId = Document.AchievementCriteria.FirstOrDefault()?.Id;
            SelectedRequirementId = Document.Requirements
                .FirstOrDefault(r => r.NodeKind == RequirementNodeKind.Atomic)?.Id
                ?? Document.Requirements.FirstOrDefault()?.Id;
            LastErrorMessage = null;
            IsDirty = true;
            error = null;
            RefreshLintAndNotify();
            return true;
        }
        catch (Exception ex)
        {
            error = $"ローカル下書きの復元に失敗しました: {ex.Message}";
            LastErrorMessage = error;
            OnChange?.Invoke();
            return false;
        }
    }

    public int SeedDefaultCrudBehaviors()
    {
        var defaults = KnowledgeGraphDocument.CreateDefaultCrudBehaviors();
        var added = 0;
        foreach (var def in defaults)
        {
            if (!Document.BehaviorCatalog.Any(b => string.Equals(b.BehaviorDefId, def.BehaviorDefId, StringComparison.Ordinal)))
            {
                Document.BehaviorCatalog.Add(def);
                added++;
            }
        }

        if (added > 0)
        {
            RefreshLintAndNotify(markDirty: true);
        }

        return added;
    }

    public bool TryLoadSample(string sampleKey, out string? error)
    {
        var fileName = sampleKey switch
        {
            "crud" => "crud-sample.v2.json",
            "doorlock" => "doorlock-sample.v2.json",
            _ => null
        };

        if (fileName is null)
        {
            error = $"未知のサンプルキー '{sampleKey}' です。";
            LastErrorMessage = error;
            OnChange?.Invoke();
            return false;
        }

        var path = FindSampleFile(_contentRootPath, fileName)
            ?? FindSampleFile(AppContext.BaseDirectory, fileName);

        if (path is null || !File.Exists(path))
        {
            error = $"サンプルファイル 'samples/{fileName}' が見つかりませんでした。";
            LastErrorMessage = error;
            OnChange?.Invoke();
            return false;
        }

        try
        {
            var json = File.ReadAllText(path);
            Document = KnowledgeGraphDocument.FromJson(json);
            ActiveSampleKey = sampleKey;
            LastErrorMessage = null;
            IsDirty = false;
            error = null;
            SelectedRationaleId = null;
            SelectedJourneyId = Document.Journeys.FirstOrDefault()?.Id;
            SelectedAcId = Document.AchievementCriteria.FirstOrDefault()?.Id;
            SelectedRequirementId = Document.Requirements
                .FirstOrDefault(r => r.NodeKind == RequirementNodeKind.Atomic)?.Id
                ?? Document.Requirements.FirstOrDefault()?.Id;

            RefreshLintAndNotify();
            return true;
        }
        catch (Exception ex)
        {
            error = $"サンプルJSONの読み込みに失敗しました: {ex.Message}";
            LastErrorMessage = error;
            OnChange?.Invoke();
            return false;
        }
    }

    public void SelectRequirement(string? requirementId)
    {
        SelectedRequirementId = requirementId;
        SelectedRationaleId = null;
        if (!string.IsNullOrWhiteSpace(requirementId))
        {
            WorkbenchMode = "usdm";
        }
        OnChange?.Invoke();
    }

    public void SelectRationale(string? rationaleId)
    {
        SelectedRationaleId = rationaleId;
        SelectedRequirementId = null;
        OnChange?.Invoke();
    }

    public void SelectJourney(string? journeyId)
    {
        SelectedJourneyId = journeyId;
        OnChange?.Invoke();
    }

    public void SetWorkbenchMode(string mode)
    {
        WorkbenchMode = string.Equals(mode, "ac", StringComparison.OrdinalIgnoreCase) ? "ac" : "usdm";
        if (WorkbenchMode == "ac" && SelectedAcId is null)
        {
            SelectedAcId = Document.AchievementCriteria.FirstOrDefault()?.Id;
        }
        OnChange?.Invoke();
    }

    public void SelectAc(string? acId, bool switchToAcMode = false)
    {
        SelectedAcId = acId;
        if (switchToAcMode)
        {
            WorkbenchMode = "ac";
        }
        OnChange?.Invoke();
    }

    public AchievementCriterion? GetSelectedAc() =>
        SelectedAcId is null
            ? null
            : Document.AchievementCriteria.FirstOrDefault(ac => string.Equals(ac.Id, SelectedAcId, StringComparison.Ordinal));

    public AchievementCriterion AddAchievementCriterion(string? rationaleId = null)
    {
        var nextIndex = 1;
        while (Document.AchievementCriteria.Any(ac => ac.Id == $"AC-{nextIndex:000}"))
        {
            nextIndex++;
        }

        var parentRationaleId = !string.IsNullOrWhiteSpace(rationaleId)
            ? rationaleId
            : (Document.Rationales.FirstOrDefault()?.Id ?? string.Empty);

        var ac = new AchievementCriterion
        {
            Id = $"AC-{nextIndex:000}",
            RationaleId = parentRationaleId,
            Title = $"新規達成基準 {nextIndex}",
            Description = string.Empty,
            LinkedCompositeIds = [],
            LinkedJourneyIds = [],
            ViewpointTags = []
        };

        Document.AchievementCriteria.Add(ac);
        SelectedAcId = ac.Id;
        WorkbenchMode = "ac";
        RefreshLintAndNotify(markDirty: true);
        return ac;
    }

    public void DeleteAchievementCriterion(string acId)
    {
        Document.AchievementCriteria.RemoveAll(ac => string.Equals(ac.Id, acId, StringComparison.Ordinal));
        if (string.Equals(SelectedAcId, acId, StringComparison.Ordinal))
        {
            SelectedAcId = Document.AchievementCriteria.FirstOrDefault()?.Id;
        }
        RefreshLintAndNotify(markDirty: true);
    }

    public IReadOnlyList<LintDiagnostic> GetDiagnosticsForAc(string acId) =>
        Diagnostics
            .Where(d => string.Equals(d.TargetId, acId, StringComparison.Ordinal))
            .ToList();

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
        RefreshLintAndNotify(markDirty: true);
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
        RefreshLintAndNotify(markDirty: true);
    }

    public void NotifyChanged()
    {
        LastErrorMessage = null;
        RefreshLintAndNotify(markDirty: true);
    }

    public bool TryImportJson(string json, out string? error)
    {
        try
        {
            var imported = KnowledgeGraphDocument.FromJson(json);
            Document = imported;
            ActiveSampleKey = string.Empty;
            SelectedRationaleId = null;
            SelectedJourneyId = Document.Journeys.FirstOrDefault()?.Id;
            SelectedAcId = Document.AchievementCriteria.FirstOrDefault()?.Id;
            SelectedRequirementId = Document.Requirements
                .FirstOrDefault(r => r.NodeKind == RequirementNodeKind.Atomic)?.Id
                ?? Document.Requirements.FirstOrDefault()?.Id;
            LastErrorMessage = null;
            IsDirty = false;
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

    public string ExportGlossaryJson() => GlossaryTransfer.ExportJson(Document.Glossary, Document.SchemaVersion);

    public bool TryImportGlossaryJson(
        string json,
        GlossaryImportMode mode,
        out GlossaryImportResult? result,
        out string? error)
    {
        try
        {
            var importedTerms = GlossaryTransfer.ParseTermsFromJson(json);
            result = GlossaryTransfer.ApplyImport(Document.Glossary, importedTerms, mode);
            NotifyChanged();
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            result = null;
            error = $"Glossary JSON の読み込みに失敗しました: {ex.Message}";
            return false;
        }
    }

    public string RenderUsdmMarkdown() => UsdmMarkdownRenderer.Render(Document);

    public string RenderJapanesePreview(CanonicalRequirementNode node)
    {
        if (node.NodeKind != RequirementNodeKind.Atomic)
        {
            return string.Empty;
        }

        return JapaneseRequirementRenderer.Render(node, Document.Glossary, Document.EscapeRecords, Document.BehaviorCatalog);
    }

    public string RenderEnglishPreview(CanonicalRequirementNode node)
    {
        if (node.NodeKind != RequirementNodeKind.Atomic)
        {
            return string.Empty;
        }

        return EnglishRequirementRenderer.Render(node, Document.Glossary, Document.EscapeRecords, Document.BehaviorCatalog);
    }

    public BehaviorDefinition? FindBehaviorDefinition(string? behaviorDefId)
    {
        if (string.IsNullOrWhiteSpace(behaviorDefId))
        {
            return null;
        }

        return Document.BehaviorCatalog.FirstOrDefault(
            b => string.Equals(b.BehaviorDefId, behaviorDefId, StringComparison.Ordinal));
    }

    public BehaviorNode CreateDefaultBehaviorNode()
    {
        var defaultBehavior = Document.BehaviorCatalog.FirstOrDefault(b => b.BehaviorDefId == "BEH-DISPLAY")
            ?? Document.BehaviorCatalog.FirstOrDefault(b => b.Effect is SemanticEffect.EmitAction or SemanticEffect.ManageEntity)
            ?? Document.BehaviorCatalog.FirstOrDefault();

        var defaultTarget = defaultBehavior is not null && defaultBehavior.AllowedTargetTypes.Count > 0
            ? Document.Glossary.FirstOrDefault(g => defaultBehavior.AllowedTargetTypes.Contains(g.ConceptType))?.Id
            : null;

        defaultTarget ??= Document.Glossary.FirstOrDefault(g => g.ConceptType == ConceptType.DomainObject)?.Id
            ?? Document.Glossary.FirstOrDefault()?.Id
            ?? string.Empty;

        var node = new BehaviorNode
        {
            BehaviorDefRef = defaultBehavior?.BehaviorDefId ?? string.Empty,
            TargetTermRef = defaultTarget,
            Timing = TimingKind.UNSPECIFIED
        };

        if (defaultBehavior is not null && !string.IsNullOrWhiteSpace(defaultTarget))
        {
            var allowed = GetAllowedValues(defaultTarget);
            if (defaultBehavior.RequiresFromValue && defaultBehavior.RequiresToValue && allowed.Count >= 2)
            {
                node.FromValue = allowed[0];
                node.ToValue = allowed[1];
            }
            else if (defaultBehavior.RequiresToValue && allowed.Count >= 1)
            {
                node.ToValue = allowed[0];
            }
            else if (defaultBehavior.RequiresFromValue && allowed.Count >= 1)
            {
                node.FromValue = allowed[0];
            }
        }

        return node;
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
        RefreshLintAndNotify(markDirty: true);
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
            Behavior = CreateDefaultBehaviorNode()
        };

        Document.Requirements.Add(child);
        SelectedRequirementId = child.Id;
        SelectedRationaleId = null;
        RefreshLintAndNotify(markDirty: true);
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
        RefreshLintAndNotify(markDirty: true);
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

        RefreshLintAndNotify(markDirty: true);
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
            RefreshLintAndNotify(markDirty: true);
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
        RefreshLintAndNotify(markDirty: true);
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

        node.Behavior = CreateDefaultBehaviorNode();

        RefreshLintAndNotify(markDirty: true);
    }

    public void AddBehaviorDefinition(BehaviorDefinition definition)
    {
        Document.BehaviorCatalog.Add(definition);
        RefreshLintAndNotify(markDirty: true);
    }

    public void DeleteBehaviorDefinition(string behaviorDefId)
    {
        Document.BehaviorCatalog.RemoveAll(b => string.Equals(b.BehaviorDefId, behaviorDefId, StringComparison.Ordinal));
        RefreshLintAndNotify(markDirty: true);
    }

    public void AddGlossaryTerm(ApprovedGlossaryTerm term)
    {
        Document.Glossary.Add(term);
        RefreshLintAndNotify(markDirty: true);
    }

    public void DeleteGlossaryTerm(string termId)
    {
        Document.Glossary.RemoveAll(t => string.Equals(t.Id, termId, StringComparison.Ordinal));
        RefreshLintAndNotify(markDirty: true);
    }

    public UserJourney AddJourney()
    {
        var nextIndex = 1;
        while (Document.Journeys.Any(j => j.Id == $"JRN-{nextIndex:000}"))
        {
            nextIndex++;
        }

        var initialStates = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var term in Document.Glossary)
        {
            if (term.ConceptType == ConceptType.LifecycleState
                && term.LogicalType.Kind == LogicalTypeKind.Enum
                && !string.IsNullOrWhiteSpace(term.Id))
            {
                var defaultVal = term.LogicalType.AllowedValues.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
                if (defaultVal is not null)
                {
                    initialStates[term.Id] = defaultVal;
                }
            }
        }

        var journey = new UserJourney
        {
            Id = $"JRN-{nextIndex:000}",
            Title = $"新規ジャーニー {nextIndex}",
            Description = string.Empty,
            InitialStates = initialStates,
            Steps = []
        };

        Document.Journeys.Add(journey);
        RefreshLintAndNotify(markDirty: true);
        return journey;
    }

    public void DeleteJourney(string journeyId)
    {
        Document.Journeys.RemoveAll(j => string.Equals(j.Id, journeyId, StringComparison.Ordinal));
        RefreshLintAndNotify(markDirty: true);
    }

    public JourneyStep AddJourneyStep(UserJourney journey)
    {
        var nextIndex = 1;
        while (journey.Steps.Any(s => s.StepId == $"STEP-{nextIndex:000}"))
        {
            nextIndex++;
        }

        var defaultReq = Document.Requirements
            .FirstOrDefault(r => r.NodeKind == RequirementNodeKind.Atomic && r.Behavior is not null)?.Id
            ?? string.Empty;

        var step = new JourneyStep
        {
            StepId = $"STEP-{nextIndex:000}",
            UserAction = string.Empty,
            RequirementRef = defaultReq
        };

        journey.Steps.Add(step);
        RefreshLintAndNotify(markDirty: true);
        return step;
    }

    public void MoveJourneyStep(UserJourney journey, int index, int delta)
    {
        var target = index + delta;
        if (index < 0 || index >= journey.Steps.Count || target < 0 || target >= journey.Steps.Count)
        {
            return;
        }

        (journey.Steps[index], journey.Steps[target]) = (journey.Steps[target], journey.Steps[index]);
        RefreshLintAndNotify(markDirty: true);
    }

    public void DeleteJourneyStep(UserJourney journey, int index)
    {
        if (index < 0 || index >= journey.Steps.Count)
        {
            return;
        }

        journey.Steps.RemoveAt(index);
        RefreshLintAndNotify(markDirty: true);
    }

    private void RefreshLintAndNotify(bool markDirty = false)
    {
        if (markDirty)
        {
            IsDirty = true;
        }

        Diagnostics = GraphLinter.Lint(Document);
        OnChange?.Invoke();

        if (markDirty)
        {
            OnDocumentMutated?.Invoke();
        }
    }

    private static string? FindSampleFile(string startDir, string fileName)
    {
        var dir = new DirectoryInfo(startDir);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "samples", fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }

        return null;
    }
}
