namespace inventory_management.Models;

public class InventoryListing
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Username { get; set; } = string.Empty;
    /// <summary>
    /// Current eBay ItemID. NOT permanent — eBay assigns a new one whenever this listing is
    /// relisted (by our own AutoRelistWorker, by eBay's native "automatically relist" setting,
    /// or manually). This field is expected to change over the item's lifetime; it is
    /// deliberately mutated in place on this same row rather than the row being
    /// recreated, so that Id and Barcode below never change. See SyncInventoryListingsFromEbayAsync
    /// and AutoRelistWorker for the two places this mutation happens.
    /// </summary>
    public string ItemId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? CoverPhotoUrl { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>
    /// LOCKED INVARIANT: permanent for the entire lifespan of this row. Set exactly once, at
    /// row creation (see GenerateItemBarcode / InventoryController), and never reassigned
    /// anywhere else in the codebase. This value is printed on a physical label and stuck to
    /// the real item, so once assigned it must keep working forever — even across relists,
    /// which change ItemId above but must never touch this field or spawn a second row for the
    /// same physical item. Do not add code that regenerates or overwrites this.
    /// </summary>
    public string Barcode { get; set; } = string.Empty;
    public DateTime ListedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? InactivatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public decimal? CurrentPrice { get; set; }
    public string? CurrencyId { get; set; }
    // NOTE: the auto-repricing feature (price_reduction_months/type/amount, minimum_price,
    // last_price_reduction_utc columns) was removed 2026-08-10 — those Postgres columns still
    // exist (this codebase never drops columns, only adds) but are unused and unmapped here.
    // See Services/AttentionRules.cs for what replaced the "Needs Attention" page's old
    // MinimumPrice-based trigger.

    /// <summary>Set by "Ignore for 30 days" on the Needs Attention page. Null, or in the past,
    /// means not currently snoozed. See Services/AttentionRules.cs.</summary>
    public DateTime? AttentionIgnoredUntilUtc { get; set; }
    /// <summary>Set by "Ignore permanently" on the Needs Attention page (reversible via the
    /// item Details page). See Services/AttentionRules.cs.</summary>
    public bool AttentionIgnoredPermanently { get; set; }

    // Auto end & relist
    public bool AutoRelistEnabled { get; set; } = true;
    /// <summary>Cached from eBay sync. Null until first sync captures it.</summary>
    public DateTime? ListingEndTimeUtc { get; set; }
    /// <summary>eBay ListingType, e.g. "FixedPriceItem", "StoresFixedPrice", "Chinese".</summary>
    public string? ListingType { get; set; }
    /// <summary>Set true while a relist job is running to prevent duplicate jobs.</summary>
    public bool RelistInProgress { get; set; }
    /// <summary>Set when a barcode label is first printed for this item. Null means never printed.</summary>
    public DateTime? LabelPrintedAtUtc { get; set; }
    /// <summary>Available quantity on eBay (Quantity - QuantitySold). Cached from eBay sync.</summary>
    public int? QuantityAvailable { get; set; }

    // Ownership classification (added 2026-08-14). Applies only to currently-active and future
    // listings — see Services/OwnershipRules.cs. Sales resolved before that feature's cutoff
    // date are never expected to have this set.
    /// <summary>Null = not yet classified (shows on Needs Attention). OwnershipTypes.Owned or
    /// OwnershipTypes.Consignment otherwise. See Services/OwnershipRules.cs.</summary>
    public string? OwnershipType { get; set; }
    /// <summary>Set when OwnershipType is Consignment. See Models/Consignor.cs.</summary>
    public Guid? ConsignorId { get; set; }
    /// <summary>Snapshotted from Consignor.DefaultSplitPercent at classification time — NOT
    /// live-linked, so a later edit to a consignor's default split doesn't silently change what
    /// an already-classified item pays out. Represents the % the consignor receives.</summary>
    public decimal? ConsignmentSplitPercent { get; set; }
    /// <summary>What the owner paid for this item. Only meaningful when OwnershipType is Owned —
    /// drives the margin calculator on price updates and the Stats page's net-earnings math.</summary>
    public decimal? PurchaseCost { get; set; }
    public DateTime? ClassifiedAtUtc { get; set; }
}

public static class OwnershipTypes
{
    public const string Owned = "Owned";
    public const string Consignment = "Consignment";
}
