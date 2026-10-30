namespace inventory_management.Models;

public class EbayDraftListing
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string DraftId { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? CoverPhotoUrl { get; set; }
    public string? ConditionId { get; set; }
    public string? ConditionDescription { get; set; }
    public decimal? Price { get; set; }
    public string? Currency { get; set; }
    public decimal? WeightPounds { get; set; }
    public decimal? LengthIn { get; set; }
    public decimal? WidthIn { get; set; }
    public decimal? HeightIn { get; set; }
    public string? CategoryId { get; set; }
    public DateTime SyncedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
