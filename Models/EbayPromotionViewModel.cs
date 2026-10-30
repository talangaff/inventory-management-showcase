namespace inventory_management.Models;

public class EbayPromotionViewModel
{
    public string PromotionId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string PromotionType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? StartDate { get; set; }
    public string? EndDate { get; set; }
    public string? PromotionHref { get; set; }
}
