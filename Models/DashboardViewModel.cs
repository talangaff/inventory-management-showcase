namespace inventory_management.Models;

public class DashboardViewModel
{
    public bool IsConnected { get; set; }

    // Work waiting — each matches its page's header count (Services/InventoryCounts.cs).
    public int ReadyToShip { get; set; }
    public int NeedsAttention { get; set; }
    public int MissingLocation { get; set; }
    public int UnprintedLabels { get; set; }

    // Store
    public int ActiveListings { get; set; }
    public decimal ListedValue { get; set; }
    public int Sold30Days { get; set; }
    public decimal Gross30Days { get; set; }

    public List<DashboardSaleViewModel> RecentSales { get; set; } = [];
}

public class DashboardSaleViewModel
{
    public string? ItemId { get; set; }
    public string Title { get; set; } = string.Empty;
    public decimal? SalePriceGross { get; set; }
    public DateTime SoldAtUtc { get; set; }
}
