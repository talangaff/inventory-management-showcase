namespace inventory_management.Models;

public class RecentOrder
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string OrderId { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string? ItemPhotoUrl { get; set; }
    public string BuyerName { get; set; } = string.Empty;
    public bool HasTracking { get; set; }
    public bool IsRetrieved { get; set; }
    public int Quantity { get; set; } = 1;
    public string? BuyerStreet1 { get; set; }
    public string? BuyerStreet2 { get; set; }
    public string? BuyerCity { get; set; }
    public string? BuyerState { get; set; }
    public string? BuyerZip { get; set; }
    public string? BuyerCountry { get; set; }
    public string? BuyerPhone { get; set; }
    public DateTime? OrderCreatedAtUtc { get; set; }
    public DateTime LastSeenAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    // Transaction financials (added 2026-08-14) — feed Models/SaleRecord.cs snapshots via
    // Services/SaleResolutionService.cs. Pulled from eBay's GetOrders response, which already
    // returns these fields at DetailLevel=ReturnAll; see EbayService.ParseOrderItems.
    public decimal? TransactionPriceGross { get; set; }
    public decimal? ShippingCollected { get; set; }
    public decimal? TaxCollected { get; set; }
    /// <summary>eBay's final value fee for this transaction. Best-effort via the Finance REST
    /// API (requires the sell.finances OAuth scope) — null until that scope is granted and the
    /// user reconnects eBay. See EbayService.GetOrderFinanceDataAsync.</summary>
    public decimal? EbayFee { get; set; }
    /// <summary>The actual cost of the shipping label bought for this order — distinct from
    /// ShippingCollected (what the buyer paid at checkout). Same best-effort Finance API lookup
    /// as EbayFee; null means eBay hasn't posted a SHIPPING_LABEL transaction for this order
    /// yet, which shows up as a Needs Attention item for manual entry once the order has
    /// shipped. See EbayService.GetOrderFinanceDataAsync and Services/AttentionRules.cs.</summary>
    public decimal? ShippingLabelCost { get; set; }
    public string? CurrencyId { get; set; }
}
