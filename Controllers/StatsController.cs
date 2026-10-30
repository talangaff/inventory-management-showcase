using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using inventory_management.Data;
using inventory_management.Models;

namespace inventory_management.Controllers;

// Reads only from SaleRecord — see Services/SaleResolutionService.cs::CreateSaleRecordsAsync,
// which only ever creates rows for classified items sold on/after
// Services/OwnershipRules.cs::StatsCutoffUtc (2026-08-01). No extra date filtering is needed
// here for that rule; it's already true by construction.
[Authorize]
public class StatsController : Controller
{
    private readonly ApplicationDbContext _dbContext;

    public StatsController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IActionResult> Index()
    {
        var username = User.Identity?.Name ?? string.Empty;

        var records = await _dbContext.SaleRecords
            .AsNoTracking()
            .Where(x => x.Username == username)
            .ToListAsync();

        var model = new StatsIndexViewModel
        {
            HasAnySaleRecords = records.Count > 0
        };

        var ownedRecords = records.Where(x => x.OwnershipType == OwnershipTypes.Owned).ToList();
        model.OwnedSaleCount = ownedRecords.Count;
        model.OwnedGrossSales = ownedRecords.Sum(x => x.SalePriceGross ?? 0);
        model.OwnedTotalCost = ownedRecords.Sum(x => x.PurchaseCost ?? 0);
        model.OwnedNetEarnings = model.OwnedGrossSales - model.OwnedTotalCost;
        model.OwnedShippingCollected = ownedRecords.Sum(x => x.ShippingCollected ?? 0);
        model.OwnedTaxCollected = ownedRecords.Sum(x => x.TaxCollected ?? 0);
        model.OwnedEbayFees = ownedRecords.Sum(x => x.EbayFee ?? 0);
        model.OwnedShippingLabelCost = ownedRecords.Sum(x => x.ShippingLabelCost ?? 0);
        // Profit = Net + ShippingCollected - ShippingLabelCost - EbayFees. ShippingCollected is
        // added back (it's revenue, not an expense) and the real label cost is subtracted
        // instead — replaces the old proxy of just subtracting ShippingCollected. Confirmed
        // with the owner once actual shipping cost became available via the Finance API.
        model.OwnedProfitAfterFees = model.OwnedNetEarnings + model.OwnedShippingCollected - model.OwnedShippingLabelCost - model.OwnedEbayFees;

        model.AnyMissingEbayFee = records.Any(x => x.EbayFee is null);
        // Not every sale ships (some are local pickup, etc.), so this only counts sales that
        // have a shipping cost from eBay's buyer-paid field but no actual label cost yet —
        // matches Services/AttentionRules.GetOrdersMissingShippingCostAsync's HasTracking gate,
        // approximated here via "buyer paid something for shipping" since SaleRecord doesn't
        // carry HasTracking directly.
        model.AnyMissingShippingLabelCost = records.Any(x => x.ShippingLabelCost is null && (x.ShippingCollected ?? 0) > 0);

        var consignmentRecords = records.Where(x => x.OwnershipType == OwnershipTypes.Consignment).ToList();
        var consignorIds = consignmentRecords
            .Where(x => x.ConsignorId.HasValue)
            .Select(x => x.ConsignorId!.Value)
            .Distinct()
            .ToList();
        var currentDefaultSplitByConsignorId = consignorIds.Count > 0
            ? await _dbContext.Consignors
                .AsNoTracking()
                .Where(x => x.Username == username && consignorIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.DefaultSplitPercent)
            : new Dictionary<Guid, decimal>();

        model.ConsignorStats = consignmentRecords
            .Where(x => x.ConsignorId.HasValue)
            .GroupBy(x => x.ConsignorId!.Value)
            .Select(g =>
            {
                var totalSold = g.Sum(x => x.SalePriceGross ?? 0);
                // Confirmed payout formula: split% x (sale price - shipping - eBay fee). Tax is
                // pass-through (eBay remits it) and excluded from both sides of this.
                var owed = g.Sum(x =>
                {
                    var split = x.ConsignorSplitPercent ?? 0;
                    var basis = (x.SalePriceGross ?? 0) - (x.ShippingCollected ?? 0) - (x.EbayFee ?? 0);
                    return basis * (split / 100m);
                });
                return new ConsignorStatsRowViewModel
                {
                    ConsignorId = g.Key,
                    ConsignorName = g.First().ConsignorName ?? "Unknown consignor",
                    CurrentDefaultSplitPercent = currentDefaultSplitByConsignorId.GetValueOrDefault(g.Key),
                    TotalSold = totalSold,
                    AmountOwedOrPaid = owed,
                    SaleCount = g.Count()
                };
            })
            .OrderBy(x => x.ConsignorName)
            .ToList();

        // Consignment summary card mirrors Owned above — built from the same per-consignor rows
        // (ConsignorId.HasValue only) so the two totals stay consistent with each other.
        var classifiedConsignmentRecords = consignmentRecords.Where(x => x.ConsignorId.HasValue).ToList();
        model.ConsignmentSaleCount = classifiedConsignmentRecords.Count;
        model.ConsignmentGrossSales = classifiedConsignmentRecords.Sum(x => x.SalePriceGross ?? 0);
        model.ConsignmentShippingCollected = classifiedConsignmentRecords.Sum(x => x.ShippingCollected ?? 0);
        model.ConsignmentTaxCollected = classifiedConsignmentRecords.Sum(x => x.TaxCollected ?? 0);
        model.ConsignmentEbayFees = classifiedConsignmentRecords.Sum(x => x.EbayFee ?? 0);
        model.ConsignmentShippingLabelCost = classifiedConsignmentRecords.Sum(x => x.ShippingLabelCost ?? 0);
        model.ConsignmentAmountOwed = model.ConsignorStats.Sum(x => x.AmountOwedOrPaid);
        model.ConsignmentNetEarnings = model.ConsignmentGrossSales - model.ConsignmentAmountOwed;
        // Same formula change as Owned above — only the store's own "Profit after fees" number.
        // The consignor payout formula (AmountOwedOrPaid above) is untouched: that's a separate,
        // previously-confirmed formula that pays a third party, not just an internal number.
        model.ConsignmentProfitAfterFees = model.ConsignmentNetEarnings + model.ConsignmentShippingCollected - model.ConsignmentShippingLabelCost - model.ConsignmentEbayFees;

        return View(model);
    }

    // Per-sale detail list: every Owned sale (consignorId null) or every sale for one
    // consignor. Image comes from the listing's current cover photo, not a per-sale snapshot —
    // SaleRecord doesn't store one and the listing's photo rarely changes after a sale anyway.
    public async Task<IActionResult> Sales(Guid? consignorId)
    {
        var username = User.Identity?.Name ?? string.Empty;

        var query = _dbContext.SaleRecords.AsNoTracking().Where(x => x.Username == username);
        query = consignorId.HasValue
            ? query.Where(x => x.OwnershipType == OwnershipTypes.Consignment && x.ConsignorId == consignorId.Value)
            : query.Where(x => x.OwnershipType == OwnershipTypes.Owned);

        var records = await query.OrderByDescending(x => x.SoldAtUtc).ToListAsync();

        var listingIds = records.Select(x => x.ListingId).Distinct().ToList();
        var photoByListingId = listingIds.Count > 0
            ? await _dbContext.InventoryListings
                .AsNoTracking()
                .Where(x => x.Username == username && listingIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.CoverPhotoUrl)
            : new Dictionary<Guid, string?>();

        string? consignorName = records.FirstOrDefault()?.ConsignorName;
        if (consignorId.HasValue && consignorName is null)
        {
            consignorName = await _dbContext.Consignors
                .AsNoTracking()
                .Where(x => x.Username == username && x.Id == consignorId.Value)
                .Select(x => x.Name)
                .FirstOrDefaultAsync();
        }

        var model = new StatsSalesViewModel
        {
            ConsignorId = consignorId,
            ConsignorName = consignorName,
            Rows = records.Select(r =>
            {
                // Same "cost" convention as the summary cards: PurchaseCost for Owned, the
                // consignor's payout for Consignment — both represent what leaves the store.
                // The consignor payout basis itself is untouched (still ShippingCollected, per
                // the previously-confirmed payout formula) — only this row's own Profit column
                // uses the real ShippingLabelCost, same 2026-08-20 formula change as the
                // Stats index summary cards.
                var cost = consignorId.HasValue
                    ? ((r.SalePriceGross ?? 0) - (r.ShippingCollected ?? 0) - (r.EbayFee ?? 0)) * ((r.ConsignorSplitPercent ?? 0) / 100m)
                    : r.PurchaseCost ?? 0;
                var profit = (r.SalePriceGross ?? 0) - cost + (r.ShippingCollected ?? 0) - (r.ShippingLabelCost ?? 0) - (r.EbayFee ?? 0);
                return new SaleRowViewModel
                {
                    ImageUrl = photoByListingId.GetValueOrDefault(r.ListingId),
                    Title = r.Title,
                    SoldAtUtc = r.SoldAtUtc,
                    SalePriceGross = r.SalePriceGross ?? 0,
                    EbayFee = r.EbayFee,
                    ShippingLabelCost = r.ShippingLabelCost,
                    Profit = profit
                };
            }).ToList()
        };

        return View(model);
    }
}
