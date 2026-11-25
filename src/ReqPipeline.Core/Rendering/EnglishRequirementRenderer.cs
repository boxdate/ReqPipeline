using ReqPipeline.Core.Domain;

namespace ReqPipeline.Core.Rendering;

public sealed class EnglishRequirementRenderer
{
    public static string Render(
        CanonicalRequirementNode node,
        IReadOnlyList<ApprovedGlossaryTerm> glossary,
        IReadOnlyList<FreeTextEscapeRecord>? escapeRecords = null)
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

        return Render(node, glossaryById, escapeRecords);
    }

    public static string Render(
        CanonicalRequirementNode node,
        IReadOnlyDictionary<string, ApprovedGlossaryTerm> glossaryById,
        IReadOnlyList<FreeTextEscapeRecord>? escapeRecords = null)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(glossaryById);

        if (node.NodeKind != RequirementNodeKind.Atomic)
        {
            throw new ArgumentException(
                $"EnglishRequirementRenderer expects an Atomic requirement node, but '{node.Id}' has NodeKind '{node.NodeKind}'.",
                nameof(node));
        }

        if (node.Behavior is null)
        {
            var pendingEscape = escapeRecords?.FirstOrDefault(
                r => string.Equals(r.RequirementId, node.Id, StringComparison.Ordinal)
                     && r.Status == EscapeRecordStatus.PENDING);

            if (pendingEscape is not null)
            {
                return $"[FreeText/PENDING: {pendingEscape.Reason}] {pendingEscape.FreeText}";
            }

            var fallbackSubject = ResolveCanonicalName(node.SubjectSystemRef, glossaryById, "System");
            return $"The {fallbackSubject} shall perform an undefined behavior.";
        }

        var subject = ResolveCanonicalName(node.SubjectSystemRef, glossaryById, "System");
        var actor = string.IsNullOrWhiteSpace(node.ActorRef)
            ? null
            : ResolveCanonicalName(node.ActorRef, glossaryById, node.ActorRef);
        var trigger = string.IsNullOrWhiteSpace(node.TriggerEventRef)
            ? null
            : ResolveCanonicalName(node.TriggerEventRef, glossaryById, node.TriggerEventRef);

        var preconditions = FormatPreconditions(node.Preconditions, glossaryById);
        var timing = FormatTiming(node.Behavior.Timing);
        var behaviorClause = FormatBehavior(node.Behavior, glossaryById);
        var coreClause = $"the {subject} shall {timing}{behaviorClause}.";

        return node.EarsPattern switch
        {
            EarsEntryPattern.Ubiquitous => FormatUbiquitous(preconditions, coreClause),
            EarsEntryPattern.EventDriven => FormatEventDriven(preconditions, actor, trigger, coreClause),
            EarsEntryPattern.StateDriven => FormatStateDriven(preconditions, coreClause),
            EarsEntryPattern.UnwantedBehavior => FormatUnwantedBehavior(preconditions, actor, trigger, coreClause),
            EarsEntryPattern.OptionalFeature => FormatOptionalFeature(preconditions, actor, trigger, coreClause),
            _ => CapitalizeFirst(coreClause)
        };
    }

    private static string FormatUbiquitous(string? preconditions, string coreClause)
    {
        return string.IsNullOrEmpty(preconditions)
            ? CapitalizeFirst(coreClause)
            : $"Where {preconditions}, {coreClause}";
    }

    private static string FormatEventDriven(string? preconditions, string? actor, string? trigger, string coreClause)
    {
        var triggerPhrase = FormatTrigger(actor, trigger);
        if (string.IsNullOrEmpty(triggerPhrase))
        {
            return FormatUbiquitous(preconditions, coreClause);
        }

        return string.IsNullOrEmpty(preconditions)
            ? $"When {triggerPhrase}, {coreClause}"
            : $"While {preconditions}, when {triggerPhrase}, {coreClause}";
    }

    private static string FormatStateDriven(string? preconditions, string coreClause)
    {
        return string.IsNullOrEmpty(preconditions)
            ? CapitalizeFirst(coreClause)
            : $"While {preconditions}, {coreClause}";
    }

    private static string FormatUnwantedBehavior(string? preconditions, string? actor, string? trigger, string coreClause)
    {
        var triggerPhrase = FormatTrigger(actor, trigger);

        if (!string.IsNullOrEmpty(preconditions) && !string.IsNullOrEmpty(triggerPhrase))
        {
            return $"If {preconditions} and {triggerPhrase}, then {coreClause}";
        }

        if (!string.IsNullOrEmpty(triggerPhrase))
        {
            return $"If {triggerPhrase}, then {coreClause}";
        }

        if (!string.IsNullOrEmpty(preconditions))
        {
            return $"If {preconditions}, then {coreClause}";
        }

        return CapitalizeFirst(coreClause);
    }

    private static string FormatOptionalFeature(string? preconditions, string? actor, string? trigger, string coreClause)
    {
        var triggerPhrase = FormatTrigger(actor, trigger);

        if (!string.IsNullOrEmpty(preconditions) && !string.IsNullOrEmpty(triggerPhrase))
        {
            return $"Where {preconditions}, when {triggerPhrase}, {coreClause}";
        }

        if (!string.IsNullOrEmpty(preconditions))
        {
            return $"Where {preconditions}, {coreClause}";
        }

        if (!string.IsNullOrEmpty(triggerPhrase))
        {
            return $"When {triggerPhrase}, {coreClause}";
        }

        return CapitalizeFirst(coreClause);
    }

    private static string? FormatTrigger(string? actor, string? trigger)
    {
        if (string.IsNullOrEmpty(trigger))
        {
            return null;
        }

        return string.IsNullOrEmpty(actor)
            ? $"{trigger} occurs"
            : $"{actor} triggers {trigger}";
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
            var stateName = ResolveCanonicalName(clause.StateTermRef, glossaryById, clause.StateTermRef);
            var formatted = clause.Operator switch
            {
                ComparisonOperator.EQ => $"{stateName} is {string.Join(" or ", clause.ExpectedValues)}",
                ComparisonOperator.NEQ => $"{stateName} is not {string.Join(" or ", clause.ExpectedValues)}",
                ComparisonOperator.IN => $"{stateName} is in [{string.Join(", ", clause.ExpectedValues)}]",
                _ => $"{stateName} is {string.Join(", ", clause.ExpectedValues)}"
            };
            parts.Add(formatted);
        }

        return string.Join(" and ", parts);
    }

    private static string FormatTiming(TimingKind timing) => timing switch
    {
        TimingKind.IMMEDIATE => "immediately ",
        TimingKind.EVENTUAL => "eventually ",
        _ => string.Empty
    };

    private static string FormatBehavior(
        BehaviorNode behavior,
        IReadOnlyDictionary<string, ApprovedGlossaryTerm> glossaryById)
    {
        var target = ResolveCanonicalName(behavior.TargetTermRef, glossaryById, behavior.TargetTermRef);

        return behavior.Kind switch
        {
            BehaviorKind.CREATE => $"create {target}",
            BehaviorKind.SET => $"set {target} to {behavior.SetValue}",
            BehaviorKind.DELETE => $"delete {target}",
            BehaviorKind.TRANSITION => $"transition {target} from {behavior.FromValue} to {behavior.ToValue}",
            BehaviorKind.DISPLAY => $"display {target}",
            BehaviorKind.PROHIBIT => behavior.ProhibitedAction.HasValue
                ? $"prohibit {behavior.ProhibitedAction.Value} on {target}"
                : $"prohibit operations on {target}",
            _ => $"process {target}"
        };
    }

    private static string ResolveCanonicalName(
        string? termRef,
        IReadOnlyDictionary<string, ApprovedGlossaryTerm> glossaryById,
        string fallback)
    {
        if (!string.IsNullOrWhiteSpace(termRef) && glossaryById.TryGetValue(termRef, out var term))
        {
            return term.CanonicalName;
        }

        return fallback;
    }

    private static string CapitalizeFirst(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
