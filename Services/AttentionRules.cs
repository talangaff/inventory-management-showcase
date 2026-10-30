using Microsoft.EntityFrameworkCore;
using inventory_management.Data;
using inventory_management.Models;

namespace inventory_management.Services;

// Shared "Needs Attention" eligibility logic, used by both InventoryController.NeedsAttention
// (full list) and NeedsAttentionBadgeViewComponent (count only) so the two can never disagree.
public static class AttentionRules
{
    public static readonly TimeSpan StaleThreshold = TimeSpan.FromDays(30);

    // An active, non-ignored listing needs attention once its "reference date" — the most
    // recent of ListedAtUtc, last sale date, and last price-change date — is StaleThreshold or
    // more in the past. A price change (manual, quick-update, or external via eBay sync) or a
    // sale both push the reference date forward, so the item naturally drops back off once
    // either happens — no separate "remove from page" logic is needed anywhere that calls this.
    public static async Task<List<AttentionCandidate>> GetStaleListingsAsync(
        ApplicationDbContext dbContext,
        string username,
        DateTime nowUtc)
    {
        var cutoff = nowUtc - StaleThreshold;

        var activeListings = await dbContext.InventoryListings
            .AsNoTracking()
            .Where(x => x.Username == username
                     && x.IsActive
                     && !x.AttentionIgnoredPermanently
                     && (x.AttentionIgnoredUntilUtc == null || x.AttentionIgnoredUntilUtc <= nowUtc))
            .Select(x => new { x.Id, x.ItemId, x.Title, x.CoverPhotoUrl, x.CurrentPrice, x.CurrencyId, x.ListedAtUtc, x.OwnershipType, x.PurchaseCost })
            .ToListAsync();

        if (activeListings.Count == 0)
        {
            return [];
        }

        var listingIds = activeListings.Select(x => x.Id).ToList();
        var itemIds = activeListings.Select(x => x.ItemId).ToList();

        var lastSaleByItemId = await dbContext.RecentOrders
            .AsNoTracking()
            .Where(x => x.Username == username && itemIds.Contains(x.ItemId) && x.OrderCreatedAtUtc != null)
            .GroupBy(x => x.ItemId)
            .Select(g => new { ItemId = g.Key, LastSale = g.Max(o => o.OrderCreatedAtUtc!.Value) })
            .ToDictionaryAsync(x => x.ItemId, x => x.LastSale);

        var lastPriceChangeByListingId = await dbContext.PriceReductionHistories
            .AsNoTracking()
            .Where(x => x.Username == username && listingIds.Contains(x.ListingId))
            .GroupBy(x => x.ListingId)
            .Select(g => new { ListingId = g.Key, LastChange = g.Max(h => h.ReducedAtUtc) })
            .ToDictionaryAsync(x => x.ListingId, x => x.LastChange);

        var result = new List<AttentionCandidate>();
        foreach (var listing in activeListings)
        {
            var reference = listing.ListedAtUtc;
            if (lastSaleByItemId.TryGetValue(listing.ItemId, out var lastSale) && lastSale > reference)
            {
                reference = lastSale;
            }
            if (lastPriceChangeByListingId.TryGetValue(listing.Id, out var lastChange) && lastChange > reference)
            {
                reference = lastChange;
            }

            if (reference <= cutoff)
            {
                result.Add(new AttentionCandidate
                {
                    ListingId = listing.Id,
                    ItemId = listing.ItemId,
                    Title = listing.Title,
                    CoverPhotoUrl = listing.CoverPhotoUrl,
                    CurrentPrice = listing.CurrentPrice,
                    CurrencyId = listing.CurrencyId,
                    ReferenceDateUtc = reference,
                    ListedAtUtc = listing.ListedAtUtc,
                    OwnershipType = listing.OwnershipType,
                    PurchaseCost = listing.PurchaseCost
                });
            }
        }

        return result;
    }

    public static async Task<int> CountStaleListingsAsync(
        ApplicationDbContext dbContext, string username, DateTime nowUtc)
        => (await GetStaleListingsAsync(dbContext, username, nowUtc)).Count;

    // An active listing whose eBay quantity has sold out (zero unsold Listed units left) while
    // it still has backstock sitting in storage — easy to lose track of since it's not visible
    // as "for sale" anywhere anymore. Independent of the staleness check above (a sold-out item
    // is, by definition, recently sold, so it would never show up as stale on its own).
    public static async Task<List<SoldOutBackstockCandidate>> GetSoldOutWithBackstockAsync(
        ApplicationDbContext dbContext, string username)
    {
        var activeListings = await dbContext.InventoryListings
            .AsNoTracking()
            .Where(x => x.Username == username && x.IsActive)
            .Select(x => new { x.Id, x.ItemId, x.Title, x.CoverPhotoUrl })
            .ToListAsync();

        if (activeListings.Count == 0)
        {
            return [];
        }

        var listingIds = activeListings.Select(x => x.Id).ToList();
        var units = await dbContext.InventoryItemUnits
            .AsNoTracking()
            .Where(x => x.Username == username && listingIds.Contains(x.ListingId) && !x.IsSold)
            .ToListAsync();
        var unitsByListingId = units.GroupBy(x => x.ListingId).ToDictionary(g => g.Key, g => g.ToList());

        var result = new List<SoldOutBackstockCandidate>();
        foreach (var listing in activeListings)
        {
            var listingUnits = unitsByListingId.GetValueOrDefault(listing.Id, []);
            var unsoldListedCount = listingUnits.Count(u => u.Status == InventoryItemUnitStatus.Listed);
            var backstockCount = listingUnits.Count(u => u.Status == InventoryItemUnitStatus.Backstock);
            if (unsoldListedCount == 0 && backstockCount > 0)
            {
                result.Add(new SoldOutBackstockCandidate
                {
                    ItemId = listing.ItemId,
                    Title = listing.Title,
                    CoverPhotoUrl = listing.CoverPhotoUrl,
                    BackstockCount = backstockCount
                });
            }
        }

        return result;
    }

    // A listing with a genuinely ambiguous pending sale (more than one unsold Listed unit could
    // be the one that sold — eBay never says which). Independent of the staleness check above:
    // a just-sold item is by definition not stale. Auto-resolves any item that's no longer
    // actually ambiguous (down to one candidate) as a side effect via SaleResolutionService, so
    // this only ever surfaces items that genuinely need the owner to pick.
    public static async Task<List<UnitResolutionCandidate>> GetItemsNeedingUnitResolutionAsync(
        ApplicationDbContext dbContext, string username)
    {
        var recentItemIds = await dbContext.RecentOrders
            .AsNoTracking()
            .Where(x => x.Username == username)
            .Select(x => x.ItemId)
            .Distinct()
            .ToListAsync();

        var ambiguousByItemId = await SaleResolutionService.AutoResolveAndGetAmbiguousAsync(
            dbContext, username, recentItemIds);
        if (ambiguousByItemId.Count == 0)
        {
            return [];
        }

        var listings = await dbContext.InventoryListings
            .AsNoTracking()
            .Where(x => x.Username == username && ambiguousByItemId.Keys.Contains(x.ItemId))
            .Select(x => new { x.ItemId, x.Title, x.CoverPhotoUrl })
            .ToListAsync();
        var listingByItemId = listings.ToDictionary(x => x.ItemId, StringComparer.Ordinal);

        var result = new List<UnitResolutionCandidate>();
        foreach (var (itemId, units) in ambiguousByItemId)
        {
            if (!listingByItemId.TryGetValue(itemId, out var listing))
            {
                continue;
            }

            result.Add(new UnitResolutionCandidate
            {
                ItemId = itemId,
                Title = listing.Title,
                CoverPhotoUrl = listing.CoverPhotoUrl,
                Units = units
            });
        }

        return result;
    }

    // An order that's shipped (HasTracking) but still has no actual shipping cost — the Finance
    // API lookup in OrdersController.SyncOrdersFromEbayAsync hasn't found a matching
    // SHIPPING_LABEL transaction for it yet (not settled, shipped without an eBay-tracked
    // label, etc.). Surfaced here so it's not silently treated as $0 forever — the owner can enter
    // the real cost by hand, using the "View on eBay" link to look it up.
    public static async Task<List<MissingShippingCostCandidate>> GetOrdersMissingShippingCostAsync(
        ApplicationDbContext dbContext, string username)
    {
        var orders = await dbContext.RecentOrders
            .AsNoTracking()
            .Where(x => x.Username == username && x.HasTracking && x.ShippingLabelCost == null)
            .OrderByDescending(x => x.OrderCreatedAtUtc)
            .ToListAsync();
        if (orders.Count == 0)
        {
            return [];
        }

        var itemIds = orders.Select(x => x.ItemId).Distinct().ToList();
        var photoByItemId = await dbContext.InventoryListings
            .AsNoTracking()
            .Where(x => x.Username == username && itemIds.Contains(x.ItemId))
            .ToDictionaryAsync(x => x.ItemId, x => x.CoverPhotoUrl, StringComparer.Ordinal);

        return orders
            .Select(x => new MissingShippingCostCandidate
            {
                OrderId = x.OrderId,
                ItemId = x.ItemId,
                ItemName = x.ItemName,
                ItemPhotoUrl = photoByItemId.GetValueOrDefault(x.ItemId) ?? x.ItemPhotoUrl,
                BuyerName = x.BuyerName,
                OrderCreatedAtUtc = x.OrderCreatedAtUtc,
                EbayOrderUrl = BuildEbayOrderUrl(x.OrderId)
            })
            .ToList();
    }

    public static async Task<int> CountOrdersMissingShippingCostAsync(
        ApplicationDbContext dbContext, string username)
        => (await GetOrdersMissingShippingCostAsync(dbContext, username)).Count;

    /// <summary>eBay's per-order shipping page — lets the owner view the order and buy/update its
    /// shipping label directly on eBay, matching how shipping is actually handled now that this
    /// app doesn't buy labels itself.</summary>
    public static string BuildEbayOrderUrl(string orderId)
    {
        return string.IsNullOrWhiteSpace(orderId)
            ? "https://www.ebay.com/ship/sold"
            : $"https://www.ebay.com/ship/single/{Uri.EscapeDataString(orderId)}";
    }

    /// <summary>The single source of truth for "how many items need attention, total" — every
    /// category on the Needs Attention page (stale, sold-out-with-backstock, needs-unit-
    /// resolved, unclassified, missing-shipping-cost, and any future category) must be added
    /// here so the nav badge (NeedsAttentionBadgeViewComponent) and the page's own header count
    /// can never drift apart or miss a category. When adding a new Needs Attention category,
    /// add its count here.</summary>
    public static async Task<int> GetTotalNeedsAttentionCountAsync(
        ApplicationDbContext dbContext, string username, DateTime nowUtc)
    {
        var stale = await CountStaleListingsAsync(dbContext, username, nowUtc);
        var soldOutWithBackstock = (await GetSoldOutWithBackstockAsync(dbContext, username)).Count;
        var needsUnitResolution = (await GetItemsNeedingUnitResolutionAsync(dbContext, username)).Count;
        var unclassified = (await OwnershipRules.GetUnclassifiedActiveListingsAsync(dbContext, username)).Count;
        var missingShippingCost = await CountOrdersMissingShippingCostAsync(dbContext, username);
        return stale + soldOutWithBackstock + needsUnitResolution + unclassified + missingShippingCost;
    }
}

public class AttentionCandidate
{
    public Guid ListingId { get; set; }
    public string ItemId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? CoverPhotoUrl { get; set; }
    public decimal? CurrentPrice { get; set; }
    public string? CurrencyId { get; set; }
    public DateTime ReferenceDateUtc { get; set; }
    public DateTime ListedAtUtc { get; set; }
    public string? OwnershipType { get; set; }
    public decimal? PurchaseCost { get; set; }
}

public class SoldOutBackstockCandidate
{
    public string ItemId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? CoverPhotoUrl { get; set; }
    public int BackstockCount { get; set; }
}

public class UnitResolutionCandidate
{
    public string ItemId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? CoverPhotoUrl { get; set; }
    public List<InventoryItemUnit> Units { get; set; } = [];
}

public class MissingShippingCostCandidate
{
    public string OrderId { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string? ItemPhotoUrl { get; set; }
    public string BuyerName { get; set; } = string.Empty;
    public DateTime? OrderCreatedAtUtc { get; set; }
    public string EbayOrderUrl { get; set; } = string.Empty;
}
