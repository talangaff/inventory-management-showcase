namespace inventory_management.Models;

/// <summary>A person the owner sells items on behalf of. DefaultSplitPercent is the % THE CONSIGNOR
/// RECEIVES of each sale — always shown spelled out in the UI so the direction is never
/// ambiguous. See Models/InventoryListing.cs (ConsignorId/ConsignmentSplitPercent) and
/// Models/SaleRecord.cs for where this feeds into classification and stats.</summary>
public class Consignor
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Username { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>% the consignor receives of each sale. New consignors default to 50.</summary>
    public decimal DefaultSplitPercent { get; set; } = 50m;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
