namespace inventory_management.Models;

public class BulkRepricePageViewModel
{
    public int Days { get; set; }
    public string Mode { get; set; } = "percent";
    public decimal Amount { get; set; }
    public decimal? MinPrice { get; set; }
    public string? Error { get; set; }
    public List<BulkRepriceRowViewModel> Rows { get; set; } = [];
}

public class BulkRepriceRowViewModel
{
    public Guid ListingId { get; set; }
    public string ItemId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? CoverPhotoUrl { get; set; }
    public string? CurrencyId { get; set; }
    public decimal CurrentPrice { get; set; }
    public DateTime LastRepricedUtc { get; set; }
    public bool EverRepriced { get; set; }
    public decimal? NewPrice { get; set; }
    public bool ClampedToMinimum { get; set; }
    public string? SkipReason { get; set; }
}
