using Microsoft.EntityFrameworkCore;
using inventory_management.Data;
using inventory_management.Models;

namespace inventory_management.Services;

/// <summary>
/// The counts shown on list-page headers, shared with the dashboard so the two can
/// never disagree. Each one is the same filter its page uses — change them together.
/// </summary>
public static class InventoryCounts
{
    /// <summary>Recent Orders / Ready to Ship only look back this far.</summary>
    public static readonly TimeSpan OrderWindow = TimeSpan.FromDays(90);

    /// <summary>Active Inventory, Missing Location, or Unprinted Labels header count.</summary>
    public static async Task<int> CountListingsAsync(
        ApplicationDbContext db, string username, bool isMissingDataPage, bool isUnprintedPage = false)
    {
        IQueryable<InventoryListing> query = db.InventoryListings
            .AsNoTracking()
            .Where(x => x.Username == username && x.IsActive);

        if (isUnprintedPage)
        {
            query = query.Where(x => db.InventoryItemUnits
                .Any(u => u.Username == username && u.ListingId == x.Id && u.LabelPrintedAtUtc == null));
        }
        else if (isMissingDataPage)
        {
            var locatedItemIds = await db.InventoryItemLocations
                .AsNoTracking()
                .Where(x => x.Username == username)
                .Select(x => x.ItemId)
                .ToListAsync();

            var boxedItemIds = await db.InventoryItemBoxAssignments
                .AsNoTracking()
                .Where(x => x.Username == username)
                .Select(x => x.ItemId)
                .ToListAsync();

            var locationSet = locatedItemIds.Concat(boxedItemIds).ToHashSet(StringComparer.Ordinal);
            query = query.Where(x => !locationSet.Contains(x.ItemId));
        }

        return await query.CountAsync();
    }

    /// <summary>Ready to Ship: order lines in the window with no tracking yet
    /// (OrdersController.BuildOrdersPageAsync with onlyReadyToShip).</summary>
    public static Task<int> CountReadyToShipAsync(ApplicationDbContext db, string username, DateTime nowUtc)
    {
        var cutoff = nowUtc - OrderWindow;
        return db.RecentOrders
            .AsNoTracking()
            .CountAsync(x => x.Username == username
                && !x.HasTracking
                && (x.OrderCreatedAtUtc == null || x.OrderCreatedAtUtc >= cutoff));
    }
}
