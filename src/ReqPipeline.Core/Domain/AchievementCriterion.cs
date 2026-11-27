namespace ReqPipeline.Core.Domain;

public sealed class AchievementCriterion
{
    public string Id { get; set; } = string.Empty;
    public string RationaleId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> LinkedCompositeIds { get; set; } = [];
    public List<string> LinkedJourneyIds { get; set; } = [];
    public List<string> ViewpointTags { get; set; } = [];
    public string? ManualVerificationNote { get; set; }
}
