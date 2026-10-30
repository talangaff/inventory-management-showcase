using Microsoft.EntityFrameworkCore;
using inventory_management.Data;
using inventory_management.Models;

namespace inventory_management.Services;

// Shared "which physical unit sold" resolution logic. eBay's order data never says which unit
// of a multi-unit listing sold, so a sale sits "pending" (order recorded, but no specific
// InventoryItemUnit marked IsSold) until it's resolved — automatically when unambiguous, or
// manually picked otherwise. Used by OrdersController (Recent Orders / Ready to Ship pickers),
// InventoryController (item Details page picker + Needs Attention "needs unit resolved"
// section) so all three surfaces agree on what's pending and can't disagree.
public static class SaleResolutionService
{
    /// <summary>For each given ItemId, the currently unsold Listed units of its listing (not
    /// backstock, not already-sold), ordered by UnitIndex.</summary>
    public static async Task<Dictionary<string, List<InventoryItemUnit>>> GetUnsoldListedUnitsByItemIdAsync(
        ApplicationDbContext dbContext, string username, IEnumerable<string> itemIds)
    {
        var itemIdList = itemIds.Distinct(StringComparer.Ordinal).ToList();
        if (itemIdList.Count == 0)
        {
            return new Dictionary<string, List<InventoryItemUnit>>(StringComparer.Ordinal);
        }

        var listings = await dbContext.InventoryListings
            .AsNoTracking()
            .Where(x => x.Username == username && itemIdList.Contains(x.ItemId))
            .Select(x => new { x.Id, x.ItemId })
            .ToListAsync();
        var itemIdByListingId = listings.ToDictionary(x => x.Id, x => x.ItemId);
        var listingIds = listings.Select(x => x.Id).ToList();

        var candidateUnits = await dbContext.InventoryItemUnits
            .AsNoTracking()
            .Where(x => x.Username == username
                && listingIds.Contains(x.ListingId)
                && x.Status == InventoryItemUnitStatus.Listed
                && !x.IsSold)
            .ToListAsync();

        return candidateUnits
            .GroupBy(x => itemIdByListingId[x.ListingId])
            .ToDictionary(g => g.Key, g => g.OrderBy(u => u.UnitIndex).ToList(), StringComparer.Ordinal);
    }

    /// <summary>Marks the given units (and their location rows, wherever they exist) sold.
    /// Location is kept indefinitely — same pattern as everywhere else in this codebase — so
    /// Ready to Ship / item Details can still show where it shipped from.</summary>
    public static async Task MarkUnitsSoldAsync(ApplicationDbContext dbContext, string username, List<Guid> unitIds)
    {
        if (unitIds.Count == 0) return;

        await dbContext.InventoryItemUnits
            .Where(x => x.Username == username && unitIds.Contains(x.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsSold, true));
        await dbContext.InventoryItemLocations
            .Where(x => x.Username == username && x.UnitId != null && unitIds.Contains(x.UnitId!.Value))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsSold, true));
        await dbContext.InventoryItemBoxAssignments
            .Where(x => x.Username == username && x.UnitId != null && unitIds.Contains(x.UnitId!.Value))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsSold, true));
        await dbContext.InventoryItemStorageUnitAssignments
            .Where(x => x.Username == username && x.UnitId != null && unitIds.Contains(x.UnitId!.Value))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsSold, true));

        await CreateSaleRecordsAsync(dbContext, username, unitIds);
    }

    /// <summary>Per-unit location display, keyed by UnitId rather than collapsed to one entry
    /// per ItemId — needed once a listing can have more than one unit with independent
    /// locations.</summary>
    public static async Task<Dictionary<Guid, string>> BuildLocationDisplayByUnitIdAsync(
        ApplicationDbContext dbContext, string username)
    {
        var result = new Dictionary<Guid, string>();

        var shelfLocations = await dbContext.InventoryItemLocations
            .AsNoTracking()
            .Where(x => x.Username == username && x.UnitId != null)
            .Select(x => new { UnitId = x.UnitId!.Value, Display = $"{x.ShelfUnitIdentifier}-{x.ShelfNumber}" })
            .ToListAsync();
        foreach (var row in shelfLocations) result[row.UnitId] = row.Display;

        var boxRows = await dbContext.InventoryItemBoxAssignments
            .AsNoTracking()
            .Where(x => x.Username == username && x.UnitId != null)
            .Join(
                dbContext.Boxes.AsNoTracking(),
                assignment => assignment.BoxId,
                box => box.Id,
                (assignment, box) => new
                {
                    UnitId = assignment.UnitId!.Value,
                    box.ShelfUnitIdentifier,
                    box.ShelfNumber,
                    box.BoxIdentifier,
                    box.StorageUnitId,
                    box.IsTransitBox
                })
            .ToListAsync();

        var suIdsFromBoxes = boxRows.Where(x => x.StorageUnitId.HasValue).Select(x => x.StorageUnitId!.Value).Distinct().ToList();
        var suNamesFromBoxes = suIdsFromBoxes.Count > 0
            ? await dbContext.StorageUnits.AsNoTracking().Where(u => suIdsFromBoxes.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Identifier)
            : new Dictionary<Guid, string>();

        foreach (var row in boxRows)
        {
            string display;
            if (row.IsTransitBox)
            {
                display = $"Box-{row.BoxIdentifier} (Transit)";
            }
            else if (row.StorageUnitId.HasValue && suNamesFromBoxes.TryGetValue(row.StorageUnitId.Value, out var suId))
            {
                display = $"{suId} (Box-{row.BoxIdentifier})";
            }
            else
            {
                display = $"{row.ShelfUnitIdentifier}-{row.ShelfNumber} (Box-{row.BoxIdentifier})";
            }
            result[row.UnitId] = display;
        }

        var suDirectRows = await dbContext.InventoryItemStorageUnitAssignments
            .AsNoTracking()
            .Where(x => x.Username == username && x.UnitId != null)
            .Join(
                dbContext.StorageUnits.AsNoTracking(),
                a => a.StorageUnitId,
                u => u.Id,
                (a, u) => new { UnitId = a.UnitId!.Value, u.Identifier })
            .ToListAsync();
        foreach (var row in suDirectRows) result[row.UnitId] = row.Identifier;

        return result;
    }

    /// <summary>For each given ItemId, how many of its recorded sales (from RecentOrders,
    /// regardless of shipped status — a sale is a sale whether or not tracking's been added)
    /// still aren't tied to a specific physical unit: min(cumulative ordered quantity, total
    /// Listed units ever created) minus units already marked sold. Capped at the physical unit
    /// count so a data mismatch can't report more pending resolutions than units that could
    /// possibly account for them.</summary>
    public static async Task<Dictionary<string, int>> GetPendingResolutionCountsByItemIdAsync(
        ApplicationDbContext dbContext, string username, IEnumerable<string> itemIds)
    {
        var itemIdList = itemIds.Distinct(StringComparer.Ordinal).ToList();
        if (itemIdList.Count == 0)
        {
            return new Dictionary<string, int>(StringComparer.Ordinal);
        }

        var orderedQtyByItemId = await dbContext.RecentOrders
            .AsNoTracking()
            .Where(x => x.Username == username && itemIdList.Contains(x.ItemId))
            .GroupBy(x => x.ItemId)
            .Select(g => new { ItemId = g.Key, Total = g.Sum(o => o.Quantity > 0 ? o.Quantity : 1) })
            .ToDictionaryAsync(x => x.ItemId, x => x.Total, StringComparer.Ordinal);

        var listings = await dbContext.InventoryListings
            .AsNoTracking()
            .Where(x => x.Username == username && itemIdList.Contains(x.ItemId))
            .Select(x => new { x.Id, x.ItemId })
            .ToListAsync();
        var itemIdByListingId = listings.ToDictionary(x => x.Id, x => x.ItemId);
        var listingIds = listings.Select(x => x.Id).ToList();

        var listedUnits = listingIds.Count > 0
            ? await dbContext.InventoryItemUnits
                .AsNoTracking()
                .Where(x => x.Username == username
                    && listingIds.Contains(x.ListingId)
                    && x.Status == InventoryItemUnitStatus.Listed)
                .ToListAsync()
            : [];
        var unitsByItemId = listedUnits
            .GroupBy(x => itemIdByListingId[x.ListingId])
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var itemId in itemIdList)
        {
            var orderedQty = orderedQtyByItemId.GetValueOrDefault(itemId, 0);
            var units = unitsByItemId.GetValueOrDefault(itemId, []);
            var totalListedCount = units.Count;
            var soldListedCount = units.Count(u => u.IsSold);
            var pending = Math.Min(orderedQty, totalListedCount) - soldListedCount;
            if (pending > 0)
            {
                result[itemId] = pending;
            }
        }

        return result;
    }

    /// <summary>The main entry point: for the given ItemIds, auto-resolves any item that has a
    /// pending (unresolved) sale AND exactly one unsold Listed unit left — there's no real
    /// ambiguity to ask about, so it's marked sold immediately rather than making the owner click a
    /// one-option picker. Saves changes as a side effect. Returns the remaining genuinely
    /// ambiguous items (pending sale, more than one unsold candidate) for callers that need to
    /// render a manual picker.</summary>
    public static async Task<Dictionary<string, List<InventoryItemUnit>>> AutoResolveAndGetAmbiguousAsync(
        ApplicationDbContext dbContext, string username, IEnumerable<string> itemIds)
    {
        var itemIdList = itemIds.Distinct(StringComparer.Ordinal).ToList();
        var pendingByItemId = await GetPendingResolutionCountsByItemIdAsync(dbContext, username, itemIdList);
        if (pendingByItemId.Count == 0)
        {
            return new Dictionary<string, List<InventoryItemUnit>>(StringComparer.Ordinal);
        }

        var candidatesByItemId = await GetUnsoldListedUnitsByItemIdAsync(dbContext, username, pendingByItemId.Keys);

        var toAutoMark = candidatesByItemId
            .Where(kv => kv.Value.Count == 1)
            .Select(kv => kv.Value[0].Id)
            .ToList();
        if (toAutoMark.Count > 0)
        {
            await MarkUnitsSoldAsync(dbContext, username, toAutoMark);
        }

        return candidatesByItemId
            .Where(kv => kv.Value.Count > 1)
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
    }

    /// <summary>The only place SaleRecord rows are ever created — called from
    /// MarkUnitsSoldAsync right after IsSold flips to true for the given units, so both
    /// auto-resolve and the manual "which unit sold?" picker feed it identically. Skips units
    /// whose listing was never classified (OwnershipType null) and sales before
    /// OwnershipRules.StatsCutoffUtc — both per the "forward-only, from 2026-08-01" design. Safe
    /// to call more than once for the same unit: the UnitId unique index makes a duplicate
    /// insert a no-op (caught and ignored) rather than a failure.</summary>
    private static async Task CreateSaleRecordsAsync(ApplicationDbContext dbContext, string username, List<Guid> unitIds)
    {
        var units = await dbContext.InventoryItemUnits
            .AsNoTracking()
            .Where(x => x.Username == username && unitIds.Contains(x.Id))
            .ToListAsync();
        if (units.Count == 0) return;

        var alreadyRecorded = await dbContext.SaleRecords
            .AsNoTracking()
            .Where(x => x.Username == username && unitIds.Contains(x.UnitId))
            .Select(x => x.UnitId)
            .ToListAsync();
        var alreadyRecordedSet = new HashSet<Guid>(alreadyRecorded);
        units = units.Where(u => !alreadyRecordedSet.Contains(u.Id)).ToList();
        if (units.Count == 0) return;

        var listingIds = units.Select(u => u.ListingId).Distinct().ToList();
        var listings = await dbContext.InventoryListings
            .AsNoTracking()
            .Where(x => x.Username == username && listingIds.Contains(x.Id))
            .ToListAsync();
        var listingById = listings.ToDictionary(x => x.Id);

        // Only classified listings produce stats data — an unclassified item that somehow sells
        // before the owner gets to Needs Attention just doesn't count anywhere, no error.
        var classifiedUnits = units
            .Where(u => listingById.TryGetValue(u.ListingId, out var l) && l.OwnershipType != null)
            .ToList();
        if (classifiedUnits.Count == 0) return;

        var consignorIds = classifiedUnits
            .Select(u => listingById[u.ListingId].ConsignorId)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
        var consignorNameById = consignorIds.Count > 0
            ? await dbContext.Consignors
                .AsNoTracking()
                .Where(x => x.Username == username && consignorIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.Name)
            : new Dictionary<Guid, string>();

        var itemIds = classifiedUnits.Select(u => listingById[u.ListingId].ItemId).Distinct().ToList();
        var alreadyClaimedOrderIds = await dbContext.SaleRecords
            .AsNoTracking()
            .Where(x => x.Username == username && itemIds.Contains(x.ItemId))
            .Select(x => x.OrderId)
            .ToListAsync();
        var claimedOrderIdSet = new HashSet<string>(alreadyClaimedOrderIds, StringComparer.Ordinal);

        var candidateOrders = await dbContext.RecentOrders
            .AsNoTracking()
            .Where(x => x.Username == username && itemIds.Contains(x.ItemId))
            .OrderBy(x => x.OrderCreatedAtUtc)
            .ToListAsync();
        var unclaimedOrdersByItemId = candidateOrders
            .Where(o => !claimedOrderIdSet.Contains(o.OrderId))
            .GroupBy(o => o.ItemId)
            .ToDictionary(g => g.Key, g => new Queue<RecentOrder>(g), StringComparer.Ordinal);

        var newRecords = new List<SaleRecord>();
        foreach (var unitGroup in classifiedUnits
            .GroupBy(u => listingById[u.ListingId].ItemId)
            .Select(g => new { ItemId = g.Key, Units = g.OrderBy(u => u.UnitIndex).ToList() }))
        {
            if (!unclaimedOrdersByItemId.TryGetValue(unitGroup.ItemId, out var orderQueue))
            {
                continue;
            }

            foreach (var unit in unitGroup.Units)
            {
                if (orderQueue.Count == 0) break;
                var order = orderQueue.Dequeue();

                if (!order.OrderCreatedAtUtc.HasValue || order.OrderCreatedAtUtc.Value < OwnershipRules.StatsCutoffUtc)
                {
                    // Pre-cutoff sale — disregarded entirely, per the forward-only design.
                    continue;
                }

                var listing = listingById[unit.ListingId];
                newRecords.Add(new SaleRecord
                {
                    Username = username,
                    ListingId = listing.Id,
                    UnitId = unit.Id,
                    ItemId = listing.ItemId,
                    Title = listing.Title,
                    OwnershipType = listing.OwnershipType!,
                    ConsignorId = listing.ConsignorId,
                    ConsignorName = listing.ConsignorId.HasValue ? consignorNameById.GetValueOrDefault(listing.ConsignorId.Value) : null,
                    ConsignorSplitPercent = listing.ConsignmentSplitPercent,
                    PurchaseCost = listing.PurchaseCost,
                    SalePriceGross = order.TransactionPriceGross,
                    ShippingCollected = order.ShippingCollected,
                    TaxCollected = order.TaxCollected,
                    EbayFee = order.EbayFee,
                    ShippingLabelCost = order.ShippingLabelCost,
                    CurrencyId = order.CurrencyId,
                    OrderId = order.OrderId,
                    SoldAtUtc = order.OrderCreatedAtUtc.Value
                });
            }
        }

        if (newRecords.Count == 0) return;

        dbContext.SaleRecords.AddRange(newRecords);
        try
        {
            await dbContext.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // UnitId unique index violation if this ever runs twice for the same unit — safe to
            // ignore, the existing row is already correct (SaleRecord fields are immutable
            // snapshots, never updated after creation).
        }
    }
}
