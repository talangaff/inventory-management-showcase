using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using inventory_management.Data;
using inventory_management.Models;
using inventory_management.Services;

namespace inventory_management.Controllers;

[Authorize]
public class BoxController : Controller
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IShelfUnitStore _shelfUnitStore;

    public BoxController(ApplicationDbContext dbContext, IShelfUnitStore shelfUnitStore)
    {
        _dbContext = dbContext;
        _shelfUnitStore = shelfUnitStore;
    }

    public async Task<IActionResult> Index()
    {
        var username = User.Identity?.Name ?? string.Empty;
        var boxes = await _dbContext.Boxes
            .AsNoTracking()
            .Where(x => x.Username == username)
            .OrderBy(x => x.BoxIdentifier)
            .ToListAsync();

        // !IsSold alone isn't enough — it only flips once a sold item finishes shipping (see
        // OrdersController), so a sold-but-unshipped item would still count/show here. Require
        // the listing to still be active inventory too.
        var itemCounts = await _dbContext.InventoryItemBoxAssignments
            .AsNoTracking()
            .Where(x => x.Username == username && !x.IsSold)
            .Join(
                _dbContext.InventoryListings.AsNoTracking().Where(l => l.Username == username && l.IsActive),
                a => a.ItemId, l => l.ItemId, (a, l) => a)
            .GroupBy(x => x.BoxId)
            .Select(g => new { BoxId = g.Key, Count = g.Count() })
            .ToListAsync();

        var countLookup = itemCounts.ToDictionary(x => x.BoxId, x => x.Count);

        var suIds = boxes
            .Where(b => b.StorageUnitId.HasValue)
            .Select(b => b.StorageUnitId!.Value)
            .Distinct()
            .ToList();

        var suLookup = suIds.Count > 0
            ? await _dbContext.StorageUnits
                .AsNoTracking()
                .Where(u => suIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.Identifier)
            : new Dictionary<Guid, string>();

        var model = new BoxIndexViewModel
        {
            Boxes = boxes.Select(box => new BoxListItemViewModel
            {
                Id = box.Id,
                BoxIdentifier = box.BoxIdentifier,
                BoxBarcode = box.BoxBarcode,
                ShelfUnitIdentifier = box.ShelfUnitIdentifier,
                ShelfNumber = box.ShelfNumber,
                ShelfBarcode = box.ShelfBarcode,
                StorageUnitId = box.StorageUnitId,
                StorageUnitIdentifier = box.StorageUnitId.HasValue
                    ? suLookup.GetValueOrDefault(box.StorageUnitId.Value, string.Empty)
                    : string.Empty,
                IsTransitBox = box.IsTransitBox,
                ItemCount = countLookup.GetValueOrDefault(box.Id)
            }).ToList()
        };

        return View(model);
    }

    [HttpGet]
    public IActionResult Create()
    {
        var model = new BoxFormViewModel();
        PopulateShelfUnits(model);
        PopulateStorageUnits(model);
        model.BoxIdentifier = GetNextBoxIdentifier(username: User.Identity?.Name ?? string.Empty);
        if (model.ShelfUnits.Any())
        {
            model.ShelfUnitId = model.ShelfUnits[0].Id;
            model.ShelfNumber = 1;
        }

        return View("Form", model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Create(BoxFormViewModel model)
    {
        var username = User.Identity?.Name ?? string.Empty;
        model.BoxIdentifier = (model.BoxIdentifier ?? string.Empty).Trim().ToUpperInvariant();

        if (BoxIdentifierInUse(username, model.BoxIdentifier))
        {
            var nextAvailable = GetNextBoxIdentifier(username);
            ModelState.AddModelError(
                nameof(model.BoxIdentifier),
                $"That box identifier already exists. The next available identifier is '{nextAvailable}'.");
        }

        ShelfUnit? shelfUnit = null;
        string? shelfBarcode = null;
        StorageUnit? storageUnit = null;
        var isTransitBox = model.LocationType == "transit";

        if (isTransitBox)
        {
            // Transit boxes have no storage location by design.
        }
        else if (model.LocationType == "storage_unit")
        {
            if (!model.StorageUnitId.HasValue)
            {
                ModelState.AddModelError(nameof(model.StorageUnitId), "Please select a storage unit.");
            }
            else
            {
                storageUnit = _dbContext.StorageUnits.FirstOrDefault(u => u.Id == model.StorageUnitId.Value);
                if (storageUnit is null)
                {
                    ModelState.AddModelError(nameof(model.StorageUnitId), "Selected storage unit not found.");
                }
            }
        }
        else
        {
            if (!TryResolveShelfLocation(model, out shelfUnit, out shelfBarcode))
            {
                ModelState.AddModelError(nameof(model.ShelfNumber), "Invalid shelving location selected.");
            }
        }

        if (!ModelState.IsValid)
        {
            PopulateShelfUnits(model);
            PopulateStorageUnits(model);
            return View("Form", model);
        }

        ShelfStatus boxStatus = ShelfStatus.ActiveInventory;
        if (shelfUnit is not null)
        {
            var slotStatus = _dbContext.ShelfSlotStatuses
                .AsNoTracking()
                .FirstOrDefault(x => x.ShelfUnitId == shelfUnit.Id && x.ShelfNumber == model.ShelfNumber);
            boxStatus = slotStatus?.Status ?? ShelfStatus.ActiveInventory;
        }

        var box = new Box
        {
            Username = username,
            BoxIdentifier = model.BoxIdentifier,
            BoxBarcode = GenerateUniqueBoxBarcode(username),
            ShelfUnitIdentifier = shelfUnit?.Identifier ?? string.Empty,
            ShelfNumber = shelfUnit is not null ? model.ShelfNumber : 0,
            ShelfBarcode = shelfBarcode ?? string.Empty,
            StorageUnitId = storageUnit?.Id,
            Status = boxStatus,
            IsTransitBox = isTransitBox,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _dbContext.Boxes.Add(box);
        _dbContext.SaveChanges();

        TempData["BoxSuccess"] = $"Box '{box.BoxBarcode}' added.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public IActionResult Edit(Guid id)
    {
        var username = User.Identity?.Name ?? string.Empty;
        var box = _dbContext.Boxes.FirstOrDefault(x => x.Id == id && x.Username == username);
        if (box is null)
        {
            return NotFound();
        }

        var model = new BoxFormViewModel
        {
            Id = box.Id,
            BoxIdentifier = box.BoxIdentifier,
            ShelfNumber = box.ShelfNumber,
            LocationType = box.IsTransitBox
                ? "transit"
                : box.StorageUnitId.HasValue ? "storage_unit" : "shelf",
            StorageUnitId = box.StorageUnitId
        };

        PopulateShelfUnits(model);
        PopulateStorageUnits(model);
        var match = model.ShelfUnits.FirstOrDefault(x => x.Identifier == box.ShelfUnitIdentifier);
        model.ShelfUnitId = match?.Id ?? model.ShelfUnits.FirstOrDefault()?.Id ?? Guid.Empty;

        return View("Form", model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Edit(BoxFormViewModel model)
    {
        var username = User.Identity?.Name ?? string.Empty;
        model.BoxIdentifier = (model.BoxIdentifier ?? string.Empty).Trim().ToUpperInvariant();

        var box = _dbContext.Boxes.FirstOrDefault(x => x.Id == model.Id && x.Username == username);
        if (box is null)
        {
            return NotFound();
        }

        if (BoxIdentifierInUse(username, model.BoxIdentifier, box.Id))
        {
            ModelState.AddModelError(nameof(model.BoxIdentifier), "That box identifier already exists.");
        }

        ShelfUnit? shelfUnit = null;
        string? shelfBarcode = null;
        StorageUnit? storageUnit = null;
        var isTransitBox = model.LocationType == "transit";

        if (isTransitBox)
        {
            // Transit boxes have no storage location by design.
        }
        else if (model.LocationType == "storage_unit")
        {
            if (!model.StorageUnitId.HasValue)
            {
                ModelState.AddModelError(nameof(model.StorageUnitId), "Please select a storage unit.");
            }
            else
            {
                storageUnit = _dbContext.StorageUnits.FirstOrDefault(u => u.Id == model.StorageUnitId.Value);
                if (storageUnit is null)
                {
                    ModelState.AddModelError(nameof(model.StorageUnitId), "Selected storage unit not found.");
                }
            }
        }
        else
        {
            if (!TryResolveShelfLocation(model, out shelfUnit, out shelfBarcode))
            {
                ModelState.AddModelError(nameof(model.ShelfNumber), "Invalid shelving location selected.");
            }
        }

        if (!ModelState.IsValid)
        {
            PopulateShelfUnits(model);
            PopulateStorageUnits(model);
            return View("Form", model);
        }

        ShelfStatus updatedBoxStatus = ShelfStatus.ActiveInventory;
        if (shelfUnit is not null)
        {
            var slotStatus = _dbContext.ShelfSlotStatuses
                .AsNoTracking()
                .FirstOrDefault(x => x.ShelfUnitId == shelfUnit.Id && x.ShelfNumber == model.ShelfNumber);
            updatedBoxStatus = slotStatus?.Status ?? ShelfStatus.ActiveInventory;
        }

        box.BoxIdentifier = model.BoxIdentifier;
        box.ShelfUnitIdentifier = shelfUnit?.Identifier ?? string.Empty;
        box.ShelfNumber = shelfUnit is not null ? model.ShelfNumber : 0;
        box.ShelfBarcode = shelfBarcode ?? string.Empty;
        box.StorageUnitId = storageUnit?.Id;
        box.Status = updatedBoxStatus;
        box.IsTransitBox = isTransitBox;
        box.UpdatedAtUtc = DateTime.UtcNow;

        _dbContext.SaveChanges();
        TempData["BoxSuccess"] = $"Box '{box.BoxBarcode}' updated.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Details(Guid id)
    {
        var username = User.Identity?.Name ?? string.Empty;
        var box = await _dbContext.Boxes
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.Username == username);

        if (box is null)
        {
            return NotFound();
        }

        var assignments = await _dbContext.InventoryItemBoxAssignments
            .AsNoTracking()
            .Where(x => x.Username == username && x.BoxId == box.Id && !x.IsSold)
            .ToListAsync();

        var candidateItemIds = assignments.Select(x => x.ItemId).Distinct().ToList();

        // !IsSold alone isn't enough — it only flips once a sold item finishes shipping (see
        // OrdersController), so a sold-but-unshipped item would still show here. Only show
        // items whose listing is still active inventory.
        var listingInfo = await _dbContext.InventoryListings
            .AsNoTracking()
            .Where(x => x.Username == username && candidateItemIds.Contains(x.ItemId))
            .Select(x => new { x.ItemId, x.CoverPhotoUrl, x.IsActive })
            .ToListAsync();

        var activeItemIds = listingInfo.Where(x => x.IsActive).Select(x => x.ItemId).ToHashSet();
        assignments = assignments.Where(a => activeItemIds.Contains(a.ItemId)).ToList();

        var photoLookup = listingInfo
            .Where(x => activeItemIds.Contains(x.ItemId))
            .ToDictionary(x => x.ItemId, x => x.CoverPhotoUrl);

        var shelfUnit = string.IsNullOrEmpty(box.ShelfUnitIdentifier)
            ? null
            : _shelfUnitStore.GetAll().FirstOrDefault(u => u.Identifier == box.ShelfUnitIdentifier);

        StorageUnit? storageUnit = null;
        if (box.StorageUnitId.HasValue)
        {
            storageUnit = await _dbContext.StorageUnits
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == box.StorageUnitId.Value);
        }

        ViewBag.BulkConfig = BuildBulkActionsConfig(username);

        var model = new BoxDetailsViewModel
        {
            BoxId = box.Id,
            BoxIdentifier = box.BoxIdentifier,
            BoxBarcode = box.BoxBarcode,
            ShelfLocation = storageUnit is not null || box.IsTransitBox
                ? string.Empty
                : $"{box.ShelfUnitIdentifier}-{box.ShelfNumber}",
            ShelfUnitId = shelfUnit?.Id,
            StorageUnitId = storageUnit?.Id,
            StorageUnitName = storageUnit?.Name ?? string.Empty,
            StorageUnitIdentifier = storageUnit?.Identifier ?? string.Empty,
            IsTransitBox = box.IsTransitBox,
            Items = assignments.Select(a => new StorageItemViewModel
            {
                ItemId = a.ItemId,
                Title = a.ItemTitle,
                CoverPhotoUrl = photoLookup.GetValueOrDefault(a.ItemId)
            }).ToList()
        };

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult RemoveItem(Guid boxId, string itemId)
    {
        var username = User.Identity?.Name ?? string.Empty;

        _dbContext.InventoryItemBoxAssignments
            .Where(x => x.Username == username && x.ItemId == itemId && x.BoxId == boxId)
            .ExecuteDelete();

        return RedirectToAction(nameof(Details), new { id = boxId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Delete(Guid id)
    {
        var username = User.Identity?.Name ?? string.Empty;
        var box = _dbContext.Boxes.FirstOrDefault(x => x.Id == id && x.Username == username);
        if (box is null)
        {
            return RedirectToAction(nameof(Index));
        }

        _dbContext.InventoryItemBoxAssignments
            .Where(x => x.Username == username && x.BoxId == box.Id)
            .ExecuteDelete();

        _dbContext.Boxes.Remove(box);
        _dbContext.SaveChanges();

        TempData["BoxSuccess"] = $"Box '{box.BoxBarcode}' deleted.";
        return RedirectToAction(nameof(Index));
    }

    private bool BoxBarcodeInUse(string username, string boxBarcode)
    {
        return _dbContext.Boxes.Any(x =>
            x.Username == username &&
            x.BoxBarcode == boxBarcode);
    }

    private bool BoxIdentifierInUse(string username, string boxIdentifier, Guid? excludeId = null)
    {
        return _dbContext.Boxes.Any(x =>
            x.Username == username &&
            x.BoxIdentifier == boxIdentifier &&
            (!excludeId.HasValue || x.Id != excludeId.Value));
    }

    private string GenerateUniqueBoxBarcode(string username)
    {
        while (true)
        {
            // Format: 3 + 12 random digits + 0
            var middle = string.Concat(Enumerable.Range(0, 12)
                .Select(_ => RandomNumberGenerator.GetInt32(0, 10).ToString()));
            var candidate = $"3{middle}0";

            if (!BoxBarcodeInUse(username, candidate))
            {
                return candidate;
            }
        }
    }

    private string GetNextBoxIdentifier(string username)
    {
        var existing = _dbContext.Boxes
            .AsNoTracking()
            .Where(x => x.Username == username)
            .Select(x => x.BoxIdentifier)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var index = 0;
        while (true)
        {
            var candidate = ConvertIndexToIdentifier(index);
            if (!existing.Contains(candidate))
            {
                return candidate;
            }

            index++;
        }
    }

    private static string ConvertIndexToIdentifier(int index)
    {
        var value = index + 1;
        var chars = new StringBuilder();

        while (value > 0)
        {
            value--;
            chars.Insert(0, (char)('A' + (value % 26)));
            value /= 26;
        }

        return chars.ToString();
    }

    private bool TryResolveShelfLocation(BoxFormViewModel model, out ShelfUnit? shelfUnit, out string? shelfBarcode)
    {
        shelfUnit = _shelfUnitStore.GetById(model.ShelfUnitId);
        shelfBarcode = null;

        if (shelfUnit is null)
        {
            return false;
        }

        if (model.ShelfNumber < 1 || model.ShelfNumber > shelfUnit.ShelfCount)
        {
            return false;
        }

        var prefix = shelfUnit.BarcodeNumber.EndsWith('0')
            ? shelfUnit.BarcodeNumber[..^1]
            : shelfUnit.BarcodeNumber;

        shelfBarcode = $"{prefix}{model.ShelfNumber}";
        return true;
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

    private void PopulateShelfUnits(BoxFormViewModel model)
    {
        model.ShelfUnits = _shelfUnitStore.GetAll()
            .Select(unit => new BoxShelfUnitOptionViewModel
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

    private void PopulateStorageUnits(BoxFormViewModel model)
    {
        model.StorageUnits = _dbContext.StorageUnits
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
