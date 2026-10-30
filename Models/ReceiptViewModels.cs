namespace inventory_management.Models;

public class ReceiptsIndexViewModel
{
    public List<ReceiptListItemViewModel> Receipts { get; set; } = [];
}

public class ReceiptListItemViewModel
{
    public Guid Id { get; set; }
    public DateOnly ReceiptDate { get; set; }
    public decimal TotalPrice { get; set; }
    public int ItemCount { get; set; }
    public int UnassignedCount { get; set; }
}

public class ReceiptDetailsViewModel
{
    public Guid Id { get; set; }
    public DateOnly ReceiptDate { get; set; }
    public decimal TotalPrice { get; set; }
    public List<ReceiptItemViewModel> Items { get; set; } = [];
    public List<QuickPickItemViewModel> QuickPickItems { get; set; } = [];
    /// <summary>Every active, non-consignment listing with no purchase cost yet: the "Items" strip.</summary>
    public List<QuickPickItemViewModel> UnassignedItems { get; set; } = [];
}

public class ReceiptItemViewModel
{
    public Guid Id { get; set; }
    public string? Name { get; set; }
    public decimal PreTaxPrice { get; set; }
    public decimal AllocatedTotalPrice { get; set; }
    public Guid? AssignedListingId { get; set; }
    public string? AssignedItemId { get; set; }
    public string? AssignedTitle { get; set; }
    public string? AssignedCoverPhotoUrl { get; set; }
    public string? AssignedBarcode { get; set; }
}

/// <summary>A candidate for the "recently listed, not yet costed" quick-pick strip. See
/// ReceiptsController.GetQuickPickCandidatesAsync for the eligibility query.</summary>
public class QuickPickItemViewModel
{
    public Guid ListingId { get; set; }
    public string ItemId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? CoverPhotoUrl { get; set; }
    public string Barcode { get; set; } = string.Empty;
    public DateTime ListedAtUtc { get; set; }
}

/// <summary>Result shape for the live search/scan-assign box on Receipt Details.</summary>
public class ItemSearchResultViewModel
{
    public Guid ListingId { get; set; }
    public string ItemId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? CoverPhotoUrl { get; set; }
    public string Barcode { get; set; } = string.Empty;
    public bool AlreadyOwned { get; set; }
}
