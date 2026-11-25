using System.Text;
using System.Text.RegularExpressions;
using ReqPipeline.Core.Analysis;

namespace ReqPipeline.Core.Rendering;

public static partial class FsmMermaidRenderer
{
    [GeneratedRegex("[^A-Za-z0-9_]")]
    private static partial Regex InvalidStateIdCharsRegex();

    public static string Render(FsmModel model, bool useJapaneseLabels = true)
    {
        ArgumentNullException.ThrowIfNull(model);

        var sb = new StringBuilder();
        sb.AppendLine("stateDiagram-v2");

        if (!string.IsNullOrWhiteSpace(model.InitialState))
        {
            sb.AppendLine($"    [*] --> {SanitizeStateId(model.InitialState)}");
        }

        foreach (var state in model.States)
        {
            var id = SanitizeStateId(state.Value);
            var displayLabel = FormatStateLabel(state, useJapaneseLabels);
            sb.AppendLine($"    state \"{EscapeLabel(displayLabel)}\" as {id}");
        }

        foreach (var transition in model.Transitions)
        {
            var fromId = SanitizeStateId(transition.FromValue);
            var toId = SanitizeStateId(transition.ToValue);
            var triggerLabel = useJapaneseLabels
                ? (!string.IsNullOrWhiteSpace(transition.TriggerDisplayName)
                    ? transition.TriggerDisplayName
                    : transition.TriggerCanonicalName)
                : transition.TriggerCanonicalName;

            var edgeLabel = string.IsNullOrWhiteSpace(triggerLabel)
                ? $"[{transition.RequirementId}]"
                : $"{triggerLabel} [{transition.RequirementId}]";

            sb.AppendLine($"    {fromId} --> {toId} : {edgeLabel}");
        }

        return sb.ToString().TrimEnd();
    }

    public static string SanitizeStateId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "State_Empty";
        }

        var sanitized = InvalidStateIdCharsRegex().Replace(value.Trim(), "_");
        if (sanitized.Length == 0 || char.IsDigit(sanitized[0]))
        {
            sanitized = "S_" + sanitized;
        }

        return sanitized;
    }

    private static string FormatStateLabel(FsmStateInfo state, bool useJapaneseLabels)
    {
        if (!useJapaneseLabels)
        {
            return state.Value;
        }

        if (!string.IsNullOrWhiteSpace(state.Label)
            && !string.Equals(state.Label, state.Value, StringComparison.Ordinal))
        {
            return $"{state.Label} ({state.Value})";
        }

        return state.Value;
    }

    private static string EscapeLabel(string label) =>
        label.Replace("\"", "\\\"", StringComparison.Ordinal);
}
