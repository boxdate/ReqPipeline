namespace ReqPipeline.Core.Domain;

public sealed class UserJourney
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    /// <summary>開始時の状態（Glossary TermId -> 英語正規値）</summary>
    public Dictionary<string, string> InitialStates { get; set; } = new(StringComparer.Ordinal);
    public List<JourneyStep> Steps { get; set; } = [];
}

public sealed class JourneyStep
{
    public string StepId { get; set; } = string.Empty;
    /// <summary>ユーザー行動の自然な説明（例: 「新規登録ボタンを押して作成画面へ移動する」）</summary>
    public string UserAction { get; set; } = string.Empty;
    /// <summary>この行動で発火・実行される Atomic 要求の ID</summary>
    public string RequirementRef { get; set; } = string.Empty;
}
