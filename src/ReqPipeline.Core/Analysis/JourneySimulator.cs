using System.Text.Json.Serialization;
using ReqPipeline.Core.Domain;

namespace ReqPipeline.Core.Analysis;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum JourneyStepStatus
{
    Passed,
    Blocked,
    InvalidRequirement,
    PreconditionFailed,
    StateMismatch
}

public sealed class JourneyStepResult
{
    public string StepId { get; init; } = string.Empty;
    public string UserAction { get; init; } = string.Empty;
    public string RequirementRef { get; init; } = string.Empty;
    public string? RequirementTitle { get; init; }
    public JourneyStepStatus Status { get; init; }
    public Dictionary<string, string> StateBefore { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> StateAfter { get; init; } = new(StringComparer.Ordinal);
    public string TransitionSummary { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

public sealed class JourneySimulationResult
{
    public string JourneyId { get; init; } = string.Empty;
    public string JourneyTitle { get; init; } = string.Empty;
    public Dictionary<string, string> InitialStates { get; init; } = new(StringComparer.Ordinal);
    public List<JourneyStepResult> Steps { get; init; } = [];
    public Dictionary<string, string> FinalStates { get; init; } = new(StringComparer.Ordinal);

    public int FailedStepCount => Steps.Count(s =>
        s.Status is JourneyStepStatus.InvalidRequirement
            or JourneyStepStatus.PreconditionFailed
            or JourneyStepStatus.StateMismatch);

    public int BlockedStepCount => Steps.Count(s => s.Status == JourneyStepStatus.Blocked);

    public int PassedStepCount => Steps.Count(s => s.Status == JourneyStepStatus.Passed);

    public bool IsAllPassed => FailedStepCount == 0;
}

/// <summary>
/// 決定論的カスタマージャーニー検証エンジン。
/// KnowledgeGraphDocument 上で UserJourney の各ステップを先頭から順番にシミュレーションする。
/// </summary>
public static class JourneySimulator
{
    public static JourneySimulationResult Simulate(KnowledgeGraphDocument document, UserJourney journey)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(journey);

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

        // 1. Initialize state: LifecycleState (Enum) defaults -> journey.InitialStates overrides
        var currentState = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var term in document.Glossary)
        {
            if (term.ConceptType == ConceptType.LifecycleState
                && term.LogicalType.Kind == LogicalTypeKind.Enum
                && !string.IsNullOrWhiteSpace(term.Id))
            {
                var defaultValue = term.LogicalType.AllowedValues.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
                if (defaultValue is not null)
                {
                    currentState[term.Id] = defaultValue;
                }
            }
        }

        foreach (var (termId, value) in journey.InitialStates)
        {
            if (!string.IsNullOrWhiteSpace(termId) && !string.IsNullOrWhiteSpace(value))
            {
                currentState[termId] = value;
            }
        }

        var initialStatesSnapshot = new Dictionary<string, string>(currentState, StringComparer.Ordinal);
        var stepResults = new List<JourneyStepResult>(journey.Steps.Count);

        // 2. Evaluate steps sequentially
        foreach (var step in journey.Steps)
        {
            var stateBefore = new Dictionary<string, string>(currentState, StringComparer.Ordinal);
            var stepResult = EvaluateStep(step, requirementsById, glossaryById, currentState, stateBefore);
            stepResults.Add(stepResult);
        }

        return new JourneySimulationResult
        {
            JourneyId = journey.Id,
            JourneyTitle = journey.Title,
            InitialStates = initialStatesSnapshot,
            Steps = stepResults,
            FinalStates = new Dictionary<string, string>(currentState, StringComparer.Ordinal)
        };
    }

    private static JourneyStepResult EvaluateStep(
        JourneyStep step,
        Dictionary<string, CanonicalRequirementNode> requirementsById,
        Dictionary<string, ApprovedGlossaryTerm> glossaryById,
        Dictionary<string, string> currentState,
        Dictionary<string, string> stateBefore)
    {
        // Rule 1: Requirement reference check
        if (string.IsNullOrWhiteSpace(step.RequirementRef)
            || !requirementsById.TryGetValue(step.RequirementRef, out var req))
        {
            return CreateResult(
                step,
                reqTitle: null,
                JourneyStepStatus.InvalidRequirement,
                stateBefore,
                currentState,
                transitionSummary: string.Empty,
                message: $"Requirement '{step.RequirementRef}' was not found in document.");
        }

        if (req.NodeKind != RequirementNodeKind.Atomic)
        {
            return CreateResult(
                step,
                req.Title,
                JourneyStepStatus.InvalidRequirement,
                stateBefore,
                currentState,
                transitionSummary: string.Empty,
                message: $"Requirement '{req.Id}' has NodeKind '{req.NodeKind}', expected 'Atomic'.");
        }

        if (req.Behavior is null)
        {
            return CreateResult(
                step,
                req.Title,
                JourneyStepStatus.InvalidRequirement,
                stateBefore,
                currentState,
                transitionSummary: string.Empty,
                message: $"Atomic requirement '{req.Id}' has no Behavior defined.");
        }

        // Rule 2: Preconditions check
        foreach (var clause in req.Preconditions)
        {
            var stateName = ResolveCanonicalName(clause.StateTermRef, glossaryById);
            var expectedText = FormatExpectedCondition(clause);

            if (!currentState.TryGetValue(clause.StateTermRef, out var actualValue)
                || string.IsNullOrWhiteSpace(actualValue))
            {
                return CreateResult(
                    step,
                    req.Title,
                    JourneyStepStatus.PreconditionFailed,
                    stateBefore,
                    currentState,
                    transitionSummary: $"{stateName}: expected {expectedText}, actual (unset)",
                    message: $"Precondition failed on '{stateName}': expected {expectedText}, but state is unset.");
            }

            if (!IsPreconditionSatisfied(clause, actualValue))
            {
                return CreateResult(
                    step,
                    req.Title,
                    JourneyStepStatus.PreconditionFailed,
                    stateBefore,
                    currentState,
                    transitionSummary: $"{stateName}: expected {expectedText}, actual {actualValue}",
                    message: $"Precondition failed on '{stateName}': expected {expectedText}, but current is '{actualValue}'.");
            }
        }

        // Rule 3: Behavior evaluation
        var behavior = req.Behavior;
        var targetName = ResolveCanonicalName(behavior.TargetTermRef, glossaryById);

        switch (behavior.Kind)
        {
            case BehaviorKind.TRANSITION:
            {
                if (string.IsNullOrWhiteSpace(behavior.FromValue) || string.IsNullOrWhiteSpace(behavior.ToValue))
                {
                    return CreateResult(
                        step,
                        req.Title,
                        JourneyStepStatus.InvalidRequirement,
                        stateBefore,
                        currentState,
                        transitionSummary: string.Empty,
                        message: $"Requirement '{req.Id}' TRANSITION behavior is missing FromValue or ToValue.");
                }

                currentState.TryGetValue(behavior.TargetTermRef, out var currentTargetVal);
                if (!string.Equals(currentTargetVal, behavior.FromValue, StringComparison.Ordinal))
                {
                    var actual = string.IsNullOrWhiteSpace(currentTargetVal) ? "(unset)" : currentTargetVal;
                    return CreateResult(
                        step,
                        req.Title,
                        JourneyStepStatus.StateMismatch,
                        stateBefore,
                        currentState,
                        transitionSummary: $"{targetName}: expected {behavior.FromValue}, actual {actual}",
                        message: $"State mismatch on '{targetName}': Expected FromValue '{behavior.FromValue}', but current is '{actual}'.");
                }

                currentState[behavior.TargetTermRef] = behavior.ToValue;
                return CreateResult(
                    step,
                    req.Title,
                    JourneyStepStatus.Passed,
                    stateBefore,
                    currentState,
                    transitionSummary: $"{targetName}: {behavior.FromValue} -> {behavior.ToValue}",
                    message: $"Transitioned '{targetName}' from '{behavior.FromValue}' to '{behavior.ToValue}'.");
            }

            case BehaviorKind.SET:
            {
                if (string.IsNullOrWhiteSpace(behavior.SetValue))
                {
                    return CreateResult(
                        step,
                        req.Title,
                        JourneyStepStatus.InvalidRequirement,
                        stateBefore,
                        currentState,
                        transitionSummary: string.Empty,
                        message: $"Requirement '{req.Id}' SET behavior is missing SetValue.");
                }

                currentState.TryGetValue(behavior.TargetTermRef, out var prevVal);
                currentState[behavior.TargetTermRef] = behavior.SetValue;
                var summary = string.IsNullOrWhiteSpace(prevVal)
                    ? $"{targetName} = {behavior.SetValue}"
                    : $"{targetName}: {prevVal} -> {behavior.SetValue}";

                return CreateResult(
                    step,
                    req.Title,
                    JourneyStepStatus.Passed,
                    stateBefore,
                    currentState,
                    transitionSummary: summary,
                    message: $"Set '{targetName}' to '{behavior.SetValue}'.");
            }

            case BehaviorKind.PROHIBIT:
            {
                var action = behavior.ProhibitedAction?.ToString() ?? "OPERATION";
                return CreateResult(
                    step,
                    req.Title,
                    JourneyStepStatus.Blocked,
                    stateBefore,
                    currentState,
                    transitionSummary: $"PROHIBIT {action} on {targetName}",
                    message: $"Prohibited {action} on '{targetName}' triggered.");
            }

            case BehaviorKind.CREATE:
            case BehaviorKind.DELETE:
            case BehaviorKind.DISPLAY:
            {
                return CreateResult(
                    step,
                    req.Title,
                    JourneyStepStatus.Passed,
                    stateBefore,
                    currentState,
                    transitionSummary: $"{behavior.Kind} {targetName}",
                    message: $"Executed {behavior.Kind} on '{targetName}'.");
            }

            default:
            {
                return CreateResult(
                    step,
                    req.Title,
                    JourneyStepStatus.InvalidRequirement,
                    stateBefore,
                    currentState,
                    transitionSummary: string.Empty,
                    message: $"Unsupported BehaviorKind '{behavior.Kind}' on requirement '{req.Id}'.");
            }
        }
    }

    private static JourneyStepResult CreateResult(
        JourneyStep step,
        string? reqTitle,
        JourneyStepStatus status,
        Dictionary<string, string> stateBefore,
        Dictionary<string, string> currentState,
        string transitionSummary,
        string message) => new()
        {
            StepId = step.StepId,
            UserAction = step.UserAction,
            RequirementRef = step.RequirementRef,
            RequirementTitle = reqTitle,
            Status = status,
            StateBefore = stateBefore,
            StateAfter = new Dictionary<string, string>(currentState, StringComparer.Ordinal),
            TransitionSummary = transitionSummary,
            Message = message
        };

    private static bool IsPreconditionSatisfied(PreconditionClause clause, string actualValue) => clause.Operator switch
    {
        ComparisonOperator.EQ or ComparisonOperator.IN =>
            clause.ExpectedValues.Contains(actualValue, StringComparer.Ordinal),
        ComparisonOperator.NEQ =>
            !clause.ExpectedValues.Contains(actualValue, StringComparer.Ordinal),
        _ => false
    };

    private static string FormatExpectedCondition(PreconditionClause clause)
    {
        var values = string.Join(", ", clause.ExpectedValues);
        return clause.Operator switch
        {
            ComparisonOperator.EQ => $"== [{values}]",
            ComparisonOperator.NEQ => $"!= [{values}]",
            ComparisonOperator.IN => $"IN [{values}]",
            _ => $"[{values}]"
        };
    }

    private static string ResolveCanonicalName(
        string? termRef,
        Dictionary<string, ApprovedGlossaryTerm> glossaryById)
    {
        if (!string.IsNullOrWhiteSpace(termRef) && glossaryById.TryGetValue(termRef, out var term))
        {
            return term.CanonicalName;
        }

        return termRef ?? string.Empty;
    }
}
