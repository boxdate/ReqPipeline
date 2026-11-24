namespace ReqPipeline.Core.Domain;

public sealed class RationaleNode
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? Stakeholder { get; set; }
}
