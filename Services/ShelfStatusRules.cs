using Microsoft.EntityFrameworkCore;
using inventory_management.Data;
using inventory_management.Models;

namespace inventory_management.Services;

public static class ShelfStatusRules
{
    // A shelf slot with at least one unsold item (directly on the slot, or in a box on
    // that slot) is always active inventory — it can never be marked Needs Sorted.
    public static async Task<bool> HasUnsoldItemsOnSlotAsync(
        ApplicationDbContext dbContext,
        string username,
        string shelfUnitIdentifier,
        int shelfNumber)
    {
        var hasDirectItems = await dbContext.InventoryItemLocations
            .AsNoTracking()
            .AnyAsync(x => x.Username == username
                && x.ShelfUnitIdentifier == shelfUnitIdentifier
                && x.ShelfNumber == shelfNumber
                && !x.IsSold);
        if (hasDirectItems)
        {
            return true;
        }

        var boxIdsOnSlot = await dbContext.Boxes
            .AsNoTracking()
            .Where(b => b.Username == username
                && b.ShelfUnitIdentifier == shelfUnitIdentifier
                && b.ShelfNumber == shelfNumber)
            .Select(b => b.Id)
            .ToListAsync();

        if (boxIdsOnSlot.Count == 0)
        {
            return false;
        }

        return await dbContext.InventoryItemBoxAssignments
            .AsNoTracking()
            .AnyAsync(x => x.Username == username && boxIdsOnSlot.Contains(x.BoxId) && !x.IsSold);
    }

    public static async Task<bool> HasUnsoldItemsAnywhereOnUnitAsync(
        ApplicationDbContext dbContext,
        string username,
        string shelfUnitIdentifier,
        int shelfCount)
    {
        for (var shelfNumber = 1; shelfNumber <= shelfCount; shelfNumber++)
        {
            if (await HasUnsoldItemsOnSlotAsync(dbContext, username, shelfUnitIdentifier, shelfNumber))
            {
                return true;
            }
        }

        return false;
    }

    public static async Task<ShelfStatus> ResolveUnitStatusAsync(
        ApplicationDbContext dbContext,
        string username,
        string shelfUnitIdentifier,
        int shelfCount,
        ShelfStatus requestedStatus)
    {
        // LOCKED INVARIANT: a shelf that isn't Active Inventory must have no items on it — the
        // UI (Shelf/Details, Shelf/Index) relies on this to skip rendering an items view at all
        // for non-active shelves. Guards every non-Active target status, not just NeedsSorted,
        // so this holds for Unlisted too.
        if (requestedStatus != ShelfStatus.ActiveInventory
            && await HasUnsoldItemsAnywhereOnUnitAsync(dbContext, username, shelfUnitIdentifier, shelfCount))
        {
            return ShelfStatus.ActiveInventory;
        }

        return requestedStatus;
    }

    // Coerces a requested slot status to ActiveInventory when the slot still holds unsold
    // items, so no non-Active status can ever be applied to a slot with active inventory on it.
    // See the LOCKED INVARIANT note on ResolveUnitStatusAsync above.
    public static async Task<ShelfStatus> ResolveSlotStatusAsync(
        ApplicationDbContext dbContext,
        string username,
        string shelfUnitIdentifier,
        int shelfNumber,
        ShelfStatus requestedStatus)
    {
        if (requestedStatus != ShelfStatus.ActiveInventory
            && await HasUnsoldItemsOnSlotAsync(dbContext, username, shelfUnitIdentifier, shelfNumber))
        {
            return ShelfStatus.ActiveInventory;
        }

        return requestedStatus;
    }
}
