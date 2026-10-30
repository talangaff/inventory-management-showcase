using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using inventory_management.Data;
using inventory_management.Models;
using inventory_management.Services;

namespace inventory_management.Controllers;

[Authorize]
public class InventoryController : Controller
{
    private const int InventoryBatchSize = 25;
    private static readonly TimeSpan SyncDebounce = TimeSpan.FromMinutes(5);
    private readonly IEbayService _ebayService;
    private readonly IShelfUnitStore _shelfUnitStore;
    private readonly ApplicationDbContext _dbContext;
    private readonly IWebHostEnvironment _webHostEnvironment;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;
    private readonly EbayOptions _ebayOptions;
    private readonly ILogger<InventoryController> _logger;

    public InventoryController(
        IEbayService ebayService,
        IShelfUnitStore shelfUnitStore,
        ApplicationDbContext dbContext,
        IWebHostEnvironment webHostEnvironment,
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache,
        IOptions<EbayOptions> ebayOptions,
        ILogger<InventoryController> logger)
    {
        _ebayService = ebayService;
        _shelfUnitStore = shelfUnitStore;
        _dbContext = dbContext;
        _webHostEnvironment = webHostEnvironment;
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _ebayOptions = ebayOptions.Value;
        _logger = logger;
    }

    // Creates a stateless, HMAC-signed OAuth state parameter so the callback
    // works regardless of which host/port eBay redirects the browser back to.
    private string CreateSignedState()
    {
        var nonce = Guid.NewGuid().ToString("N");
        var key = System.Text.Encoding.UTF8.GetBytes(_ebayOptions.ClientSecret);
        var data = System.Text.Encoding.UTF8.GetBytes(nonce);
        var sig = Convert.ToBase64String(HMACSHA256.HashData(key, data))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        return $"{nonce}.{sig}";
    }

    private bool ValidateSignedState(string? state)
    {
        if (string.IsNullOrWhiteSpace(state)) return false;
        var dot = state.IndexOf('.');
        if (dot < 0) return false;
        var nonce = state[..dot];
        var sig = state[(dot + 1)..];
        var key = System.Text.Encoding.UTF8.GetBytes(_ebayOptions.ClientSecret);
        var data = System.Text.Encoding.UTF8.GetBytes(nonce);
        var expected = Convert.ToBase64String(HMACSHA256.HashData(key, data))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        return CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(expected),
            System.Text.Encoding.UTF8.GetBytes(sig));
    }

    public IActionResult Index()
    {
        return RedirectToAction(nameof(ActiveInventory));
    }

    public async Task<IActionResult> ActiveInventory([FromQuery] string sort = "title")
    {
        return await BuildInventoryPageAsync(isMissingDataPage: false, sort: sort);
    }

    public async Task<IActionResult> MissingData([FromQuery] string sort = "title")
    {
        return await BuildInventoryPageAsync(isMissingDataPage: true, sort: sort);
    }

    public async Task<IActionResult> UnprintedLabels([FromQuery] string sort = "title")
    {
        return await BuildInventoryPageAsync(isMissingDataPage: false, isUnprintedPage: true, sort: sort);
    }

    [HttpGet]
    public async Task<IActionResult> NeedsAttention()
    {
        var username = User.Identity?.Name ?? string.Empty;
        var candidates = await AttentionRules.GetStaleListingsAsync(_dbContext, username, DateTime.UtcNow);

        // Every item here has had no price change in the last 30 days by definition (that's
        // part of what makes it "stale" — see AttentionRules), but older history still shows
        // how the price has moved over its whole lifetime. Batch-fetch, not N+1.
        var listingIds = candidates.Select(x => x.ListingId).ToList();
        var priceHistoryByListingId = listingIds.Count > 0
            ? (await _dbContext.PriceReductionHistories
                .AsNoTracking()
                .Where(x => x.Username == username && listingIds.Contains(x.ListingId))
                .OrderByDescending(x => x.ReducedAtUtc)
                .Select(x => new { x.ListingId, Row = new PriceReductionHistoryRowViewModel
                {
                    OldPrice = x.OldPrice,
                    NewPrice = x.NewPrice,
                    ReducedAtUtc = x.ReducedAtUtc,
                    ChangeType = x.ChangeType
                } })
                .ToListAsync())
                .GroupBy(x => x.ListingId)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Row).ToList())
            : new Dictionary<Guid, List<PriceReductionHistoryRowViewModel>>();

        var items = candidates
            .OrderBy(x => x.ReferenceDateUtc)
            .ThenBy(x => x.Title)
            .Select(x => new NeedsAttentionItemViewModel
            {
                ListingId = x.ListingId,
                ItemId = x.ItemId,
                Title = x.Title,
                CoverPhotoUrl = x.CoverPhotoUrl,
                CurrentPrice = x.CurrentPrice,
                CurrencyId = x.CurrencyId,
                ReferenceDateUtc = x.ReferenceDateUtc,
                ListedAtUtc = x.ListedAtUtc,
                PriceHistory = priceHistoryByListingId.GetValueOrDefault(x.ListingId, []),
                OwnershipType = x.OwnershipType,
                PurchaseCost = x.PurchaseCost
            })
            .ToList();

        var soldOutWithBackstock = (await AttentionRules.GetSoldOutWithBackstockAsync(_dbContext, username))
            .OrderBy(x => x.Title)
            .Select(x => new SoldOutBackstockItemViewModel
            {
                ItemId = x.ItemId,
                Title = x.Title,
                CoverPhotoUrl = x.CoverPhotoUrl,
                BackstockCount = x.BackstockCount
            })
            .ToList();

        var unitResolutionCandidates = await AttentionRules.GetItemsNeedingUnitResolutionAsync(_dbContext, username);
        var needsUnitResolution = new List<UnitResolutionItemViewModel>();
        if (unitResolutionCandidates.Count > 0)
        {
            var locationByUnitId = await SaleResolutionService.BuildLocationDisplayByUnitIdAsync(_dbContext, username);
            needsUnitResolution = unitResolutionCandidates
                .OrderBy(x => x.Title)
                .Select(x => new UnitResolutionItemViewModel
                {
                    ItemId = x.ItemId,
                    Title = x.Title,
                    CoverPhotoUrl = x.CoverPhotoUrl,
                    UnitOptions = x.Units
                        .Select(u => new UnsoldUnitOptionViewModel
                        {
                            UnitId = u.Id,
                            Barcode = u.Barcode,
                            LocationDisplay = locationByUnitId.TryGetValue(u.Id, out var loc) ? loc : "No location"
                        })
                        .ToList()
                })
                .ToList();
        }

        var unclassifiedCandidates = await OwnershipRules.GetUnclassifiedActiveListingsAsync(_dbContext, username);
        var unclassified = new List<UnclassifiedItemViewModel>();
        if (unclassifiedCandidates.Count > 0)
        {
            var consignorOptions = await _dbContext.Consignors
                .AsNoTracking()
                .Where(x => x.Username == username)
                .OrderBy(x => x.Name)
                .Select(x => new ConsignorOptionViewModel { Id = x.Id, Name = x.Name, DefaultSplitPercent = x.DefaultSplitPercent })
                .ToListAsync();

            var mostRecentConsignor = await OwnershipRules.GetMostRecentConsignorAsync(_dbContext, username);
            var mostRecentOption = mostRecentConsignor is null
                ? null
                : new ConsignorOptionViewModel { Id = mostRecentConsignor.Id, Name = mostRecentConsignor.Name, DefaultSplitPercent = mostRecentConsignor.DefaultSplitPercent };

            unclassified = unclassifiedCandidates
                .Select(x => new UnclassifiedItemViewModel
                {
                    ItemId = x.ItemId,
                    Title = x.Title,
                    CoverPhotoUrl = x.CoverPhotoUrl,
                    CurrentPrice = x.CurrentPrice,
                    CurrencyId = x.CurrencyId,
                    ConsignorOptions = consignorOptions,
                    MostRecentConsignor = mostRecentOption
                })
                .ToList();
        }

        var missingShippingCost = (await AttentionRules.GetOrdersMissingShippingCostAsync(_dbContext, username))
            .Select(x => new MissingShippingCostItemViewModel
            {
                OrderId = x.OrderId,
                ItemId = x.ItemId,
                ItemName = x.ItemName,
                ItemPhotoUrl = x.ItemPhotoUrl,
                BuyerName = x.BuyerName,
                OrderCreatedAtUtc = x.OrderCreatedAtUtc,
                EbayOrderUrl = x.EbayOrderUrl
            })
            .ToList();

        return View(new NeedsAttentionPageViewModel
        {
            StaleItems = items,
            SoldOutWithBackstock = soldOutWithBackstock,
            NeedsUnitResolution = needsUnitResolution,
            Unclassified = unclassified,
            MissingShippingCost = missingShippingCost
        });
    }

    // AJAX-driven from the Missing Shipping Cost section of Views/Inventory/NeedsAttention.cshtml.
    // Sets the actual shipping cost by hand for an order the Finance API hasn't returned one for
    // yet, and propagates it to a matching SaleRecord the same way the automatic Finance API
    // lookup in OrdersController does, so Stats picks it up immediately too.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetShippingCost(string orderId, decimal shippingLabelCost)
    {
        var isAjax = string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(orderId))
        {
            return ErrorOrRedirect("Order ID is required.", isAjax, nameof(NeedsAttention));
        }
        if (shippingLabelCost < 0)
        {
            return ErrorOrRedirect("Shipping cost can't be negative.", isAjax, nameof(NeedsAttention));
        }

        var username = User.Identity?.Name ?? string.Empty;
        var rows = await _dbContext.RecentOrders
            .Where(x => x.Username == username && x.OrderId == orderId)
            .ToListAsync();
        if (rows.Count == 0)
        {
            return ErrorOrRedirect("Order not found.", isAjax, nameof(NeedsAttention));
        }

        var nowUtc = DateTime.UtcNow;
        foreach (var row in rows)
        {
            row.ShippingLabelCost = shippingLabelCost;
            row.UpdatedAtUtc = nowUtc;
        }

        foreach (var saleRecord in await _dbContext.SaleRecords
            .Where(x => x.Username == username && x.OrderId == orderId)
            .ToListAsync())
        {
            saleRecord.ShippingLabelCost = shippingLabelCost;
        }

        await _dbContext.SaveChangesAsync();

        if (isAjax)
        {
            return Json(new { success = true });
        }
        return RedirectToAction(nameof(NeedsAttention));
    }

    // Promotes up to `count` backstock units (oldest first) of an item's listing to Listed
    // status. This only updates our own records — it does not call eBay to relist or raise the
    // listed quantity there; the next sync's unit reconciliation will simply see the promoted
    // units already counted toward Listed and won't create duplicates. AJAX-driven from
    // Views/Inventory/NeedsAttention.cshtml.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RelistFromBackstock(string itemId, int count)
    {
        var isAjax = string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(itemId) || count < 1)
        {
            return ErrorOrRedirect("Item ID and a positive count are required.", isAjax, nameof(NeedsAttention));
        }

        var username = User.Identity?.Name ?? string.Empty;
        var listing = await _dbContext.InventoryListings
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Username == username && x.ItemId == itemId);
        if (listing is null)
        {
            return ErrorOrRedirect("Item not found.", isAjax, nameof(NeedsAttention));
        }

        var backstockUnits = await _dbContext.InventoryItemUnits
            .Where(x => x.Username == username
                && x.ListingId == listing.Id
                && x.Status == InventoryItemUnitStatus.Backstock
                && !x.IsSold)
            .OrderBy(x => x.UnitIndex)
            .Take(count)
            .ToListAsync();

        foreach (var unit in backstockUnits)
        {
            unit.Status = InventoryItemUnitStatus.Listed;
        }
        await _dbContext.SaveChangesAsync();

        if (isAjax)
        {
            return Json(new { success = true, promoted = backstockUnits.Count });
        }
        TempData["InventorySuccess"] = $"Relisted {backstockUnits.Count} unit(s) from backstock.";
        return RedirectToAction(nameof(NeedsAttention));
    }

    // Adds one new backstock unit (fresh barcode, own optional location, never counted in
    // eBay's QuantityAvailable) to an existing listing. AJAX-driven from the item Details page.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddBackstockUnit(string itemId)
    {
        var isAjax = string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(itemId))
        {
            if (isAjax) return Json(new { success = false, message = "Item ID is required." });
            return RedirectToAction(nameof(Index));
        }

        var username = User.Identity?.Name ?? string.Empty;
        var listing = await _dbContext.InventoryListings
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Username == username && x.ItemId == itemId);
        if (listing is null)
        {
            if (isAjax) return Json(new { success = false, message = "Item not found." });
            return RedirectToAction(nameof(Index));
        }

        var nextIndex = 1 + await _dbContext.InventoryItemUnits
            .Where(x => x.Username == username && x.ListingId == listing.Id)
            .Select(x => (int?)x.UnitIndex)
            .MaxAsync() ?? 1;

        var unit = new InventoryItemUnit
        {
            Username = username,
            ListingId = listing.Id,
            UnitIndex = nextIndex,
            Barcode = GenerateItemBarcode(),
            Status = InventoryItemUnitStatus.Backstock,
            CreatedAtUtc = DateTime.UtcNow
        };
        _dbContext.InventoryItemUnits.Add(unit);
        await _dbContext.SaveChangesAsync();

        if (isAjax)
        {
            return Json(new { success = true, unitId = unit.Id, barcode = unit.Barcode });
        }
        return RedirectToAction(nameof(Details), new { id = itemId });
    }

    // Removes a backstock unit that was added by mistake or no longer exists. Deliberately
    // scoped to Status == Backstock only — Listed units (whether quantity>1 or unit 1, which
    // reuses the listing's own permanent Barcode) are never deletable here, matching the
    // "never delete/regenerate existing barcodes" invariant for real listed inventory. Also
    // scoped to !IsSold — a sold unit's location is kept for Ready to Ship history, same as
    // everywhere else in this codebase. AJAX-driven from the item Details page.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteBackstockUnit([FromBody] DeleteBackstockUnitRequest request)
    {
        var isAjax = string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);
        var username = User.Identity?.Name ?? string.Empty;
        var unitId = request?.UnitId ?? Guid.Empty;

        var unit = await _dbContext.InventoryItemUnits
            .FirstOrDefaultAsync(x => x.Username == username && x.Id == unitId);
        if (unit is null || unit.Status != InventoryItemUnitStatus.Backstock || unit.IsSold)
        {
            if (isAjax) return Json(new { success = false, message = "That unit can't be deleted." });
            return RedirectToAction(nameof(Index));
        }

        var itemId = (await _dbContext.InventoryListings
            .AsNoTracking()
            .Where(x => x.Id == unit.ListingId)
            .Select(x => x.ItemId)
            .FirstOrDefaultAsync()) ?? string.Empty;

        // Location tables aren't DB-FK'd to units (added as a plain nullable column, per this
        // project's "additive only" schema convention), so clean them up explicitly rather than
        // leaving orphaned rows keyed to a UnitId that no longer exists.
        await _dbContext.InventoryItemLocations
            .Where(x => x.Username == username && x.UnitId == unitId)
            .ExecuteDeleteAsync();
        await _dbContext.InventoryItemBoxAssignments
            .Where(x => x.Username == username && x.UnitId == unitId)
            .ExecuteDeleteAsync();
        await _dbContext.InventoryItemStorageUnitAssignments
            .Where(x => x.Username == username && x.UnitId == unitId)
            .ExecuteDeleteAsync();

        _dbContext.InventoryItemUnits.Remove(unit);
        await _dbContext.SaveChangesAsync();

        if (isAjax)
        {
            return Json(new { success = true });
        }
        return RedirectToAction(nameof(Details), new { id = itemId });
    }

    // Resolves a pending sale to a specific physical unit — same underlying action as
    // Orders/MarkUnitSold, just reachable from wherever the owner happens to be looking at the
    // item: the Details page (regardless of whether the order has shipped — a sale that was
    // never picked before shipping used to be unresolvable forever) and the Needs Attention
    // "needs unit resolved" section. AJAX-driven from both.
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

    // Sets a 30-day snooze, or (with permanent=true) a standing ignore, on Needs Attention
    // eligibility for one item. AJAX-driven from Views/Inventory/NeedsAttention.cshtml.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> IgnoreAttention(string itemId, bool permanent)
    {
        var isAjax = string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(itemId))
        {
            return ErrorOrRedirect("Item ID is required.", isAjax, nameof(NeedsAttention));
        }

        var username = User.Identity?.Name ?? string.Empty;
        var listing = await _dbContext.InventoryListings
            .FirstOrDefaultAsync(x => x.Username == username && x.ItemId == itemId);
        if (listing is null)
        {
            return ErrorOrRedirect("Item not found.", isAjax, nameof(NeedsAttention));
        }

        if (permanent)
        {
            listing.AttentionIgnoredPermanently = true;
            listing.AttentionIgnoredUntilUtc = null;
        }
        else
        {
            listing.AttentionIgnoredUntilUtc = DateTime.UtcNow.AddDays(30);
        }
        listing.UpdatedAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        if (isAjax)
        {
            return Json(new { success = true });
        }
        return RedirectToAction(nameof(NeedsAttention));
    }

    // Snoozes every item currently showing on Needs Attention for 30 days in one shot. Confirmed
    // client-side before this fires. AJAX-driven from Views/Inventory/NeedsAttention.cshtml.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> IgnoreAllAttention()
    {
        var isAjax = string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);
        var username = User.Identity?.Name ?? string.Empty;
        var nowUtc = DateTime.UtcNow;

        var candidates = await AttentionRules.GetStaleListingsAsync(_dbContext, username, nowUtc);
        if (candidates.Count == 0)
        {
            if (isAjax) return Json(new { success = true, count = 0 });
            return RedirectToAction(nameof(NeedsAttention));
        }

        var listingIds = candidates.Select(x => x.ListingId).ToList();
        var count = await _dbContext.InventoryListings
            .Where(x => x.Username == username && listingIds.Contains(x.Id))
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.AttentionIgnoredUntilUtc, nowUtc.AddDays(30))
                .SetProperty(x => x.UpdatedAtUtc, nowUtc));

        if (isAjax)
        {
            return Json(new { success = true, count });
        }
        TempData["InventorySuccess"] = $"Ignored {count} item(s) for 30 days.";
        return RedirectToAction(nameof(NeedsAttention));
    }

    // Classifies a listing as Owned with a purchase cost. Available both from Needs Attention's
    // "Needs classification" section (AJAX) and the item Details page's always-available
    // "change classification" control (classic post). Re-classifying an already-Owned or
    // already-Consignment item just overwrites the classification — reclassification is
    // intentionally allowed any time, not just when unclassified.
    // purchaseCost is deliberately nullable here, NOT a plain decimal: $0 is a legitimate,
    // explicit purchase cost (the item was free, or already owned before this feature existed),
    // so it must be distinguishable from "the field was left blank." A non-nullable decimal
    // parameter would silently model-bind a blank/missing field to 0, making an accidental
    // omission indistinguishable from an intentional $0 — this rejects that case instead of
    // guessing. Everywhere else in this codebase (views, JS, Stats, SaleRecord) already checks
    // PurchaseCost.HasValue, never PurchaseCost == 0 / > 0, to tell "set" from "unset" — keep it
    // that way in any code that touches this field.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ClassifyOwned(string itemId, decimal? purchaseCost)
    {
        var isAjax = string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(itemId))
        {
            return ErrorOrRedirect("Item ID is required.", isAjax, nameof(NeedsAttention));
        }
        if (!purchaseCost.HasValue)
        {
            return ErrorOrRedirect("Enter a purchase cost — use 0 if it was free or already owned.", isAjax, nameof(NeedsAttention));
        }
        if (purchaseCost.Value < 0)
        {
            return ErrorOrRedirect("Purchase cost can't be negative.", isAjax, nameof(NeedsAttention));
        }

        var username = User.Identity?.Name ?? string.Empty;
        var listing = await _dbContext.InventoryListings
            .FirstOrDefaultAsync(x => x.Username == username && x.ItemId == itemId);
        if (listing is null)
        {
            return ErrorOrRedirect("Item not found.", isAjax, nameof(NeedsAttention));
        }

        listing.OwnershipType = OwnershipTypes.Owned;
        listing.PurchaseCost = purchaseCost.Value;
        listing.ConsignorId = null;
        listing.ConsignmentSplitPercent = null;
        listing.ClassifiedAtUtc = DateTime.UtcNow;
        listing.UpdatedAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();
        // Classifying removes this item from Needs Attention's "Needs classification" count —
        // invalidate the nav badge's 60-second cache (see NeedsAttentionBadgeViewComponent) so
        // it reflects the new total on the very next page load instead of up to a minute later.
        _cache.Remove($"attention_count_{username}");

        if (isAjax)
        {
            return Json(new { success = true });
        }
        TempData["InventorySuccess"] = "Marked as Owned.";
        return RedirectToAction(nameof(Details), new { id = itemId });
    }

    // Classifies a listing as Consignment under the given consignor, snapshotting that
    // consignor's CURRENT default split % onto the listing (not live-linked — see
    // InventoryListing.ConsignmentSplitPercent) so a later edit to the consignor's default
    // doesn't silently change what an already-classified item pays out.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ClassifyConsignment(string itemId, Guid consignorId)
    {
        var isAjax = string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(itemId))
        {
            return ErrorOrRedirect("Item ID is required.", isAjax, nameof(NeedsAttention));
        }

        var username = User.Identity?.Name ?? string.Empty;
        var consignor = await _dbContext.Consignors
            .FirstOrDefaultAsync(x => x.Username == username && x.Id == consignorId);
        if (consignor is null)
        {
            return ErrorOrRedirect("Consignor not found.", isAjax, nameof(NeedsAttention));
        }

        var listing = await _dbContext.InventoryListings
            .FirstOrDefaultAsync(x => x.Username == username && x.ItemId == itemId);
        if (listing is null)
        {
            return ErrorOrRedirect("Item not found.", isAjax, nameof(NeedsAttention));
        }

        listing.OwnershipType = OwnershipTypes.Consignment;
        listing.ConsignorId = consignor.Id;
        listing.ConsignmentSplitPercent = consignor.DefaultSplitPercent;
        listing.PurchaseCost = null;
        listing.ClassifiedAtUtc = DateTime.UtcNow;
        listing.UpdatedAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();
        _cache.Remove($"attention_count_{username}");

        if (isAjax)
        {
            return Json(new { success = true });
        }
        TempData["InventorySuccess"] = $"Marked as Consignment ({consignor.Name}).";
        return RedirectToAction(nameof(Details), new { id = itemId });
    }

    // Reverses IgnoreAttention. Plain form-post control on the item Details page — deliberately
    // not AJAX, this is a low-frequency "undo" action, not part of the Needs Attention grid.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UnignoreAttention(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
        {
            return NotFound();
        }

        var username = User.Identity?.Name ?? string.Empty;
        var listing = await _dbContext.InventoryListings
            .FirstOrDefaultAsync(x => x.Username == username && x.ItemId == itemId);
        if (listing is null)
        {
            return NotFound();
        }

        listing.AttentionIgnoredPermanently = false;
        listing.AttentionIgnoredUntilUtc = null;
        listing.UpdatedAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        return RedirectToAction(nameof(Details), new { id = itemId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleAutoRelist(string itemId, bool enabled)
    {
        if (string.IsNullOrWhiteSpace(itemId))
        {
            return NotFound();
        }

        var username = User.Identity?.Name ?? string.Empty;
        var listing = await _dbContext.InventoryListings
            .FirstOrDefaultAsync(x => x.Username == username && x.ItemId == itemId);

        if (listing is null)
        {
            return NotFound();
        }

        listing.AutoRelistEnabled = enabled;
        listing.UpdatedAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        TempData["RelistSuccess"] = enabled ? "Auto end & relist enabled." : "Auto end & relist disabled.";
        return RedirectToAction(nameof(Details), new { id = itemId });
    }

    [HttpGet]
    public async Task<IActionResult> Details(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return NotFound();
        }

        var username = User.Identity?.Name ?? string.Empty;
        var listing = await _dbContext.InventoryListings
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Username == username && x.ItemId == id);

        if (listing is null)
        {
            return NotFound();
        }

        var model = new InventoryItemDetailsViewModel
        {
            ItemId = listing.ItemId,
            Title = listing.Title,
            CoverPhotoUrl = listing.CoverPhotoUrl,
            Barcode = listing.Barcode,
            IsActive = listing.IsActive,
            ListedAtUtc = listing.ListedAtUtc,
            LastSeenAtUtc = listing.LastSeenAtUtc,
            InactivatedAtUtc = listing.InactivatedAtUtc,
            AttentionIgnoredUntilUtc = listing.AttentionIgnoredUntilUtc,
            AttentionIgnoredPermanently = listing.AttentionIgnoredPermanently,
            AutoRelistEnabled = listing.AutoRelistEnabled,
            ListingEndTimeUtc = listing.ListingEndTimeUtc,
            ListingType = listing.ListingType,
            OwnershipType = listing.OwnershipType,
            ConsignmentSplitPercent = listing.ConsignmentSplitPercent,
            PurchaseCost = listing.PurchaseCost,
            ClassifiedAtUtc = listing.ClassifiedAtUtc
        };

        if (listing.ConsignorId.HasValue)
        {
            model.ConsignorName = await _dbContext.Consignors
                .AsNoTracking()
                .Where(x => x.Username == username && x.Id == listing.ConsignorId.Value)
                .Select(x => x.Name)
                .FirstOrDefaultAsync();
        }
        model.ConsignorOptions = await _dbContext.Consignors
            .AsNoTracking()
            .Where(x => x.Username == username)
            .OrderBy(x => x.Name)
            .Select(x => new ConsignorOptionViewModel { Id = x.Id, Name = x.Name, DefaultSplitPercent = x.DefaultSplitPercent })
            .ToListAsync();

        var boxAssignment = await _dbContext.InventoryItemBoxAssignments
            .AsNoTracking()
            .Where(x => x.Username == username && x.ItemId == id)
            .Join(
                _dbContext.Boxes.AsNoTracking(),
                a => a.BoxId,
                b => b.Id,
                (a, b) => new { a, b })
            .FirstOrDefaultAsync();

        if (boxAssignment != null)
        {
            model.Storage = new InventoryItemStorageViewModel
            {
                IsBox = true,
                DisplayText = $"Box {boxAssignment.b.BoxIdentifier} — {boxAssignment.b.ShelfUnitIdentifier}-{boxAssignment.b.ShelfNumber}",
                BoxId = boxAssignment.b.Id
            };
        }
        else
        {
            var shelfLocation = await _dbContext.InventoryItemLocations
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Username == username && x.ItemId == id);
            if (shelfLocation != null)
            {
                var shelfUnit = _shelfUnitStore.GetAll()
                    .FirstOrDefault(u => u.Identifier == shelfLocation.ShelfUnitIdentifier);
                model.Storage = new InventoryItemStorageViewModel
                {
                    IsBox = false,
                    DisplayText = $"{shelfLocation.ShelfUnitIdentifier}-{shelfLocation.ShelfNumber}",
                    ShelfUnitId = shelfUnit?.Id
                };
            }
        }

        model.Units = await BuildUnitDetailsAsync(username, listing.Id);

        var orders = await _dbContext.RecentOrders
            .AsNoTracking()
            .Where(x => x.Username == username && x.ItemId == id)
            .OrderByDescending(x => x.OrderCreatedAtUtc)
            .Take(20)
            .Select(x => new InventoryItemOrderRowViewModel
            {
                OrderId = x.OrderId,
                BuyerName = x.BuyerName,
                Quantity = x.Quantity,
                OrderCreatedAtUtc = x.OrderCreatedAtUtc,
                HasTracking = x.HasTracking,
                IsRetrieved = x.IsRetrieved
            })
            .ToListAsync();
        model.RecentOrders = orders;

        // A sale needs a specific unit picked when eBay's order data left more than one unsold
        // Listed unit as a candidate (see SaleResolutionService) — auto-resolves the unambiguous
        // case (down to exactly one candidate) as a side effect instead of leaving it stuck.
        var ambiguousUnits = await SaleResolutionService.AutoResolveAndGetAmbiguousAsync(_dbContext, username, [id]);
        if (ambiguousUnits.TryGetValue(id, out var candidateUnits))
        {
            var locationByUnitId = await SaleResolutionService.BuildLocationDisplayByUnitIdAsync(_dbContext, username);
            model.UnsoldOrderUnitOptions = candidateUnits
                .Select(u => new UnsoldUnitOptionViewModel
                {
                    UnitId = u.Id,
                    Barcode = u.Barcode,
                    LocationDisplay = locationByUnitId.TryGetValue(u.Id, out var loc) ? loc : "No location"
                })
                .ToList();
        }

        var relistHistory = await _dbContext.RelistHistories
            .AsNoTracking()
            .Where(x => x.Username == username && x.Listing != null && x.Listing.ItemId == id)
            .OrderByDescending(x => x.RelistedAtUtc)
            .Take(20)
            .Select(x => new RelistHistoryRowViewModel
            {
                OldItemId = x.OldItemId,
                NewItemId = x.NewItemId,
                RelistedAtUtc = x.RelistedAtUtc,
                ErrorMessage = x.ErrorMessage
            })
            .ToListAsync();
        model.RelistHistory = relistHistory;

        var priceReductionHistory = await _dbContext.PriceReductionHistories
            .AsNoTracking()
            .Where(x => x.Username == username && x.Listing != null && x.Listing.ItemId == id)
            .OrderByDescending(x => x.ReducedAtUtc)
            .Take(20)
            .Select(x => new PriceReductionHistoryRowViewModel
            {
                OldPrice = x.OldPrice,
                NewPrice = x.NewPrice,
                ReducedAtUtc = x.ReducedAtUtc,
                ChangeType = x.ChangeType
            })
            .ToListAsync();
        model.PriceReductionHistory = priceReductionHistory;

        if (_ebayService.IsConnected())
        {
            model.EbayDetails = await _ebayService.GetItemDetailsAsync(id);

            if (model.EbayDetails != null)
            {
                var freshType = model.EbayDetails.ListingType;
                var freshEnd = model.EbayDetails.EndTime;

                // Use fresh eBay values for this render
                if (!string.IsNullOrWhiteSpace(freshType))
                    model.ListingType = freshType;
                if (freshEnd.HasValue)
                    model.ListingEndTimeUtc = freshEnd;

                // Persist to DB so AutoRelistWorker and future page loads have the data
                var q = _dbContext.InventoryListings.Where(x => x.Username == username && x.ItemId == id);
                if (!string.IsNullOrWhiteSpace(freshType) && freshEnd.HasValue)
                    await q.ExecuteUpdateAsync(s => s
                        .SetProperty(x => x.ListingType, freshType)
                        .SetProperty(x => x.ListingEndTimeUtc, freshEnd)
                        .SetProperty(x => x.UpdatedAtUtc, DateTime.UtcNow));
                else if (!string.IsNullOrWhiteSpace(freshType))
                    await q.ExecuteUpdateAsync(s => s
                        .SetProperty(x => x.ListingType, freshType)
                        .SetProperty(x => x.UpdatedAtUtc, DateTime.UtcNow));
                else if (freshEnd.HasValue)
                    await q.ExecuteUpdateAsync(s => s
                        .SetProperty(x => x.ListingEndTimeUtc, freshEnd)
                        .SetProperty(x => x.UpdatedAtUtc, DateTime.UtcNow));
            }
        }

        return View(model);
    }

    // Also called via fetch (form-encoded body, same field names) from the Needs Attention
    // page's quick-update — detected via the X-Requested-With header, same isAjax convention
    // used by RemoveFromStorage/AssignLocation/AssignBox elsewhere in this controller. The
    // Details page's classic <form> post keeps working unchanged either way.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdatePrice(string itemId, decimal newPrice, decimal? oldPrice)
    {
        var isAjax = string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(itemId))
        {
            if (isAjax) return Json(new { success = false, message = "Item ID is required." });
            TempData["EbayPriceError"] = "Item ID is required.";
            return RedirectToAction(nameof(Details), new { id = itemId });
        }

        if (newPrice <= 0)
        {
            if (isAjax) return Json(new { success = false, message = "Price must be greater than zero." });
            TempData["EbayPriceError"] = "Price must be greater than zero.";
            return RedirectToAction(nameof(Details), new { id = itemId });
        }

        var username = User.Identity?.Name ?? string.Empty;

        try
        {
            await _ebayService.UpdateItemPriceAsync(itemId, newPrice);

            var listing = await _dbContext.InventoryListings
                .FirstOrDefaultAsync(x => x.Username == username && x.ItemId == itemId);

            if (listing != null)
            {
                var previousPrice = oldPrice ?? listing.CurrentPrice ?? newPrice;
                _dbContext.PriceReductionHistories.Add(new Models.PriceReductionHistory
                {
                    Username = username,
                    ListingId = listing.Id,
                    OldPrice = previousPrice,
                    NewPrice = newPrice,
                    ReducedAtUtc = DateTime.UtcNow,
                    ChangeType = "Manual"
                });
                listing.CurrentPrice = newPrice;
                listing.UpdatedAtUtc = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();
            }

            if (isAjax) return Json(new { success = true, newPrice });

            TempData["EbayPriceSuccess"] = $"Price updated to {newPrice:N2} on eBay.";
        }
        catch (Exception ex)
        {
            if (isAjax) return Json(new { success = false, message = ex.Message });
            TempData["EbayPriceError"] = ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id = itemId });
    }

    // Bulk Reprice page: a GET form (so the rule lives in the URL and Preview is just a reload)
    // that lists every active listing not repriced in `days` days with its computed new price.
    // Nothing is changed here — applying happens one item at a time via BulkRepriceItem below.
    [HttpGet]
    public async Task<IActionResult> BulkReprice(int? days, string? mode, decimal? amount, decimal? minPrice)
    {
        var username = User.Identity?.Name ?? string.Empty;
        var model = new BulkRepricePageViewModel
        {
            Days = days ?? RepriceRules.DefaultDays,
            Mode = mode ?? RepriceRules.ModePercent,
            Amount = amount ?? RepriceRules.DefaultAmount,
            MinPrice = minPrice
        };

        model.Error = RepriceRules.Validate(model.Days, model.Mode, model.Amount, model.MinPrice);
        if (model.Error != null)
        {
            return View(model);
        }

        var candidates = await RepriceRules.GetCandidatesAsync(_dbContext, username, model.Days, DateTime.UtcNow);
        model.Rows = candidates
            .OrderBy(x => x.LastRepricedUtc)
            .ThenBy(x => x.Title)
            .Select(x =>
            {
                var result = RepriceRules.Compute(x.CurrentPrice, model.Mode, model.Amount, model.MinPrice);
                return new BulkRepriceRowViewModel
                {
                    ListingId = x.ListingId,
                    ItemId = x.ItemId,
                    Title = x.Title,
                    CoverPhotoUrl = x.CoverPhotoUrl,
                    CurrencyId = x.CurrencyId,
                    CurrentPrice = x.CurrentPrice,
                    LastRepricedUtc = x.LastRepricedUtc,
                    EverRepriced = x.EverRepriced,
                    NewPrice = result.NewPrice,
                    ClampedToMinimum = result.ClampedToMinimum,
                    SkipReason = result.SkipReason
                };
            })
            .ToList();

        return View(model);
    }

    // Applies the bulk rule to ONE listing (the page's JS walks the selected rows sequentially,
    // so a 200-item run can't hit the proxy timeout and each row reports its own result). The new
    // price is recomputed here from the rule — never trusted from the client — and expectedPrice
    // must still match the stored price, so a double-click or a stale preview can't reduce an
    // item twice.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkRepriceItem(Guid listingId, decimal expectedPrice, string mode, decimal amount, decimal? minPrice)
    {
        var username = User.Identity?.Name ?? string.Empty;

        var error = RepriceRules.Validate(0, mode, amount, minPrice);
        if (error != null)
        {
            return Json(new { success = false, message = error });
        }

        var listing = await _dbContext.InventoryListings
            .FirstOrDefaultAsync(x => x.Username == username && x.Id == listingId && x.IsActive);
        if (listing?.CurrentPrice == null)
        {
            return Json(new { success = false, message = "Listing not found or no longer active." });
        }

        var currentPrice = listing.CurrentPrice.Value;
        if (currentPrice != expectedPrice)
        {
            return Json(new { success = false, message = $"Price changed since preview (now ${currentPrice:N2}) — refresh and try again." });
        }

        var result = RepriceRules.Compute(currentPrice, mode, amount, minPrice);
        if (result.NewPrice == null)
        {
            return Json(new { success = false, skipped = true, message = result.SkipReason });
        }

        var newPrice = result.NewPrice.Value;
        try
        {
            await _ebayService.UpdateItemPriceAsync(listing.ItemId, newPrice);
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }

        var nowUtc = DateTime.UtcNow;
        _dbContext.PriceReductionHistories.Add(new PriceReductionHistory
        {
            Username = username,
            ListingId = listing.Id,
            OldPrice = currentPrice,
            NewPrice = newPrice,
            ReducedAtUtc = nowUtc,
            ChangeType = RepriceRules.ChangeType
        });
        listing.CurrentPrice = newPrice;
        listing.UpdatedAtUtc = nowUtc;
        await _dbContext.SaveChangesAsync();

        return Json(new { success = true, newPrice });
    }

    [HttpGet]
    public async Task<IActionResult> MissingDataBarcodes()
    {
        var username = User.Identity?.Name ?? string.Empty;

        var locatedItemIds = await _dbContext.InventoryItemLocations
            .AsNoTracking()
            .Where(x => x.Username == username)
            .Select(x => x.ItemId)
            .ToListAsync();

        var boxedItemIds = await _dbContext.InventoryItemBoxAssignments
            .AsNoTracking()
            .Where(x => x.Username == username)
            .Select(x => x.ItemId)
            .ToListAsync();

        var locationSet = locatedItemIds.Concat(boxedItemIds).ToHashSet(StringComparer.Ordinal);

        var items = await _dbContext.InventoryListings
            .AsNoTracking()
            .Where(x => x.Username == username && x.IsActive)
            .OrderBy(x => x.Title)
            .ThenBy(x => x.ItemId)
            .Select(x => new { x.ItemId, x.Title, x.Barcode })
            .ToListAsync();

        var missing = items
            .Where(x => !locationSet.Contains(x.ItemId))
            .Select(x => new { x.Barcode, Title = TruncateAtWordBoundary(x.Title, 40) })
            .ToList();

        return Json(missing);
    }

    [HttpGet]
    public async Task<IActionResult> UnprintedBarcodes()
    {
        var username = User.Identity?.Name ?? string.Empty;

        var activeListingIds = await _dbContext.InventoryListings
            .AsNoTracking()
            .Where(x => x.Username == username && x.IsActive)
            .Select(x => x.Id)
            .ToListAsync();

        var units = await _dbContext.InventoryItemUnits
            .AsNoTracking()
            .Where(x => x.Username == username && x.LabelPrintedAtUtc == null && activeListingIds.Contains(x.ListingId))
            .OrderBy(x => x.ListingId)
            .ThenBy(x => x.UnitIndex)
            .ToListAsync();

        if (units.Count == 0)
        {
            return Json(Array.Empty<object>());
        }

        var listingTitlesById = await _dbContext.InventoryListings
            .AsNoTracking()
            .Where(x => x.Username == username && units.Select(u => u.ListingId).Distinct().Contains(x.Id))
            .Select(x => new { x.Id, x.Title })
            .ToDictionaryAsync(x => x.Id, x => x.Title);

        var unitCountByListingId = units
            .GroupBy(x => x.ListingId)
            .ToDictionary(g => g.Key, g => g.Count());

        var result = units
            .OrderBy(x => listingTitlesById.GetValueOrDefault(x.ListingId, ""))
            .ThenBy(x => x.UnitIndex)
            .Select(x =>
            {
                var title = TruncateAtWordBoundary(listingTitlesById.GetValueOrDefault(x.ListingId, ""), 40);
                var totalUnits = unitCountByListingId.GetValueOrDefault(x.ListingId, 1);
                var suffix = totalUnits > 1
                    ? $" (unit {x.UnitIndex} of {totalUnits}{(x.Status == InventoryItemUnitStatus.Backstock ? ", backstock" : "")})"
                    : x.Status == InventoryItemUnitStatus.Backstock ? " (backstock)" : "";
                return new
                {
                    UnitId = x.Id,
                    Barcode = x.Barcode,
                    Title = title + suffix
                };
            })
            .ToList();

        return Json(result);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkLabelPrinted([FromBody] MarkLabelPrintedRequest request)
    {
        if (request.UnitIds == null || request.UnitIds.Count == 0)
            return Json(new { success = false, message = "No unit IDs provided." });

        var username = User.Identity?.Name ?? string.Empty;
        var now = DateTime.UtcNow;

        var units = await _dbContext.InventoryItemUnits
            .Where(x => x.Username == username && request.UnitIds.Contains(x.Id) && x.LabelPrintedAtUtc == null)
            .ToListAsync();

        foreach (var unit in units)
            unit.LabelPrintedAtUtc = now;

        await _dbContext.SaveChangesAsync();
        return Json(new { success = true });
    }

    /// <summary>Every unit of a listing plus each one's own location, for the item Details page.
    /// Mirrors the per-unit location lookups in HomeController/OrdersController but scoped to a
    /// single listing rather than a batch, since Details renders one item at a time.</summary>
    private async Task<List<UnitDetailViewModel>> BuildUnitDetailsAsync(string username, Guid listingId)
    {
        var units = await _dbContext.InventoryItemUnits
            .AsNoTracking()
            .Where(x => x.Username == username && x.ListingId == listingId)
            .OrderBy(x => x.UnitIndex)
            .ToListAsync();
        if (units.Count == 0) return [];

        var unitIds = units.Select(x => x.Id).ToList();

        var shelfByUnitId = await _dbContext.InventoryItemLocations
            .AsNoTracking()
            .Where(x => x.Username == username && x.UnitId != null && unitIds.Contains(x.UnitId!.Value))
            .ToDictionaryAsync(x => x.UnitId!.Value, x => $"{x.ShelfUnitIdentifier}-{x.ShelfNumber}");

        var boxByUnitId = await _dbContext.InventoryItemBoxAssignments
            .AsNoTracking()
            .Where(x => x.Username == username && x.UnitId != null && unitIds.Contains(x.UnitId!.Value))
            .Join(_dbContext.Boxes.AsNoTracking(), a => a.BoxId, b => b.Id,
                (a, b) => new { UnitId = a.UnitId!.Value, Display = $"Box {b.BoxIdentifier}" })
            .ToDictionaryAsync(x => x.UnitId, x => x.Display);

        var suByUnitId = await _dbContext.InventoryItemStorageUnitAssignments
            .AsNoTracking()
            .Where(x => x.Username == username && x.UnitId != null && unitIds.Contains(x.UnitId!.Value))
            .Join(_dbContext.StorageUnits.AsNoTracking(), a => a.StorageUnitId, u => u.Id,
                (a, u) => new { UnitId = a.UnitId!.Value, u.Identifier })
            .ToDictionaryAsync(x => x.UnitId, x => x.Identifier);

        return units.Select(u => new UnitDetailViewModel
        {
            UnitId = u.Id,
            UnitIndex = u.UnitIndex,
            Barcode = u.Barcode,
            Status = u.Status,
            IsSold = u.IsSold,
            LabelPrintedAtUtc = u.LabelPrintedAtUtc,
            LocationDisplay = shelfByUnitId.GetValueOrDefault(u.Id)
                ?? boxByUnitId.GetValueOrDefault(u.Id)
                ?? suByUnitId.GetValueOrDefault(u.Id)
                ?? "No location"
        }).ToList();
    }

    private static string TruncateAtWordBoundary(string input, int maxLength)
    {
        var normalized = (input ?? "").Trim();
        if (normalized.Length <= maxLength) return normalized;
        var cutAt = normalized.LastIndexOf(' ', maxLength);
        return cutAt > 0 ? normalized[..cutAt] : normalized[..maxLength];
    }

    [HttpGet]
    public async Task<IActionResult> LoadMore(string mode = "active", int cursor = 1, string sort = "title")
    {
        var isMissingDataPage = string.Equals(mode, "missing", StringComparison.OrdinalIgnoreCase);
        var isUnprintedPage = string.Equals(mode, "unprinted", StringComparison.OrdinalIgnoreCase);
        var username = User.Identity?.Name ?? string.Empty;

        var model = new InventoryIndexViewModel
        {
            IsMissingDataPage = isMissingDataPage,
            IsUnprintedPage = isUnprintedPage,
            Sort = sort
        };
        PopulateShelfOptions(model);
        await PopulateBoxOptionsAsync(model);
        await PopulateSavedLocationsAsync(model);
        await PopulateSavedBoxAssignmentsAsync(model);

        foreach (var itemId in model.SavedBoxesByItemId.Keys)
        {
            model.SavedLocationsByItemId.Remove(itemId);
        }

        var (listings, hasMore, nextCursor) = await LoadListingsBatchAsync(
            username,
            isMissingDataPage,
            isUnprintedPage,
            cursor <= 0 ? 1 : cursor,
            sort);

        model.Listings = listings;
        model.HasMore = hasMore;
        model.NextCursor = nextCursor;

        Response.Headers["X-Has-More"] = hasMore ? "true" : "false";
        Response.Headers["X-Next-Cursor"] = nextCursor.ToString();
        return PartialView("_InventoryItems", model);
    }

    [HttpGet]
    public async Task<IActionResult> Search(string? q, string mode = "active", string sort = "title")
    {
        var username = User.Identity?.Name ?? string.Empty;
        var isMissingDataPage = string.Equals(mode, "missing", StringComparison.OrdinalIgnoreCase);
        var isUnprintedPage = string.Equals(mode, "unprinted", StringComparison.OrdinalIgnoreCase);

        var model = new InventoryIndexViewModel
        {
            IsMissingDataPage = isMissingDataPage,
            IsUnprintedPage = isUnprintedPage,
            Sort = sort
        };
        PopulateShelfOptions(model);
        await PopulateBoxOptionsAsync(model);
        await PopulateSavedLocationsAsync(model);
        await PopulateSavedBoxAssignmentsAsync(model);

        foreach (var itemId in model.SavedBoxesByItemId.Keys)
            model.SavedLocationsByItemId.Remove(itemId);

        var words = (q ?? string.Empty)
            .Trim()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(w => w.ToLowerInvariant())
            .ToArray();

        if (words.Length == 0)
            return PartialView("_InventoryItems", model);

        IQueryable<InventoryListing> baseQuery = _dbContext.InventoryListings
            .AsNoTracking()
            .Where(x => x.Username == username && x.IsActive);

        if (isUnprintedPage)
        {
            baseQuery = baseQuery.Where(x => _dbContext.InventoryItemUnits
                .Any(u => u.Username == username && u.ListingId == x.Id && u.LabelPrintedAtUtc == null));
        }
        else if (isMissingDataPage)
        {
            var locationSet = model.SavedLocationsByItemId.Keys
                .Concat(model.SavedBoxesByItemId.Keys)
                .ToHashSet(StringComparer.Ordinal);
            baseQuery = baseQuery.Where(x => !locationSet.Contains(x.ItemId));
        }

        IOrderedQueryable<InventoryListing> ordered = sort switch
        {
            "date_desc" => baseQuery.OrderByDescending(x => x.ListedAtUtc).ThenBy(x => x.ItemId),
            "date_asc"  => baseQuery.OrderBy(x => x.ListedAtUtc).ThenBy(x => x.ItemId),
            "price_asc" => baseQuery.OrderBy(x => x.CurrentPrice == null ? 1 : 0).ThenBy(x => x.CurrentPrice).ThenBy(x => x.ItemId),
            "price_desc" => baseQuery.OrderByDescending(x => x.CurrentPrice ?? decimal.MinValue).ThenBy(x => x.ItemId),
            _           => baseQuery.OrderBy(x => x.Title).ThenBy(x => x.ItemId),
        };

        var allListings = await ordered.ToListAsync();

        // Build a searchable location string per item (already loaded above)
        var locationTextByItemId = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (itemId, loc) in model.SavedLocationsByItemId)
            locationTextByItemId[itemId] = $"{loc.ShelfUnitIdentifier}-{loc.ShelfNumber} {loc.ShelfBarcode}".ToLowerInvariant();
        foreach (var (itemId, box) in model.SavedBoxesByItemId)
        {
            var boxText = $"box {box.BoxIdentifier} {box.BoxBarcode} {box.ShelfUnitIdentifier}-{box.ShelfNumber} {box.ShelfBarcode}".ToLowerInvariant();
            locationTextByItemId[itemId] = locationTextByItemId.TryGetValue(itemId, out var prev) ? prev + " " + boxText : boxText;
        }

        var matchedListings = allListings
            .Where(x =>
            {
                locationTextByItemId.TryGetValue(x.ItemId, out var locText);
                var searchable = $"{x.Title} {x.ItemId} {x.Barcode} {locText ?? string.Empty}".ToLowerInvariant();
                return words.All(w => searchable.Contains(w));
            })
            .ToList();

        model.Listings = await BuildEbayListingViewModelsAsync(username, matchedListings, includeUnprintedUnits: isUnprintedPage);

        return PartialView("_InventoryItems", model);
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
            var synced = await SyncInventoryListingsFromEbayAsync(username);
            return Json(new { ok = synced });
        }
        catch
        {
            return Json(new { ok = false });
        }
    }

    private async Task<IActionResult> BuildInventoryPageAsync(bool isMissingDataPage, bool isUnprintedPage = false, string sort = "title")
    {
        var pageTitle = isMissingDataPage ? "Missing Location" : isUnprintedPage ? "Unprinted Labels" : "Active Inventory";
        var model = new InventoryIndexViewModel
        {
            IsConnected = _ebayService.IsConnected(),
            IsMissingDataPage = isMissingDataPage,
            IsUnprintedPage = isUnprintedPage,
            PageTitle = pageTitle,
            Sort = sort
        };
        PopulateShelfOptions(model);
        await PopulateBoxOptionsAsync(model);

        if (!_ebayService.IsConfigured())
        {
            model.ErrorMessage = "eBay API settings are missing. Add Ebay config in appsettings.Development.json.";
            return View("Index", model);
        }

        if (!model.IsConnected)
        {
            return View("Index", model);
        }

        try
        {
            var username = User.Identity?.Name ?? string.Empty;
            await PopulateSavedLocationsAsync(model);
            await PopulateSavedBoxAssignmentsAsync(model);

            foreach (var itemId in model.SavedBoxesByItemId.Keys)
            {
                model.SavedLocationsByItemId.Remove(itemId);
            }

            var (listings, hasMore, nextCursor) = await LoadListingsBatchAsync(username, isMissingDataPage, isUnprintedPage, 1, sort);
            model.Listings = listings;
            model.HasMore = hasMore;
            model.NextCursor = nextCursor;
            model.TotalCount = await CountListingsAsync(username, isMissingDataPage, isUnprintedPage);
        }
        catch (Exception ex)
        {
            model.ErrorMessage = ex.Message;
        }

        return View("Index", model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult ConnectEbay()
    {
        if (!_ebayService.IsConfigured())
        {
            return RedirectToInventoryPage();
        }

        var state = CreateSignedState();
        var authorizeUrl = _ebayService.BuildAuthorizeUrl(state);
        return Redirect(authorizeUrl);
    }

    public async Task<IActionResult> EbayCallback(string? code, string? state, string? error, string? error_description)
    {
        if (!ValidateSignedState(state))
        {
            TempData["InventoryError"] = "Invalid eBay OAuth state. Please try connecting again.";
            return RedirectToInventoryPage();
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            var detail = !string.IsNullOrWhiteSpace(error_description) ? error_description
                       : !string.IsNullOrWhiteSpace(error) ? error
                       : "no authorization code returned";
            TempData["InventoryError"] = $"eBay authorization failed: {detail}";
            return RedirectToInventoryPage();
        }

        try
        {
            await _ebayService.ExchangeCodeForTokensAsync(code);
        }
        catch (Exception ex)
        {
            TempData["InventoryError"] = ex.Message;
        }

        return RedirectToInventoryPage();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult DisconnectEbay(string? returnAction = null)
    {
        _ebayService.Disconnect();
        return RedirectToInventoryPage(returnAction);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult AssignLocation(AssignInventoryLocationViewModel model, string? returnAction = null)
    {
        var isAjax = string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);

        if (!ModelState.IsValid)
        {
            return ErrorOrRedirect("Invalid location input.", isAjax, returnAction);
        }

        var unit = _shelfUnitStore.GetById(model.ShelfUnitId);
        if (unit is null)
        {
            return ErrorOrRedirect("Selected shelving unit was not found.", isAjax, returnAction);
        }

        if (model.ShelfNumber < 1 || model.ShelfNumber > unit.ShelfCount)
        {
            return ErrorOrRedirect("Selected shelf number is out of range for that unit.", isAjax, returnAction);
        }

        var prefix = unit.BarcodeNumber.EndsWith('0')
            ? unit.BarcodeNumber[..^1]
            : unit.BarcodeNumber;
        var shelfBarcode = $"{prefix}{model.ShelfNumber}";
        var username = User.Identity?.Name ?? string.Empty;

        var existing = _dbContext.InventoryItemLocations
            .FirstOrDefault(x => x.Username == username && x.ItemId == model.ItemId);

        if (existing is null)
        {
            existing = new InventoryItemLocation
            {
                Username = username,
                ItemId = model.ItemId
            };
            _dbContext.InventoryItemLocations.Add(existing);
        }

        existing.ItemTitle = model.ItemTitle;
        existing.ShelfUnitIdentifier = unit.Identifier;
        existing.ShelfNumber = model.ShelfNumber;
        existing.ShelfBarcode = shelfBarcode;
        existing.UpdatedAtUtc = DateTime.UtcNow;
        existing.IsSold = false;

        var existingBoxAssignment = _dbContext.InventoryItemBoxAssignments
            .FirstOrDefault(x => x.Username == username && x.ItemId == model.ItemId);
        if (existingBoxAssignment is not null)
        {
            _dbContext.InventoryItemBoxAssignments.Remove(existingBoxAssignment);
        }

        _dbContext.SaveChanges();
        if (isAjax)
        {
            return Json(new
            {
                success = true,
                shelfUnitIdentifier = unit.Identifier,
                shelfNumber = model.ShelfNumber,
                shelfBarcode,
                removeFromMissingData = string.Equals(returnAction, nameof(MissingData), StringComparison.OrdinalIgnoreCase)
            });
        }

        return RedirectToInventoryPage(returnAction);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult AssignBox(AssignInventoryBoxViewModel model, string? returnAction = null)
    {
        var isAjax = string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);

        if (!ModelState.IsValid)
        {
            return ErrorOrRedirect("Invalid box input.", isAjax, returnAction);
        }

        var username = User.Identity?.Name ?? string.Empty;
        var box = _dbContext.Boxes.FirstOrDefault(x => x.Username == username && x.Id == model.BoxId);
        if (box is null)
        {
            return ErrorOrRedirect("Selected box was not found.", isAjax, returnAction);
        }

        var existing = _dbContext.InventoryItemBoxAssignments
            .FirstOrDefault(x => x.Username == username && x.ItemId == model.ItemId);

        if (existing is null)
        {
            existing = new InventoryItemBoxAssignment
            {
                Username = username,
                ItemId = model.ItemId
            };
            _dbContext.InventoryItemBoxAssignments.Add(existing);
        }

        existing.ItemTitle = model.ItemTitle;
        existing.BoxId = box.Id;
        existing.BoxBarcode = box.BoxBarcode;
        existing.UpdatedAtUtc = DateTime.UtcNow;
        existing.IsSold = false;

        var existingShelfLocation = _dbContext.InventoryItemLocations
            .FirstOrDefault(x => x.Username == username && x.ItemId == model.ItemId);
        if (existingShelfLocation is not null)
        {
            _dbContext.InventoryItemLocations.Remove(existingShelfLocation);
        }

        _dbContext.SaveChanges();
        var message = $"Assigned item '{model.ItemTitle}' to box '{box.BoxBarcode}'.";
        if (isAjax)
        {
            return Json(new
            {
                success = true,
                message,
                boxBarcode = box.BoxBarcode,
                shelfUnitIdentifier = box.ShelfUnitIdentifier,
                shelfNumber = box.ShelfNumber,
                shelfBarcode = box.ShelfBarcode,
                removeFromMissingData = string.Equals(returnAction, nameof(MissingData), StringComparison.OrdinalIgnoreCase)
            });
        }

        TempData["InventorySuccess"] = message;
        return RedirectToInventoryPage(returnAction);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkAssignLocation([FromBody] BulkAssignLocationViewModel model)
    {
        if (!ModelState.IsValid || model.ItemIds.Count == 0)
        {
            return Json(new { success = false, message = "Invalid input." });
        }

        var unit = _shelfUnitStore.GetById(model.ShelfUnitId);
        if (unit is null)
        {
            return Json(new { success = false, message = "Selected shelving unit was not found." });
        }

        if (model.ShelfNumber < 1 || model.ShelfNumber > unit.ShelfCount)
        {
            return Json(new { success = false, message = "Selected shelf number is out of range for that unit." });
        }

        var prefix = unit.BarcodeNumber.EndsWith('0') ? unit.BarcodeNumber[..^1] : unit.BarcodeNumber;
        var shelfBarcode = $"{prefix}{model.ShelfNumber}";
        var username = User.Identity?.Name ?? string.Empty;

        var titles = await _dbContext.InventoryListings
            .AsNoTracking()
            .Where(x => x.Username == username && model.ItemIds.Contains(x.ItemId))
            .ToDictionaryAsync(x => x.ItemId, x => x.Title);

        // A multi-unit item can have more than one location row (one per unit) sharing the same
        // ItemId — bulk assign only ever targets one location per item, so pick one to update
        // (arbitrarily, by grouping) rather than crashing on the duplicate key.
        var existingLocations = (await _dbContext.InventoryItemLocations
            .Where(x => x.Username == username && model.ItemIds.Contains(x.ItemId))
            .ToListAsync())
            .GroupBy(x => x.ItemId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var existingBoxes = await _dbContext.InventoryItemBoxAssignments
            .Where(x => x.Username == username && model.ItemIds.Contains(x.ItemId))
            .ToListAsync();

        foreach (var itemId in model.ItemIds)
        {
            if (!titles.ContainsKey(itemId)) continue;

            if (existingLocations.TryGetValue(itemId, out var loc))
            {
                loc.ItemTitle = titles[itemId];
                loc.ShelfUnitIdentifier = unit.Identifier;
                loc.ShelfNumber = model.ShelfNumber;
                loc.ShelfBarcode = shelfBarcode;
                loc.UpdatedAtUtc = DateTime.UtcNow;
                loc.IsSold = false;
            }
            else
            {
                _dbContext.InventoryItemLocations.Add(new InventoryItemLocation
                {
                    Username = username,
                    ItemId = itemId,
                    ItemTitle = titles[itemId],
                    ShelfUnitIdentifier = unit.Identifier,
                    ShelfNumber = model.ShelfNumber,
                    ShelfBarcode = shelfBarcode,
                    UpdatedAtUtc = DateTime.UtcNow
                });
            }
        }

        if (existingBoxes.Count > 0)
        {
            _dbContext.InventoryItemBoxAssignments.RemoveRange(existingBoxes);
        }

        await _dbContext.SaveChangesAsync();

        return Json(new
        {
            success = true,
            shelfUnitIdentifier = unit.Identifier,
            shelfNumber = model.ShelfNumber,
            shelfBarcode,
            assignedCount = titles.Count
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkAssignBox([FromBody] BulkAssignBoxViewModel model)
    {
        if (!ModelState.IsValid || model.ItemIds.Count == 0)
        {
            return Json(new { success = false, message = "Invalid input." });
        }

        var username = User.Identity?.Name ?? string.Empty;
        var box = await _dbContext.Boxes.FirstOrDefaultAsync(x => x.Username == username && x.Id == model.BoxId);
        if (box is null)
        {
            return Json(new { success = false, message = "Selected box was not found." });
        }

        var titles = await _dbContext.InventoryListings
            .AsNoTracking()
            .Where(x => x.Username == username && model.ItemIds.Contains(x.ItemId))
            .ToDictionaryAsync(x => x.ItemId, x => x.Title);

        // Same reasoning as BulkAssignLocation above: a multi-unit item can have more than one
        // box-assignment row sharing the same ItemId, so pick one to update instead of crashing.
        var existingBoxes = (await _dbContext.InventoryItemBoxAssignments
            .Where(x => x.Username == username && model.ItemIds.Contains(x.ItemId))
            .ToListAsync())
            .GroupBy(x => x.ItemId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var existingLocations = await _dbContext.InventoryItemLocations
            .Where(x => x.Username == username && model.ItemIds.Contains(x.ItemId))
            .ToListAsync();

        foreach (var itemId in model.ItemIds)
        {
            if (!titles.ContainsKey(itemId)) continue;

            if (existingBoxes.TryGetValue(itemId, out var assignment))
            {
                assignment.ItemTitle = titles[itemId];
                assignment.BoxId = box.Id;
                assignment.BoxBarcode = box.BoxBarcode;
                assignment.UpdatedAtUtc = DateTime.UtcNow;
                assignment.IsSold = false;
            }
            else
            {
                _dbContext.InventoryItemBoxAssignments.Add(new InventoryItemBoxAssignment
                {
                    Username = username,
                    ItemId = itemId,
                    ItemTitle = titles[itemId],
                    BoxId = box.Id,
                    BoxBarcode = box.BoxBarcode,
                    UpdatedAtUtc = DateTime.UtcNow
                });
            }
        }

        if (existingLocations.Count > 0)
        {
            _dbContext.InventoryItemLocations.RemoveRange(existingLocations);
        }

        await _dbContext.SaveChangesAsync();

        return Json(new
        {
            success = true,
            boxIdentifier = box.BoxIdentifier,
            boxBarcode = box.BoxBarcode,
            shelfUnitIdentifier = box.ShelfUnitIdentifier,
            shelfNumber = box.ShelfNumber,
            shelfBarcode = box.ShelfBarcode,
            assignedCount = titles.Count
        });
    }

    // Shared by the main inventory page and every storage-location Details page (Box, Shelf,
    // StorageUnit): clears whatever placement selected items currently have — shelf slot, box,
    // or direct storage-unit assignment — regardless of which page the request came from, since
    // an item can only ever be in one of those at a time.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkRemoveLocation([FromBody] BulkRemoveLocationViewModel model)
    {
        if (!ModelState.IsValid || model.ItemIds.Count == 0)
        {
            return Json(new { success = false, message = "Invalid input." });
        }

        var username = User.Identity?.Name ?? string.Empty;

        var removedLocations = await _dbContext.InventoryItemLocations
            .Where(x => x.Username == username && model.ItemIds.Contains(x.ItemId))
            .ExecuteDeleteAsync();
        var removedBoxes = await _dbContext.InventoryItemBoxAssignments
            .Where(x => x.Username == username && model.ItemIds.Contains(x.ItemId))
            .ExecuteDeleteAsync();
        var removedStorageUnits = await _dbContext.InventoryItemStorageUnitAssignments
            .Where(x => x.Username == username && model.ItemIds.Contains(x.ItemId))
            .ExecuteDeleteAsync();

        return Json(new
        {
            success = true,
            removedCount = removedLocations + removedBoxes + removedStorageUnits
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult RemoveFromStorage(string ItemId, string? returnAction = null, string? ItemTitle = null)
    {
        var isAjax = string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(ItemId))
        {
            return ErrorOrRedirect("Item ID is required.", isAjax, returnAction);
        }

        var username = User.Identity?.Name ?? string.Empty;

        var shelfLocation = _dbContext.InventoryItemLocations
            .FirstOrDefault(x => x.Username == username && x.ItemId == ItemId);
        if (shelfLocation is not null)
        {
            _dbContext.InventoryItemLocations.Remove(shelfLocation);
        }

        var boxAssignment = _dbContext.InventoryItemBoxAssignments
            .FirstOrDefault(x => x.Username == username && x.ItemId == ItemId);
        if (boxAssignment is not null)
        {
            _dbContext.InventoryItemBoxAssignments.Remove(boxAssignment);
        }

        _dbContext.SaveChanges();

        var title = (ItemTitle ?? ItemId).Trim();
        var message = string.IsNullOrEmpty(title) ? "Storage location removed." : $"Storage location removed for '{title}'.";

        if (isAjax)
        {
            return Json(new
            {
                success = true,
                message,
                addToMissingData = string.Equals(returnAction, nameof(ActiveInventory), StringComparison.OrdinalIgnoreCase)
            });
        }

        TempData["InventorySuccess"] = message;
        return RedirectToInventoryPage(returnAction);
    }

    private IActionResult RedirectToInventoryPage(string? returnAction = null)
    {
        if (string.Equals(returnAction, nameof(MissingData), StringComparison.OrdinalIgnoreCase))
        {
            return RedirectToAction(nameof(MissingData));
        }

        if (string.Equals(returnAction, nameof(NeedsAttention), StringComparison.OrdinalIgnoreCase))
        {
            return RedirectToAction(nameof(NeedsAttention));
        }

        return RedirectToAction(nameof(ActiveInventory));
    }

    private IActionResult ErrorOrRedirect(string message, bool isAjax, string? returnAction)
    {
        if (isAjax)
        {
            return Json(new { success = false, message });
        }

        TempData["InventoryError"] = message;
        return RedirectToInventoryPage(returnAction);
    }

    private void PopulateShelfOptions(InventoryIndexViewModel model)
    {
        model.ShelfUnits = _shelfUnitStore.GetAll()
            .Select(unit => new ShelfUnitOptionViewModel
            {
                Id = unit.Id,
                Identifier = unit.Identifier,
                ShelfCount = unit.ShelfCount,
                BarcodePrefix = unit.BarcodeNumber.EndsWith('0')
                    ? unit.BarcodeNumber[..^1]
                    : unit.BarcodeNumber
            })
            .ToList();
    }

    private async Task PopulateSavedLocationsAsync(InventoryIndexViewModel model)
    {
        var username = User.Identity?.Name ?? string.Empty;
        var saved = await _dbContext.InventoryItemLocations
            .AsNoTracking()
            .Where(x => x.Username == username)
            .ToListAsync();

        // A listing with more than one physical unit can now have more than one location row
        // (one per unit, all sharing the same ItemId) — this summary dictionary backs the
        // per-listing row badge on the inventory list, not the per-unit detail view, so when a
        // listing has multiple locations we just show its most recently updated one here.
        model.SavedLocationsByItemId = saved
            .GroupBy(x => x.ItemId, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var x = g.OrderByDescending(l => l.UpdatedAtUtc).First();
                    return new ItemLocationViewModel
                    {
                        ShelfUnitIdentifier = x.ShelfUnitIdentifier,
                        ShelfNumber = x.ShelfNumber,
                        ShelfBarcode = x.ShelfBarcode
                    };
                },
                StringComparer.Ordinal);
    }

    private async Task PopulateBoxOptionsAsync(InventoryIndexViewModel model)
    {
        var username = User.Identity?.Name ?? string.Empty;
        model.Boxes = await _dbContext.Boxes
            .AsNoTracking()
            .Where(x => x.Username == username)
            .OrderBy(x => x.BoxIdentifier)
            .Select(x => new BoxOptionViewModel
            {
                Id = x.Id,
                BoxIdentifier = x.BoxIdentifier,
                BoxBarcode = x.BoxBarcode
            })
            .ToListAsync();
    }

    private async Task PopulateSavedBoxAssignmentsAsync(InventoryIndexViewModel model)
    {
        var username = User.Identity?.Name ?? string.Empty;
        var saved = await _dbContext.InventoryItemBoxAssignments
            .AsNoTracking()
            .Where(x => x.Username == username)
            .Join(
                _dbContext.Boxes.AsNoTracking(),
                assignment => assignment.BoxId,
                box => box.Id,
                (assignment, box) => new
                {
                    assignment.ItemId,
                    assignment.BoxBarcode,
                    assignment.UpdatedAtUtc,
                    box.BoxIdentifier,
                    box.ShelfUnitIdentifier,
                    box.ShelfNumber,
                    box.ShelfBarcode
                })
            .ToListAsync();

        // Same reasoning as PopulateSavedLocationsAsync above: a multi-unit listing can now have
        // more than one box-assignment row sharing the same ItemId, so this per-listing summary
        // dictionary shows the most recently updated one.
        model.SavedBoxesByItemId = saved
            .GroupBy(x => x.ItemId, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var x = g.OrderByDescending(b => b.UpdatedAtUtc).First();
                    return new ItemBoxViewModel
                    {
                        BoxIdentifier = x.BoxIdentifier,
                        BoxBarcode = x.BoxBarcode,
                        ShelfUnitIdentifier = x.ShelfUnitIdentifier,
                        ShelfNumber = x.ShelfNumber,
                        ShelfBarcode = x.ShelfBarcode
                    };
                },
                StringComparer.Ordinal);
    }

    private Task<int> CountListingsAsync(string username, bool isMissingDataPage, bool isUnprintedPage = false)
        => InventoryCounts.CountListingsAsync(_dbContext, username, isMissingDataPage, isUnprintedPage);

    private async Task<(List<EbayListingViewModel> Listings, bool HasMore, int NextCursor)> LoadListingsBatchAsync(
        string username,
        bool isMissingDataPage,
        bool isUnprintedPage,
        int cursor,
        string sort = "title")
    {
        var skip = Math.Max(cursor - 1, 0) * InventoryBatchSize;

        IQueryable<InventoryListing> query = _dbContext.InventoryListings
            .AsNoTracking()
            .Where(x => x.Username == username && x.IsActive);

        if (isUnprintedPage)
        {
            query = query.Where(x => _dbContext.InventoryItemUnits
                .Any(u => u.Username == username && u.ListingId == x.Id && u.LabelPrintedAtUtc == null));
        }
        else if (isMissingDataPage)
        {
            var locatedItemIds = await _dbContext.InventoryItemLocations
                .AsNoTracking()
                .Where(x => x.Username == username)
                .Select(x => x.ItemId)
                .ToListAsync();

            var boxedItemIds = await _dbContext.InventoryItemBoxAssignments
                .AsNoTracking()
                .Where(x => x.Username == username)
                .Select(x => x.ItemId)
                .ToListAsync();

            var locationSet = locatedItemIds.Concat(boxedItemIds).ToHashSet(StringComparer.Ordinal);
            query = query.Where(x => !locationSet.Contains(x.ItemId));
        }

        IOrderedQueryable<InventoryListing> ordered = sort switch
        {
            "date_desc" => query.OrderByDescending(x => x.ListedAtUtc).ThenBy(x => x.ItemId),
            "date_asc"  => query.OrderBy(x => x.ListedAtUtc).ThenBy(x => x.ItemId),
            "price_asc" => query.OrderBy(x => x.CurrentPrice == null ? 1 : 0).ThenBy(x => x.CurrentPrice).ThenBy(x => x.ItemId),
            "price_desc" => query.OrderByDescending(x => x.CurrentPrice ?? decimal.MinValue).ThenBy(x => x.ItemId),
            _           => query.OrderBy(x => x.Title).ThenBy(x => x.ItemId),
        };

        var batch = await ordered
            .Skip(skip)
            .Take(InventoryBatchSize + 1)
            .ToListAsync();

        var hasMore = batch.Count > InventoryBatchSize;
        var pageOfListings = batch.Take(InventoryBatchSize).ToList();
        var listings = await BuildEbayListingViewModelsAsync(username, pageOfListings, includeUnprintedUnits: isUnprintedPage);

        return (listings, hasMore, cursor + 1);
    }

    /// <summary>Builds EbayListingViewModels for a page of listings, including PrimaryUnitId
    /// (unit 1, reusing the listing's own Barcode) and, for callers on the Unprinted Labels
    /// page, each listing's still-unprinted units so the view can render one print row per
    /// unit rather than per listing.</summary>
    private async Task<List<EbayListingViewModel>> BuildEbayListingViewModelsAsync(
        string username, List<InventoryListing> pageOfListings, bool includeUnprintedUnits = false)
    {
        var listingIds = pageOfListings.Select(x => x.Id).ToList();
        var unitsByListingId = await _dbContext.InventoryItemUnits
            .AsNoTracking()
            .Where(x => x.Username == username && listingIds.Contains(x.ListingId))
            .OrderBy(x => x.UnitIndex)
            .ToListAsync();
        var unitsLookup = unitsByListingId.GroupBy(x => x.ListingId).ToDictionary(g => g.Key, g => g.ToList());

        return pageOfListings.Select(x =>
        {
            var units = unitsLookup.GetValueOrDefault(x.Id, []);
            var primaryUnitId = units.FirstOrDefault(u => u.UnitIndex == 1)?.Id ?? Guid.Empty;
            var vm = new EbayListingViewModel
            {
                ItemId = x.ItemId,
                Title = x.Title,
                CoverPhotoUrl = x.CoverPhotoUrl,
                Barcode = x.Barcode,
                ListedAtUtc = x.ListedAtUtc,
                CurrentPrice = x.CurrentPrice,
                CurrencyId = x.CurrencyId,
                LabelPrintedAtUtc = x.LabelPrintedAtUtc,
                QuantityAvailable = x.QuantityAvailable,
                PrimaryUnitId = primaryUnitId
            };
            if (includeUnprintedUnits)
            {
                vm.UnprintedUnits = units
                    .Where(u => u.LabelPrintedAtUtc == null)
                    .Select(u => new UnitPrintStatusViewModel
                    {
                        UnitId = u.Id,
                        UnitIndex = u.UnitIndex,
                        Barcode = u.Barcode,
                        Status = u.Status,
                        TotalUnitCount = units.Count
                    })
                    .ToList();
            }
            return vm;
        }).ToList();
    }

    // LOCKED INVARIANT: once an InventoryListing row exists, its Barcode must never change and
    // a second row must never be created for the same physical item. eBay ItemID is NOT a
    // stable identifier over an item's lifetime — it changes whenever the listing is relisted,
    // whether that's through our own AutoRelistWorker, eBay's native "automatically relist"
    // listing setting, or a manual relist. Both of the known relist paths are handled by
    // mutating ItemId in place on the SAME existing row rather than ever inserting a new one:
    //   - AutoRelistWorker: relists through our own eBay API calls, so it already knows the
    //     old/new ItemID pair directly and updates the row itself.
    //   - This method: eBay can relist an item on its own without telling us anything that
    //     links the old ItemID to the new one — the new "item" in GetActiveListingsAsync looks
    //     indistinguishable from a brand-new listing. The only way to recognize it is a value we
    //     plant on the eBay side ourselves and read back: this method writes each row's
    //     permanent Barcode into eBay's Custom Label (SKU) field (IEbayService.UpdateItemSkuAsync)
    //     and, when an incoming ItemID doesn't match any existing row, checks whether its SKU
    //     matches an existing row's Barcode before ever creating a new row. Do not remove this
    //     SKU round-trip or replace it with ItemID-only matching — that is exactly the bug this
    //     was built to fix (see 2026-08-09 log entry in Projects/Inventory Management.md).
    private async Task<bool> SyncInventoryListingsFromEbayAsync(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return false;
        }

        var cacheKey = $"inv_sync_{username}";
        if (_cache.TryGetValue(cacheKey, out _))
        {
            return false;
        }

        await PurgeExpiredInactiveListingsAsync(username);

        var nowUtc = DateTime.UtcNow;
        var activeListingsFromEbay = await _ebayService.GetActiveListingsAsync();
        var incomingByItemId = activeListingsFromEbay
            .Where(x => !string.IsNullOrWhiteSpace(x.ItemId))
            .GroupBy(x => x.ItemId, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToDictionary(x => x.ItemId, StringComparer.Ordinal);

        var existing = await _dbContext.InventoryListings
            .Where(x => x.Username == username)
            .ToListAsync();

        var existingByItemId = existing.ToDictionary(x => x.ItemId, StringComparer.Ordinal);
        var existingByBarcode = existing
            .Where(x => !string.IsNullOrWhiteSpace(x.Barcode))
            .ToDictionary(x => x.Barcode, StringComparer.Ordinal);

        // (ItemId, Barcode) pairs that need the SKU push after SaveChangesAsync: brand-new rows
        // (never had a SKU) and existing rows whose eBay SKU doesn't yet mirror their Barcode
        // (first sync after this feature shipped, or the SKU was cleared on eBay's side).
        var barcodesToPushAsSku = new List<(string ItemId, string Barcode)>();

        // Price changes detected during sync (i.e. made directly on eBay, outside this app) —
        // see AttentionRules and the "ALL price history" requirement in the 2026-08-10 log entry.
        var priceHistoryToAdd = new List<PriceReductionHistory>();

        // Per-unit tracking (multi-quantity listings + backstock): every listing keeps its own
        // InventoryItemUnit rows, one per physical copy. Unit 1 always reuses the listing's own
        // Barcode. Loaded up front and mutated in place as units are added below so counts stay
        // correct within a single sync pass without re-querying per listing.
        var existingUnits = await _dbContext.InventoryItemUnits
            .Where(x => x.Username == username)
            .ToListAsync();
        var unitsByListingId = existingUnits
            .GroupBy(x => x.ListingId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Cumulative quantity sold per item, from order history (bounded by the 90-day
        // RecentOrders retention window). Used below to catch a restock that happens before a
        // recent sale has been manually resolved to a specific unit (MarkUnitSold) — see the
        // "unsoldListedCount" comment inside ReconcileUnits for why that signal alone isn't
        // enough on its own.
        var recentSoldQuantityByItemId = await _dbContext.RecentOrders
            .AsNoTracking()
            .Where(x => x.Username == username)
            .GroupBy(x => x.ItemId)
            .Select(g => new { ItemId = g.Key, Total = g.Sum(o => o.Quantity > 0 ? o.Quantity : 1) })
            .ToDictionaryAsync(x => x.ItemId, x => x.Total, StringComparer.Ordinal);

        // Ensures a listing has a unit-1 row (reusing its own Barcode, never a new value) and
        // creates enough new units (fresh barcodes) to cover eBay's current QuantityAvailable.
        // Never removes or reclassifies a unit here — a drop in QuantityAvailable is what a sale
        // looks like from eBay's side and is handled by order fulfillment, not sync (see the
        // "never delete/regenerate existing barcodes" instruction).
        //
        // How many units are "needed" is the max of two independent signals, because either one
        // alone can under-count during the window between a sale appearing on eBay and the owner
        // manually picking which physical unit it was (MarkUnitSold):
        //   - unsoldListedCount: units we've created that aren't yet flagged IsSold. This alone
        //     is WRONG right after a sale but before it's picked — the sold unit still looks
        //     "unsold" to us, so if the owner restocks in that window (quantity back up to what it
        //     was), targetQuantity == unsoldListedCount and no new unit gets created, even
        //     though a genuinely new physical item now needs its own barcode.
        //   - order-history based: (current QuantityAvailable + everything RecentOrders says
        //     has ever sold for this item) vs. every Listed unit we've ever created for it. This
        //     catches the case above because a sale is recorded here the moment eBay reports it,
        //     independent of whether MarkUnitSold has run yet.
        void ReconcileUnits(Guid listingId, string listingBarcode, string itemId, int? quantityAvailable)
        {
            if (!unitsByListingId.TryGetValue(listingId, out var units))
            {
                units = new List<InventoryItemUnit>();
                unitsByListingId[listingId] = units;
            }

            if (units.Count == 0)
            {
                var unit1 = new InventoryItemUnit
                {
                    Username = username,
                    ListingId = listingId,
                    UnitIndex = 1,
                    Barcode = listingBarcode,
                    Status = InventoryItemUnitStatus.Listed,
                    CreatedAtUtc = nowUtc
                };
                _dbContext.InventoryItemUnits.Add(unit1);
                units.Add(unit1);
            }

            var targetQuantity = quantityAvailable.GetValueOrDefault(1);
            if (targetQuantity < 1)
            {
                targetQuantity = 1;
            }

            var listedUnits = units.Where(u => u.Status == InventoryItemUnitStatus.Listed).ToList();
            var unsoldListedCount = listedUnits.Count(u => !u.IsSold);
            var totalListedCount = listedUnits.Count;
            var everSoldQuantity = recentSoldQuantityByItemId.GetValueOrDefault(itemId, 0);

            var shortfallFromUnresolvedSoldCount = targetQuantity - unsoldListedCount;
            var shortfallFromOrderHistory = (targetQuantity + everSoldQuantity) - totalListedCount;
            var unitsToCreate = Math.Max(0, Math.Max(shortfallFromUnresolvedSoldCount, shortfallFromOrderHistory));

            if (unitsToCreate > 0)
            {
                var nextIndex = units.Count == 0 ? 1 : units.Max(u => u.UnitIndex) + 1;
                for (var i = 0; i < unitsToCreate; i++)
                {
                    var unit = new InventoryItemUnit
                    {
                        Username = username,
                        ListingId = listingId,
                        UnitIndex = nextIndex++,
                        Barcode = GenerateItemBarcode(),
                        Status = InventoryItemUnitStatus.Listed,
                        CreatedAtUtc = nowUtc
                    };
                    _dbContext.InventoryItemUnits.Add(unit);
                    units.Add(unit);
                }
            }
        }

        foreach (var incoming in incomingByItemId.Values)
        {
            if (existingByItemId.TryGetValue(incoming.ItemId, out var row))
            {
                ApplySyncedFields(row, incoming, nowUtc, username, priceHistoryToAdd);
                ReconcileUnits(row.Id, row.Barcode, incoming.ItemId, row.QuantityAvailable);

                if (!string.Equals(incoming.Sku, row.Barcode, StringComparison.Ordinal))
                {
                    barcodesToPushAsSku.Add((row.ItemId, row.Barcode));
                }
                continue;
            }

            if (!string.IsNullOrWhiteSpace(incoming.Sku)
                && existingByBarcode.TryGetValue(incoming.Sku, out var relistedRow))
            {
                // Same physical item, relisted under a new ItemID without going through our own
                // relist flow. Carry the existing row forward — same Id, same Barcode — instead
                // of creating a new row, so the printed barcode label keeps working.
                var oldItemId = relistedRow.ItemId;

                await _dbContext.InventoryItemLocations
                    .Where(x => x.Username == username && x.ItemId == oldItemId)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.ItemId, incoming.ItemId));
                await _dbContext.InventoryItemBoxAssignments
                    .Where(x => x.Username == username && x.ItemId == oldItemId)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.ItemId, incoming.ItemId));
                await _dbContext.InventoryItemStorageUnitAssignments
                    .Where(x => x.Username == username && x.ItemId == oldItemId)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.ItemId, incoming.ItemId));
                // Also migrate sale records, or a genuinely-recent sale under the old ItemId
                // would look invisible to AttentionRules' "last sale date" lookup, wrongly
                // making the item look stale on Needs Attention.
                await _dbContext.RecentOrders
                    .Where(x => x.Username == username && x.ItemId == oldItemId)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.ItemId, incoming.ItemId));

                ApplySyncedFields(relistedRow, incoming, nowUtc, username, priceHistoryToAdd);
                relistedRow.ItemId = incoming.ItemId;
                ReconcileUnits(relistedRow.Id, relistedRow.Barcode, incoming.ItemId, relistedRow.QuantityAvailable);

                _dbContext.RelistHistories.Add(new RelistHistory
                {
                    Username = username,
                    ListingId = relistedRow.Id,
                    OldItemId = oldItemId,
                    NewItemId = incoming.ItemId,
                    RelistedAtUtc = nowUtc
                });

                _logger.LogInformation(
                    "Sync: recognized item {OldId} as relisted to {NewId} via SKU match (barcode {Barcode}) for user {Username}.",
                    oldItemId, incoming.ItemId, relistedRow.Barcode, username);
                continue;
            }

            var barcode = GenerateItemBarcode();
            var newListing = new InventoryListing
            {
                Username = username,
                ItemId = incoming.ItemId,
                Title = incoming.Title,
                CoverPhotoUrl = incoming.CoverPhotoUrl,
                IsActive = true,
                Barcode = barcode,
                ListedAtUtc = nowUtc,
                LastSeenAtUtc = nowUtc,
                InactivatedAtUtc = null,
                UpdatedAtUtc = nowUtc,
                CurrentPrice = incoming.CurrentPrice,
                CurrencyId = incoming.CurrencyId,
                ListingEndTimeUtc = incoming.EndTime,
                ListingType = incoming.ListingType,
                AutoRelistEnabled = true,
                QuantityAvailable = incoming.QuantityAvailable
            };
            _dbContext.InventoryListings.Add(newListing);
            // newListing.Id is already assigned (Guid.NewGuid() default), so units can be
            // created and linked to it in the same SaveChangesAsync as the listing itself.
            ReconcileUnits(newListing.Id, newListing.Barcode, incoming.ItemId, newListing.QuantityAvailable);
            barcodesToPushAsSku.Add((incoming.ItemId, barcode));
        }

        foreach (var row in existing)
        {
            if (incomingByItemId.ContainsKey(row.ItemId) || !row.IsActive)
            {
                continue;
            }

            row.IsActive = false;
            row.InactivatedAtUtc = nowUtc;
            row.UpdatedAtUtc = nowUtc;
        }

        if (priceHistoryToAdd.Count > 0)
        {
            _dbContext.PriceReductionHistories.AddRange(priceHistoryToAdd);
        }

        await _dbContext.SaveChangesAsync();

        _cache.Set(cacheKey, true, SyncDebounce);

        // Best-effort: plant/refresh the SKU on eBay so a future relist (however it happens)
        // can still be matched back to this row. Never let a failure here fail the sync itself.
        foreach (var (itemId, barcode) in barcodesToPushAsSku)
        {
            try
            {
                await _ebayService.UpdateItemSkuAsync(itemId, barcode);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Sync: failed to push SKU {Barcode} to eBay item {ItemId}.", barcode, itemId);
            }
        }

        return true;
    }

    private static void ApplySyncedFields(
        InventoryListing row,
        EbayListingViewModel incoming,
        DateTime nowUtc,
        string username,
        List<PriceReductionHistory> priceHistoryToAdd)
    {
        row.Title = incoming.Title;
        if (!string.IsNullOrWhiteSpace(incoming.CoverPhotoUrl))
        {
            row.CoverPhotoUrl = incoming.CoverPhotoUrl;
        }
        row.IsActive = true;
        row.LastSeenAtUtc = nowUtc;
        row.InactivatedAtUtc = null;
        row.UpdatedAtUtc = nowUtc;
        if (incoming.CurrentPrice.HasValue)
        {
            // A price that differs from what we already had on file means it changed directly
            // on eBay, outside this app (a manual UI edit or the eBay sync's own detection are
            // the only two writers of CurrentPrice, and this runs before either — so any
            // mismatch here is by definition external). Log it so "all price history" holds.
            if (row.CurrentPrice.HasValue && row.CurrentPrice.Value != incoming.CurrentPrice.Value)
            {
                priceHistoryToAdd.Add(new PriceReductionHistory
                {
                    Username = username,
                    ListingId = row.Id,
                    OldPrice = row.CurrentPrice.Value,
                    NewPrice = incoming.CurrentPrice.Value,
                    ReducedAtUtc = nowUtc,
                    ChangeType = "External"
                });
            }
            row.CurrentPrice = incoming.CurrentPrice;
            row.CurrencyId = incoming.CurrencyId;
        }
        if (incoming.EndTime.HasValue)
        {
            row.ListingEndTimeUtc = incoming.EndTime;
        }
        if (!string.IsNullOrWhiteSpace(incoming.ListingType))
        {
            row.ListingType = incoming.ListingType;
        }
        if (incoming.QuantityAvailable.HasValue)
        {
            row.QuantityAvailable = incoming.QuantityAvailable;
        }
    }

    private static string GenerateItemBarcode()
    {
        var digits = new char[12];
        for (var i = 0; i < digits.Length; i++)
            digits[i] = (char)('0' + Random.Shared.Next(0, 10));
        return "1" + new string(digits) + "1";
    }

    private static string SanitizeForPath(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }

    private string BuildPhotoFilePath(string username, string itemId)
    {
        return Path.Combine(
            _webHostEnvironment.WebRootPath,
            "images", "inventory",
            SanitizeForPath(username),
            SanitizeForPath(itemId) + ".jpg");
    }

    private async Task PurgeExpiredInactiveListingsAsync(string username)
    {
        var cutoffUtc = DateTime.UtcNow.AddDays(-90);

        var itemIdsToPurge = await _dbContext.InventoryListings
            .AsNoTracking()
            .Where(x =>
                x.Username == username &&
                !x.IsActive &&
                x.InactivatedAtUtc.HasValue &&
                x.InactivatedAtUtc.Value <= cutoffUtc)
            .Select(x => x.ItemId)
            .ToListAsync();

        if (itemIdsToPurge.Count == 0)
        {
            return;
        }

        // Storage location is kept indefinitely rather than deleted here — it stays
        // available for historical display even after the listing itself is purged.

        var listingsToDelete = await _dbContext.InventoryListings
            .Where(x => x.Username == username && itemIdsToPurge.Contains(x.ItemId))
            .ToListAsync();
        if (listingsToDelete.Count > 0)
        {
            _dbContext.InventoryListings.RemoveRange(listingsToDelete);
        }

        foreach (var itemId in itemIdsToPurge)
        {
            var filePath = BuildPhotoFilePath(username, itemId);
            if (System.IO.File.Exists(filePath))
            {
                System.IO.File.Delete(filePath);
            }
        }

        await _dbContext.SaveChangesAsync();
    }
}
