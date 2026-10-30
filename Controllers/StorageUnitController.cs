using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using inventory_management.Data;
using inventory_management.Models;
using inventory_management.Services;

namespace inventory_management.Controllers;

[Authorize]
public class StorageUnitController : Controller
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IShelfUnitStore _shelfUnitStore;

    public StorageUnitController(ApplicationDbContext dbContext, IShelfUnitStore shelfUnitStore)
    {
        _dbContext = dbContext;
        _shelfUnitStore = shelfUnitStore;
    }

    public IActionResult Index()
    {
        var username = User.Identity?.Name ?? string.Empty;
        var units = _dbContext.StorageUnits
            .AsNoTracking()
            .OrderBy(u => u.Identifier)
            .ToList();

        var shelfUnitCounts = _dbContext.ShelfUnits
            .AsNoTracking()
            .Where(x => x.StorageUnitId.HasValue)
            .GroupBy(x => x.StorageUnitId!.Value)
            .Select(g => new { StorageUnitId = g.Key, Count = g.Count() })
            .ToDictionary(x => x.StorageUnitId, x => x.Count);

        var boxCounts = _dbContext.Boxes
            .AsNoTracking()
            .Where(x => x.Username == username && x.StorageUnitId.HasValue)
            .GroupBy(x => x.StorageUnitId!.Value)
            .Select(g => new { StorageUnitId = g.Key, Count = g.Count() })
            .ToDictionary(x => x.StorageUnitId, x => x.Count);

        // !IsSold alone isn't enough — it only flips once a sold item finishes shipping (see
        // OrdersController), so a sold-but-unshipped item would still count here. Require the
        // listing to still be active inventory too.
        var activeItemIds = _dbContext.InventoryListings
            .AsNoTracking()
            .Where(x => x.Username == username && x.IsActive)
            .Select(x => x.ItemId)
            .ToList();

        var itemCounts = _dbContext.InventoryItemStorageUnitAssignments
            .AsNoTracking()
            .Where(x => x.Username == username && !x.IsSold && activeItemIds.Contains(x.ItemId))
            .GroupBy(x => x.StorageUnitId)
            .Select(g => new { StorageUnitId = g.Key, Count = g.Count() })
            .ToDictionary(x => x.StorageUnitId, x => x.Count);

        var model = new StorageUnitIndexViewModel
        {
            StorageUnits = units.Select(u => new StorageUnitListItemViewModel
            {
                Id = u.Id,
                Name = u.Name,
                Identifier = u.Identifier,
                BarcodeNumber = u.BarcodeNumber,
                ShelfUnitCount = shelfUnitCounts.GetValueOrDefault(u.Id),
                BoxCount = boxCounts.GetValueOrDefault(u.Id),
                DirectItemCount = itemCounts.GetValueOrDefault(u.Id)
            }).ToList()
        };

        if (TempData["StorageUnitSuccess"] is string msg)
        {
            ViewBag.SuccessMessage = msg;
        }

        return View(model);
    }

    [HttpGet]
    public IActionResult Create()
    {
        return View("Form", new StorageUnitFormViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Create(StorageUnitFormViewModel model)
    {
        model.Identifier = (model.Identifier ?? string.Empty).Trim().ToUpperInvariant();
        model.Name = (model.Name ?? string.Empty).Trim();

        if (_dbContext.StorageUnits.Any(u => u.Identifier == model.Identifier))
        {
            ModelState.AddModelError(nameof(model.Identifier), "That identifier is already in use.");
        }

        if (!ModelState.IsValid)
        {
            return View("Form", model);
        }

        var unit = new StorageUnit
        {
            Name = model.Name,
            Identifier = model.Identifier,
            BarcodeNumber = GenerateUniqueBarcode(),
            UpdatedAtUtc = DateTime.UtcNow
        };

        _dbContext.StorageUnits.Add(unit);
        _dbContext.SaveChanges();

        TempData["StorageUnitSuccess"] = $"Storage unit '{unit.Name}' added.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public IActionResult Edit(Guid id)
    {
        var unit = _dbContext.StorageUnits.FirstOrDefault(u => u.Id == id);
        if (unit is null) return NotFound();

        return View("Form", new StorageUnitFormViewModel
        {
            Id = unit.Id,
            Name = unit.Name,
            Identifier = unit.Identifier
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Edit(StorageUnitFormViewModel model)
    {
        model.Identifier = (model.Identifier ?? string.Empty).Trim().ToUpperInvariant();
        model.Name = (model.Name ?? string.Empty).Trim();

        var unit = _dbContext.StorageUnits.FirstOrDefault(u => u.Id == model.Id);
        if (unit is null) return NotFound();

        if (_dbContext.StorageUnits.Any(u => u.Id != model.Id && u.Identifier == model.Identifier))
        {
            ModelState.AddModelError(nameof(model.Identifier), "That identifier is already in use.");
        }

        if (!ModelState.IsValid)
        {
            return View("Form", model);
        }

        unit.Name = model.Name;
        unit.Identifier = model.Identifier;
        unit.UpdatedAtUtc = DateTime.UtcNow;

        _dbContext.SaveChanges();
        TempData["StorageUnitSuccess"] = $"Storage unit '{unit.Name}' updated.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var unit = await _dbContext.StorageUnits
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == id);
        if (unit is null) return NotFound();

        var username = User.Identity?.Name ?? string.Empty;

        var shelfUnits = await _dbContext.ShelfUnits
            .AsNoTracking()
            .Where(x => x.StorageUnitId == id)
            .ToListAsync();

        var boxes = await _dbContext.Boxes
            .AsNoTracking()
            .Where(x => x.Username == username && x.StorageUnitId == id)
            .ToListAsync();

        // !IsSold alone isn't enough — it only flips once a sold item finishes shipping (see
        // OrdersController), so a sold-but-unshipped item would still show/count below. Require
        // the listing to still be active inventory too.
        var activeItemIds = (await _dbContext.InventoryListings
            .AsNoTracking()
            .Where(x => x.Username == username && x.IsActive)
            .Select(x => x.ItemId)
            .ToListAsync()).ToHashSet();

        var directAssignments = await _dbContext.InventoryItemStorageUnitAssignments
            .AsNoTracking()
            .Where(x => x.Username == username && x.StorageUnitId == id && !x.IsSold && activeItemIds.Contains(x.ItemId))
            .ToListAsync();

        // Item counts for shelf units (sold/inactive items hidden — location kept indefinitely
        // for Ready to Ship, but no longer shown as physically stored)
        var shelfUnitIdentifiers = shelfUnits.Select(x => x.Identifier).ToList();
        var shelfDirectCounts = await _dbContext.InventoryItemLocations
            .AsNoTracking()
            .Where(x => x.Username == username && shelfUnitIdentifiers.Contains(x.ShelfUnitIdentifier) && !x.IsSold && activeItemIds.Contains(x.ItemId))
            .GroupBy(x => x.ShelfUnitIdentifier)
            .Select(g => new { Identifier = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Identifier, x => x.Count);

        // Item counts for boxes (sold/inactive items hidden, see above)
        var boxIds = boxes.Select(b => b.Id).ToList();
        var boxItemCounts = await _dbContext.InventoryItemBoxAssignments
            .AsNoTracking()
            .Where(x => x.Username == username && boxIds.Contains(x.BoxId) && !x.IsSold && activeItemIds.Contains(x.ItemId))
            .GroupBy(x => x.BoxId)
            .Select(g => new { BoxId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.BoxId, x => x.Count);

        // Cover photos for direct items
        var directItemIds = directAssignments.Select(x => x.ItemId).ToList();
        var coverPhotos = await _dbContext.InventoryListings
            .AsNoTracking()
            .Where(x => x.Username == username && directItemIds.Contains(x.ItemId))
            .Select(x => new { x.ItemId, x.CoverPhotoUrl })
            .ToDictionaryAsync(x => x.ItemId, x => x.CoverPhotoUrl);

        ViewBag.BulkConfig = BuildBulkActionsConfig(username);

        var model = new StorageUnitDetailsViewModel
        {
            Id = unit.Id,
            Name = unit.Name,
            Identifier = unit.Identifier,
            BarcodeNumber = unit.BarcodeNumber,
            ShelfUnits = shelfUnits.Select(s => new StorageUnitShelfSummaryViewModel
            {
                Id = s.Id,
                Identifier = s.Identifier,
                ShelfCount = s.ShelfCount,
                ItemCount = shelfDirectCounts.GetValueOrDefault(s.Identifier)
            }).ToList(),
            Boxes = boxes.Select(b => new StorageUnitBoxSummaryViewModel
            {
                Id = b.Id,
                BoxIdentifier = b.BoxIdentifier,
                ItemCount = boxItemCounts.GetValueOrDefault(b.Id)
            }).ToList(),
            DirectItems = directAssignments.Select(a => new StorageItemViewModel
            {
                ItemId = a.ItemId,
                Title = a.ItemTitle,
                CoverPhotoUrl = coverPhotos.GetValueOrDefault(a.ItemId)
            }).ToList()
        };

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Delete(Guid id)
    {
        var unit = _dbContext.StorageUnits.FirstOrDefault(u => u.Id == id);
        if (unit is null) return RedirectToAction(nameof(Index));

        // Nullify storage_unit_id on associated shelf units
        var affectedShelfUnits = _dbContext.ShelfUnits
            .Where(x => x.StorageUnitId == id)
            .ToList();
        foreach (var s in affectedShelfUnits)
        {
            s.StorageUnitId = null;
        }

        // Nullify storage_unit_id on associated boxes
        var affectedBoxes = _dbContext.Boxes
            .Where(x => x.StorageUnitId == id)
            .ToList();
        foreach (var b in affectedBoxes)
        {
            b.StorageUnitId = null;
        }

        // Delete direct item assignments (cascade handles DB, but do explicit for safety)
        _dbContext.InventoryItemStorageUnitAssignments
            .Where(x => x.StorageUnitId == id)
            .ExecuteDelete();

        _dbContext.StorageUnits.Remove(unit);
        _dbContext.SaveChanges();

        TempData["StorageUnitSuccess"] = $"Storage unit '{unit.Name}' deleted.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult RemoveItem(Guid storageUnitId, string itemId)
    {
        var username = User.Identity?.Name ?? string.Empty;
        _dbContext.InventoryItemStorageUnitAssignments
            .Where(x => x.Username == username && x.StorageUnitId == storageUnitId && x.ItemId == itemId)
            .ExecuteDelete();

        return RedirectToAction(nameof(Details), new { id = storageUnitId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult RemoveShelfUnit(Guid storageUnitId, Guid shelfUnitId)
    {
        var shelfUnit = _dbContext.ShelfUnits.FirstOrDefault(x => x.Id == shelfUnitId && x.StorageUnitId == storageUnitId);
        if (shelfUnit is not null)
        {
            shelfUnit.StorageUnitId = null;
            _dbContext.SaveChanges();
        }
        return RedirectToAction(nameof(Details), new { id = storageUnitId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult RemoveBox(Guid storageUnitId, Guid boxId)
    {
        var username = User.Identity?.Name ?? string.Empty;
        var box = _dbContext.Boxes.FirstOrDefault(x => x.Id == boxId && x.Username == username && x.StorageUnitId == storageUnitId);
        if (box is not null)
        {
            box.StorageUnitId = null;
            _dbContext.SaveChanges();
        }
        return RedirectToAction(nameof(Details), new { id = storageUnitId });
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

    private string GenerateUniqueBarcode()
    {
        var used = _dbContext.StorageUnits
            .AsNoTracking()
            .Select(u => u.BarcodeNumber)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        while (true)
        {
            var middle = string.Concat(Enumerable.Range(0, 12)
                .Select(_ => RandomNumberGenerator.GetInt32(0, 10).ToString()));
            var candidate = $"4{middle}0";

            if (!used.Contains(candidate))
            {
                return candidate;
            }
        }
    }
}
