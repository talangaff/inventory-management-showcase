using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using inventory_management.Data;
using inventory_management.Models;

namespace inventory_management.Controllers;

[Authorize]
public class ShippingBoxesController : Controller
{
    private readonly ApplicationDbContext _db;

    public ShippingBoxesController(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> Index()
    {
        var username = User.Identity?.Name ?? string.Empty;
        var boxes = await _db.ShippingBoxes
            .Where(b => b.Username == username)
            .OrderBy(b => b.Name)
            .Select(b => new ShippingBoxRowViewModel
            {
                Id = b.Id,
                Name = b.Name,
                LengthIn = b.LengthIn,
                WidthIn = b.WidthIn,
                HeightIn = b.HeightIn,
                WeightLbs = b.WeightLbs,
                WeightOz = b.WeightOz,
                Quantity = b.Quantity
            })
            .ToListAsync();

        return View(new ShippingBoxIndexViewModel { Boxes = boxes });
    }

    [HttpGet]
    public IActionResult Create()
    {
        return View(new ShippingBoxFormViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ShippingBoxFormViewModel model)
    {
        var username = User.Identity?.Name ?? string.Empty;

        if (ModelState.IsValid)
        {
            if (await NameInUse(username, model.Name, null))
            {
                ModelState.AddModelError(nameof(model.Name), "A shipping box with this name already exists.");
            }
        }

        if (!ModelState.IsValid)
            return View(model);

        var box = new ShippingBox
        {
            Username = username,
            Name = model.Name.Trim(),
            LengthIn = model.LengthIn,
            WidthIn = model.WidthIn,
            HeightIn = model.HeightIn,
            WeightLbs = model.WeightLbs,
            WeightOz = model.WeightOz,
            Quantity = 0,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _db.ShippingBoxes.Add(box);
        await _db.SaveChangesAsync();

        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(Guid id)
    {
        var username = User.Identity?.Name ?? string.Empty;
        var box = await _db.ShippingBoxes
            .FirstOrDefaultAsync(b => b.Id == id && b.Username == username);

        if (box == null)
            return NotFound();

        return View(new ShippingBoxFormViewModel
        {
            Id = box.Id,
            Name = box.Name,
            LengthIn = box.LengthIn,
            WidthIn = box.WidthIn,
            HeightIn = box.HeightIn,
            WeightLbs = box.WeightLbs,
            WeightOz = box.WeightOz
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(ShippingBoxFormViewModel model)
    {
        var username = User.Identity?.Name ?? string.Empty;

        if (model.Id == null)
            return NotFound();

        if (ModelState.IsValid)
        {
            if (await NameInUse(username, model.Name, model.Id))
            {
                ModelState.AddModelError(nameof(model.Name), "A shipping box with this name already exists.");
            }
        }

        if (!ModelState.IsValid)
            return View(model);

        var box = await _db.ShippingBoxes
            .FirstOrDefaultAsync(b => b.Id == model.Id && b.Username == username);

        if (box == null)
            return NotFound();

        box.Name = model.Name.Trim();
        box.LengthIn = model.LengthIn;
        box.WidthIn = model.WidthIn;
        box.HeightIn = model.HeightIn;
        box.WeightLbs = model.WeightLbs;
        box.WeightOz = model.WeightOz;
        box.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddStock(ShippingBoxAddStockViewModel model)
    {
        var username = User.Identity?.Name ?? string.Empty;

        if (!ModelState.IsValid)
            return RedirectToAction(nameof(Index));

        var box = await _db.ShippingBoxes
            .FirstOrDefaultAsync(b => b.Id == model.Id && b.Username == username);

        if (box == null)
            return NotFound();

        box.Quantity += model.Amount;
        box.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id)
    {
        var username = User.Identity?.Name ?? string.Empty;
        var box = await _db.ShippingBoxes
            .FirstOrDefaultAsync(b => b.Id == id && b.Username == username);

        if (box == null)
            return NotFound();

        _db.ShippingBoxes.Remove(box);
        await _db.SaveChangesAsync();

        return RedirectToAction(nameof(Index));
    }

    private async Task<bool> NameInUse(string username, string name, Guid? excludeId)
    {
        var trimmed = name.Trim();
        return await _db.ShippingBoxes
            .AnyAsync(b => b.Username == username
                        && b.Name == trimmed
                        && (excludeId == null || b.Id != excludeId));
    }
}
