namespace inventory_management.Models;

public class PriceReductionHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Username { get; set; } = string.Empty;
    public Guid ListingId { get; set; }
    public decimal OldPrice { get; set; }
    public decimal NewPrice { get; set; }
    public DateTime ReducedAtUtc { get; set; } = DateTime.UtcNow;
    /// <summary>"Manual" for user-initiated price changes, "External" for changes detected via eBay sync, "Bulk" for the Bulk Reprice page; "AutoReduction" is legacy (feature removed).</summary>
    public string ChangeType { get; set; } = "AutoReduction";

    public InventoryListing? Listing { get; set; }
}
