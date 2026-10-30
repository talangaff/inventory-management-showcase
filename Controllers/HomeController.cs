using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using inventory_management.Data;
using inventory_management.Models;
using inventory_management.Services;

namespace inventory_management.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly IEbayService _ebayService;
    private readonly IShelfUnitStore _shelfUnitStore;
    private readonly ApplicationDbContext _dbContext;

    public HomeController(
        ILogger<HomeController> logger,
        IEbayService ebayService,
        IShelfUnitStore shelfUnitStore,
        ApplicationDbContext dbContext)
    {
        _logger = logger;
        _ebayService = ebayService;
        _shelfUnitStore = shelfUnitStore;
        _dbContext = dbContext;
    }

    [Authorize]
    public async Task<IActionResult> Index()
    {
        var username = User.Identity?.Name ?? string.Empty;
        var now = DateTime.UtcNow;
        var since = now.AddDays(-30);

        var model = new DashboardViewModel
        {
            IsConnected = _ebayService.IsConnected(),
            ReadyToShip = await InventoryCounts.CountReadyToShipAsync(_dbContext, username, now),
            NeedsAttention = await AttentionRules.GetTotalNeedsAttentionCountAsync(_dbContext, username, now),
            MissingLocation = await InventoryCounts.CountListingsAsync(_dbContext, username, isMissingDataPage: true),
            UnprintedLabels = await InventoryCounts.CountListingsAsync(_dbContext, username, isMissingDataPage: false, isUnprintedPage: true),
            ActiveListings = await InventoryCounts.CountListingsAsync(_dbContext, username, isMissingDataPage: false),
        };

        // Asking-price total of what's listed: price x quantity still available (1 when
        // eBay hasn't reported a quantity).
        model.ListedValue = await _dbContext.InventoryListings
            .AsNoTracking()
            .Where(x => x.Username == username && x.IsActive && x.CurrentPrice != null)
            .SumAsync(x => x.CurrentPrice!.Value * (x.QuantityAvailable ?? 1));

        var recentSales = _dbContext.SaleRecords
            .AsNoTracking()
            .Where(x => x.Username == username && x.SoldAtUtc >= since);
        model.Sold30Days = await recentSales.CountAsync();
        model.Gross30Days = await recentSales.SumAsync(x => x.SalePriceGross ?? 0m);

        // Link through the listing's CURRENT ItemId: eBay issues a new one on every relist,
        // so the ItemId stored on the sale can point at nothing by now.
        model.RecentSales = await (
            from s in _dbContext.SaleRecords.AsNoTracking()
            where s.Username == username
            orderby s.SoldAtUtc descending
            join l in _dbContext.InventoryListings.AsNoTracking() on s.ListingId equals l.Id into ls
            from l in ls.DefaultIfEmpty()
            select new DashboardSaleViewModel
            {
                ItemId = l != null ? l.ItemId : null,
                Title = s.Title,
                SalePriceGross = s.SalePriceGross,
                SoldAtUtc = s.SoldAtUtc
            })
            .Take(6)
            .ToListAsync();

        return View(model);
    }

    [Authorize]
    public IActionResult Scan()
    {
        return View();
    }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> ResolveBarcode(string? barcode)
    {
        var scanned = (barcode ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(scanned))
        {
            return Json(new { found = false, type = "unknown", message = "Barcode not found" });
        }

        var username = User.Identity?.Name ?? string.Empty;

        // A scanned barcode is either a listing's own Barcode (always unit 1) or, for a listing
        // with more than one physical unit, one of its other units' own barcode — those only
        // ever live in InventoryItemUnits, never on InventoryListings.Barcode. Every barcode
        // ever printed still resolves here: unit 1 via the first branch exactly as before, any
        // other unit via the fallback.
        var itemListing = await _dbContext.InventoryListings
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Username == username && x.Barcode == scanned);
        Guid? resolvedUnitId = null;
        if (itemListing is null)
        {
            var scannedUnit = await _dbContext.InventoryItemUnits
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Username == username && x.Barcode == scanned);
            if (scannedUnit is not null)
            {
                itemListing = await _dbContext.InventoryListings
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Username == username && x.Id == scannedUnit.ListingId);
                resolvedUnitId = scannedUnit.Id;
            }
        }
        else
        {
            resolvedUnitId = await _dbContext.InventoryItemUnits
                .AsNoTracking()
                .Where(x => x.Username == username && x.ListingId == itemListing.Id && x.UnitIndex == 1)
                .Select(x => (Guid?)x.Id)
                .FirstOrDefaultAsync();
        }

        if (itemListing is not null)
        {
            var itemTitle = await ResolveItemTitleAsync(itemListing.ItemId, itemListing.Title);

            // Location rows are keyed by UnitId (not ItemId) so each physical unit of a
            // multi-quantity listing can have its own separate location.
            string? locationText = null;
            var shelfLoc = await _dbContext.InventoryItemLocations
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Username == username && x.UnitId == resolvedUnitId);
            if (shelfLoc is not null)
            {
                locationText = $"{shelfLoc.ShelfUnitIdentifier}-{shelfLoc.ShelfNumber}";
            }
            else
            {
                var boxAssign = await _dbContext.InventoryItemBoxAssignments
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Username == username && x.UnitId == resolvedUnitId);
                if (boxAssign is not null)
                {
                    var assignedBox = await _dbContext.Boxes
                        .AsNoTracking()
                        .FirstOrDefaultAsync(x => x.Id == boxAssign.BoxId);
                    locationText = assignedBox is not null ? $"Box-{assignedBox.BoxIdentifier}" : "Box";
                }
                else
                {
                    var suAssign = await _dbContext.InventoryItemStorageUnitAssignments
                        .AsNoTracking()
                        .FirstOrDefaultAsync(x => x.Username == username && x.UnitId == resolvedUnitId);
                    if (suAssign is not null)
                    {
                        var su = await _dbContext.StorageUnits
                            .AsNoTracking()
                            .FirstOrDefaultAsync(x => x.Id == suAssign.StorageUnitId);
                        locationText = su is not null ? su.Identifier : "Storage Unit";
                    }
                }
            }

            return Json(new
            {
                found = true,
                type = "item",
                itemId = itemListing.ItemId,
                unitId = resolvedUnitId,
                title = itemTitle,
                coverPhotoUrl = itemListing.CoverPhotoUrl,
                locationText,
                message = $"Item Name: {itemTitle}"
            });
        }

        var box = await _dbContext.Boxes
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Username == username && x.BoxBarcode == scanned);
        if (box is not null)
        {
            var boxLocationText = box.IsTransitBox
                ? "Transit Box"
                : box.StorageUnitId.HasValue
                    ? "Storage Unit"
                    : $"{box.ShelfUnitIdentifier}-{box.ShelfNumber}";

            return Json(new
            {
                found = true,
                type = "box",
                storageEligible = true,
                storageBarcode = box.BoxBarcode,
                storageLabel = $"Box-{box.BoxIdentifier}",
                message = $"Box: {box.BoxIdentifier} ({box.BoxBarcode}) on {boxLocationText}"
            });
        }

        var units = _shelfUnitStore.GetAll();
        var unitMatch = units.FirstOrDefault(x => x.BarcodeNumber == scanned);
        if (unitMatch is not null)
        {
            return Json(new
            {
                found = true,
                type = "shelf_unit",
                storageEligible = false,
                message = $"Shelf Identifier: {unitMatch.Identifier}"
            });
        }

        if (TryResolveShelfSlotBarcode(scanned, units, out var shelfUnit, out var shelfNumber))
        {
            var slotStatusRecord = _dbContext.ShelfSlotStatuses
                .AsNoTracking()
                .FirstOrDefault(x => x.ShelfUnitId == shelfUnit.Id && x.ShelfNumber == shelfNumber);
            return Json(new
            {
                found = true,
                type = "shelf_slot",
                storageEligible = true,
                storageBarcode = scanned,
                storageLabel = $"{shelfUnit.Identifier}-{shelfNumber}",
                message = $"Shelf Identifier: {shelfUnit.Identifier}, Shelf Number: {shelfNumber}",
                shelfUnitId = shelfUnit.Id,
                shelfUnitIdentifier = shelfUnit.Identifier,
                shelfNumber,
                slotStatus = slotStatusRecord != null ? (int)slotStatusRecord.Status : 0
            });
        }

        var storageUnit = await _dbContext.StorageUnits
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.BarcodeNumber == scanned);
        if (storageUnit is not null)
        {
            return Json(new
            {
                found = true,
                type = "storage_unit",
                storageEligible = true,
                storageBarcode = scanned,
                storageLabel = storageUnit.Identifier,
                message = $"Storage Unit: {storageUnit.Name} ({storageUnit.Identifier})"
            });
        }

        return Json(new { found = false, type = "unknown", message = "Barcode not found" });
    }

    [HttpGet]
    [Authorize]
    public IActionResult GetShelfUnit(string? barcode)
    {
        var scanned = (barcode ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(scanned))
            return Json(new { found = false });

        var units = _shelfUnitStore.GetAll();
        var unit = units.FirstOrDefault(u => u.BarcodeNumber == scanned);
        if (unit is null)
            return Json(new { found = false });

        var slotStatusMap = _dbContext.ShelfSlotStatuses
            .AsNoTracking()
            .Where(x => x.ShelfUnitId == unit.Id)
            .ToDictionary(x => x.ShelfNumber, x => (int)x.Status);

        var slots = Enumerable.Range(1, unit.ShelfCount)
            .Select(n => new { shelfNumber = n, status = slotStatusMap.GetValueOrDefault(n, 0) })
            .ToList();

        return Json(new
        {
            found = true,
            id = unit.Id,
            identifier = unit.Identifier,
            status = (int)unit.Status,
            shelfCount = unit.ShelfCount,
            slots
        });
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> UpdateShelfUnit([FromBody] UpdateShelfUnitRequest request)
    {
        var unit = _shelfUnitStore.GetById(request.Id);
        if (unit is null)
            return Json(new { success = false, message = "Shelf unit not found." });

        var username = User.Identity?.Name ?? string.Empty;

        unit.Status = await ShelfStatusRules.ResolveUnitStatusAsync(
            _dbContext, username, unit.Identifier, unit.ShelfCount, (ShelfStatus)request.Status);
        _shelfUnitStore.Update(unit);

        var existingSlots = _dbContext.ShelfSlotStatuses
            .Where(x => x.ShelfUnitId == unit.Id)
            .ToList();

        foreach (var slot in request.Slots)
        {
            var resolvedSlotStatus = await ShelfStatusRules.ResolveSlotStatusAsync(
                _dbContext, username, unit.Identifier, slot.ShelfNumber, (ShelfStatus)slot.Status);

            var existing = existingSlots.FirstOrDefault(x => x.ShelfNumber == slot.ShelfNumber);
            if (existing is null)
            {
                _dbContext.ShelfSlotStatuses.Add(new ShelfSlotStatus
                {
                    ShelfUnitId = unit.Id,
                    ShelfNumber = slot.ShelfNumber,
                    Status = resolvedSlotStatus
                });
            }
            else
            {
                existing.Status = resolvedSlotStatus;
            }

            // Propagate per-slot status changes to boxes on that slot
            var boxesOnSlot = _dbContext.Boxes
                .Where(b => b.Username == username && b.ShelfUnitIdentifier == unit.Identifier && b.ShelfNumber == slot.ShelfNumber)
                .ToList();
            foreach (var b in boxesOnSlot)
            {
                b.Status = resolvedSlotStatus;
            }
        }

        await _dbContext.SaveChangesAsync();
        return Json(new { success = true, message = $"Shelf unit '{unit.Identifier}' updated." });
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> UpdateShelfSlotStatus([FromBody] UpdateShelfSlotRequest request)
    {
        var unit = _shelfUnitStore.GetById(request.ShelfUnitId);
        if (unit is null)
            return Json(new { success = false, message = "Shelf unit not found." });

        var username = User.Identity?.Name ?? string.Empty;
        var requestedStatus = (ShelfStatus)request.Status;

        // Quick shelf-type switch (Shelf/Index, Shelf/Details): unlike the bulk Edit form, this
        // is a single explicit user action, so a slot that still holds unsold items should be
        // told no and left alone, not silently coerced back to Active Inventory.
        if (requestedStatus != ShelfStatus.ActiveInventory
            && await ShelfStatusRules.HasUnsoldItemsOnSlotAsync(_dbContext, username, unit.Identifier, request.ShelfNumber))
        {
            return Json(new { success = false, message = "Cannot change shelf type until all items are removed." });
        }

        var resolvedStatus = requestedStatus;

        var existing = await _dbContext.ShelfSlotStatuses
            .FirstOrDefaultAsync(x => x.ShelfUnitId == request.ShelfUnitId && x.ShelfNumber == request.ShelfNumber);

        if (existing is null)
        {
            _dbContext.ShelfSlotStatuses.Add(new ShelfSlotStatus
            {
                ShelfUnitId = request.ShelfUnitId,
                ShelfNumber = request.ShelfNumber,
                Status = resolvedStatus
            });
        }
        else
        {
            existing.Status = resolvedStatus;
        }

        // Propagate slot status to all boxes on this shelf slot
        var boxesOnSlot = await _dbContext.Boxes
            .Where(b => b.Username == username && b.ShelfUnitIdentifier == unit.Identifier && b.ShelfNumber == request.ShelfNumber)
            .ToListAsync();
        foreach (var b in boxesOnSlot)
        {
            b.Status = resolvedStatus;
        }

        await _dbContext.SaveChangesAsync();
        return Json(new { success = true, message = $"{unit.Identifier}-{request.ShelfNumber} status updated." });
    }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> GetShelfSlotItems(string? shelfUnitIdentifier, int shelfNumber)
    {
        var username = User.Identity?.Name ?? string.Empty;
        var items = await _dbContext.InventoryItemLocations
            .AsNoTracking()
            .Where(x => x.Username == username && x.ShelfUnitIdentifier == shelfUnitIdentifier && x.ShelfNumber == shelfNumber && !x.IsSold)
            .Select(x => new { x.ItemId, x.ItemTitle })
            .ToListAsync();

        return Json(new { items });
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> RemoveItemLocation([FromBody] RemoveItemLocationRequest request)
    {
        var username = User.Identity?.Name ?? string.Empty;
        var itemId = (request.ItemId ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(itemId))
            return Json(new { success = false, message = "Item ID is required." });

        var shelfLocation = await _dbContext.InventoryItemLocations
            .FirstOrDefaultAsync(x => x.Username == username && x.ItemId == itemId);
        if (shelfLocation is not null)
            _dbContext.InventoryItemLocations.Remove(shelfLocation);

        var boxAssignment = await _dbContext.InventoryItemBoxAssignments
            .FirstOrDefaultAsync(x => x.Username == username && x.ItemId == itemId);
        if (boxAssignment is not null)
            _dbContext.InventoryItemBoxAssignments.Remove(boxAssignment);

        var storageUnitAssignment = await _dbContext.InventoryItemStorageUnitAssignments
            .FirstOrDefaultAsync(x => x.Username == username && x.ItemId == itemId);
        if (storageUnitAssignment is not null)
            _dbContext.InventoryItemStorageUnitAssignments.Remove(storageUnitAssignment);

        await _dbContext.SaveChangesAsync();
        return Json(new { success = true, message = "Location removed." });
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> AssignScannedItem([FromBody] ScanAssignmentRequest request)
    {
        var itemBarcode = (request.ItemBarcode ?? string.Empty).Trim();
        var storageBarcode = (request.StorageBarcode ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(storageBarcode))
        {
            return Json(new { success = false, message = "No storage location is selected." });
        }

        var username = User.Identity?.Name ?? string.Empty;

        // Same barcode resolution as ResolveBarcode: a scanned barcode is either the listing's
        // own Barcode (unit 1) or, for a multi-unit listing, another unit's own barcode which
        // only lives in InventoryItemUnits.
        var itemListing = await _dbContext.InventoryListings
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Username == username && x.Barcode == itemBarcode);
        Guid resolvedUnitId;
        if (itemListing is null)
        {
            var scannedUnit = await _dbContext.InventoryItemUnits
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Username == username && x.Barcode == itemBarcode);
            if (scannedUnit is null)
            {
                return Json(new { success = false, message = "Scanned item barcode is invalid." });
            }
            itemListing = await _dbContext.InventoryListings
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Username == username && x.Id == scannedUnit.ListingId);
            if (itemListing is null)
            {
                return Json(new { success = false, message = "Scanned item barcode is invalid." });
            }
            resolvedUnitId = scannedUnit.Id;
        }
        else
        {
            resolvedUnitId = await _dbContext.InventoryItemUnits
                .AsNoTracking()
                .Where(x => x.Username == username && x.ListingId == itemListing.Id && x.UnitIndex == 1)
                .Select(x => x.Id)
                .FirstAsync();
        }

        var itemId = itemListing.ItemId;
        var itemTitle = await ResolveItemTitleAsync(itemId, itemListing.Title);

        var box = await _dbContext.Boxes
            .FirstOrDefaultAsync(x => x.Username == username && x.BoxBarcode == storageBarcode);
        if (box is not null)
        {
            var assignment = await _dbContext.InventoryItemBoxAssignments
                .FirstOrDefaultAsync(x => x.Username == username && x.UnitId == resolvedUnitId);
            if (assignment is null)
            {
                assignment = new InventoryItemBoxAssignment
                {
                    Username = username,
                    ItemId = itemId,
                    UnitId = resolvedUnitId
                };
                _dbContext.InventoryItemBoxAssignments.Add(assignment);
            }

            assignment.ItemTitle = itemTitle;
            assignment.BoxId = box.Id;
            assignment.BoxBarcode = box.BoxBarcode;
            assignment.UpdatedAtUtc = DateTime.UtcNow;
            assignment.IsSold = false;

            var existingShelfLocation = await _dbContext.InventoryItemLocations
                .FirstOrDefaultAsync(x => x.Username == username && x.UnitId == resolvedUnitId);
            if (existingShelfLocation is not null)
            {
                _dbContext.InventoryItemLocations.Remove(existingShelfLocation);
            }

            var existingStorageUnitAssignment = await _dbContext.InventoryItemStorageUnitAssignments
                .FirstOrDefaultAsync(x => x.Username == username && x.UnitId == resolvedUnitId);
            if (existingStorageUnitAssignment is not null)
            {
                _dbContext.InventoryItemStorageUnitAssignments.Remove(existingStorageUnitAssignment);
            }

            // When an item is added to a box on a shelf, mark that shelf slot and the box as Active
            if (!string.IsNullOrEmpty(box.ShelfUnitIdentifier) && box.ShelfNumber > 0)
            {
                var boxUnits = _shelfUnitStore.GetAll();
                var boxShelfUnit = boxUnits.FirstOrDefault(u => u.Identifier == box.ShelfUnitIdentifier);
                if (boxShelfUnit is not null)
                {
                    var slotRecord = await _dbContext.ShelfSlotStatuses
                        .FirstOrDefaultAsync(x => x.ShelfUnitId == boxShelfUnit.Id && x.ShelfNumber == box.ShelfNumber);
                    if (slotRecord is null)
                    {
                        _dbContext.ShelfSlotStatuses.Add(new ShelfSlotStatus
                        {
                            ShelfUnitId = boxShelfUnit.Id,
                            ShelfNumber = box.ShelfNumber,
                            Status = ShelfStatus.ActiveInventory
                        });
                    }
                    else
                    {
                        slotRecord.Status = ShelfStatus.ActiveInventory;
                    }
                }

                box.Status = ShelfStatus.ActiveInventory;
            }

            await _dbContext.SaveChangesAsync();
            return Json(new
            {
                success = true,
                message = $"Assigned '{itemTitle}' to Box-{box.BoxIdentifier}."
            });
        }

        var units = _shelfUnitStore.GetAll();
        if (TryResolveShelfSlotBarcode(storageBarcode, units, out var shelfUnit, out var shelfNumber))
        {
            var location = await _dbContext.InventoryItemLocations
                .FirstOrDefaultAsync(x => x.Username == username && x.UnitId == resolvedUnitId);
            if (location is null)
            {
                location = new InventoryItemLocation
                {
                    Username = username,
                    ItemId = itemId,
                    UnitId = resolvedUnitId
                };
                _dbContext.InventoryItemLocations.Add(location);
            }

            location.ItemTitle = itemTitle;
            location.ShelfUnitIdentifier = shelfUnit.Identifier;
            location.ShelfNumber = shelfNumber;
            location.ShelfBarcode = storageBarcode;
            location.UpdatedAtUtc = DateTime.UtcNow;
            location.IsSold = false;

            var existingBoxAssignment = await _dbContext.InventoryItemBoxAssignments
                .FirstOrDefaultAsync(x => x.Username == username && x.UnitId == resolvedUnitId);
            if (existingBoxAssignment is not null)
            {
                _dbContext.InventoryItemBoxAssignments.Remove(existingBoxAssignment);
            }

            var existingStorageUnitAssignment2 = await _dbContext.InventoryItemStorageUnitAssignments
                .FirstOrDefaultAsync(x => x.Username == username && x.UnitId == resolvedUnitId);
            if (existingStorageUnitAssignment2 is not null)
            {
                _dbContext.InventoryItemStorageUnitAssignments.Remove(existingStorageUnitAssignment2);
            }

            if (shelfUnit.Status != ShelfStatus.ActiveInventory)
            {
                var trackedUnit = _shelfUnitStore.GetById(shelfUnit.Id);
                if (trackedUnit is not null)
                {
                    trackedUnit.Status = ShelfStatus.ActiveInventory;
                    _shelfUnitStore.Update(trackedUnit);
                }
            }

            // When an item is added directly to a shelf slot, mark that slot and all boxes on it as Active
            var slotRecord = await _dbContext.ShelfSlotStatuses
                .FirstOrDefaultAsync(x => x.ShelfUnitId == shelfUnit.Id && x.ShelfNumber == shelfNumber);
            if (slotRecord is null)
            {
                _dbContext.ShelfSlotStatuses.Add(new ShelfSlotStatus
                {
                    ShelfUnitId = shelfUnit.Id,
                    ShelfNumber = shelfNumber,
                    Status = ShelfStatus.ActiveInventory
                });
            }
            else
            {
                slotRecord.Status = ShelfStatus.ActiveInventory;
            }

            var boxesOnSlot = await _dbContext.Boxes
                .Where(b => b.Username == username && b.ShelfUnitIdentifier == shelfUnit.Identifier && b.ShelfNumber == shelfNumber)
                .ToListAsync();
            foreach (var b in boxesOnSlot)
            {
                b.Status = ShelfStatus.ActiveInventory;
            }

            await _dbContext.SaveChangesAsync();
            return Json(new
            {
                success = true,
                message = $"Assigned '{itemTitle}' to {shelfUnit.Identifier}-{shelfNumber}."
            });
        }

        var storageUnit = await _dbContext.StorageUnits
            .FirstOrDefaultAsync(u => u.BarcodeNumber == storageBarcode);
        if (storageUnit is not null)
        {
            var suAssignment = await _dbContext.InventoryItemStorageUnitAssignments
                .FirstOrDefaultAsync(x => x.Username == username && x.UnitId == resolvedUnitId);
            if (suAssignment is null)
            {
                suAssignment = new InventoryItemStorageUnitAssignment
                {
                    Username = username,
                    ItemId = itemId,
                    UnitId = resolvedUnitId
                };
                _dbContext.InventoryItemStorageUnitAssignments.Add(suAssignment);
            }

            suAssignment.ItemTitle = itemTitle;
            suAssignment.StorageUnitId = storageUnit.Id;
            suAssignment.UpdatedAtUtc = DateTime.UtcNow;
            suAssignment.IsSold = false;

            var existingShelf2 = await _dbContext.InventoryItemLocations
                .FirstOrDefaultAsync(x => x.Username == username && x.UnitId == resolvedUnitId);
            if (existingShelf2 is not null) _dbContext.InventoryItemLocations.Remove(existingShelf2);

            var existingBox2 = await _dbContext.InventoryItemBoxAssignments
                .FirstOrDefaultAsync(x => x.Username == username && x.UnitId == resolvedUnitId);
            if (existingBox2 is not null) _dbContext.InventoryItemBoxAssignments.Remove(existingBox2);

            await _dbContext.SaveChangesAsync();
            return Json(new
            {
                success = true,
                message = $"Assigned '{itemTitle}' to storage unit {storageUnit.Identifier}."
            });
        }

        return Json(new { success = false, message = "Selected storage barcode is invalid for item assignment." });
    }

    private async Task<string> ResolveItemTitleAsync(string itemId, string? storedTitle = null)
    {
        if (!string.IsNullOrWhiteSpace(storedTitle))
            return storedTitle;

        if (_ebayService.IsConfigured() && _ebayService.IsConnected())
        {
            try
            {
                var listings = await _ebayService.GetActiveListingsAsync();
                var listing = listings.FirstOrDefault(x => x.ItemId == itemId);
                if (listing is not null)
                {
                    return listing.Title;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unable to resolve scanned item barcode against eBay listings.");
            }
        }

        return itemId;
    }

    private static bool TryResolveShelfSlotBarcode(
        string scanned,
        IReadOnlyList<ShelfUnit> units,
        out ShelfUnit shelfUnit,
        out int shelfNumber)
    {
        shelfUnit = null!;
        shelfNumber = 0;

        foreach (var unit in units)
        {
            if (string.IsNullOrWhiteSpace(unit.BarcodeNumber) ||
                unit.BarcodeNumber.Length != scanned.Length ||
                !unit.BarcodeNumber.EndsWith('0'))
            {
                continue;
            }

            var prefix = unit.BarcodeNumber[..^1];
            if (!scanned.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var last = scanned[^1];
            if (last is < '1' or > '9')
            {
                continue;
            }

            shelfNumber = last - '0';
            if (shelfNumber <= unit.ShelfCount)
            {
                shelfUnit = unit;
                return true;
            }
        }

        return false;
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
