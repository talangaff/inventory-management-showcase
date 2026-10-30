namespace inventory_management.Models;

public class OrdersIndexViewModel
{
    public string PageTitle { get; set; } = "Recent Orders";
    public bool IsConnected { get; set; }
    public bool IsReadyToShipPage { get; set; }
    public string? ErrorMessage { get; set; }
    public List<OrderListItemViewModel> Orders { get; set; } = [];
    public string Sort { get; set; } = "date_desc";
}

public class OrderListItemViewModel
{
    public string OrderId { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string? ItemPhotoUrl { get; set; }
    public string BuyerName { get; set; } = string.Empty;
    public string StorageLocationDisplay { get; set; } = string.Empty;
    public bool HasTracking { get; set; }
    public int Quantity { get; set; } = 1;
    public DateTime? OrderCreatedAtUtc { get; set; }
    public string Barcode { get; set; } = string.Empty;

    /// <summary>Populated only when this item's listing currently has more than one unsold
    /// Listed unit (eBay never reports which physical unit sold) — the caller must pick which
    /// one shipped via MarkUnitSold before it's reflected in shelf/box/storage-unit tracking.
    /// Empty for the common quantity=1 case, which resolves automatically instead.</summary>
    public List<UnsoldUnitOptionViewModel> UnsoldUnitOptions { get; set; } = [];

    // Buyer shipping address — populated during sync, not displayed in UI
    public string? BuyerStreet1 { get; set; }
    public string? BuyerStreet2 { get; set; }
    public string? BuyerCity { get; set; }
    public string? BuyerState { get; set; }
    public string? BuyerZip { get; set; }
    public string? BuyerCountry { get; set; }
    public string? BuyerPhone { get; set; }

    // Transaction financials (added 2026-08-14), pulled from GetOrders — feed
    // Models/SaleRecord.cs via Services/SaleResolutionService.cs. See RecentOrder.cs for what
    // these persist to.
    public decimal? TransactionPriceGross { get; set; }
    public decimal? ShippingCollected { get; set; }
    public decimal? TaxCollected { get; set; }
    public string? CurrencyId { get; set; }
}

public class RetrieveItemViewModel
{
    public string ItemId { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string? ItemPhotoUrl { get; set; }
    public string StorageLocationDisplay { get; set; } = string.Empty;
    public int TotalQuantity { get; set; } = 1;
    public List<string> BuyerNames { get; set; } = [];
    public string Barcode { get; set; } = string.Empty;
}

public class RetrieveFromInventoryViewModel
{
    public string PageTitle { get; set; } = "Retrieve from Inventory";
    public bool IsConnected { get; set; }
    public string? ErrorMessage { get; set; }
    public List<RetrieveItemViewModel> Items { get; set; } = [];
}

public class MarkRetrievedRequest
{
    public string? ItemId { get; set; }
}

public class MarkTrackingRequest
{
    public string? OrderId { get; set; }
}

public class UnsoldUnitOptionViewModel
{
    public Guid UnitId { get; set; }
    public string Barcode { get; set; } = string.Empty;
    public string LocationDisplay { get; set; } = string.Empty;
}

public class MarkUnitSoldRequest
{
    public string? ItemId { get; set; }
    public Guid UnitId { get; set; }
}
