using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ReqPipeline.Core.Domain;

namespace ReqPipeline.Core.Validation;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LintSeverity
{
    Error,
    Warning
}

public sealed record LintDiagnostic(
    LintSeverity Severity,
    string RuleId,
    string TargetId,
    string Message);

public sealed class GraphLinter
{
    public const string RuleMissingReference = "LINT-001";
    public const string RuleConceptTypeMismatch = "LINT-002";
    public const string RuleLogicalDomainViolation = "LINT-003";
    public const string RuleNodeStructureViolation = "LINT-004";
    public const string RuleBehaviorFieldIntegrity = "LINT-005";
    public const string RuleTopLevelRationaleMissing = "LINT-006";
    public const string RulePendingEscapeRecord = "LINT-007";
    public const string RuleRequirementConflict = "LINT-008";
    public const string RuleGlossarySymbolConvention = "LINT-009";
    public const string RuleGlossaryValueLabelsIntegrity = "LINT-010";
    public const string RuleDeprecatedLabelUsage = "LINT-011";

    private static readonly Regex AsciiSymbolRegex = new("^[A-Za-z][A-Za-z0-9_]*$", RegexOptions.Compiled);

    public static List<LintDiagnostic> Lint(KnowledgeGraphDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var diagnostics = new List<LintDiagnostic>();

        var rationalesById = new Dictionary<string, RationaleNode>(StringComparer.Ordinal);
        foreach (var rationale in document.Rationales)
        {
            if (!string.IsNullOrWhiteSpace(rationale.Id))
            {
                rationalesById.TryAdd(rationale.Id, rationale);
            }
        }

        var glossaryById = new Dictionary<string, ApprovedGlossaryTerm>(StringComparer.Ordinal);
        foreach (var term in document.Glossary)
        {
            if (!string.IsNullOrWhiteSpace(term.Id))
            {
                glossaryById.TryAdd(term.Id, term);
            }
        }

        var requirementsById = new Dictionary<string, CanonicalRequirementNode>(StringComparer.Ordinal);
        foreach (var req in document.Requirements)
        {
            if (!string.IsNullOrWhiteSpace(req.Id))
            {
                requirementsById.TryAdd(req.Id, req);
            }
        }

        var childCountByParent = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var req in document.Requirements)
        {
            if (!string.IsNullOrWhiteSpace(req.ParentRequirementId))
            {
                childCountByParent.TryGetValue(req.ParentRequirementId, out var count);
                childCountByParent[req.ParentRequirementId] = count + 1;
            }
        }

        var pendingEscapeRequirementIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var escape in document.EscapeRecords)
        {
            if (string.IsNullOrWhiteSpace(escape.RequirementId) || !requirementsById.ContainsKey(escape.RequirementId))
            {
                diagnostics.Add(new LintDiagnostic(
                    LintSeverity.Error,
                    RuleMissingReference,
                    escape.Id,
                    $"FreeTextEscapeRecord '{escape.Id}' references unknown RequirementId '{escape.RequirementId}'."));
            }

            if (escape.Status == EscapeRecordStatus.PENDING)
            {
                if (!string.IsNullOrWhiteSpace(escape.RequirementId))
                {
                    pendingEscapeRequirementIds.Add(escape.RequirementId);
                }

                diagnostics.Add(new LintDiagnostic(
                    LintSeverity.Warning,
                    RulePendingEscapeRecord,
                    escape.Id,
                    $"FreeTextEscapeRecord '{escape.Id}' (Requirement '{escape.RequirementId}') is still PENDING approval."));
            }
        }

        foreach (var req in document.Requirements)
        {
            ValidateReferencesAndTypes(req, rationalesById, glossaryById, requirementsById, diagnostics);
            ValidateLogicalDomainClosure(req, glossaryById, diagnostics);
            ValidateNodeStructure(req, childCountByParent, pendingEscapeRequirementIds, diagnostics);
            ValidateBehaviorIntegrity(req, glossaryById, diagnostics);
            ValidateTopLevelRationale(req, rationalesById, diagnostics);
        }

        ValidateRequirementConflicts(document.Requirements, glossaryById, diagnostics);
        ValidateGlossaryDefinitions(document.Glossary, diagnostics);
        ValidateDeprecatedLabelUsage(document, diagnostics);

        return diagnostics;
    }

    private static void ValidateReferencesAndTypes(
        CanonicalRequirementNode req,
        Dictionary<string, RationaleNode> rationalesById,
        Dictionary<string, ApprovedGlossaryTerm> glossaryById,
        Dictionary<string, CanonicalRequirementNode> requirementsById,
        List<LintDiagnostic> diagnostics)
    {
        if (!string.IsNullOrWhiteSpace(req.ParentRequirementId) && !requirementsById.ContainsKey(req.ParentRequirementId))
        {
            diagnostics.Add(new LintDiagnostic(
                LintSeverity.Error,
                RuleMissingReference,
                req.Id,
                $"ParentRequirementId '{req.ParentRequirementId}' does not exist in Requirements."));
        }

        foreach (var ratRef in req.RationaleRefs)
        {
            if (string.IsNullOrWhiteSpace(ratRef) || !rationalesById.ContainsKey(ratRef))
            {
                diagnostics.Add(new LintDiagnostic(
                    LintSeverity.Error,
                    RuleMissingReference,
                    req.Id,
                    $"RationaleRef '{ratRef}' does not exist in Rationales."));
            }
        }

        if (!string.IsNullOrWhiteSpace(req.SubjectSystemRef))
        {
            if (!glossaryById.TryGetValue(req.SubjectSystemRef, out var systemTerm))
            {
                diagnostics.Add(new LintDiagnostic(
                    LintSeverity.Error,
                    RuleMissingReference,
                    req.Id,
                    $"SubjectSystemRef '{req.SubjectSystemRef}' does not exist in Glossary."));
            }
            else if (systemTerm.ConceptType != ConceptType.System)
            {
                diagnostics.Add(new LintDiagnostic(
                    LintSeverity.Error,
                    RuleConceptTypeMismatch,
                    req.Id,
                    $"SubjectSystemRef '{req.SubjectSystemRef}' must have ConceptType '{ConceptType.System}', but found '{systemTerm.ConceptType}'."));
            }
        }

        if (!string.IsNullOrWhiteSpace(req.ActorRef))
        {
            if (!glossaryById.TryGetValue(req.ActorRef, out var actorTerm))
            {
                diagnostics.Add(new LintDiagnostic(
                    LintSeverity.Error,
                    RuleMissingReference,
                    req.Id,
                    $"ActorRef '{req.ActorRef}' does not exist in Glossary."));
            }
            else if (actorTerm.ConceptType != ConceptType.Actor)
            {
                diagnostics.Add(new LintDiagnostic(
                    LintSeverity.Error,
                    RuleConceptTypeMismatch,
                    req.Id,
                    $"ActorRef '{req.ActorRef}' must have ConceptType '{ConceptType.Actor}', but found '{actorTerm.ConceptType}'."));
            }
        }

        if (!string.IsNullOrWhiteSpace(req.TriggerEventRef))
        {
            if (!glossaryById.TryGetValue(req.TriggerEventRef, out var eventTerm))
            {
                diagnostics.Add(new LintDiagnostic(
                    LintSeverity.Error,
                    RuleMissingReference,
                    req.Id,
                    $"TriggerEventRef '{req.TriggerEventRef}' does not exist in Glossary."));
            }
            else if (eventTerm.ConceptType != ConceptType.Event)
            {
                diagnostics.Add(new LintDiagnostic(
                    LintSeverity.Error,
                    RuleConceptTypeMismatch,
                    req.Id,
                    $"TriggerEventRef '{req.TriggerEventRef}' must have ConceptType '{ConceptType.Event}', but found '{eventTerm.ConceptType}'."));
            }
        }

        foreach (var pre in req.Preconditions)
        {
            if (string.IsNullOrWhiteSpace(pre.StateTermRef) || !glossaryById.TryGetValue(pre.StateTermRef, out var stateTerm))
            {
                diagnostics.Add(new LintDiagnostic(
                    LintSeverity.Error,
                    RuleMissingReference,
                    req.Id,
                    $"Precondition StateTermRef '{pre.StateTermRef}' does not exist in Glossary."));
            }
            else if (stateTerm.ConceptType is not (ConceptType.StateVariable or ConceptType.LifecycleState))
            {
                diagnostics.Add(new LintDiagnostic(
                    LintSeverity.Error,
                    RuleConceptTypeMismatch,
                    req.Id,
                    $"Precondition StateTermRef '{pre.StateTermRef}' must have ConceptType '{ConceptType.StateVariable}' or '{ConceptType.LifecycleState}', but found '{stateTerm.ConceptType}'."));
            }
        }

        if (req.Behavior is not null)
        {
            if (string.IsNullOrWhiteSpace(req.Behavior.TargetTermRef) || !glossaryById.ContainsKey(req.Behavior.TargetTermRef))
            {
                diagnostics.Add(new LintDiagnostic(
                    LintSeverity.Error,
                    RuleMissingReference,
                    req.Id,
                    $"Behavior TargetTermRef '{req.Behavior.TargetTermRef}' does not exist in Glossary."));
            }
        }
    }

    private static void ValidateLogicalDomainClosure(
        CanonicalRequirementNode req,
        Dictionary<string, ApprovedGlossaryTerm> glossaryById,
        List<LintDiagnostic> diagnostics)
    {
        foreach (var pre in req.Preconditions)
        {
            if (string.IsNullOrWhiteSpace(pre.StateTermRef) || !glossaryById.TryGetValue(pre.StateTermRef, out var stateTerm))
            {
                continue;
            }

            if (pre.ExpectedValues.Count == 0)
            {
                diagnostics.Add(new LintDiagnostic(
                    LintSeverity.Error,
                    RuleLogicalDomainViolation,
                    req.Id,
                    $"Precondition on '{pre.StateTermRef}' must specify at least one expected value."));
                continue;
            }

            foreach (var expected in pre.ExpectedValues)
            {
                if (!stateTerm.LogicalType.IsValidValue(expected))
                {
                    diagnostics.Add(new LintDiagnostic(
                        LintSeverity.Error,
                        RuleLogicalDomainViolation,
                        req.Id,
                        $"Precondition expected value '{expected}' is not valid for '{stateTerm.Id}' ({FormatLogicalDomain(stateTerm.LogicalType)})."));
                }
            }
        }

        if (req.Behavior is null || string.IsNullOrWhiteSpace(req.Behavior.TargetTermRef))
        {
            return;
        }

        if (!glossaryById.TryGetValue(req.Behavior.TargetTermRef, out var targetTerm))
        {
            return;
        }

        if (req.Behavior.FromValue is not null && !targetTerm.LogicalType.IsValidValue(req.Behavior.FromValue))
        {
            diagnostics.Add(new LintDiagnostic(
                LintSeverity.Error,
                RuleLogicalDomainViolation,
                req.Id,
                $"Behavior FromValue '{req.Behavior.FromValue}' is not valid for '{targetTerm.Id}' ({FormatLogicalDomain(targetTerm.LogicalType)})."));
        }

        if (req.Behavior.ToValue is not null && !targetTerm.LogicalType.IsValidValue(req.Behavior.ToValue))
        {
            diagnostics.Add(new LintDiagnostic(
                LintSeverity.Error,
                RuleLogicalDomainViolation,
                req.Id,
                $"Behavior ToValue '{req.Behavior.ToValue}' is not valid for '{targetTerm.Id}' ({FormatLogicalDomain(targetTerm.LogicalType)})."));
        }

        if (req.Behavior.SetValue is not null && !targetTerm.LogicalType.IsValidValue(req.Behavior.SetValue))
        {
            diagnostics.Add(new LintDiagnostic(
                LintSeverity.Error,
                RuleLogicalDomainViolation,
                req.Id,
                $"Behavior SetValue '{req.Behavior.SetValue}' is not valid for '{targetTerm.Id}' ({FormatLogicalDomain(targetTerm.LogicalType)})."));
        }
    }

    private static void ValidateNodeStructure(
        CanonicalRequirementNode req,
        Dictionary<string, int> childCountByParent,
        HashSet<string> pendingEscapeRequirementIds,
        List<LintDiagnostic> diagnostics)
    {
        if (req.NodeKind == RequirementNodeKind.Composite)
        {
            if (!childCountByParent.TryGetValue(req.Id, out var count) || count == 0)
            {
                diagnostics.Add(new LintDiagnostic(
                    LintSeverity.Error,
                    RuleNodeStructureViolation,
                    req.Id,
                    $"Composite requirement '{req.Id}' must have at least one child requirement."));
            }
        }
        else if (req.NodeKind == RequirementNodeKind.Atomic)
        {
            var hasBehavior = req.Behavior is not null;
            var hasPendingEscape = pendingEscapeRequirementIds.Contains(req.Id);
            if (!hasBehavior && !hasPendingEscape)
            {
                diagnostics.Add(new LintDiagnostic(
                    LintSeverity.Error,
                    RuleNodeStructureViolation,
                    req.Id,
                    $"Atomic requirement '{req.Id}' must define a Behavior or have a PENDING FreeTextEscapeRecord."));
            }
        }
    }

    private static void ValidateBehaviorIntegrity(
        CanonicalRequirementNode req,
        Dictionary<string, ApprovedGlossaryTerm> glossaryById,
        List<LintDiagnostic> diagnostics)
    {
        if (req.Behavior is null)
        {
            return;
        }

        var behavior = req.Behavior;
        switch (behavior.Kind)
        {
            case BehaviorKind.TRANSITION:
                if (string.IsNullOrWhiteSpace(behavior.FromValue) || string.IsNullOrWhiteSpace(behavior.ToValue))
                {
                    diagnostics.Add(new LintDiagnostic(
                        LintSeverity.Error,
                        RuleBehaviorFieldIntegrity,
                        req.Id,
                        $"BehaviorKind 'TRANSITION' on '{req.Id}' requires both FromValue and ToValue."));
                }
                break;

            case BehaviorKind.SET:
                if (string.IsNullOrWhiteSpace(behavior.SetValue))
                {
                    diagnostics.Add(new LintDiagnostic(
                        LintSeverity.Error,
                        RuleBehaviorFieldIntegrity,
                        req.Id,
                        $"BehaviorKind 'SET' on '{req.Id}' requires SetValue."));
                }
                break;

            case BehaviorKind.CREATE:
            case BehaviorKind.DELETE:
                if (!string.IsNullOrWhiteSpace(behavior.TargetTermRef)
                    && glossaryById.TryGetValue(behavior.TargetTermRef, out var targetTerm)
                    && targetTerm.ConceptType != ConceptType.DomainObject)
                {
                    diagnostics.Add(new LintDiagnostic(
                        LintSeverity.Error,
                        RuleBehaviorFieldIntegrity,
                        req.Id,
                        $"BehaviorKind '{behavior.Kind}' on '{req.Id}' requires target '{targetTerm.Id}' to have ConceptType '{ConceptType.DomainObject}', but found '{targetTerm.ConceptType}'."));
                }
                break;
        }
    }

    private static void ValidateTopLevelRationale(
        CanonicalRequirementNode req,
        Dictionary<string, RationaleNode> rationalesById,
        List<LintDiagnostic> diagnostics)
    {
        if (!string.IsNullOrWhiteSpace(req.ParentRequirementId))
        {
            return;
        }

        var hasValidRationale = req.RationaleRefs.Any(r => !string.IsNullOrWhiteSpace(r) && rationalesById.ContainsKey(r));
        if (!hasValidRationale)
        {
            diagnostics.Add(new LintDiagnostic(
                LintSeverity.Error,
                RuleTopLevelRationaleMissing,
                req.Id,
                $"Top-level requirement '{req.Id}' must reference at least one valid Rationale."));
        }
    }

    private static void ValidateRequirementConflicts(
        List<CanonicalRequirementNode> requirements,
        Dictionary<string, ApprovedGlossaryTerm> glossaryById,
        List<LintDiagnostic> diagnostics)
    {
        var atomics = requirements
            .Where(r => r.NodeKind == RequirementNodeKind.Atomic && r.Behavior is not null)
            .ToList();

        for (var i = 0; i < atomics.Count; i++)
        {
            for (var j = i + 1; j < atomics.Count; j++)
            {
                var a = atomics[i];
                var b = atomics[j];

                if (!HasOverlappingSubjectAndTrigger(a, b))
                {
                    continue;
                }

                if (!ArePreconditionsSatisfiableTogether(a, b, glossaryById))
                {
                    continue;
                }

                var conflictReason = DetectBehaviorConflict(a.Behavior!, b.Behavior!);
                if (conflictReason is null)
                {
                    continue;
                }

                diagnostics.Add(new LintDiagnostic(
                    LintSeverity.Error,
                    RuleRequirementConflict,
                    a.Id,
                    $"要求 [{b.Id}] と条件・トリガーが重複し、動作が衝突しています（{conflictReason}）。"));

                diagnostics.Add(new LintDiagnostic(
                    LintSeverity.Error,
                    RuleRequirementConflict,
                    b.Id,
                    $"要求 [{a.Id}] と条件・トリガーが重複し、動作が衝突しています（{conflictReason}）。"));
            }
        }
    }

    private static bool HasOverlappingSubjectAndTrigger(CanonicalRequirementNode a, CanonicalRequirementNode b)
    {
        if (!string.Equals(a.SubjectSystemRef, b.SubjectSystemRef, StringComparison.Ordinal))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(a.TriggerEventRef) || string.IsNullOrWhiteSpace(b.TriggerEventRef))
        {
            return true;
        }

        return string.Equals(a.TriggerEventRef, b.TriggerEventRef, StringComparison.Ordinal);
    }

    private static bool ArePreconditionsSatisfiableTogether(
        CanonicalRequirementNode a,
        CanonicalRequirementNode b,
        Dictionary<string, ApprovedGlossaryTerm> glossaryById)
    {
        var stateRefs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pre in a.Preconditions)
        {
            if (!string.IsNullOrWhiteSpace(pre.StateTermRef))
            {
                stateRefs.Add(pre.StateTermRef);
            }
        }
        foreach (var pre in b.Preconditions)
        {
            if (!string.IsNullOrWhiteSpace(pre.StateTermRef))
            {
                stateRefs.Add(pre.StateTermRef);
            }
        }

        foreach (var stateRef in stateRefs)
        {
            if (!glossaryById.TryGetValue(stateRef, out var term))
            {
                continue;
            }

            var domain = GetLogicalDomainValues(term.LogicalType);
            if (domain is null)
            {
                continue;
            }

            var allowedA = ComputeSatisfyingValues(a.Preconditions, stateRef, domain);
            var allowedB = ComputeSatisfyingValues(b.Preconditions, stateRef, domain);

            allowedA.IntersectWith(allowedB);
            if (allowedA.Count == 0)
            {
                return false;
            }
        }

        return true;
    }

    private static HashSet<string>? GetLogicalDomainValues(LogicalTypeDefinition logicalType) => logicalType.Kind switch
    {
        LogicalTypeKind.Boolean => new HashSet<string>(["true", "false"], StringComparer.Ordinal),
        LogicalTypeKind.Enum => new HashSet<string>(logicalType.AllowedValues, StringComparer.Ordinal),
        _ => null
    };

    private static HashSet<string> ComputeSatisfyingValues(
        List<PreconditionClause> preconditions,
        string stateRef,
        HashSet<string> domain)
    {
        var allowed = new HashSet<string>(domain, StringComparer.Ordinal);
        foreach (var clause in preconditions)
        {
            if (!string.Equals(clause.StateTermRef, stateRef, StringComparison.Ordinal))
            {
                continue;
            }

            var clauseSet = clause.Operator switch
            {
                ComparisonOperator.EQ or ComparisonOperator.IN =>
                    new HashSet<string>(domain.Where(v => clause.ExpectedValues.Contains(v, StringComparer.Ordinal)), StringComparer.Ordinal),
                ComparisonOperator.NEQ =>
                    new HashSet<string>(domain.Where(v => !clause.ExpectedValues.Contains(v, StringComparer.Ordinal)), StringComparer.Ordinal),
                _ => new HashSet<string>(domain, StringComparer.Ordinal)
            };

            allowed.IntersectWith(clauseSet);
        }

        return allowed;
    }

    private static string? DetectBehaviorConflict(BehaviorNode a, BehaviorNode b)
    {
        if (string.IsNullOrWhiteSpace(a.TargetTermRef)
            || !string.Equals(a.TargetTermRef, b.TargetTermRef, StringComparison.Ordinal))
        {
            return null;
        }

        // 1. 許可 vs 禁止の衝突
        if ((a.Kind == BehaviorKind.PROHIBIT) != (b.Kind == BehaviorKind.PROHIBIT))
        {
            var prohibit = a.Kind == BehaviorKind.PROHIBIT ? a : b;
            var allow = a.Kind == BehaviorKind.PROHIBIT ? b : a;

            if (prohibit.ProhibitedAction is null || prohibit.ProhibitedAction == allow.Kind)
            {
                return "許可と禁止の矛盾";
            }

            return null;
        }

        // 2. 値の非決定的な衝突 (TRANSITION / SET)
        if (a.Kind == BehaviorKind.TRANSITION && b.Kind == BehaviorKind.TRANSITION)
        {
            var fromOverlaps = string.IsNullOrWhiteSpace(a.FromValue)
                || string.IsNullOrWhiteSpace(b.FromValue)
                || string.Equals(a.FromValue, b.FromValue, StringComparison.Ordinal);

            var toDiffers = !string.IsNullOrWhiteSpace(a.ToValue)
                && !string.IsNullOrWhiteSpace(b.ToValue)
                && !string.Equals(a.ToValue, b.ToValue, StringComparison.Ordinal);

            if (fromOverlaps && toDiffers)
            {
                return "遷移先の競合";
            }
        }
        else if (a.Kind == BehaviorKind.SET && b.Kind == BehaviorKind.SET)
        {
            var setDiffers = !string.IsNullOrWhiteSpace(a.SetValue)
                && !string.IsNullOrWhiteSpace(b.SetValue)
                && !string.Equals(a.SetValue, b.SetValue, StringComparison.Ordinal);

            if (setDiffers)
            {
                return "設定値の競合";
            }
        }
        else if ((a.Kind == BehaviorKind.SET && b.Kind == BehaviorKind.TRANSITION)
                 || (a.Kind == BehaviorKind.TRANSITION && b.Kind == BehaviorKind.SET))
        {
            var setNode = a.Kind == BehaviorKind.SET ? a : b;
            var transNode = a.Kind == BehaviorKind.TRANSITION ? a : b;

            if (!string.IsNullOrWhiteSpace(setNode.SetValue)
                && !string.IsNullOrWhiteSpace(transNode.ToValue)
                && !string.Equals(setNode.SetValue, transNode.ToValue, StringComparison.Ordinal))
            {
                return "設定値と遷移先の競合";
            }
        }

        return null;
    }

    private static void ValidateGlossaryDefinitions(
        List<ApprovedGlossaryTerm> glossary,
        List<LintDiagnostic> diagnostics)
    {
        var seenCanonicalNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var term in glossary)
        {
            var targetId = string.IsNullOrWhiteSpace(term.Id) ? term.CanonicalName : term.Id;

            if (string.IsNullOrWhiteSpace(term.CanonicalName) || !AsciiSymbolRegex.IsMatch(term.CanonicalName))
            {
                diagnostics.Add(new LintDiagnostic(
                    LintSeverity.Error,
                    RuleGlossarySymbolConvention,
                    targetId,
                    $"Glossary term '{targetId}' has invalid CanonicalName '{term.CanonicalName}'. Expected ASCII symbol matching ^[A-Za-z][A-Za-z0-9_]*$."));
            }
            else if (!seenCanonicalNames.Add(term.CanonicalName))
            {
                diagnostics.Add(new LintDiagnostic(
                    LintSeverity.Error,
                    RuleGlossarySymbolConvention,
                    targetId,
                    $"Glossary term '{targetId}' has duplicate CanonicalName '{term.CanonicalName}'."));
            }

            if (term.LogicalType.Kind == LogicalTypeKind.Enum)
            {
                var seenValues = new HashSet<string>(StringComparer.Ordinal);
                foreach (var val in term.LogicalType.AllowedValues)
                {
                    if (string.IsNullOrWhiteSpace(val) || !AsciiSymbolRegex.IsMatch(val))
                    {
                        diagnostics.Add(new LintDiagnostic(
                            LintSeverity.Error,
                            RuleGlossarySymbolConvention,
                            targetId,
                            $"Glossary term '{targetId}' has invalid AllowedValue '{val}'. Expected ASCII symbol matching ^[A-Za-z][A-Za-z0-9_]*$."));
                    }
                    else if (!seenValues.Add(val))
                    {
                        diagnostics.Add(new LintDiagnostic(
                            LintSeverity.Error,
                            RuleGlossarySymbolConvention,
                            targetId,
                            $"Glossary term '{targetId}' has duplicate AllowedValue '{val}'."));
                    }
                }
            }

            foreach (var key in term.LogicalType.ValueLabels.Keys)
            {
                if (!term.LogicalType.IsValidValue(key))
                {
                    diagnostics.Add(new LintDiagnostic(
                        LintSeverity.Error,
                        RuleGlossaryValueLabelsIntegrity,
                        targetId,
                        $"Glossary term '{targetId}' has ValueLabels key '{key}' which is not valid for LogicalType ({FormatLogicalDomain(term.LogicalType)})."));
                }
            }
        }
    }

    private static void ValidateDeprecatedLabelUsage(
        KnowledgeGraphDocument document,
        List<LintDiagnostic> diagnostics)
    {
        var deprecatedEntries = new List<(string Label, ApprovedGlossaryTerm Term)>();
        foreach (var term in document.Glossary)
        {
            foreach (var label in term.DeprecatedLabels)
            {
                if (!string.IsNullOrWhiteSpace(label))
                {
                    deprecatedEntries.Add((label, term));
                }
            }
        }

        if (deprecatedEntries.Count == 0)
        {
            return;
        }

        foreach (var term in document.Glossary)
        {
            var targetId = string.IsNullOrWhiteSpace(term.Id) ? term.CanonicalName : term.Id;
            foreach (var (label, sourceTerm) in deprecatedEntries)
            {
                if (!string.IsNullOrWhiteSpace(term.DisplayName)
                    && term.DisplayName.Contains(label, StringComparison.Ordinal))
                {
                    diagnostics.Add(new LintDiagnostic(
                        LintSeverity.Error,
                        RuleDeprecatedLabelUsage,
                        targetId,
                        $"Glossary term '{targetId}' DisplayName '{term.DisplayName}' contains deprecated label '{label}' (defined on '{sourceTerm.Id}')."));
                }

                foreach (var alias in term.Aliases)
                {
                    if (!string.IsNullOrWhiteSpace(alias) && alias.Contains(label, StringComparison.Ordinal))
                    {
                        diagnostics.Add(new LintDiagnostic(
                            LintSeverity.Error,
                            RuleDeprecatedLabelUsage,
                            targetId,
                            $"Glossary term '{targetId}' Alias '{alias}' contains deprecated label '{label}' (defined on '{sourceTerm.Id}')."));
                    }
                }
            }
        }

        foreach (var rationale in document.Rationales)
        {
            CheckTextForDeprecatedLabels(rationale.Id, "Rationale.Title", rationale.Title, deprecatedEntries, diagnostics);
            CheckTextForDeprecatedLabels(rationale.Id, "Rationale.Description", rationale.Description, deprecatedEntries, diagnostics);
        }

        foreach (var req in document.Requirements)
        {
            CheckTextForDeprecatedLabels(req.Id, "Requirement.Title", req.Title, deprecatedEntries, diagnostics);
            CheckTextForDeprecatedLabels(req.Id, "Requirement.Summary", req.Summary, deprecatedEntries, diagnostics);
        }

        foreach (var escape in document.EscapeRecords)
        {
            CheckTextForDeprecatedLabels(escape.Id, "FreeTextEscapeRecord.FreeText", escape.FreeText, deprecatedEntries, diagnostics);
        }
    }

    private static void CheckTextForDeprecatedLabels(
        string targetId,
        string fieldName,
        string? text,
        List<(string Label, ApprovedGlossaryTerm Term)> deprecatedEntries,
        List<LintDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        foreach (var (label, sourceTerm) in deprecatedEntries)
        {
            if (text.Contains(label, StringComparison.Ordinal))
            {
                diagnostics.Add(new LintDiagnostic(
                    LintSeverity.Error,
                    RuleDeprecatedLabelUsage,
                    targetId,
                    $"{fieldName} on '{targetId}' contains deprecated label '{label}' (use '{sourceTerm.GetDisplayName()}' / '{sourceTerm.CanonicalName}' instead)."));
            }
        }
    }

    private static string FormatLogicalDomain(LogicalTypeDefinition logicalType) => logicalType.Kind switch
    {
        LogicalTypeKind.Boolean => "Boolean: true | false",
        LogicalTypeKind.Enum => $"Enum: [{string.Join(", ", logicalType.AllowedValues)}]",
        _ => "None"
    };
}
