namespace inventory_management.Models;

public class InventoryItemDetailsViewModel
{
    public string ItemId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? CoverPhotoUrl { get; set; }
    public string Barcode { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime ListedAtUtc { get; set; }
    public DateTime LastSeenAtUtc { get; set; }
    public DateTime? InactivatedAtUtc { get; set; }

    /// <summary>From eBay GetItem when available: current price, quantity, view URL.</summary>
    public EbayItemDetailsViewModel? EbayDetails { get; set; }

    /// <summary>Storage: shelf only, or box (with box location).</summary>
    public InventoryItemStorageViewModel? Storage { get; set; }

    /// <summary>Recent orders that include this item.</summary>
    public List<InventoryItemOrderRowViewModel> RecentOrders { get; set; } = [];

    // Needs Attention ignore state (see AttentionRules)
    public DateTime? AttentionIgnoredUntilUtc { get; set; }
    public bool AttentionIgnoredPermanently { get; set; }

    /// <summary>All price changes for this item: manual (this app), external (detected via
    /// eBay sync), and any legacy AutoReduction rows from the removed auto-repricing feature.</summary>
    public List<PriceReductionHistoryRowViewModel> PriceReductionHistory { get; set; } = [];

    // Auto end & relist
    public bool AutoRelistEnabled { get; set; }
    public DateTime? ListingEndTimeUtc { get; set; }
    public string? ListingType { get; set; }
    public List<RelistHistoryRowViewModel> RelistHistory { get; set; } = [];

    /// <summary>Every physical unit of this listing, unit 1 first. Populated whenever this
    /// listing has more than one unit or any backstock, so multi-quantity/backstock tracking is
    /// visible here — the common quantity=1 case has exactly one row (unit 1) and the page's
    /// existing single Storage summary above already covers it.</summary>
    public List<UnitDetailViewModel> Units { get; set; } = [];

    /// <summary>Populated only when this listing has a pending sale that's genuinely ambiguous
    /// (more than one unsold Listed unit) — eBay never says which physical unit sold, so the owner
    /// picks here. See Services/SaleResolutionService.cs. Empty otherwise, including once the
    /// pending sale auto-resolves down to a single candidate.</summary>
    public List<UnsoldUnitOptionViewModel> UnsoldOrderUnitOptions { get; set; } = [];

    // Ownership classification (see Services/OwnershipRules.cs). Null OwnershipType means
    // unclassified — surfaces on Needs Attention.
    public string? OwnershipType { get; set; }
    public string? ConsignorName { get; set; }
    public decimal? ConsignmentSplitPercent { get; set; }
    public decimal? PurchaseCost { get; set; }
    public DateTime? ClassifiedAtUtc { get; set; }
    /// <summary>Every consignor, for the "change classification" consignor picker.</summary>
    public List<ConsignorOptionViewModel> ConsignorOptions { get; set; } = [];
}

public class ConsignorOptionViewModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal DefaultSplitPercent { get; set; }
}

public class UnitDetailViewModel
{
    public Guid UnitId { get; set; }
    public int UnitIndex { get; set; }
    public string Barcode { get; set; } = string.Empty;
    /// <summary>InventoryItemUnitStatus.Listed or .Backstock.</summary>
    public string Status { get; set; } = string.Empty;
    public bool IsSold { get; set; }
    public DateTime? LabelPrintedAtUtc { get; set; }
    public string LocationDisplay { get; set; } = string.Empty;
}

/// <summary>ChangeType is "Manual" (edited in this app), "External" (detected via eBay sync —
/// price changed outside the app), "Bulk" (Bulk Reprice page — see RepriceRules), or the legacy "AutoReduction" (from the removed
/// auto-repricing feature; historical rows only, nothing writes this value anymore).</summary>
public class PriceReductionHistoryRowViewModel
{
    public decimal OldPrice { get; set; }
    public decimal NewPrice { get; set; }
    public DateTime ReducedAtUtc { get; set; }
    public string ChangeType { get; set; } = "Manual";
}

public class RelistHistoryRowViewModel
{
    public string OldItemId { get; set; } = string.Empty;
    public string? NewItemId { get; set; }
    public DateTime RelistedAtUtc { get; set; }
    public string? ErrorMessage { get; set; }
}

public class EbayItemDetailsViewModel
{
    public decimal? CurrentPrice { get; set; }
    public string? CurrencyId { get; set; }
    public int? Quantity { get; set; }
    public int? QuantitySold { get; set; }
    public string? ViewItemUrl { get; set; }
    public string? ConditionDisplayName { get; set; }
    public string? ListingStatus { get; set; }
    /// <summary>eBay ListingType value, e.g. "FixedPriceItem", "Chinese", "StoresFixedPrice".</summary>
    public string? ListingType { get; set; }
    /// <summary>True when the listing is fixed-price (Buy-It-Now); false for auctions.</summary>
    public bool IsBuyItNow => ListingType is "FixedPriceItem" or "StoresFixedPrice";
    /// <summary>Listing end time from eBay GetItem response.</summary>
    public DateTime? EndTime { get; set; }
}

public class InventoryItemStorageViewModel
{
    public bool IsBox { get; set; }
    public string DisplayText { get; set; } = string.Empty;
    public Guid? BoxId { get; set; }
    public Guid? ShelfUnitId { get; set; }
}

public class InventoryItemOrderRowViewModel
{
    public string OrderId { get; set; } = string.Empty;
    public string BuyerName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public DateTime? OrderCreatedAtUtc { get; set; }
    public bool HasTracking { get; set; }
    public bool IsRetrieved { get; set; }
}

public class NeedsAttentionItemViewModel
{
    public Guid ListingId { get; set; }
    public string ItemId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? CoverPhotoUrl { get; set; }
    public decimal? CurrentPrice { get; set; }
    public string? CurrencyId { get; set; }
    /// <summary>Max(ListedAtUtc, last sale date, last price-change date) — the "staleness clock."
    /// See Services/AttentionRules.cs.</summary>
    public DateTime ReferenceDateUtc { get; set; }
    public DateTime ListedAtUtc { get; set; }
    /// <summary>Every recorded price change for this item (manual, quick-update, and external —
    /// see PriceReductionHistory), newest first. Every item on this page has none in the last
    /// 30 days by definition, but older history still shows how the price has moved over time.</summary>
    public List<PriceReductionHistoryRowViewModel> PriceHistory { get; set; } = [];
    /// <summary>Owned-only (see Services/OwnershipRules.cs) — drives the live margin readout
    /// next to this card's quick price editor. Null for Consignment/unclassified items.</summary>
    public decimal? PurchaseCost { get; set; }
    public string? OwnershipType { get; set; }
}

/// <summary>An active listing that's never been classified Owned or Consignment — see
/// Services/OwnershipRules.cs::GetUnclassifiedActiveListingsAsync. Unlike the other Needs
/// Attention categories, this one has no snooze/ignore — it's a data-quality gate.</summary>
public class UnclassifiedItemViewModel
{
    public string ItemId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? CoverPhotoUrl { get; set; }
    public decimal? CurrentPrice { get; set; }
    public string? CurrencyId { get; set; }
    public List<ConsignorOptionViewModel> ConsignorOptions { get; set; } = [];
    /// <summary>The consignor most recently used, if any — backs the one-click quick button
    /// (e.g. "Steve Consignment"). Null if no listing has ever been classified as Consignment.</summary>
    public ConsignorOptionViewModel? MostRecentConsignor { get; set; }
}

/// <summary>An active listing with zero unsold Listed units left (sold out on eBay) that still
/// has backstock — surfaced on Needs Attention so it's not forgotten while sitting in
/// storage. See AttentionRules.GetSoldOutWithBackstockAsync.</summary>
public class SoldOutBackstockItemViewModel
{
    public string ItemId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? CoverPhotoUrl { get; set; }
    public int BackstockCount { get; set; }
}

/// <summary>A listing with a sale that's landed but hasn't been tied to a specific physical unit
/// yet because more than one unsold unit could be the one that sold — see
/// AttentionRules.GetItemsNeedingUnitResolutionAsync / Services/SaleResolutionService.cs.</summary>
public class UnitResolutionItemViewModel
{
    public string ItemId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? CoverPhotoUrl { get; set; }
    public List<UnsoldUnitOptionViewModel> UnitOptions { get; set; } = [];
}

public class NeedsAttentionPageViewModel
{
    public List<NeedsAttentionItemViewModel> StaleItems { get; set; } = [];
    public List<SoldOutBackstockItemViewModel> SoldOutWithBackstock { get; set; } = [];
    public List<UnitResolutionItemViewModel> NeedsUnitResolution { get; set; } = [];
    public List<UnclassifiedItemViewModel> Unclassified { get; set; } = [];
    public List<MissingShippingCostItemViewModel> MissingShippingCost { get; set; } = [];
}

/// <summary>A shipped order eBay's Finance API hasn't returned an actual shipping cost for yet
/// — see Services/AttentionRules.GetOrdersMissingShippingCostAsync.</summary>
public class MissingShippingCostItemViewModel
{
    public string OrderId { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string? ItemPhotoUrl { get; set; }
    public string BuyerName { get; set; } = string.Empty;
    public DateTime? OrderCreatedAtUtc { get; set; }
    public string EbayOrderUrl { get; set; } = string.Empty;
}
