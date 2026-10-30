using Microsoft.EntityFrameworkCore;
using inventory_management.Data;
using inventory_management.Models;

namespace inventory_management.Services;

// Ownership classification (Owned vs. Consignment), margin calculation, and the sales-stats
// cutoff date — shared by Needs Attention, the item Details page, and the Stats page so none of
// them can drift on the rules. Added 2026-08-14.
public static class OwnershipRules
{
    /// <summary>Hard cutoff: sales before this date are disregarded entirely — no forced
    /// classification, never shown in stats, per explicit instruction that this feature is
    /// forward-only. Never change this to backfill old data.</summary>
    public static readonly DateTime StatsCutoffUtc = new(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Active listings with no OwnershipType set yet — the "Needs classification"
    /// category on Needs Attention. Unlike the other categories on that page, this one has no
    /// snooze/ignore mechanism: it's a data-quality gate, not a nudge, so it doesn't disappear
    /// until actually classified.</summary>
    public static async Task<List<UnclassifiedCandidate>> GetUnclassifiedActiveListingsAsync(
        ApplicationDbContext dbContext, string username)
    {
        return await dbContext.InventoryListings
            .AsNoTracking()
            .Where(x => x.Username == username && x.IsActive && x.OwnershipType == null)
            .OrderBy(x => x.Title)
            .Select(x => new UnclassifiedCandidate
            {
                ListingId = x.Id,
                ItemId = x.ItemId,
                Title = x.Title,
                CoverPhotoUrl = x.CoverPhotoUrl,
                CurrentPrice = x.CurrentPrice,
                CurrencyId = x.CurrencyId
            })
            .ToListAsync();
    }

    /// <summary>The consignor from whichever Consignment listing was classified most recently —
    /// backs the Needs Attention one-click quick button (e.g. "Steve Consignment"). Null if no
    /// listing has ever been classified as Consignment.</summary>
    public static async Task<Consignor?> GetMostRecentConsignorAsync(ApplicationDbContext dbContext, string username)
    {
        var mostRecentConsignorId = await dbContext.InventoryListings
            .AsNoTracking()
            .Where(x => x.Username == username
                     && x.OwnershipType == OwnershipTypes.Consignment
                     && x.ConsignorId != null)
            .OrderByDescending(x => x.ClassifiedAtUtc)
            .Select(x => x.ConsignorId!.Value)
            .FirstOrDefaultAsync();

        if (mostRecentConsignorId == Guid.Empty)
        {
            return null;
        }

        return await dbContext.Consignors
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Username == username && x.Id == mostRecentConsignorId);
    }

    /// <summary>Margin thresholds shared by the Details page and Needs Attention's quick price
    /// editor: &gt;=25% green, 0-25% yellow, &lt;=0% red. Mirrored in wwwroot/js for the live
    /// keystroke-driven readout — keep both in sync if these thresholds ever change.</summary>
    public static (decimal? MarginPercent, string ColorClass) ComputeMargin(decimal salePrice, decimal? cost)
    {
        if (!cost.HasValue || salePrice <= 0)
        {
            return (null, "text-muted");
        }

        var margin = (salePrice - cost.Value) / salePrice * 100m;
        var colorClass = margin switch
        {
            >= 25m => "text-success",
            > 0m => "text-warning",
            _ => "text-danger"
        };
        return (margin, colorClass);
    }
}

public class UnclassifiedCandidate
{
    public Guid ListingId { get; set; }
    public string ItemId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? CoverPhotoUrl { get; set; }
    public decimal? CurrentPrice { get; set; }
    public string? CurrencyId { get; set; }
}
