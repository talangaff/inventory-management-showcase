namespace inventory_management.Models;

public class StatsIndexViewModel
{
    public bool HasAnySaleRecords { get; set; }
    public bool AnyMissingEbayFee { get; set; }
    /// <summary>True if any shipped sale is missing its actual shipping label cost — those
    /// sales show up as Needs Attention items for manual entry (see AttentionRules), and until
    /// filled in, ShippingLabelCost totals below are understated the same way eBay fees can be.</summary>
    public bool AnyMissingShippingLabelCost { get; set; }

    // Owned items
    public decimal OwnedGrossSales { get; set; }
    public decimal OwnedTotalCost { get; set; }
    /// <summary>Net = Gross - Cost, the owner's literal formula for "what I earned."</summary>
    public decimal OwnedNetEarnings { get; set; }
    public decimal OwnedShippingCollected { get; set; }
    public decimal OwnedTaxCollected { get; set; }
    public decimal OwnedEbayFees { get; set; }
    /// <summary>Actual cost of shipping labels bought for these sales.</summary>
    public decimal OwnedShippingLabelCost { get; set; }
    /// <summary>Net + ShippingCollected - ShippingLabelCost - EbayFees: real money in the door
    /// (item sale + shipping the buyer paid) minus real money out (eBay's fee, the actual
    /// shipping label cost) — confirmed 2026-08-20 once actual shipping cost became available.
    /// For reference — not the headline number (Net above is).</summary>
    public decimal OwnedProfitAfterFees { get; set; }
    public int OwnedSaleCount { get; set; }

    // Consignment (all consignors combined) — mirrors the Owned card above, but "Cost" here is
    // the amount owed/paid to consignors (the store's cost of the consigned goods it sold),
    // playing the same role PurchaseCost plays for Owned.
    public decimal ConsignmentGrossSales { get; set; }
    public decimal ConsignmentAmountOwed { get; set; }
    /// <summary>Net = Gross - AmountOwed: the store's actual cut of consignment sales.</summary>
    public decimal ConsignmentNetEarnings { get; set; }
    public decimal ConsignmentShippingCollected { get; set; }
    public decimal ConsignmentTaxCollected { get; set; }
    public decimal ConsignmentEbayFees { get; set; }
    public decimal ConsignmentShippingLabelCost { get; set; }
    /// <summary>Same real-money formula as OwnedProfitAfterFees, applied on top of the
    /// consignor payout: Net (Gross - AmountOwed) + ShippingCollected - ShippingLabelCost -
    /// EbayFees. The consignor payout formula itself (AmountOwedOrPaid) is untouched by this —
    /// it still uses ShippingCollected, since that's a separate, previously-confirmed formula
    /// that pays a third party.</summary>
    public decimal ConsignmentProfitAfterFees { get; set; }
    public int ConsignmentSaleCount { get; set; }

    public List<ConsignorStatsRowViewModel> ConsignorStats { get; set; } = [];
}

public class ConsignorStatsRowViewModel
{
    public Guid ConsignorId { get; set; }
    public string ConsignorName { get; set; } = string.Empty;
    /// <summary>The consignor's current default split — shown for context; individual sales may
    /// have used a different split % snapshotted at classification time.</summary>
    public decimal CurrentDefaultSplitPercent { get; set; }
    public decimal TotalSold { get; set; }
    /// <summary>Split% x (SalePriceGross - ShippingCollected - EbayFee), summed per sale — the
    /// confirmed payout formula.</summary>
    public decimal AmountOwedOrPaid { get; set; }
    public int SaleCount { get; set; }
}

/// <summary>Per-sale detail list backing /Stats/Sales — either every Owned sale
/// (ConsignorId null) or every sale for one consignor.</summary>
public class StatsSalesViewModel
{
    public Guid? ConsignorId { get; set; }
    public string? ConsignorName { get; set; }
    public List<SaleRowViewModel> Rows { get; set; } = [];
}

public class SaleRowViewModel
{
    public string? ImageUrl { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime SoldAtUtc { get; set; }
    public decimal SalePriceGross { get; set; }
    /// <summary>Null means eBay hasn't posted the fee yet, not that the fee is zero — see the
    /// Stats index page's missing-fee warning.</summary>
    public decimal? EbayFee { get; set; }
    /// <summary>Actual shipping label cost — null means it hasn't posted yet or hasn't been
    /// entered manually (see the Missing Shipping Cost Needs Attention section). A null value
    /// is treated as $0 in Profit below, so Profit may be overstated until it's filled in.</summary>
    public decimal? ShippingLabelCost { get; set; }
    /// <summary>Store's profit: SalePrice - Cost (PurchaseCost or consignor payout) +
    /// ShippingCollected - ShippingLabelCost - eBayFee. Treats a still-missing eBay fee or
    /// shipping cost as $0, same caveat as the rest of this page.</summary>
    public decimal Profit { get; set; }
}
