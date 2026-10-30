namespace inventory_management.Models;

public class InventoryItemLocation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Username { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public string ItemTitle { get; set; } = string.Empty;
    /// <summary>Which physical InventoryItemUnit this location belongs to. Nullable only for the
    /// instant between column-add and backfill at startup; every row has one in practice. One
    /// listing can have multiple location rows, one per unit, once it has more than one unit.</summary>
    public Guid? UnitId { get; set; }
    public string ShelfUnitIdentifier { get; set; } = string.Empty;
    public int ShelfNumber { get; set; }
    public string ShelfBarcode { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    /// <summary>True once the item has sold. Location is kept indefinitely so Ready to Ship can
    /// still display where it was shipped from, but sold items are filtered off the shelf/box/
    /// storage-unit browsing pages.</summary>
    public bool IsSold { get; set; }
}
