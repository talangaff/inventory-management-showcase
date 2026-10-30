using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using inventory_management.Data;
using inventory_management.Models;
using inventory_management.Services;

namespace inventory_management.Controllers;

[Authorize]
public class ReceiptsController : Controller
{
    private const int QuickPickCount = 8;

    private readonly ApplicationDbContext _dbContext;

    public ReceiptsController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IActionResult> Index()
    {
        var username = User.Identity?.Name ?? string.Empty;

        var receipts = await _dbContext.Receipts
            .AsNoTracking()
            .Where(x => x.Username == username)
            .OrderByDescending(x => x.ReceiptDate)
            .ThenByDescending(x => x.CreatedAtUtc)
            .ToListAsync();

        var receiptIds = receipts.Select(x => x.Id).ToList();
        var itemCounts = await _dbContext.ReceiptItems
            .AsNoTracking()
            .Where(x => x.Username == username && receiptIds.Contains(x.ReceiptId))
            .GroupBy(x => x.ReceiptId)
            .Select(g => new { ReceiptId = g.Key, Total = g.Count(), Unassigned = g.Count(i => i.AssignedListingId == null) })
            .ToDictionaryAsync(x => x.ReceiptId);

        var model = new ReceiptsIndexViewModel
        {
            Receipts = receipts.Select(r =>
            {
                itemCounts.TryGetValue(r.Id, out var counts);
                return new ReceiptListItemViewModel
                {
                    Id = r.Id,
                    ReceiptDate = r.ReceiptDate,
                    TotalPrice = r.TotalPrice,
                    ItemCount = counts?.Total ?? 0,
                    UnassignedCount = counts?.Unassigned ?? 0
                };
            }).ToList()
        };

        if (TempData["ReceiptSuccess"] is string msg) ViewBag.SuccessMessage = msg;
        if (TempData["ReceiptError"] is string err) ViewBag.ErrorMessage = err;

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(DateOnly receiptDate, decimal totalPrice)
    {
        var username = User.Identity?.Name ?? string.Empty;

        if (totalPrice < 0)
        {
            TempData["ReceiptError"] = "Total price can't be negative.";
            return RedirectToAction(nameof(Index));
        }

        var receipt = new Receipt
        {
            Username = username,
            ReceiptDate = receiptDate,
            TotalPrice = totalPrice
        };
        _dbContext.Receipts.Add(receipt);
        await _dbContext.SaveChangesAsync();

        return RedirectToAction(nameof(Details), new { id = receipt.Id });
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var username = User.Identity?.Name ?? string.Empty;

        var receipt = await _dbContext.Receipts
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Username == username && x.Id == id);
        if (receipt is null)
        {
            TempData["ReceiptError"] = "Receipt not found.";
            return RedirectToAction(nameof(Index));
        }

        var model = await BuildDetailsViewModelAsync(receipt, username);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteReceipt(Guid id)
    {
        var username = User.Identity?.Name ?? string.Empty;

        var receipt = await _dbContext.Receipts
            .FirstOrDefaultAsync(x => x.Username == username && x.Id == id);
        if (receipt is null)
        {
            TempData["ReceiptError"] = "Receipt not found.";
            return RedirectToAction(nameof(Index));
        }

        var items = await _dbContext.ReceiptItems
            .Where(x => x.Username == username && x.ReceiptId == id)
            .ToListAsync();
        await ClearAssignmentsAsync(username, items);

        _dbContext.ReceiptItems.RemoveRange(items);
        _dbContext.Receipts.Remove(receipt);
        await _dbContext.SaveChangesAsync();

        TempData["ReceiptSuccess"] = "Receipt deleted.";
        return RedirectToAction(nameof(Index));
    }

    // Header edits (date/total) change the implied tax rate for every item on the receipt, so
    // this always recalculates and re-pushes to any already-assigned listing's PurchaseCost —
    // per the owner's explicit instruction that editing a receipt keeps assigned costs in sync.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateHeader(Guid id, DateOnly receiptDate, decimal totalPrice)
    {
        var username = User.Identity?.Name ?? string.Empty;

        if (totalPrice < 0)
        {
            return Json(new { success = false, message = "Total price can't be negative." });
        }

        var receipt = await _dbContext.Receipts
            .FirstOrDefaultAsync(x => x.Username == username && x.Id == id);
        if (receipt is null)
        {
            return Json(new { success = false, message = "Receipt not found." });
        }

        receipt.ReceiptDate = receiptDate;
        receipt.TotalPrice = totalPrice;
        receipt.UpdatedAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        await ReceiptCalculator.RecalculateAsync(_dbContext, id, username);

        var model = await BuildDetailsViewModelAsync(receipt, username);
        return Json(new { success = true, items = model.Items, quickPickItems = model.QuickPickItems, unassignedItems = model.UnassignedItems });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddItem(Guid receiptId, decimal preTaxPrice, string? name, Guid? listingId)
    {
        var username = User.Identity?.Name ?? string.Empty;

        if (preTaxPrice < 0)
        {
            return Json(new { success = false, message = "Price can't be negative." });
        }

        var receipt = await _dbContext.Receipts
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Username == username && x.Id == receiptId);
        if (receipt is null)
        {
            return Json(new { success = false, message = "Receipt not found." });
        }

        // Picked from the "Items" strip: the new line arrives already assigned.
        InventoryListing? pickedListing = null;
        if (listingId.HasValue)
        {
            pickedListing = await _dbContext.InventoryListings
                .FirstOrDefaultAsync(x => x.Username == username && x.Id == listingId.Value);
            if (pickedListing is null)
            {
                return Json(new { success = false, message = "That inventory item no longer exists." });
            }
        }

        var newItem = new ReceiptItem
        {
            Username = username,
            ReceiptId = receiptId,
            Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim(),
            PreTaxPrice = preTaxPrice
        };
        _dbContext.ReceiptItems.Add(newItem);
        await _dbContext.SaveChangesAsync();

        await ReceiptCalculator.RecalculateAsync(_dbContext, receiptId, username);

        if (pickedListing is not null)
        {
            AssignListing(newItem, pickedListing, DateTime.UtcNow);
            await _dbContext.SaveChangesAsync();
            // Re-run so the listing's PurchaseCost takes this line's tax-included cost.
            await ReceiptCalculator.RecalculateAsync(_dbContext, receiptId, username);
        }

        var model = await BuildDetailsViewModelAsync(receipt, username);
        return Json(new { success = true, items = model.Items, quickPickItems = model.QuickPickItems, unassignedItems = model.UnassignedItems });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateItem(Guid itemId, decimal preTaxPrice, string? name)
    {
        var username = User.Identity?.Name ?? string.Empty;

        if (preTaxPrice < 0)
        {
            return Json(new { success = false, message = "Price can't be negative." });
        }

        var item = await _dbContext.ReceiptItems
            .FirstOrDefaultAsync(x => x.Username == username && x.Id == itemId);
        if (item is null)
        {
            return Json(new { success = false, message = "Item not found." });
        }

        item.PreTaxPrice = preTaxPrice;
        item.Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        item.UpdatedAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        await ReceiptCalculator.RecalculateAsync(_dbContext, item.ReceiptId, username);

        var receipt = await _dbContext.Receipts.AsNoTracking()
            .FirstAsync(x => x.Username == username && x.Id == item.ReceiptId);
        var model = await BuildDetailsViewModelAsync(receipt, username);
        return Json(new { success = true, items = model.Items, quickPickItems = model.QuickPickItems, unassignedItems = model.UnassignedItems });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveItem(Guid itemId)
    {
        var username = User.Identity?.Name ?? string.Empty;

        var item = await _dbContext.ReceiptItems
            .FirstOrDefaultAsync(x => x.Username == username && x.Id == itemId);
        if (item is null)
        {
            return Json(new { success = false, message = "Item not found." });
        }

        var receiptId = item.ReceiptId;
        await ClearAssignmentsAsync(username, [item]);
        _dbContext.ReceiptItems.Remove(item);
        await _dbContext.SaveChangesAsync();

        await ReceiptCalculator.RecalculateAsync(_dbContext, receiptId, username);

        var receipt = await _dbContext.Receipts.AsNoTracking()
            .FirstAsync(x => x.Username == username && x.Id == receiptId);
        var model = await BuildDetailsViewModelAsync(receipt, username);
        return Json(new { success = true, items = model.Items, quickPickItems = model.QuickPickItems, unassignedItems = model.UnassignedItems });
    }

    // Assigns a receipt line to an inventory item: marks that listing Owned with a purchase
    // cost equal to the line's tax-included allocated price (matching InventoryController's
    // ClassifyOwned exactly), then lets ReceiptCalculator push the authoritative computed value
    // so this can never drift from what the receipt actually implies.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignItem(Guid receiptItemId, Guid listingId)
    {
        var username = User.Identity?.Name ?? string.Empty;

        var item = await _dbContext.ReceiptItems
            .FirstOrDefaultAsync(x => x.Username == username && x.Id == receiptItemId);
        if (item is null)
        {
            return Json(new { success = false, message = "Receipt item not found." });
        }

        var listing = await _dbContext.InventoryListings
            .FirstOrDefaultAsync(x => x.Username == username && x.Id == listingId);
        if (listing is null)
        {
            return Json(new { success = false, message = "Inventory item not found." });
        }

        AssignListing(item, listing, DateTime.UtcNow);
        await _dbContext.SaveChangesAsync();

        await ReceiptCalculator.RecalculateAsync(_dbContext, item.ReceiptId, username);

        var receipt = await _dbContext.Receipts.AsNoTracking()
            .FirstAsync(x => x.Username == username && x.Id == item.ReceiptId);
        var model = await BuildDetailsViewModelAsync(receipt, username);
        return Json(new { success = true, items = model.Items, quickPickItems = model.QuickPickItems, unassignedItems = model.UnassignedItems });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UnassignItem(Guid receiptItemId)
    {
        var username = User.Identity?.Name ?? string.Empty;

        var item = await _dbContext.ReceiptItems
            .FirstOrDefaultAsync(x => x.Username == username && x.Id == receiptItemId);
        if (item is null)
        {
            return Json(new { success = false, message = "Receipt item not found." });
        }

        await ClearAssignmentsAsync(username, [item]);
        await _dbContext.SaveChangesAsync();

        var receipt = await _dbContext.Receipts.AsNoTracking()
            .FirstAsync(x => x.Username == username && x.Id == item.ReceiptId);
        var model = await BuildDetailsViewModelAsync(receipt, username);
        return Json(new { success = true, items = model.Items, quickPickItems = model.QuickPickItems, unassignedItems = model.UnassignedItems });
    }

    // Live search box on Receipt Details: matches active listings by title, eBay ItemID, or
    // permanent Barcode. Deliberately not restricted to uncosted items — search is a deliberate
    // user action and may legitimately be used to correct an already-costed item.
    [HttpGet]
    public async Task<IActionResult> SearchItems(string? q)
    {
        var username = User.Identity?.Name ?? string.Empty;
        var term = (q ?? string.Empty).Trim().ToLowerInvariant();
        if (term.Length == 0)
        {
            return Json(Array.Empty<ItemSearchResultViewModel>());
        }

        var results = await _dbContext.InventoryListings
            .AsNoTracking()
            .Where(x => x.Username == username && x.IsActive
                     && (x.Title.ToLower().Contains(term) || x.ItemId.Contains(term) || x.Barcode.Contains(term)))
            .OrderByDescending(x => x.ListedAtUtc)
            .Take(20)
            .Select(x => new ItemSearchResultViewModel
            {
                ListingId = x.Id,
                ItemId = x.ItemId,
                Title = x.Title,
                CoverPhotoUrl = x.CoverPhotoUrl,
                Barcode = x.Barcode,
                AlreadyOwned = x.PurchaseCost != null
            })
            .ToListAsync();

        return Json(results);
    }

    // Exact-match resolution for the "scan a QR code, press Enter" flow — same barcode
    // resolution HomeController.ResolveBarcode uses for the Scan page (a scanned code is either
    // a listing's own permanent Barcode, or, for multi-unit listings, one of its other units'
    // barcode), collapsed to the parent listing since PurchaseCost lives on the listing, not
    // the unit.
    [HttpGet]
    public async Task<IActionResult> ResolveScan(string? barcode)
    {
        var username = User.Identity?.Name ?? string.Empty;
        var scanned = (barcode ?? string.Empty).Trim();
        if (scanned.Length == 0)
        {
            return Json(new { found = false });
        }

        var listing = await _dbContext.InventoryListings
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Username == username && x.Barcode == scanned);

        if (listing is null)
        {
            var unit = await _dbContext.InventoryItemUnits
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Username == username && x.Barcode == scanned);
            if (unit is not null)
            {
                listing = await _dbContext.InventoryListings
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Username == username && x.Id == unit.ListingId);
            }
        }

        if (listing is null)
        {
            return Json(new { found = false });
        }

        return Json(new
        {
            found = true,
            item = new ItemSearchResultViewModel
            {
                ListingId = listing.Id,
                ItemId = listing.ItemId,
                Title = listing.Title,
                CoverPhotoUrl = listing.CoverPhotoUrl,
                Barcode = listing.Barcode,
                AlreadyOwned = listing.PurchaseCost != null
            }
        });
    }

    private async Task<ReceiptDetailsViewModel> BuildDetailsViewModelAsync(Receipt receipt, string username)
    {
        var items = await _dbContext.ReceiptItems
            .AsNoTracking()
            .Where(x => x.Username == username && x.ReceiptId == receipt.Id)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync();

        var assignedListingIds = items
            .Where(x => x.AssignedListingId.HasValue)
            .Select(x => x.AssignedListingId!.Value)
            .ToList();
        var assignedListings = assignedListingIds.Count == 0
            ? new List<InventoryListing>()
            : await _dbContext.InventoryListings
                .AsNoTracking()
                .Where(x => x.Username == username && assignedListingIds.Contains(x.Id))
                .ToListAsync();
        var listingsById = assignedListings.ToDictionary(x => x.Id);

        var itemViewModels = items.Select(item =>
        {
            InventoryListing? listing = item.AssignedListingId.HasValue
                ? listingsById.GetValueOrDefault(item.AssignedListingId.Value)
                : null;
            return new ReceiptItemViewModel
            {
                Id = item.Id,
                Name = item.Name,
                PreTaxPrice = item.PreTaxPrice,
                AllocatedTotalPrice = item.AllocatedTotalPrice,
                AssignedListingId = item.AssignedListingId,
                AssignedItemId = listing?.ItemId,
                AssignedTitle = listing?.Title,
                AssignedCoverPhotoUrl = listing?.CoverPhotoUrl,
                AssignedBarcode = listing?.Barcode
            };
        }).ToList();

        return new ReceiptDetailsViewModel
        {
            Id = receipt.Id,
            ReceiptDate = receipt.ReceiptDate,
            TotalPrice = receipt.TotalPrice,
            Items = itemViewModels,
            QuickPickItems = await GetQuickPickCandidatesAsync(username, QuickPickCount),
            UnassignedItems = await GetQuickPickCandidatesAsync(username, null)
        };
    }

    // "Most recently listed items that aren't assigned to a receipt" — per the owner's explicit
    // scope: active, not Consignment (null or Owned), and no PurchaseCost set yet. Once a
    // listing has been given a cost (via this receipt or otherwise) it naturally drops out.
    // The same candidates feed the "Items" strip at the top of the page (take = null: all of
    // them, newest first), so the strip and the per-line quick picks always agree.
    private async Task<List<QuickPickItemViewModel>> GetQuickPickCandidatesAsync(string username, int? take)
    {
        var query = _dbContext.InventoryListings
            .AsNoTracking()
            .Where(x => x.Username == username
                     && x.IsActive
                     && x.OwnershipType != OwnershipTypes.Consignment
                     && x.PurchaseCost == null)
            .OrderByDescending(x => x.ListedAtUtc)
            .AsQueryable();
        if (take.HasValue) query = query.Take(take.Value);
        return await query
            .Select(x => new QuickPickItemViewModel
            {
                ListingId = x.Id,
                ItemId = x.ItemId,
                Title = x.Title,
                CoverPhotoUrl = x.CoverPhotoUrl,
                Barcode = x.Barcode,
                ListedAtUtc = x.ListedAtUtc
            })
            .ToListAsync();
    }

    // Assigning a receipt line to a listing marks the listing Owned at that line's cost.
    // Used by AssignItem and by AddItem when the line is picked from the "Items" strip.
    private static void AssignListing(ReceiptItem item, InventoryListing listing, DateTime nowUtc)
    {
        item.AssignedListingId = listing.Id;
        item.AssignedAtUtc = nowUtc;
        item.UpdatedAtUtc = nowUtc;

        listing.OwnershipType = OwnershipTypes.Owned;
        listing.PurchaseCost = item.AllocatedTotalPrice;
        listing.ConsignorId = null;
        listing.ConsignmentSplitPercent = null;
        listing.ClassifiedAtUtc = nowUtc;
        listing.UpdatedAtUtc = nowUtc;
    }

    // Shared undo for "unassign" and "remove item": reverts what AssignItem set, so a listing
    // never keeps a stale Owned/PurchaseCost classification whose source line no longer backs
    // it. Does not touch a listing that was classified independently of any receipt.
    private async Task ClearAssignmentsAsync(string username, List<ReceiptItem> items)
    {
        var listingIds = items
            .Where(x => x.AssignedListingId.HasValue)
            .Select(x => x.AssignedListingId!.Value)
            .ToList();
        if (listingIds.Count == 0)
        {
            return;
        }

        var listings = await _dbContext.InventoryListings
            .Where(x => x.Username == username && listingIds.Contains(x.Id))
            .ToListAsync();
        var nowUtc = DateTime.UtcNow;
        foreach (var listing in listings)
        {
            listing.OwnershipType = null;
            listing.PurchaseCost = null;
            listing.ClassifiedAtUtc = null;
            listing.UpdatedAtUtc = nowUtc;
        }

        foreach (var item in items)
        {
            item.AssignedListingId = null;
            item.AssignedAtUtc = null;
            item.UpdatedAtUtc = nowUtc;
        }
    }
}
