using ReqPipeline.Core.Domain;

namespace ReqPipeline.Core.Rendering;

public sealed class JapaneseRequirementRenderer
{
    public static string Render(
        CanonicalRequirementNode node,
        IReadOnlyList<ApprovedGlossaryTerm> glossary,
        IReadOnlyList<FreeTextEscapeRecord>? escapeRecords = null,
        IReadOnlyList<BehaviorDefinition>? behaviorCatalog = null)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(glossary);

        var glossaryById = new Dictionary<string, ApprovedGlossaryTerm>(StringComparer.Ordinal);
        foreach (var term in glossary)
        {
            if (!string.IsNullOrWhiteSpace(term.Id))
            {
                glossaryById.TryAdd(term.Id, term);
            }
        }

        Dictionary<string, BehaviorDefinition>? behaviorCatalogById = null;
        if (behaviorCatalog is not null)
        {
            behaviorCatalogById = new Dictionary<string, BehaviorDefinition>(StringComparer.Ordinal);
            foreach (var def in behaviorCatalog)
            {
                if (!string.IsNullOrWhiteSpace(def.BehaviorDefId))
                {
                    behaviorCatalogById.TryAdd(def.BehaviorDefId, def);
                }
            }
        }

        return Render(node, glossaryById, escapeRecords, behaviorCatalogById);
    }

    public static string Render(
        CanonicalRequirementNode node,
        IReadOnlyDictionary<string, ApprovedGlossaryTerm> glossaryById,
        IReadOnlyList<FreeTextEscapeRecord>? escapeRecords = null,
        IReadOnlyDictionary<string, BehaviorDefinition>? behaviorCatalogById = null)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(glossaryById);

        if (node.NodeKind != RequirementNodeKind.Atomic)
        {
            throw new ArgumentException(
                $"JapaneseRequirementRenderer expects an Atomic requirement node, but '{node.Id}' has NodeKind '{node.NodeKind}'.",
                nameof(node));
        }

        if (node.Behavior is null)
        {
            var pendingEscape = escapeRecords?.FirstOrDefault(
                r => string.Equals(r.RequirementId, node.Id, StringComparison.Ordinal)
                     && r.Status == EscapeRecordStatus.PENDING);

            if (pendingEscape is not null)
            {
                return $"[自由記述/未承認: {pendingEscape.Reason}] {pendingEscape.FreeText}";
            }

            var fallbackSubject = ResolveTermName(node.SubjectSystemRef, glossaryById, "システム");
            return $"{fallbackSubject}は、未定義の動作を行うこと。";
        }

        var subject = ResolveTermName(node.SubjectSystemRef, glossaryById, "システム");
        var actor = string.IsNullOrWhiteSpace(node.ActorRef)
            ? null
            : ResolveTermName(node.ActorRef, glossaryById, node.ActorRef);
        var trigger = string.IsNullOrWhiteSpace(node.TriggerEventRef)
            ? null
            : ResolveTermName(node.TriggerEventRef, glossaryById, node.TriggerEventRef);

        var preconditions = FormatPreconditions(node.Preconditions, glossaryById);
        var timing = FormatTiming(node.Behavior.Timing);
        var behaviorClause = FormatBehavior(node.Behavior, glossaryById, behaviorCatalogById);
        var coreAction = $"{subject}は、{timing}{behaviorClause}。";

        return node.EarsPattern switch
        {
            EarsEntryPattern.Ubiquitous => FormatUbiquitous(preconditions, coreAction),
            EarsEntryPattern.EventDriven => FormatEventDriven(preconditions, actor, trigger, coreAction),
            EarsEntryPattern.StateDriven => FormatStateDriven(preconditions, coreAction),
            EarsEntryPattern.UnwantedBehavior => FormatUnwantedBehavior(preconditions, actor, trigger, coreAction),
            EarsEntryPattern.OptionalFeature => FormatOptionalFeature(preconditions, actor, trigger, coreAction),
            _ => coreAction
        };
    }

    private static string FormatUbiquitous(string? preconditions, string coreAction)
    {
        return string.IsNullOrEmpty(preconditions)
            ? coreAction
            : $"{preconditions}の場合、{coreAction}";
    }

    private static string FormatEventDriven(string? preconditions, string? actor, string? trigger, string coreAction)
    {
        var triggerEvent = FormatTriggerOccurred(actor, trigger);
        if (string.IsNullOrEmpty(triggerEvent))
        {
            return FormatUbiquitous(preconditions, coreAction);
        }

        return string.IsNullOrEmpty(preconditions)
            ? $"{triggerEvent}とき、{coreAction}"
            : $"{preconditions}の状態で、{triggerEvent}とき、{coreAction}";
    }

    private static string FormatStateDriven(string? preconditions, string coreAction)
    {
        return string.IsNullOrEmpty(preconditions)
            ? coreAction
            : $"{preconditions}の間、{coreAction}";
    }

    private static string FormatUnwantedBehavior(string? preconditions, string? actor, string? trigger, string coreAction)
    {
        var triggerEvent = FormatTriggerOccurred(actor, trigger);

        if (!string.IsNullOrEmpty(preconditions) && !string.IsNullOrEmpty(triggerEvent))
        {
            return $"もし{preconditions}であり、{triggerEvent}ならば、{coreAction}";
        }

        if (!string.IsNullOrEmpty(triggerEvent))
        {
            return $"もし{triggerEvent}ならば、{coreAction}";
        }

        if (!string.IsNullOrEmpty(preconditions))
        {
            return $"もし{preconditions}ならば、{coreAction}";
        }

        return coreAction;
    }

    private static string FormatOptionalFeature(string? preconditions, string? actor, string? trigger, string coreAction)
    {
        var triggerEvent = FormatTriggerOccurred(actor, trigger);

        if (!string.IsNullOrEmpty(preconditions) && !string.IsNullOrEmpty(triggerEvent))
        {
            return $"{preconditions}の場合、{triggerEvent}とき、{coreAction}";
        }

        if (!string.IsNullOrEmpty(preconditions))
        {
            return $"{preconditions}の場合、{coreAction}";
        }

        if (!string.IsNullOrEmpty(triggerEvent))
        {
            return $"{triggerEvent}とき、{coreAction}";
        }

        return coreAction;
    }

    private static string? FormatTriggerOccurred(string? actor, string? trigger)
    {
        if (string.IsNullOrEmpty(trigger))
        {
            return null;
        }

        return string.IsNullOrEmpty(actor)
            ? $"{trigger}が発生した"
            : $"{actor}による{trigger}が発生した";
    }

    private static string? FormatPreconditions(
        IReadOnlyList<PreconditionClause> preconditions,
        IReadOnlyDictionary<string, ApprovedGlossaryTerm> glossaryById)
    {
        if (preconditions.Count == 0)
        {
            return null;
        }

        var parts = new List<string>(preconditions.Count);
        foreach (var clause in preconditions)
        {
            var stateName = ResolveTermName(clause.StateTermRef, glossaryById, clause.StateTermRef);
            var valueLabels = clause.ExpectedValues
                .Select(v => ResolveValueLabel(clause.StateTermRef, v, glossaryById))
                .ToList();
            var formatted = clause.Operator switch
            {
                ComparisonOperator.EQ => $"{stateName}が{string.Join("または", valueLabels)}",
                ComparisonOperator.NEQ => $"{stateName}が{string.Join("・", valueLabels)}以外",
                ComparisonOperator.IN => $"{stateName}が[{string.Join("、", valueLabels)}]のいずれか",
                _ => $"{stateName}が{string.Join("、", valueLabels)}"
            };
            parts.Add(formatted);
        }

        return string.Join("かつ", parts);
    }

    private static string FormatTiming(TimingKind timing) => timing switch
    {
        TimingKind.IMMEDIATE => "即座に",
        TimingKind.EVENTUAL => "最終的に",
        _ => string.Empty
    };

    private static string FormatBehavior(
        BehaviorNode behavior,
        IReadOnlyDictionary<string, ApprovedGlossaryTerm> glossaryById,
        IReadOnlyDictionary<string, BehaviorDefinition>? behaviorCatalogById)
    {
        var target = ResolveTermName(behavior.TargetTermRef, glossaryById, behavior.TargetTermRef);

        if (behaviorCatalogById is null
            || string.IsNullOrWhiteSpace(behavior.BehaviorDefRef)
            || !behaviorCatalogById.TryGetValue(behavior.BehaviorDefRef, out var behaviorDef))
        {
            return string.IsNullOrWhiteSpace(behavior.BehaviorDefRef)
                ? $"{target}を処理すること"
                : $"{target}に対して{behavior.BehaviorDefRef}を実行すること";
        }

        var from = ResolveValueLabel(behavior.TargetTermRef, behavior.FromValue, glossaryById);
        var rawTo = behaviorDef.Effect == SemanticEffect.Prohibit
            ? behavior.ProhibitedToValue
            : behavior.ToValue;
        var to = ResolveValueLabel(behavior.TargetTermRef, rawTo, glossaryById);

        var prohibitedAction = "操作";
        if (!string.IsNullOrWhiteSpace(behavior.ProhibitedBehaviorDefRef))
        {
            if (behaviorCatalogById.TryGetValue(behavior.ProhibitedBehaviorDefRef, out var prohibitedDef)
                && !string.IsNullOrWhiteSpace(prohibitedDef.JapaneseLabel))
            {
                prohibitedAction = prohibitedDef.JapaneseLabel;
            }
            else
            {
                prohibitedAction = behavior.ProhibitedBehaviorDefRef;
            }
        }

        var template = !string.IsNullOrWhiteSpace(behaviorDef.JapaneseTemplate)
            ? behaviorDef.JapaneseTemplate
            : "{target}を処理すること";

        var hasToPlaceholder = template.Contains("{to}", StringComparison.Ordinal);
        var rendered = template
            .Replace("{target}", target, StringComparison.Ordinal)
            .Replace("{from}", from, StringComparison.Ordinal)
            .Replace("{to}", to, StringComparison.Ordinal)
            .Replace("{prohibitedAction}", prohibitedAction, StringComparison.Ordinal);

        if (behaviorDef.Effect == SemanticEffect.Prohibit
            && !hasToPlaceholder
            && !string.IsNullOrWhiteSpace(behavior.ProhibitedToValue))
        {
            rendered += $"（遷移先: {to}）";
        }

        return rendered;
    }

    private static string ResolveTermName(
        string? termRef,
        IReadOnlyDictionary<string, ApprovedGlossaryTerm> glossaryById,
        string fallback)
    {
        if (!string.IsNullOrWhiteSpace(termRef) && glossaryById.TryGetValue(termRef, out var term))
        {
            return term.GetDisplayName();
        }

        return fallback;
    }

    private static string ResolveValueLabel(
        string? termRef,
        string? value,
        IReadOnlyDictionary<string, ApprovedGlossaryTerm> glossaryById)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(termRef) && glossaryById.TryGetValue(termRef, out var term))
        {
            return term.LogicalType.GetValueLabel(value);
        }

        return value;
    }
}
