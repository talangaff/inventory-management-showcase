using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using inventory_management.Data;
using inventory_management.Models;

namespace inventory_management.Controllers;

[Authorize]
public class ConsignorsController : Controller
{
    private readonly ApplicationDbContext _dbContext;

    public ConsignorsController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IActionResult> Index()
    {
        var username = User.Identity?.Name ?? string.Empty;

        var consignors = await _dbContext.Consignors
            .AsNoTracking()
            .Where(x => x.Username == username)
            .OrderBy(x => x.Name)
            .ToListAsync();

        var activeCounts = await _dbContext.InventoryListings
            .AsNoTracking()
            .Where(x => x.Username == username && x.IsActive && x.ConsignorId != null)
            .GroupBy(x => x.ConsignorId!.Value)
            .Select(g => new { ConsignorId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ConsignorId, x => x.Count);

        var saleCounts = await _dbContext.SaleRecords
            .AsNoTracking()
            .Where(x => x.Username == username && x.ConsignorId != null)
            .GroupBy(x => x.ConsignorId!.Value)
            .Select(g => new { ConsignorId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ConsignorId, x => x.Count);

        var model = new ConsignorsIndexViewModel
        {
            Consignors = consignors.Select(c => new ConsignorListItemViewModel
            {
                Id = c.Id,
                Name = c.Name,
                DefaultSplitPercent = c.DefaultSplitPercent,
                ActiveListingCount = activeCounts.GetValueOrDefault(c.Id),
                SaleRecordCount = saleCounts.GetValueOrDefault(c.Id)
            }).ToList()
        };

        if (TempData["ConsignorSuccess"] is string msg)
        {
            ViewBag.SuccessMessage = msg;
        }
        if (TempData["ConsignorError"] is string err)
        {
            ViewBag.ErrorMessage = err;
        }

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ConsignorFormRequest model)
    {
        var username = User.Identity?.Name ?? string.Empty;
        var name = (model.Name ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            TempData["ConsignorError"] = "Name is required.";
            return RedirectToAction(nameof(Index));
        }
        if (model.DefaultSplitPercent < 0 || model.DefaultSplitPercent > 100)
        {
            TempData["ConsignorError"] = "Default split % must be between 0 and 100.";
            return RedirectToAction(nameof(Index));
        }
        if (await _dbContext.Consignors.AnyAsync(x => x.Username == username && x.Name == name))
        {
            TempData["ConsignorError"] = $"A consignor named '{name}' already exists.";
            return RedirectToAction(nameof(Index));
        }

        _dbContext.Consignors.Add(new Consignor
        {
            Username = username,
            Name = name,
            DefaultSplitPercent = model.DefaultSplitPercent
        });
        await _dbContext.SaveChangesAsync();

        TempData["ConsignorSuccess"] = $"Added consignor '{name}'.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(ConsignorFormRequest model)
    {
        var username = User.Identity?.Name ?? string.Empty;
        var name = (model.Name ?? string.Empty).Trim();

        var consignor = await _dbContext.Consignors
            .FirstOrDefaultAsync(x => x.Username == username && x.Id == model.Id);
        if (consignor is null)
        {
            TempData["ConsignorError"] = "Consignor not found.";
            return RedirectToAction(nameof(Index));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            TempData["ConsignorError"] = "Name is required.";
            return RedirectToAction(nameof(Index));
        }
        if (model.DefaultSplitPercent < 0 || model.DefaultSplitPercent > 100)
        {
            TempData["ConsignorError"] = "Default split % must be between 0 and 100.";
            return RedirectToAction(nameof(Index));
        }
        if (await _dbContext.Consignors.AnyAsync(x => x.Username == username && x.Id != model.Id && x.Name == name))
        {
            TempData["ConsignorError"] = $"A consignor named '{name}' already exists.";
            return RedirectToAction(nameof(Index));
        }

        // Deliberately does NOT touch already-classified listings' snapshotted
        // ConsignmentSplitPercent, or any existing SaleRecord — those are frozen at
        // classification/sale time by design (see InventoryListing.ConsignmentSplitPercent).
        consignor.Name = name;
        consignor.DefaultSplitPercent = model.DefaultSplitPercent;
        consignor.UpdatedAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        TempData["ConsignorSuccess"] = $"Updated consignor '{name}'.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var username = User.Identity?.Name ?? string.Empty;

        var consignor = await _dbContext.Consignors
            .FirstOrDefaultAsync(x => x.Username == username && x.Id == id);
        if (consignor is null)
        {
            TempData["ConsignorError"] = "Consignor not found.";
            return RedirectToAction(nameof(Index));
        }

        var referencedByListing = await _dbContext.InventoryListings
            .AnyAsync(x => x.Username == username && x.ConsignorId == id);
        var referencedBySale = await _dbContext.SaleRecords
            .AnyAsync(x => x.Username == username && x.ConsignorId == id);

        if (referencedByListing || referencedBySale)
        {
            TempData["ConsignorError"] = $"Can't delete '{consignor.Name}' — reclassify or reassign their items first (they're still referenced by an item or a past sale).";
            return RedirectToAction(nameof(Index));
        }

        _dbContext.Consignors.Remove(consignor);
        await _dbContext.SaveChangesAsync();

        TempData["ConsignorSuccess"] = $"Deleted consignor '{consignor.Name}'.";
        return RedirectToAction(nameof(Index));
    }
}
