using ReqPipeline.Core.Domain;

namespace ReqPipeline.Core.Analysis;

public sealed class FsmStateInfo
{
    public string Value { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public bool IsInitial { get; init; }
    public bool IsReachable { get; init; }
    public bool IsSink { get; init; }
    public int IncomingCount { get; init; }
    public int OutgoingCount { get; init; }
}

public sealed class FsmTransition
{
    public string RequirementId { get; init; } = string.Empty;
    public string RequirementTitle { get; init; } = string.Empty;
    public string FromValue { get; init; } = string.Empty;
    public string ToValue { get; init; } = string.Empty;
    public string? TriggerEventRef { get; init; }
    public string? TriggerCanonicalName { get; init; }
    public string? TriggerDisplayName { get; init; }
    public string? ActorDisplayName { get; init; }
}

public sealed class FsmModel
{
    public string StateTermId { get; init; } = string.Empty;
    public string CanonicalName { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string? InitialState { get; init; }
    public List<FsmStateInfo> States { get; init; } = [];
    public List<FsmTransition> Transitions { get; init; } = [];
}

public static class FsmGraphBuilder
{
    public static List<FsmModel> Build(KnowledgeGraphDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var glossaryById = new Dictionary<string, ApprovedGlossaryTerm>(StringComparer.Ordinal);
        foreach (var term in document.Glossary)
        {
            if (!string.IsNullOrWhiteSpace(term.Id))
            {
                glossaryById.TryAdd(term.Id, term);
            }
        }

        var models = new List<FsmModel>();

        foreach (var stateTerm in document.Glossary)
        {
            if (stateTerm.ConceptType != ConceptType.LifecycleState
                || stateTerm.LogicalType.Kind != LogicalTypeKind.Enum)
            {
                continue;
            }

            var allowedValues = stateTerm.LogicalType.AllowedValues
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var allowedSet = new HashSet<string>(allowedValues, StringComparer.Ordinal);
            var initialState = allowedValues.Count > 0 ? allowedValues[0] : null;

            var transitions = new List<FsmTransition>();
            var adjacency = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var incomingCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            var outgoingCounts = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var val in allowedValues)
            {
                adjacency[val] = [];
                incomingCounts[val] = 0;
                outgoingCounts[val] = 0;
            }

            foreach (var req in document.Requirements)
            {
                if (req.NodeKind != RequirementNodeKind.Atomic
                    || req.Behavior is null
                    || req.Behavior.Kind != BehaviorKind.TRANSITION
                    || !string.Equals(req.Behavior.TargetTermRef, stateTerm.Id, StringComparison.Ordinal))
                {
                    continue;
                }

                var fromValue = req.Behavior.FromValue;
                var toValue = req.Behavior.ToValue;
                if (string.IsNullOrWhiteSpace(fromValue)
                    || string.IsNullOrWhiteSpace(toValue)
                    || !allowedSet.Contains(fromValue)
                    || !allowedSet.Contains(toValue))
                {
                    continue;
                }

                ApprovedGlossaryTerm? triggerTerm = null;
                if (!string.IsNullOrWhiteSpace(req.TriggerEventRef))
                {
                    glossaryById.TryGetValue(req.TriggerEventRef, out triggerTerm);
                }

                ApprovedGlossaryTerm? actorTerm = null;
                if (!string.IsNullOrWhiteSpace(req.ActorRef))
                {
                    glossaryById.TryGetValue(req.ActorRef, out actorTerm);
                }

                transitions.Add(new FsmTransition
                {
                    RequirementId = req.Id,
                    RequirementTitle = req.Title,
                    FromValue = fromValue,
                    ToValue = toValue,
                    TriggerEventRef = req.TriggerEventRef,
                    TriggerCanonicalName = triggerTerm?.CanonicalName,
                    TriggerDisplayName = triggerTerm?.GetDisplayName(),
                    ActorDisplayName = actorTerm?.GetDisplayName()
                });

                adjacency[fromValue].Add(toValue);
                outgoingCounts[fromValue]++;
                incomingCounts[toValue]++;
            }

            // BFS from InitialState to determine reachability
            var reachable = new HashSet<string>(StringComparer.Ordinal);
            if (initialState is not null)
            {
                var queue = new Queue<string>();
                reachable.Add(initialState);
                queue.Enqueue(initialState);

                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    foreach (var next in adjacency[current])
                    {
                        if (reachable.Add(next))
                        {
                            queue.Enqueue(next);
                        }
                    }
                }
            }

            var states = new List<FsmStateInfo>(allowedValues.Count);
            foreach (var val in allowedValues)
            {
                var outCount = outgoingCounts[val];
                states.Add(new FsmStateInfo
                {
                    Value = val,
                    Label = stateTerm.LogicalType.GetValueLabel(val),
                    IsInitial = string.Equals(val, initialState, StringComparison.Ordinal),
                    IsReachable = reachable.Contains(val),
                    IsSink = outCount == 0,
                    IncomingCount = incomingCounts[val],
                    OutgoingCount = outCount
                });
            }

            models.Add(new FsmModel
            {
                StateTermId = stateTerm.Id,
                CanonicalName = stateTerm.CanonicalName,
                DisplayName = stateTerm.GetDisplayName(),
                InitialState = initialState,
                States = states,
                Transitions = transitions
            });
        }

        return models;
    }
}
