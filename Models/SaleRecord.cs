namespace inventory_management.Models;

/// <summary>Permanent snapshot of a resolved sale, created exactly once per InventoryItemUnit
/// (see the UnitId unique index) by Services/SaleResolutionService.cs::MarkUnitsSoldAsync — the
/// single place a unit's IsSold ever flips to true. Independent of RecentOrder's 90-day purge,
/// since this is the durable source of truth for the Stats page. Only created for classified
/// items (OwnershipType set) whose order is on/after Services/OwnershipRules.cs::StatsCutoffUtc
/// (2026-08-01) — sales before that date, or items that were never classified, never get a row
/// here. Every field is a snapshot at sale-resolution time; later edits to the listing or
/// consignor (e.g. a changed default split %) never retroactively change an existing
/// SaleRecord.</summary>
public class SaleRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Username { get; set; } = string.Empty;
    public Guid ListingId { get; set; }
    public Guid UnitId { get; set; }
    public string ItemId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string OwnershipType { get; set; } = string.Empty;
    public Guid? ConsignorId { get; set; }
    public string? ConsignorName { get; set; }
    /// <summary>% the consignor received of this specific sale, snapshotted at resolution time.</summary>
    public decimal? ConsignorSplitPercent { get; set; }
    public decimal? PurchaseCost { get; set; }
    public decimal? SalePriceGross { get; set; }
    public decimal? ShippingCollected { get; set; }
    public decimal? TaxCollected { get; set; }
    public decimal? EbayFee { get; set; }
    /// <summary>Actual cost of the shipping label bought for this order — see
    /// RecentOrder.ShippingLabelCost for how it's sourced.</summary>
    public decimal? ShippingLabelCost { get; set; }
    public string? CurrencyId { get; set; }
    public string OrderId { get; set; } = string.Empty;
    public DateTime SoldAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
