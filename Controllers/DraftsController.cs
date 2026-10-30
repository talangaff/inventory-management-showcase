using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using inventory_management.Data;
using inventory_management.Models;
using inventory_management.Services;

namespace inventory_management.Controllers;

public class DraftsController : Controller
{
    private readonly IEbayService _ebayService;
    private readonly ApplicationDbContext _db;
    private readonly IMemoryCache _cache;

    private static readonly string DefaultDescriptionTemplate =
        "<p>Item is in the condition described above. Please review all photos carefully before purchasing.</p>\n" +
        "<p>Fast shipping! Items are carefully packaged and shipped within 1-2 business days.</p>\n" +
        "<p>Feel free to message with any questions.</p>";

    public DraftsController(IEbayService ebayService, ApplicationDbContext db, IMemoryCache cache)
    {
        _ebayService = ebayService;
        _db = db;
        _cache = cache;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var username = User.Identity!.Name!;
        var model = new DraftIndexViewModel
        {
            IsConnected = _ebayService.IsConnected()
        };

        if (!model.IsConnected)
        {
            return View(model);
        }

        var drafts = await _db.EbayDraftListings
            .AsNoTracking()
            .Where(x => x.Username == username)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync();

        model.Drafts = drafts.Select(d => new DraftListItemViewModel
        {
            DraftId = d.DraftId,
            Title = d.Title,
            CoverPhotoUrl = d.CoverPhotoUrl,
            ConditionId = d.ConditionId,
            Price = d.Price,
            Currency = d.Currency,
            SyncedAtUtc = d.SyncedAtUtc
        }).ToList();

        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return RedirectToAction(nameof(Index));
        }

        var username = User.Identity!.Name!;

        if (!_ebayService.IsConnected())
        {
            return RedirectToAction(nameof(Index));
        }

        var draft = await _db.EbayDraftListings
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Username == username && x.DraftId == id);

        if (draft is null)
        {
            return RedirectToAction(nameof(Index));
        }

        var settings = await _db.UserDraftSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Username == username);

        var allDraftIds = await _db.EbayDraftListings
            .AsNoTracking()
            .Where(x => x.Username == username)
            .OrderBy(x => x.CreatedAtUtc)
            .Select(x => x.DraftId)
            .ToListAsync();

        var position = allDraftIds.IndexOf(id);
        var nextDraftId = (position >= 0 && position < allDraftIds.Count - 1)
            ? allDraftIds[position + 1]
            : null;

        var model = new DraftEditViewModel
        {
            IsConnected = true,
            DraftId = draft.DraftId,
            Title = draft.Title,
            CoverPhotoUrl = draft.CoverPhotoUrl,
            CategoryId = draft.CategoryId,
            ConditionId = draft.ConditionId,
            ConditionDescription = draft.ConditionDescription,
            Price = draft.Price,
            Currency = draft.Currency ?? "USD",
            WeightPounds = draft.WeightPounds,
            LengthIn = draft.LengthIn,
            WidthIn = draft.WidthIn,
            HeightIn = draft.HeightIn,
            Description = draft.Description,
            DescriptionTemplate = settings?.DescriptionTemplate ?? DefaultDescriptionTemplate,
            NextDraftId = nextDraftId,
            DraftPosition = position + 1,
            TotalDrafts = allDraftIds.Count
        };

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save([FromForm] DraftSaveRequest request)
    {
        var username = User.Identity!.Name!;

        if (!_ebayService.IsConnected())
        {
            return Json(new { ok = false, error = "Not connected to eBay." });
        }

        try
        {
            var input = new DraftUpdateInput
            {
                ConditionId = request.ConditionId,
                ConditionDescription = request.ConditionDescription,
                Price = request.Price,
                Currency = request.Currency ?? "USD",
                WeightPounds = request.WeightPounds,
                LengthIn = request.LengthIn,
                WidthIn = request.WidthIn,
                HeightIn = request.HeightIn,
                Description = request.Description
            };

            await _ebayService.UpdateDraftAsync(request.DraftId, input);

            // Update local cache
            var draft = await _db.EbayDraftListings
                .FirstOrDefaultAsync(x => x.Username == username && x.DraftId == request.DraftId);

            if (draft is not null)
            {
                draft.ConditionId = request.ConditionId;
                draft.ConditionDescription = request.ConditionDescription;
                draft.Price = request.Price;
                draft.Currency = request.Currency ?? "USD";
                draft.WeightPounds = request.WeightPounds;
                draft.LengthIn = request.LengthIn;
                draft.WidthIn = request.WidthIn;
                draft.HeightIn = request.HeightIn;
                draft.Description = request.Description;
                draft.SyncedAtUtc = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }

            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, error = ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Publish(string id)
    {
        var username = User.Identity!.Name!;

        if (!_ebayService.IsConnected())
        {
            TempData["ErrorMessage"] = "Not connected to eBay.";
            return RedirectToAction(nameof(Index));
        }

        // Find next draft before publishing so we can redirect there
        var allDraftIds = await _db.EbayDraftListings
            .AsNoTracking()
            .Where(x => x.Username == username)
            .OrderBy(x => x.CreatedAtUtc)
            .Select(x => x.DraftId)
            .ToListAsync();

        var position = allDraftIds.IndexOf(id);
        var nextDraftId = (position >= 0 && position < allDraftIds.Count - 1)
            ? allDraftIds[position + 1]
            : null;

        try
        {
            await _ebayService.PublishDraftAsync(id);

            // Remove from local DB — it's now a live listing
            var draft = await _db.EbayDraftListings
                .FirstOrDefaultAsync(x => x.Username == username && x.DraftId == id);
            if (draft is not null)
            {
                _db.EbayDraftListings.Remove(draft);
                await _db.SaveChangesAsync();
            }

            TempData["SuccessMessage"] = "Draft published successfully!";

            if (nextDraftId is not null)
            {
                return RedirectToAction(nameof(Edit), new { id = nextDraftId });
            }

            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = $"Publish failed: {ex.Message}";
            return RedirectToAction(nameof(Edit), new { id });
        }
    }

    [HttpGet]
    public async Task<IActionResult> TriggerSync()
    {
        if (!_ebayService.IsConnected())
        {
            return Json(new { ok = false });
        }

        var username = User.Identity!.Name!;
        var cacheKey = $"drafts_sync_{username}";

        if (_cache.TryGetValue(cacheKey, out _))
        {
            return Json(new { ok = false });
        }

        try
        {
            var drafts = await _ebayService.GetDraftListingsAsync();
            var now = DateTime.UtcNow;

            foreach (var draft in drafts)
            {
                var existing = await _db.EbayDraftListings
                    .FirstOrDefaultAsync(x => x.Username == username && x.DraftId == draft.DraftId);

                if (existing is null)
                {
                    _db.EbayDraftListings.Add(new EbayDraftListing
                    {
                        Id = Guid.NewGuid(),
                        Username = username,
                        DraftId = draft.DraftId,
                        Title = draft.Title,
                        Description = draft.Description,
                        CoverPhotoUrl = draft.CoverPhotoUrl,
                        ConditionId = draft.ConditionId,
                        ConditionDescription = draft.ConditionDescription,
                        Price = draft.Price,
                        Currency = draft.Currency,
                        WeightPounds = draft.WeightPounds,
                        LengthIn = draft.LengthIn,
                        WidthIn = draft.WidthIn,
                        HeightIn = draft.HeightIn,
                        CategoryId = draft.CategoryId,
                        SyncedAtUtc = now,
                        CreatedAtUtc = now
                    });
                }
                else
                {
                    existing.Title = draft.Title;
                    existing.CoverPhotoUrl = draft.CoverPhotoUrl;
                    existing.CategoryId = draft.CategoryId;
                    // Don't overwrite user-edited fields from eBay (description, condition, price, dims)
                    existing.SyncedAtUtc = now;
                }
            }

            await _db.SaveChangesAsync();

            _cache.Set(cacheKey, true, TimeSpan.FromMinutes(5));
            return Json(new { ok = true });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Drafts] TriggerSync error: {ex.Message}");
            return Json(new { ok = false, error = ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetTemplate()
    {
        var username = User.Identity!.Name!;
        var settings = await _db.UserDraftSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Username == username);

        return Json(new { template = settings?.DescriptionTemplate ?? DefaultDescriptionTemplate });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveTemplate([FromForm] string template)
    {
        var username = User.Identity!.Name!;
        var settings = await _db.UserDraftSettings
            .FirstOrDefaultAsync(x => x.Username == username);

        if (settings is null)
        {
            _db.UserDraftSettings.Add(new UserDraftSettings
            {
                Id = Guid.NewGuid(),
                Username = username,
                DescriptionTemplate = template ?? string.Empty,
                UpdatedAtUtc = DateTime.UtcNow
            });
        }
        else
        {
            settings.DescriptionTemplate = template ?? string.Empty;
            settings.UpdatedAtUtc = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
        return Json(new { ok = true });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveDraft(string id)
    {
        var username = User.Identity!.Name!;
        var draft = await _db.EbayDraftListings
            .FirstOrDefaultAsync(x => x.Username == username && x.DraftId == id);

        if (draft is not null)
        {
            _db.EbayDraftListings.Remove(draft);
            await _db.SaveChangesAsync();
        }

        return RedirectToAction(nameof(Index));
    }
}
