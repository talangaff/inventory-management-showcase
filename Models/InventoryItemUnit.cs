namespace inventory_management.Models;

public static class InventoryItemUnitStatus
{
    public const string Listed = "Listed";
    public const string Backstock = "Backstock";
}

/// <summary>
/// One row per physical unit of an InventoryListing. Every listing has at least a unit #1,
/// which always reuses the parent listing's existing Barcode (never a new value — see the
/// LOCKED INVARIANT note on InventoryListing.Barcode). Units 2+ exist only for listings whose
/// eBay QuantityAvailable is greater than 1, or for backstock (extra owned units not currently
/// listed on eBay), and get their own freshly generated barcode via GenerateItemBarcode.
/// </summary>
public class InventoryItemUnit
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Username { get; set; } = string.Empty;
    public Guid ListingId { get; set; }
    public int UnitIndex { get; set; }
    public string Barcode { get; set; } = string.Empty;
    /// <summary>InventoryItemUnitStatus.Listed or .Backstock.</summary>
    public string Status { get; set; } = InventoryItemUnitStatus.Listed;
    public bool IsSold { get; set; }
    public DateTime? LabelPrintedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class DeleteBackstockUnitRequest
{
    public Guid UnitId { get; set; }
}
