namespace inventory_management.Models;

public class InventoryIndexViewModel
{
    public bool IsConnected { get; set; }
    public string? ErrorMessage { get; set; }
    public string PageTitle { get; set; } = "Active Inventory";
    public bool IsMissingDataPage { get; set; }
    public List<EbayListingViewModel> Listings { get; set; } = [];
    public List<ShelfUnitOptionViewModel> ShelfUnits { get; set; } = [];
    public Dictionary<string, ItemLocationViewModel> SavedLocationsByItemId { get; set; } = [];
    public List<BoxOptionViewModel> Boxes { get; set; } = [];
    public Dictionary<string, ItemBoxViewModel> SavedBoxesByItemId { get; set; } = [];
    public bool HasMore { get; set; }
    public int NextCursor { get; set; } = 2;
    public int TotalCount { get; set; }
    public string Sort { get; set; } = "title";
    public bool IsUnprintedPage { get; set; }
}

public class EbayListingViewModel
{
    public string ItemId { get; set; } = string.Empty;
    /// <summary>
    /// eBay Custom Label (SKU). LOCKED INVARIANT: sync writes each item's permanent local
    /// Barcode into this field on eBay (see IEbayService.UpdateItemSkuAsync) and reads it back
    /// here to recognize the item again if eBay relists it under a new ItemID. See
    /// SyncInventoryListingsFromEbayAsync in InventoryController.
    /// </summary>
    public string? Sku { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? CoverPhotoUrl { get; set; }
    public string Barcode { get; set; } = string.Empty;
    public DateTime ListedAtUtc { get; set; }
    public decimal? CurrentPrice { get; set; }
    public string? CurrencyId { get; set; }
    /// <summary>Populated during eBay sync from GetMyeBaySelling &lt;EndTime&gt;.</summary>
    public DateTime? EndTime { get; set; }
    /// <summary>Populated during eBay sync from GetMyeBaySelling &lt;ListingType&gt;.</summary>
    public string? ListingType { get; set; }
    public DateTime? LabelPrintedAtUtc { get; set; }
    /// <summary>Available quantity on eBay (Quantity - QuantitySold). Null if unknown.</summary>
    public int? QuantityAvailable { get; set; }
    /// <summary>Unit #1's InventoryItemUnit.Id — the unit whose Barcode equals this listing's own
    /// Barcode. Used by the single per-listing "Print QR Code" button on the Active Inventory /
    /// Missing Location pages, which only ever prints/marks that one unit.</summary>
    public Guid PrimaryUnitId { get; set; }
    /// <summary>Populated only on the Unprinted Labels page (IsUnprintedPage): every unit of this
    /// listing that still needs a label printed, including backstock units. A listing keeps
    /// appearing on that page until this list is empty for it.</summary>
    public List<UnitPrintStatusViewModel> UnprintedUnits { get; set; } = [];
}

public class UnitPrintStatusViewModel
{
    public Guid UnitId { get; set; }
    public int UnitIndex { get; set; }
    public string Barcode { get; set; } = string.Empty;
    /// <summary>InventoryItemUnitStatus.Listed or .Backstock.</summary>
    public string Status { get; set; } = string.Empty;
    /// <summary>Total units (printed + unprinted) this listing has, so the UI can show "2 of 3".</summary>
    public int TotalUnitCount { get; set; }
}

public class ShelfUnitOptionViewModel
{
    public Guid Id { get; set; }
    public string Identifier { get; set; } = string.Empty;
    public int ShelfCount { get; set; }
    public string BarcodePrefix { get; set; } = string.Empty;
}

public class ItemLocationViewModel
{
    public string ShelfUnitIdentifier { get; set; } = string.Empty;
    public int ShelfNumber { get; set; }
    public string ShelfBarcode { get; set; } = string.Empty;
}

public class BoxOptionViewModel
{
    public Guid Id { get; set; }
    public string BoxIdentifier { get; set; } = string.Empty;
    public string BoxBarcode { get; set; } = string.Empty;
}

public class ItemBoxViewModel
{
    public string BoxIdentifier { get; set; } = string.Empty;
    public string BoxBarcode { get; set; } = string.Empty;
    public string ShelfUnitIdentifier { get; set; } = string.Empty;
    public int ShelfNumber { get; set; }
    public string ShelfBarcode { get; set; } = string.Empty;
}

public class MarkLabelPrintedRequest
{
    public List<Guid> UnitIds { get; set; } = [];
}
