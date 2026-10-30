namespace inventory_management.Models;

public class RelistHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Username { get; set; } = string.Empty;
    public Guid ListingId { get; set; }
    public string OldItemId { get; set; } = string.Empty;
    public string? NewItemId { get; set; }
    public DateTime RelistedAtUtc { get; set; } = DateTime.UtcNow;
    public string? ErrorMessage { get; set; }

    public InventoryListing? Listing { get; set; }
}
