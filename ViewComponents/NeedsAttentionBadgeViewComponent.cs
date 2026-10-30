using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using inventory_management.Data;
using inventory_management.Services;

namespace inventory_management.ViewComponents;

// Renders the Needs Attention item count next to every nav link to that page (see
// Views/Shared/_Layout.cshtml and Views/Home/Index.cshtml). Runs on every page load, so the
// count is cached briefly (unlike AttentionRules.GetStaleListingsAsync itself, which the
// Needs Attention page calls directly and must always be fresh right after an ignore/update).
public class NeedsAttentionBadgeViewComponent : ViewComponent
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);

    private readonly ApplicationDbContext _dbContext;
    private readonly IMemoryCache _cache;

    public NeedsAttentionBadgeViewComponent(ApplicationDbContext dbContext, IMemoryCache cache)
    {
        _dbContext = dbContext;
        _cache = cache;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var username = UserClaimsPrincipal?.Identity?.Name ?? string.Empty;
        if (string.IsNullOrWhiteSpace(username))
        {
            return Content(string.Empty);
        }

        var cacheKey = $"attention_count_{username}";
        if (!_cache.TryGetValue(cacheKey, out int count))
        {
            // Total across every Needs Attention category (stale, sold-out-with-backstock,
            // needs-unit-resolved, and any future category) — see
            // AttentionRules.GetTotalNeedsAttentionCountAsync, the single source of truth this
            // and the page's own header count both read from.
            count = await AttentionRules.GetTotalNeedsAttentionCountAsync(_dbContext, username, DateTime.UtcNow);
            _cache.Set(cacheKey, count, CacheDuration);
        }

        return View(count);
    }
}
