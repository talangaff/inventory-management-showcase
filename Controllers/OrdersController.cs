using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using inventory_management.Data;
using inventory_management.Models;
using inventory_management.Services;

namespace inventory_management.Controllers;

[Authorize]
public class OrdersController : Controller
{
    private static readonly TimeSpan SyncDebounce = TimeSpan.FromMinutes(5);
    // Prevents concurrent syncs for the same user (e.g. layout pre-sync + page JS racing).
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _syncLocks = new(StringComparer.Ordinal);
    private readonly IEbayService _ebayService;
    private readonly ApplicationDbContext _dbContext;
    private readonly IMemoryCache _cache;
    private readonly ILogger<OrdersController> _logger;

    public OrdersController(
        IEbayService ebayService,
        ApplicationDbContext dbContext,
        IMemoryCache cache,
        ILogger<OrdersController> logger)
    {
        _ebayService = ebayService;
        _dbContext = dbContext;
        _cache = cache;
        _logger = logger;
    }

    public IActionResult Index()
    {
        return RedirectToAction(nameof(Recent));
    }

    public async Task<IActionResult> Recent([FromQuery] string sort = "date_desc")
    {
        return await BuildOrdersPageAsync(onlyReadyToShip: false, sort: sort);
    }

    public async Task<IActionResult> ReadyToShip([FromQuery] string sort = "date_desc")
    {
        return await BuildOrdersPageAsync(onlyReadyToShip: true, sort: sort);
    }

    public async Task<IActionResult> RetrieveFromInventory()
    {
        var model = new RetrieveFromInventoryViewModel
        {
            IsConnected = _ebayService.IsConnected()
        };

        if (!_ebayService.IsConfigured() || !model.IsConnected)
        {
            model.ErrorMessage = "Connect your eBay account in Inventory first.";
            return View(model);
        }

        try
        {
            var username = User.Identity?.Name ?? string.Empty;
            var storageByItemId = await BuildStorageDisplayByItemIdAsync(username);
            var listingsByItemId = await _dbContext.InventoryListings
                .AsNoTracking()
                .Where(x => x.Username == username)
                .Select(x => new { x.ItemId, x.CoverPhotoUrl, x.Barcode })
                .ToDictionaryAsync(x => x.ItemId, StringComparer.Ordinal);

            var cutoff = DateTime.UtcNow.AddDays(-90);
            var dbOrders = await _dbContext.RecentOrders
                .AsNoTracking()
                .Where(x => x.Username == username
                    && !x.HasTracking
                    && !x.IsRetrieved
                    && (x.OrderCreatedAtUtc == null || x.OrderCreatedAtUtc >= cutoff))
                .ToListAsync();

            model.Items = dbOrders
                .GroupBy(x => x.ItemId, StringComparer.Ordinal)
                .Select(g =>
                {
                    var first = g.First();
                    listingsByItemId.TryGetValue(g.Key, out var listing);
                    return new RetrieveItemViewModel
                    {
                        ItemId = g.Key,
                        ItemName = first.ItemName,
                        ItemPhotoUrl = listing?.CoverPhotoUrl ?? first.ItemPhotoUrl,
                        StorageLocationDisplay = storageByItemId.TryGetValue(g.Key, out var loc) ? loc : string.Empty,
                        TotalQuantity = g.Sum(x => x.Quantity > 0 ? x.Quantity : 1),
                        BuyerNames = g.Select(x => x.BuyerName).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                        Barcode = listing?.Barcode ?? string.Empty
                    };
                })
                .OrderBy(x => x.StorageLocationDisplay)
                .ThenBy(x => x.ItemName)
                .ToList();
        }
        catch (Exception ex)
        {
            model.ErrorMessage = ex.Message;
        }

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkRetrieved([FromBody] MarkRetrievedRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.ItemId))
        {
            return Json(new { success = false });
        }

        var username = User.Identity?.Name ?? string.Empty;
        var rows = await _dbContext.RecentOrders
            .Where(x => x.Username == username && x.ItemId == request.ItemId)
            .ToListAsync();

        var nowUtc = DateTime.UtcNow;
        foreach (var row in rows)
        {
            row.IsRetrieved = true;
            row.UpdatedAtUtc = nowUtc;
        }

        await _dbContext.SaveChangesAsync();
        return Json(new { success = true });
    }

    [HttpGet]
    public async Task<IActionResult> TriggerSync()
    {
        if (!_ebayService.IsConfigured() || !_ebayService.IsConnected())
        {
            return Json(new { ok = false });
        }

        var username = User.Identity?.Name ?? string.Empty;
        try
        {
            var synced = await SyncOrdersFromEbayAsync(username);
            return Json(new { ok = synced });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Order sync failed for {Username}", username);
            return Json(new { ok = false });
        }
    }

    private async Task<IActionResult> BuildOrdersPageAsync(bool onlyReadyToShip, string sort = "date_desc")
    {
        var model = new OrdersIndexViewModel
        {
            PageTitle = onlyReadyToShip ? "Ready to Ship" : "Recent Orders",
            IsReadyToShipPage = onlyReadyToShip,
            IsConnected = _ebayService.IsConnected(),
            Sort = sort
        };

        if (!_ebayService.IsConfigured())
        {
            model.ErrorMessage = "eBay API settings are missing. Add Ebay config in appsettings.Development.json.";
            return View("Index", model);
        }

        if (!model.IsConnected)
        {
            model.ErrorMessage = "Connect your eBay account in Inventory first.";
            return View("Index", model);
        }

        try
        {
            var username = User.Identity?.Name ?? string.Empty;
            var storageByItemId = await BuildStorageDisplayByItemIdAsync(username);
            var listingsByItemId = await _dbContext.InventoryListings
                .AsNoTracking()
                .Where(x => x.Username == username)
                .Select(x => new { x.ItemId, x.CoverPhotoUrl, x.Barcode })
                .ToDictionaryAsync(x => x.ItemId, StringComparer.Ordinal);

            var cutoff = DateTime.UtcNow.AddDays(-90);
            var ordersQuery = _dbContext.RecentOrders
                .AsNoTracking()
                .Where(x => x.Username == username && (x.OrderCreatedAtUtc == null || x.OrderCreatedAtUtc >= cutoff));

            var dbOrders = await (sort switch
            {
                "date_asc" => ordersQuery.OrderBy(x => x.OrderCreatedAtUtc ?? DateTime.MaxValue).ThenBy(x => x.ItemId),
                "title"    => ordersQuery.OrderBy(x => x.ItemName).ThenBy(x => x.ItemId),
                _          => ordersQuery.OrderByDescending(x => x.OrderCreatedAtUtc ?? DateTime.MinValue).ThenBy(x => x.ItemId),
            }).ToListAsync();

            var filteredOrders = dbOrders.Where(x => !onlyReadyToShip || !x.HasTracking).ToList();

            // Multi-unit items: surface a manual picker wherever there's real ambiguity (more
            // than one unsold Listed unit) for a pending sale — not restricted to unshipped
            // orders, so an item that got marked shipped before it was ever picked can still be
            // resolved here. AutoResolveAndGetAmbiguousAsync also auto-resolves any item that's
            // no longer actually ambiguous (down to exactly one unsold unit) as a side effect,
            // instead of leaving it stuck with a one-option picker forever.
            var unsoldUnitsByItemId = await SaleResolutionService.AutoResolveAndGetAmbiguousAsync(
                _dbContext, username, filteredOrders.Select(x => x.ItemId));
            var locationByUnitId = unsoldUnitsByItemId.Count > 0
                ? await SaleResolutionService.BuildLocationDisplayByUnitIdAsync(_dbContext, username)
                : new Dictionary<Guid, string>();

            var rows = filteredOrders
                .Select(x =>
                {
                    listingsByItemId.TryGetValue(x.ItemId, out var listing);
                    var unsoldUnits = unsoldUnitsByItemId.TryGetValue(x.ItemId, out var units)
                        ? units.Select(u => new UnsoldUnitOptionViewModel
                        {
                            UnitId = u.Id,
                            Barcode = u.Barcode,
                            LocationDisplay = locationByUnitId.TryGetValue(u.Id, out var loc) ? loc : "No location"
                        }).ToList()
                        : [];
                    return new OrderListItemViewModel
                    {
                        OrderId = x.OrderId,
                        ItemId = x.ItemId,
                        ItemName = x.ItemName,
                        ItemPhotoUrl = listing?.CoverPhotoUrl ?? x.ItemPhotoUrl,
                        BuyerName = x.BuyerName,
                        HasTracking = x.HasTracking,
                        Quantity = x.Quantity,
                        OrderCreatedAtUtc = x.OrderCreatedAtUtc,
                        StorageLocationDisplay = storageByItemId.TryGetValue(x.ItemId, out var locationDisplay)
                            ? locationDisplay
                            : string.Empty,
                        Barcode = listing?.Barcode ?? string.Empty,
                        UnsoldUnitOptions = unsoldUnits
                    };
                })
                .ToList();

            model.Orders = rows;
        }
        catch (Exception ex)
        {
            model.ErrorMessage = ex.Message;
        }

        return View("Index", model);
    }

    private async Task<bool> SyncOrdersFromEbayAsync(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return false;
        }

        var cacheKey = $"orders_sync_{username}";
        if (_cache.TryGetValue(cacheKey, out _))
        {
            return false;
        }

        // Prevent two concurrent syncs for the same user (e.g. layout pre-sync races page JS).
        var semaphore = _syncLocks.GetOrAdd(username, _ => new SemaphoreSlim(1, 1));
        if (!await semaphore.WaitAsync(TimeSpan.Zero))
        {
            // Another sync is already running — wait for it to finish, then report debounced.
            await semaphore.WaitAsync();
            semaphore.Release();
            return false;
        }

        try
        {
            // Re-check cache in case the sync that just finished already set it.
            if (_cache.TryGetValue(cacheKey, out _))
            {
                return false;
            }

        var nowUtc = DateTime.UtcNow;

        // Build known-photo map from DB so GetRecentOrdersAsync can skip GetItem API calls
        // for orders whose photos we already have stored.
        var existingForPhotos = await _dbContext.RecentOrders
            .Where(x => x.Username == username && !string.IsNullOrEmpty(x.ItemPhotoUrl))
            .Select(x => new { x.ItemId, x.ItemPhotoUrl })
            .ToListAsync();
        var knownPhotos = existingForPhotos
            .GroupBy(x => x.ItemId)
            .ToDictionary(g => g.Key, g => g.First().ItemPhotoUrl!, StringComparer.Ordinal);

        // Fetch 90 days to match the DB display window — orders can remain unshipped for
        // longer than 30 days, so we need the full window to keep HasTracking up to date.
        var cancelledOrderIds = new HashSet<string>(StringComparer.Ordinal);
        var ebayOrders = await _ebayService.GetRecentOrdersAsync(90, knownPhotos, cancelledOrderIds);

        var existing = await _dbContext.RecentOrders
            .Where(x => x.Username == username)
            .ToListAsync();

        var existingByKey = existing.ToDictionary(
            x => (x.OrderId, x.ItemId));

        var newlySoldItemIds = new HashSet<string>(StringComparer.Ordinal);
        var newOrderIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var order in ebayOrders)
        {
            var key = (order.OrderId, order.ItemId);
            if (existingByKey.TryGetValue(key, out var row))
            {
                row.ItemName = order.ItemName;
                row.BuyerName = order.BuyerName;
                // Only flip HasTracking to true; never clear it if we set it locally
                if (order.HasTracking)
                {
                    row.HasTracking = true;
                }
                row.Quantity = order.Quantity;
                if (!string.IsNullOrWhiteSpace(order.ItemPhotoUrl) && string.IsNullOrWhiteSpace(row.ItemPhotoUrl))
                {
                    row.ItemPhotoUrl = order.ItemPhotoUrl;
                }
                row.OrderCreatedAtUtc = order.OrderCreatedAtUtc;
                row.TransactionPriceGross = order.TransactionPriceGross;
                row.ShippingCollected = order.ShippingCollected;
                row.TaxCollected = order.TaxCollected;
                if (!string.IsNullOrWhiteSpace(order.CurrencyId))
                {
                    row.CurrencyId = order.CurrencyId;
                }
                // Update buyer address if we now have one
                if (!string.IsNullOrWhiteSpace(order.BuyerStreet1))
                {
                    row.BuyerStreet1 = order.BuyerStreet1;
                    row.BuyerStreet2 = order.BuyerStreet2;
                    row.BuyerCity = order.BuyerCity;
                    row.BuyerState = order.BuyerState;
                    row.BuyerZip = order.BuyerZip;
                    row.BuyerCountry = order.BuyerCountry;
                    row.BuyerPhone = order.BuyerPhone;
                }
                row.LastSeenAtUtc = nowUtc;
                row.UpdatedAtUtc = nowUtc;
            }
            else
            {
                // A brand-new order means this item just sold — mark its storage location
                // sold so it drops off the shelf/box/storage-unit browsing pages while
                // remaining visible (via the kept location) on Ready to Ship.
                newlySoldItemIds.Add(order.ItemId);

                var newRow = new RecentOrder
                {
                    Username = username,
                    OrderId = order.OrderId,
                    ItemId = order.ItemId,
                    ItemName = order.ItemName,
                    ItemPhotoUrl = order.ItemPhotoUrl,
                    BuyerName = order.BuyerName,
                    HasTracking = order.HasTracking,
                    Quantity = order.Quantity,
                    OrderCreatedAtUtc = order.OrderCreatedAtUtc,
                    BuyerStreet1 = order.BuyerStreet1,
                    BuyerStreet2 = order.BuyerStreet2,
                    BuyerCity = order.BuyerCity,
                    BuyerState = order.BuyerState,
                    BuyerZip = order.BuyerZip,
                    BuyerCountry = order.BuyerCountry,
                    BuyerPhone = order.BuyerPhone,
                    TransactionPriceGross = order.TransactionPriceGross,
                    ShippingCollected = order.ShippingCollected,
                    TaxCollected = order.TaxCollected,
                    CurrencyId = order.CurrencyId,
                    LastSeenAtUtc = nowUtc,
                    UpdatedAtUtc = nowUtc
                };
                _dbContext.RecentOrders.Add(newRow);
                newOrderIds.Add(order.OrderId);

                // eBay can return more than one transaction for the same (OrderId, ItemId) pair
                // within a single 90-day pull (e.g. a buyer adding the same listing to an
                // already-open combined-invoice order). Without this, a second occurrence in
                // this same batch would also fall into this "new" branch and attempt a second
                // insert with the same key, which the unique index on
                // (username, order_id, item_id) rejects — failing the whole SaveChangesAsync
                // batch and silently dropping every other order in this sync, not just the
                // duplicate. Registering it here makes the next occurrence take the update path.
                existingByKey[key] = newRow;
            }
        }

        // Best-effort eBay fee + shipping-label-cost lookup, primarily for orders that are
        // brand-new this sync. Also retries a small batch of older orders still missing either
        // field — e.g. because sell.finances wasn't granted yet when they were first synced, or
        // eBay hadn't posted the ledger transaction yet — so reconnecting eBay / eBay settling
        // later self-heals instead of leaving those rows permanently null. One batched call per
        // sync rather than one per order — see EbayService.GetOrderFinanceDataAsync. Orders
        // still missing a shipping cost after this become Needs Attention items for manual
        // entry — see Services/AttentionRules.GetOrdersMissingShippingCostAsync.
        const int MissingFinanceDataRetryBatchSize = 25;
        var retryOrderIds = existing
            .Where(x => (x.EbayFee is null || x.ShippingLabelCost is null) && !newOrderIds.Contains(x.OrderId))
            .Select(x => x.OrderId)
            .Distinct(StringComparer.Ordinal)
            .Take(MissingFinanceDataRetryBatchSize);
        var financeOrderIds = newOrderIds.Concat(retryOrderIds).Distinct(StringComparer.Ordinal).ToList();

        var financeDataByOrderId = await _ebayService.GetOrderFinanceDataAsync(financeOrderIds);
        foreach (var (financeOrderId, data) in financeDataByOrderId)
        {
            if (data.Fee is null && data.ShippingLabelCost is null)
            {
                continue;
            }

            foreach (var row in _dbContext.RecentOrders.Local.Where(x => x.Username == username && x.OrderId == financeOrderId))
            {
                if (data.Fee.HasValue) row.EbayFee = data.Fee;
                if (data.ShippingLabelCost.HasValue) row.ShippingLabelCost = data.ShippingLabelCost;
            }
            // RecentOrder rows can already have been snapshotted into a permanent SaleRecord
            // (see SaleResolutionService.CreateSaleRecordsAsync) before this data was available
            // — propagate the backfill so Stats picks it up too.
            foreach (var saleRecord in await _dbContext.SaleRecords
                .Where(x => x.Username == username && x.OrderId == financeOrderId
                    && (x.EbayFee == null || x.ShippingLabelCost == null))
                .ToListAsync())
            {
                if (data.Fee.HasValue) saleRecord.EbayFee = data.Fee;
                if (data.ShippingLabelCost.HasValue) saleRecord.ShippingLabelCost = data.ShippingLabelCost;
            }
        }

        // Also check the Fulfillment REST API — the Trading API misses tracking for international
        // orders shipped via eBay International Shipping / GSP where ShipmentTrackingDetails
        // is not populated. Returns an empty set on any error, so it's safe to always call.
        var fulfilledIds = await _ebayService.GetFulfilledOrderIdsAsync();
        // Use Local to cover both pre-existing rows and orders inserted during this sync cycle.
        foreach (var row in _dbContext.RecentOrders.Local.Where(x => x.Username == username && !x.HasTracking && fulfilledIds.Contains(x.OrderId)))
        {
            row.HasTracking = true;
            row.UpdatedAtUtc = nowUtc;
        }

        // Orders cancelled after we first synced them: ParseOrderItems skips cancelled orders, so
        // without this the stale row would sit on Ready to Ship forever.
        var cancelledRows = _dbContext.RecentOrders.Local
            .Where(x => x.Username == username && cancelledOrderIds.Contains(x.OrderId))
            .ToList();
        if (cancelledRows.Count > 0)
        {
            _dbContext.RecentOrders.RemoveRange(cancelledRows);
        }

        // Purge records older than 90 days.
        var cutoff = nowUtc.AddDays(-90);
        var toDelete = existing
            .Where(x => x.OrderCreatedAtUtc.HasValue && x.OrderCreatedAtUtc.Value < cutoff)
            .ToList();
        if (toDelete.Count > 0)
        {
            _dbContext.RecentOrders.RemoveRange(toDelete);
        }

        await _dbContext.SaveChangesAsync();

        if (newlySoldItemIds.Count > 0)
        {
            // Only auto-resolves when unambiguous (one unsold unit, true for every quantity=1
            // item); multi-unit items are left alone until picked via MarkUnitSold — on Recent
            // Orders/Ready to Ship, the item Details page, or Needs Attention — eBay's order
            // data never says which physical unit sold.
            await SaleResolutionService.AutoResolveAndGetAmbiguousAsync(_dbContext, username, newlySoldItemIds);
        }

        _cache.Set(cacheKey, true, SyncDebounce);
        return true;
        } // end try
        finally
        {
            semaphore.Release();
        }
    }

    private async Task<Dictionary<string, string>> BuildStorageDisplayByItemIdAsync(string username)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        var shelfLocations = await _dbContext.InventoryItemLocations
            .AsNoTracking()
            .Where(x => x.Username == username)
            .Select(x => new
            {
                x.ItemId,
                Display = $"{x.ShelfUnitIdentifier}-{x.ShelfNumber}"
            })
            .ToListAsync();

        foreach (var row in shelfLocations)
        {
            result[row.ItemId] = row.Display;
        }

        var boxRows = await _dbContext.InventoryItemBoxAssignments
            .AsNoTracking()
            .Where(x => x.Username == username)
            .Join(
                _dbContext.Boxes.AsNoTracking(),
                assignment => assignment.BoxId,
                box => box.Id,
                (assignment, box) => new
                {
                    assignment.ItemId,
                    box.ShelfUnitIdentifier,
                    box.ShelfNumber,
                    box.BoxIdentifier,
                    box.StorageUnitId,
                    box.IsTransitBox
                })
            .ToListAsync();

        var suIdsFromBoxes = boxRows
            .Where(x => x.StorageUnitId.HasValue)
            .Select(x => x.StorageUnitId!.Value)
            .Distinct()
            .ToList();

        var suNamesFromBoxes = suIdsFromBoxes.Count > 0
            ? await _dbContext.StorageUnits
                .AsNoTracking()
                .Where(u => suIdsFromBoxes.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.Identifier)
            : new Dictionary<Guid, string>();

        foreach (var row in boxRows)
        {
            string display;
            if (row.IsTransitBox)
            {
                // Transit boxes have no shelf/storage-unit location by design (see Box.IsTransitBox),
                // so ShelfUnitIdentifier/ShelfNumber are blank/0 here — showing them would render
                // as the meaningless "-0 (Box-B)".
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
            result[row.ItemId] = display;
        }

        var suDirectRows = await _dbContext.InventoryItemStorageUnitAssignments
            .AsNoTracking()
            .Where(x => x.Username == username)
            .Join(
                _dbContext.StorageUnits.AsNoTracking(),
                a => a.StorageUnitId,
                u => u.Id,
                (a, u) => new { a.ItemId, u.Identifier })
            .ToListAsync();

        foreach (var row in suDirectRows)
        {
            result[row.ItemId] = row.Identifier;
        }

        return result;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkUnitSold([FromBody] MarkUnitSoldRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.ItemId) || request.UnitId == Guid.Empty)
        {
            return Json(new { success = false, message = "Item and unit are required." });
        }

        var username = User.Identity?.Name ?? string.Empty;
        var candidatesByItemId = await SaleResolutionService.GetUnsoldListedUnitsByItemIdAsync(
            _dbContext, username, [request.ItemId]);
        if (!candidatesByItemId.TryGetValue(request.ItemId, out var candidates)
            || !candidates.Any(u => u.Id == request.UnitId))
        {
            return Json(new { success = false, message = "That unit is not an unsold listed unit of this item." });
        }

        await SaleResolutionService.MarkUnitsSoldAsync(_dbContext, username, [request.UnitId]);
        return Json(new { success = true });
    }
}
