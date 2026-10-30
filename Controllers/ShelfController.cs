using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using inventory_management.Data;
using inventory_management.Models;
using inventory_management.Services;

namespace inventory_management.Controllers;

[Authorize]
public class ShelfController : Controller
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IShelfUnitStore _shelfUnitStore;

    public ShelfController(ApplicationDbContext dbContext, IShelfUnitStore shelfUnitStore)
    {
        _dbContext = dbContext;
        _shelfUnitStore = shelfUnitStore;
    }

    public IActionResult Index()
    {
        var username = User.Identity?.Name ?? string.Empty;
        var units = _shelfUnitStore.GetAll();
        var unitIds = units.Select(u => u.Id).ToList();
        var unitIdentifiers = units.Select(u => u.Identifier).ToList();

        var suIds = units
            .Where(u => u.StorageUnitId.HasValue)
            .Select(u => u.StorageUnitId!.Value)
            .Distinct()
            .ToList();

        var suNames = suIds.Count > 0
            ? _dbContext.StorageUnits
                .AsNoTracking()
                .Where(u => suIds.Contains(u.Id))
                .ToDictionary(u => u.Id, u => u.Name)
            : new Dictionary<Guid, string>();

        var allSlotStatuses = unitIds.Count > 0
            ? _dbContext.ShelfSlotStatuses
                .AsNoTracking()
                .Where(x => unitIds.Contains(x.ShelfUnitId))
                .ToList()
            : new List<ShelfSlotStatus>();

        var slotStatusesByUnit = allSlotStatuses
            .GroupBy(x => x.ShelfUnitId)
            .ToDictionary(g => g.Key, g => g.ToDictionary(x => x.ShelfNumber, x => x.Status));

        // Same "still active inventory, not just !IsSold" rule as everywhere else (Box/StorageUnit
        // Details, Box/Index) — see those for why !IsSold alone isn't enough.
        var activeItemIds = _dbContext.InventoryListings
            .AsNoTracking()
            .Where(x => x.Username == username && x.IsActive)
            .Select(x => x.ItemId)
            .ToHashSet();

        var directCountsByIdentifier = _dbContext.InventoryItemLocations
            .AsNoTracking()
            .Where(x => x.Username == username && unitIdentifiers.Contains(x.ShelfUnitIdentifier)
                && !x.IsSold && activeItemIds.Contains(x.ItemId))
            .GroupBy(x => x.ShelfUnitIdentifier)
            .Select(g => new { Identifier = g.Key, Count = g.Count() })
            .ToDictionary(x => x.Identifier, x => x.Count);

        var boxCountsByIdentifier = _dbContext.Boxes
            .AsNoTracking()
            .Where(b => b.Username == username && unitIdentifiers.Contains(b.ShelfUnitIdentifier))
            .Join(
                _dbContext.InventoryItemBoxAssignments.AsNoTracking()
                    .Where(a => a.Username == username && !a.IsSold && activeItemIds.Contains(a.ItemId)),
                b => b.Id, a => a.BoxId, (b, a) => b.ShelfUnitIdentifier)
            .GroupBy(identifier => identifier)
            .Select(g => new { Identifier = g.Key, Count = g.Count() })
            .ToDictionary(x => x.Identifier, x => x.Count);

        var itemCountByUnitId = units.ToDictionary(
            u => u.Id,
            u => directCountsByIdentifier.GetValueOrDefault(u.Identifier)
                + boxCountsByIdentifier.GetValueOrDefault(u.Identifier));

        return View(new ShelfIndexViewModel
        {
            Units = units,
            StorageUnitNameById = suNames,
            SlotStatusesByUnitId = slotStatusesByUnit,
            ItemCountByUnitId = itemCountByUnitId
        });
    }

    [HttpGet]
    public IActionResult Create()
    {
        var model = new CreateShelfUnitViewModel
        {
            Identifier = _shelfUnitStore.GetNextIdentifier(),
            ShelfCount = 5,
            StorageUnits = GetStorageUnitOptions()
        };

        return View(model);
    }

    [HttpGet]
    public IActionResult Edit(Guid id)
    {
        var unit = _shelfUnitStore.GetById(id);
        if (unit is null)
        {
            return NotFound();
        }

        var existingStatuses = _dbContext.ShelfSlotStatuses
            .AsNoTracking()
            .Where(x => x.ShelfUnitId == unit.Id)
            .ToDictionary(x => x.ShelfNumber, x => x.Status);

        var shelfStatuses = Enumerable.Range(1, unit.ShelfCount)
            .Select(n => new ShelfSlotStatusEditModel
            {
                ShelfNumber = n,
                Status = existingStatuses.GetValueOrDefault(n, ShelfStatus.ActiveInventory)
            })
            .ToList();

        var model = new CreateShelfUnitViewModel
        {
            Id = unit.Id,
            Identifier = unit.Identifier,
            ShelfCount = unit.ShelfCount,
            StorageUnitId = unit.StorageUnitId,
            Status = unit.Status,
            StorageUnits = GetStorageUnitOptions(),
            ShelfStatuses = shelfStatuses
        };

        return View("Create", model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Create(CreateShelfUnitViewModel model)
    {
        model.Identifier = (model.Identifier ?? string.Empty).Trim().ToUpperInvariant();

        if (_shelfUnitStore.Exists(model.Identifier))
        {
            var nextAvailable = _shelfUnitStore.GetNextIdentifier();
            ModelState.AddModelError(
                nameof(model.Identifier),
                $"That unit identifier already exists. The next available identifier is '{nextAvailable}'.");
        }

        if (!ModelState.IsValid)
        {
            model.StorageUnits = GetStorageUnitOptions();
            return View("Create", model);
        }

        var unit = new ShelfUnit
        {
            Identifier = model.Identifier,
            BarcodeNumber = _shelfUnitStore.GenerateUniqueBarcode(),
            ShelfCount = model.ShelfCount,
            StorageUnitId = model.StorageUnitId,
            Status = model.Status
        };

        _shelfUnitStore.Add(unit);
        TempData["ShelfSuccess"] = $"Shelving unit '{unit.Identifier}' added.";

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(CreateShelfUnitViewModel model)
    {
        model.Identifier = (model.Identifier ?? string.Empty).Trim().ToUpperInvariant();

        var existing = _shelfUnitStore.GetById(model.Id);
        if (existing is null)
        {
            return NotFound();
        }

        if (_shelfUnitStore.ExistsForDifferentUnit(model.Id, model.Identifier))
        {
            ModelState.AddModelError(nameof(model.Identifier), "That unit identifier already exists.");
        }

        var username = User.Identity?.Name ?? string.Empty;

        if (!ModelState.IsValid)
        {
            model.StorageUnits = GetStorageUnitOptions();
            return View(model);
        }

        existing.Identifier = model.Identifier;
        existing.ShelfCount = model.ShelfCount;
        existing.StorageUnitId = model.StorageUnitId;
        existing.Status = await ShelfStatusRules.ResolveUnitStatusAsync(
            _dbContext, username, existing.Identifier, existing.ShelfCount, model.Status);

        _shelfUnitStore.Update(existing);

        // Save per-shelf statuses
        var existingSlotStatuses = _dbContext.ShelfSlotStatuses
            .Where(x => x.ShelfUnitId == existing.Id)
            .ToList();

        foreach (var slotModel in model.ShelfStatuses)
        {
            var resolvedSlotStatus = await ShelfStatusRules.ResolveSlotStatusAsync(
                _dbContext, username, existing.Identifier, slotModel.ShelfNumber, slotModel.Status);

            var slotRecord = existingSlotStatuses.FirstOrDefault(x => x.ShelfNumber == slotModel.ShelfNumber);
            if (slotRecord is null)
            {
                _dbContext.ShelfSlotStatuses.Add(new ShelfSlotStatus
                {
                    ShelfUnitId = existing.Id,
                    ShelfNumber = slotModel.ShelfNumber,
                    Status = resolvedSlotStatus
                });
            }
            else
            {
                slotRecord.Status = resolvedSlotStatus;
            }

            // Propagate slot status to all boxes on this shelf slot
            var boxesOnSlot = _dbContext.Boxes
                .Where(b => b.Username == username && b.ShelfUnitIdentifier == existing.Identifier && b.ShelfNumber == slotModel.ShelfNumber)
                .ToList();
            foreach (var b in boxesOnSlot)
            {
                b.Status = resolvedSlotStatus;
            }
        }

        // Remove any slot status rows for shelves that no longer exist
        var validShelfNumbers = model.ShelfStatuses.Select(x => x.ShelfNumber).ToHashSet();
        foreach (var orphan in existingSlotStatuses.Where(x => !validShelfNumbers.Contains(x.ShelfNumber)))
        {
            _dbContext.ShelfSlotStatuses.Remove(orphan);
        }

        await _dbContext.SaveChangesAsync();

        TempData["ShelfSuccess"] = $"Shelving unit '{existing.Identifier}' updated.";

        return RedirectToAction(nameof(Index));
    }

    // Full-page "View Items" screen for a single shelf slot (direct items + items in boxes
    // on that slot), styled like Box/Details. Linked from the "View Items" button on Shelf/Index.
    public async Task<IActionResult> ShelfSlotItems(Guid shelfUnitId, int shelfNumber)
    {
        var unit = _shelfUnitStore.GetById(shelfUnitId);
        if (unit is null)
        {
            return NotFound();
        }

        var username = User.Identity?.Name ?? string.Empty;

        var activeItemIds = (await _dbContext.InventoryListings
            .AsNoTracking()
            .Where(x => x.Username == username && x.IsActive)
            .Select(x => x.ItemId)
            .ToListAsync()).ToHashSet();

        var directItems = await _dbContext.InventoryItemLocations
            .AsNoTracking()
            .Where(x => x.Username == username && x.ShelfUnitIdentifier == unit.Identifier
                && x.ShelfNumber == shelfNumber && !x.IsSold && activeItemIds.Contains(x.ItemId))
            .ToListAsync();

        var boxesOnSlot = await _dbContext.Boxes
            .AsNoTracking()
            .Where(b => b.Username == username && b.ShelfUnitIdentifier == unit.Identifier && b.ShelfNumber == shelfNumber)
            .ToListAsync();

        var boxIds = boxesOnSlot.Select(b => b.Id).ToList();
        var boxItems = await _dbContext.InventoryItemBoxAssignments
            .AsNoTracking()
            .Where(x => x.Username == username && boxIds.Contains(x.BoxId) && !x.IsSold && activeItemIds.Contains(x.ItemId))
            .ToListAsync();

        var allItemIds = directItems.Select(x => x.ItemId).Concat(boxItems.Select(x => x.ItemId)).Distinct().ToList();
        var photoLookup = await _dbContext.InventoryListings
            .AsNoTracking()
            .Where(x => x.Username == username && allItemIds.Contains(x.ItemId))
            .ToDictionaryAsync(x => x.ItemId, x => x.CoverPhotoUrl);

        ViewBag.BulkConfig = BuildBulkActionsConfig(username);

        var model = new ShelfSlotItemsViewModel
        {
            ShelfUnitId = unit.Id,
            ShelfUnitIdentifier = unit.Identifier,
            ShelfNumber = shelfNumber,
            DirectItems = directItems.Select(x => new StorageItemViewModel
            {
                ItemId = x.ItemId,
                Title = x.ItemTitle,
                CoverPhotoUrl = photoLookup.GetValueOrDefault(x.ItemId)
            }).ToList(),
            Boxes = boxesOnSlot
                .Select(b => new ShelfSlotBoxViewModel
                {
                    BoxId = b.Id,
                    BoxIdentifier = b.BoxIdentifier,
                    BoxBarcode = b.BoxBarcode,
                    Items = boxItems
                        .Where(bi => bi.BoxId == b.Id)
                        .Select(bi => new StorageItemViewModel
                        {
                            ItemId = bi.ItemId,
                            Title = bi.ItemTitle,
                            CoverPhotoUrl = photoLookup.GetValueOrDefault(bi.ItemId)
                        })
                        .ToList()
                })
                .Where(b => b.Items.Count > 0)
                .ToList()
        };

        return View(model);
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var unit = _shelfUnitStore.GetById(id);
        if (unit is null)
        {
            return NotFound();
        }

        var username = User.Identity?.Name ?? string.Empty;

        // !IsSold alone isn't enough — it only flips once a sold item finishes shipping (see
        // OrdersController), so a sold-but-unshipped item would still show below. Require the
        // listing to still be active inventory too.
        var activeItemIds = (await _dbContext.InventoryListings
            .AsNoTracking()
            .Where(x => x.Username == username && x.IsActive)
            .Select(x => x.ItemId)
            .ToListAsync()).ToHashSet();

        // Items directly on shelf slots (sold/inactive items are hidden — their location is kept
        // indefinitely for the Ready to Ship page, but they're no longer physically "on" the shelf)
        var directItems = await _dbContext.InventoryItemLocations
            .AsNoTracking()
            .Where(x => x.Username == username && x.ShelfUnitIdentifier == unit.Identifier && !x.IsSold && activeItemIds.Contains(x.ItemId))
            .ToListAsync();

        // Boxes on this shelf unit
        var boxes = await _dbContext.Boxes
            .AsNoTracking()
            .Where(x => x.Username == username && x.ShelfUnitIdentifier == unit.Identifier)
            .ToListAsync();

        // Items in those boxes (sold/inactive items hidden, see above)
        var boxIds = boxes.Select(b => b.Id).ToList();
        var boxItems = await _dbContext.InventoryItemBoxAssignments
            .AsNoTracking()
            .Where(x => x.Username == username && boxIds.Contains(x.BoxId) && !x.IsSold && activeItemIds.Contains(x.ItemId))
            .ToListAsync();

        // Per-shelf statuses
        var slotStatuses = await _dbContext.ShelfSlotStatuses
            .AsNoTracking()
            .Where(x => x.ShelfUnitId == unit.Id)
            .ToDictionaryAsync(x => x.ShelfNumber, x => x.Status);

        // Cover photos lookup
        var allItemIds = directItems.Select(x => x.ItemId)
            .Concat(boxItems.Select(x => x.ItemId))
            .Distinct()
            .ToList();

        var coverPhotos = await _dbContext.InventoryListings
            .AsNoTracking()
            .Where(x => x.Username == username && allItemIds.Contains(x.ItemId))
            .Select(x => new { x.ItemId, x.CoverPhotoUrl })
            .ToListAsync();

        var photoLookup = coverPhotos.ToDictionary(x => x.ItemId, x => x.CoverPhotoUrl);

        // Build shelves (highest first)
        var shelves = new List<ShelfSlotViewModel>();
        for (var i = unit.ShelfCount; i >= 1; i--)
        {
            var shelfNumber = i;
            var slotDirectItems = directItems
                .Where(x => x.ShelfNumber == shelfNumber)
                .Select(x => new StorageItemViewModel
                {
                    ItemId = x.ItemId,
                    Title = x.ItemTitle,
                    CoverPhotoUrl = photoLookup.GetValueOrDefault(x.ItemId)
                })
                .ToList();

            var slotBoxes = boxes
                .Where(b => b.ShelfNumber == shelfNumber)
                .Select(b => new ShelfSlotBoxViewModel
                {
                    BoxId = b.Id,
                    BoxIdentifier = b.BoxIdentifier,
                    BoxBarcode = b.BoxBarcode,
                    Items = boxItems
                        .Where(bi => bi.BoxId == b.Id)
                        .Select(bi => new StorageItemViewModel
                        {
                            ItemId = bi.ItemId,
                            Title = bi.ItemTitle,
                            CoverPhotoUrl = photoLookup.GetValueOrDefault(bi.ItemId)
                        })
                        .ToList()
                })
                .ToList();

            shelves.Add(new ShelfSlotViewModel
            {
                ShelfNumber = shelfNumber,
                Status = slotStatuses.GetValueOrDefault(shelfNumber, ShelfStatus.ActiveInventory),
                DirectItems = slotDirectItems,
                Boxes = slotBoxes
            });
        }

        ViewBag.BulkConfig = BuildBulkActionsConfig(username);

        var model = new ShelfDetailsViewModel
        {
            Id = unit.Id,
            Identifier = unit.Identifier,
            ShelfCount = unit.ShelfCount,
            Status = unit.Status,
            Shelves = shelves
        };

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult RemoveItem(Guid shelfId, string itemId, string source, int? shelfNumber = null)
    {
        var username = User.Identity?.Name ?? string.Empty;

        if (source == "shelf")
        {
            _dbContext.InventoryItemLocations
                .Where(x => x.Username == username && x.ItemId == itemId)
                .ExecuteDelete();
        }
        else if (source == "box")
        {
            _dbContext.InventoryItemBoxAssignments
                .Where(x => x.Username == username && x.ItemId == itemId)
                .ExecuteDelete();
        }

        if (shelfNumber.HasValue)
        {
            return RedirectToAction(nameof(ShelfSlotItems), new { shelfUnitId = shelfId, shelfNumber = shelfNumber.Value });
        }

        return RedirectToAction(nameof(Details), new { id = shelfId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Delete(Guid id)
    {
        var unit = _shelfUnitStore.GetById(id);
        if (unit is null)
        {
            return RedirectToAction(nameof(Index));
        }

        var username = User.Identity?.Name ?? string.Empty;

        // Boxes on this unit become unlocated rather than being deleted — same "unlink, don't
        // destroy" precedent as StorageUnitController.Delete.
        var affectedBoxes = _dbContext.Boxes
            .Where(b => b.Username == username && b.ShelfUnitIdentifier == unit.Identifier)
            .ToList();
        foreach (var b in affectedBoxes)
        {
            b.ShelfUnitIdentifier = string.Empty;
            b.ShelfNumber = 0;
            b.ShelfBarcode = string.Empty;
        }

        // Items placed directly on this unit's shelves lose that location (there's no shelf
        // left for them to be "on").
        _dbContext.InventoryItemLocations
            .Where(x => x.Username == username && x.ShelfUnitIdentifier == unit.Identifier)
            .ExecuteDelete();

        // Per-shelf status rows for this unit are meaningless once it's gone.
        _dbContext.ShelfSlotStatuses
            .Where(x => x.ShelfUnitId == unit.Id)
            .ExecuteDelete();

        // Delete() saves changes itself, which also flushes the box updates above.
        _shelfUnitStore.Delete(unit);

        TempData["ShelfSuccess"] = $"Shelving unit '{unit.Identifier}' deleted.";
        return RedirectToAction(nameof(Index));
    }

    // Feeds the shared bulk-select toolbar (Views/Shared/_BulkItemActionsBar.cshtml) on Details.
    private BulkItemActionsConfigViewModel BuildBulkActionsConfig(string username)
    {
        return new BulkItemActionsConfigViewModel
        {
            ShelfUnits = _shelfUnitStore.GetAll()
                .Select(unit => new ShelfUnitOptionViewModel
                {
                    Id = unit.Id,
                    Identifier = unit.Identifier,
                    ShelfCount = unit.ShelfCount,
                    BarcodePrefix = unit.BarcodeNumber.EndsWith('0') ? unit.BarcodeNumber[..^1] : unit.BarcodeNumber
                })
                .ToList(),
            Boxes = _dbContext.Boxes
                .AsNoTracking()
                .Where(b => b.Username == username)
                .OrderBy(b => b.BoxIdentifier)
                .Select(b => new BoxOptionViewModel { Id = b.Id, BoxIdentifier = b.BoxIdentifier, BoxBarcode = b.BoxBarcode })
                .ToList()
        };
    }

    private List<StorageUnitOptionViewModel> GetStorageUnitOptions()
    {
        return _dbContext.StorageUnits
            .AsNoTracking()
            .OrderBy(u => u.Identifier)
            .Select(u => new StorageUnitOptionViewModel
            {
                Id = u.Id,
                Name = u.Name,
                Identifier = u.Identifier
            })
            .ToList();
    }
}
