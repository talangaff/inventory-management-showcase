using Microsoft.EntityFrameworkCore;
using inventory_management.Data;

namespace inventory_management.Services;

// Shared bulk-repricing logic, used by both the Bulk Reprice preview (InventoryController.BulkReprice)
// and the per-item apply endpoint (InventoryController.BulkRepriceItem) so the price shown in the
// preview and the price actually sent to eBay always come from the same rule.
public static class RepriceRules
{
    public const int DefaultDays = 30;
    public const decimal DefaultAmount = 10m;
    public const string ModePercent = "percent";
    public const string ModeDollar = "dollar";

    // Written to PriceReductionHistory.ChangeType for every bulk change. A history row is what
    // moves an item's "last repriced" date forward, so a repriced item drops off the next
    // preview on its own — same reset behavior as AttentionRules.
    public const string ChangeType = "Bulk";

    // Active listings whose last reprice — the newest PriceReductionHistory row of any type
    // (Manual, External, Bulk, legacy AutoReduction), or ListedAtUtc if it has never been
    // repriced — is `days` or more in the past. Sales are deliberately NOT counted here (unlike
    // AttentionRules): this is "not repriced in N days," not "stale."
    public static async Task<List<RepriceCandidate>> GetCandidatesAsync(
        ApplicationDbContext dbContext, string username, int days, DateTime nowUtc)
    {
        var cutoff = nowUtc.AddDays(-days);

        var listings = await dbContext.InventoryListings
            .AsNoTracking()
            .Where(x => x.Username == username && x.IsActive && x.CurrentPrice != null)
            .Select(x => new { x.Id, x.ItemId, x.Title, x.CoverPhotoUrl, x.CurrentPrice, x.CurrencyId, x.ListedAtUtc })
            .ToListAsync();

        if (listings.Count == 0)
        {
            return [];
        }

        var listingIds = listings.Select(x => x.Id).ToList();
        var lastRepriceByListingId = await dbContext.PriceReductionHistories
            .AsNoTracking()
            .Where(x => x.Username == username && listingIds.Contains(x.ListingId))
            .GroupBy(x => x.ListingId)
            .Select(g => new { ListingId = g.Key, LastChange = g.Max(h => h.ReducedAtUtc) })
            .ToDictionaryAsync(x => x.ListingId, x => x.LastChange);

        var result = new List<RepriceCandidate>();
        foreach (var listing in listings)
        {
            var lastRepriced = listing.ListedAtUtc;
            var everRepriced = lastRepriceByListingId.TryGetValue(listing.Id, out var lastChange);
            if (everRepriced && lastChange > lastRepriced)
            {
                lastRepriced = lastChange;
            }

            if (lastRepriced <= cutoff)
            {
                result.Add(new RepriceCandidate
                {
                    ListingId = listing.Id,
                    ItemId = listing.ItemId,
                    Title = listing.Title,
                    CoverPhotoUrl = listing.CoverPhotoUrl,
                    CurrentPrice = listing.CurrentPrice!.Value,
                    CurrencyId = listing.CurrencyId,
                    LastRepricedUtc = lastRepriced,
                    EverRepriced = everRepriced
                });
            }
        }

        return result;
    }

    // Returns null when the rule inputs themselves are invalid (shown as a form error).
    public static string? Validate(int days, string mode, decimal amount, decimal? minPrice)
    {
        if (days < 0) return "Days must be 0 or more.";
        if (mode != ModePercent && mode != ModeDollar) return "Choose percent or dollar amount.";
        if (amount <= 0) return "Decrease amount must be greater than zero.";
        if (mode == ModePercent && amount >= 100) return "Percent decrease must be less than 100.";
        if (minPrice is < 0) return "Minimum price can't be negative.";
        return null;
    }

    // The single pricing rule:
    //  - current price already AT or BELOW the minimum → left untouched (no change at all)
    //  - reduced price below the minimum → set to the minimum
    //  - otherwise → the reduced price, rounded to the cent
    public static RepriceResult Compute(decimal currentPrice, string mode, decimal amount, decimal? minPrice)
    {
        if (minPrice.HasValue && currentPrice <= minPrice.Value)
        {
            return RepriceResult.Skip(currentPrice < minPrice.Value
                ? "Already below minimum"
                : "Already at minimum");
        }

        var reduced = mode == ModePercent
            ? currentPrice * (1 - amount / 100m)
            : currentPrice - amount;
        reduced = Math.Round(reduced, 2, MidpointRounding.AwayFromZero);

        if (minPrice.HasValue && reduced < minPrice.Value)
        {
            return new RepriceResult { NewPrice = minPrice.Value, ClampedToMinimum = true };
        }

        if (reduced <= 0)
        {
            return RepriceResult.Skip("Would drop to $0 or less");
        }

        if (reduced >= currentPrice)
        {
            return RepriceResult.Skip("No change after rounding");
        }

        return new RepriceResult { NewPrice = reduced };
    }
}

public class RepriceCandidate
{
    public Guid ListingId { get; set; }
    public string ItemId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? CoverPhotoUrl { get; set; }
    public decimal CurrentPrice { get; set; }
    public string? CurrencyId { get; set; }
    public DateTime LastRepricedUtc { get; set; }
    public bool EverRepriced { get; set; }
}

public class RepriceResult
{
    // Null when the item is skipped (see SkipReason).
    public decimal? NewPrice { get; set; }
    public bool ClampedToMinimum { get; set; }
    public string? SkipReason { get; set; }

    public static RepriceResult Skip(string reason) => new() { SkipReason = reason };
}
