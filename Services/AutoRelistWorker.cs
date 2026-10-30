using Microsoft.EntityFrameworkCore;
using inventory_management.Data;
using inventory_management.Models;

namespace inventory_management.Services;

public class AutoRelistWorker : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan RelistWindow = TimeSpan.FromHours(24);
    private static readonly TimeSpan StuckJobTimeout = TimeSpan.FromHours(2);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AutoRelistWorker> _logger;

    public AutoRelistWorker(IServiceScopeFactory scopeFactory, ILogger<AutoRelistWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Brief startup delay so the app fully initializes before first run.
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessAllUsersAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AutoRelistWorker encountered an unhandled error.");
            }

            await Task.Delay(CheckInterval, stoppingToken);
        }
    }

    private async Task ProcessAllUsersAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ebayService = scope.ServiceProvider.GetRequiredService<IEbayService>();

        // Get all users that have an eBay connection.
        var usernames = await db.UserEbayConnections
            .AsNoTracking()
            .Select(x => x.Username)
            .ToListAsync(ct);

        foreach (var username in usernames)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                await ProcessUserAsync(db, ebayService, username, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AutoRelistWorker failed for user {Username}.", username);
            }
        }
    }

    private async Task ProcessUserAsync(
        ApplicationDbContext db,
        IEbayService ebayService,
        string username,
        CancellationToken ct)
    {
        var nowUtc = DateTime.UtcNow;
        var stuckCutoff = nowUtc - StuckJobTimeout;

        // Un-stick any jobs that have been in-progress too long (crash recovery).
        var stuckListings = await db.InventoryListings
            .Where(x => x.Username == username
                     && x.RelistInProgress
                     && x.UpdatedAtUtc < stuckCutoff)
            .ToListAsync(ct);

        if (stuckListings.Count > 0)
        {
            foreach (var stuck in stuckListings)
            {
                stuck.RelistInProgress = false;
                stuck.UpdatedAtUtc = nowUtc;
                _logger.LogWarning("Cleared stuck relist job for item {ItemId} (user {Username}).", stuck.ItemId, username);
            }
            await db.SaveChangesAsync(ct);
        }

        // Find BuyItNow listings that have auto-relist enabled and expire within the relist window.
        var windowCutoff = nowUtc + RelistWindow;
        var candidates = await db.InventoryListings
            .Where(x => x.Username == username
                     && x.IsActive
                     && x.AutoRelistEnabled
                     && !x.RelistInProgress
                     && x.ListingEndTimeUtc != null
                     && x.ListingEndTimeUtc <= windowCutoff
                     && x.ListingEndTimeUtc > nowUtc
                     && (x.ListingType == "FixedPriceItem" || x.ListingType == "StoresFixedPrice"))
            .ToListAsync(ct);

        if (candidates.Count == 0) return;

        _logger.LogInformation("AutoRelistWorker: {Count} candidate(s) to relist for user {Username}.", candidates.Count, username);

        foreach (var listing in candidates)
        {
            if (ct.IsCancellationRequested) break;

            // Claim the job: set relist_in_progress = true to prevent duplicate runs.
            listing.RelistInProgress = true;
            listing.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            var oldItemId = listing.ItemId;
            string? newItemId = null;
            string? errorMessage = null;

            try
            {
                _logger.LogInformation("AutoRelistWorker: ending item {ItemId} for user {Username}.", oldItemId, username);
                await ebayService.EndItemAsync(username, oldItemId);

                _logger.LogInformation("AutoRelistWorker: relisting item {ItemId} for user {Username}.", oldItemId, username);
                newItemId = await ebayService.RelistItemAsync(username, oldItemId);

                // LOCKED INVARIANT: mutate ItemId on this SAME row — never delete/recreate.
                // Id and Barcode must never change over an item's lifespan (see InventoryListing.cs
                // and SyncInventoryListingsFromEbayAsync, which handles the other relist path: eBay
                // relisting an item on its own without going through this worker).
                listing.ItemId = newItemId;
                listing.ListingEndTimeUtc = null; // will be refreshed on next sync

                // Location/box/storage-unit assignments are keyed by ItemId — carry them over
                // to the new ItemId so a relisted item never loses its physical location.
                await db.InventoryItemLocations
                    .Where(x => x.Username == username && x.ItemId == oldItemId)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.ItemId, newItemId), ct);
                await db.InventoryItemBoxAssignments
                    .Where(x => x.Username == username && x.ItemId == oldItemId)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.ItemId, newItemId), ct);
                await db.InventoryItemStorageUnitAssignments
                    .Where(x => x.Username == username && x.ItemId == oldItemId)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.ItemId, newItemId), ct);
                // Sale records too — otherwise AttentionRules' "last sale date" lookup (keyed by
                // the current ItemId) would miss a genuinely-recent sale still filed under the
                // old ItemId, wrongly making the item look stale on Needs Attention.
                await db.RecentOrders
                    .Where(x => x.Username == username && x.ItemId == oldItemId)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.ItemId, newItemId), ct);

                _logger.LogInformation("AutoRelistWorker: item {OldId} relisted as {NewId} for user {Username}.", oldItemId, newItemId, username);
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                _logger.LogError(ex, "AutoRelistWorker: failed to relist item {ItemId} for user {Username}.", oldItemId, username);
            }
            finally
            {
                listing.RelistInProgress = false;
                listing.UpdatedAtUtc = DateTime.UtcNow;

                // Record the relist attempt in history regardless of success or failure.
                db.RelistHistories.Add(new RelistHistory
                {
                    Username = username,
                    ListingId = listing.Id,
                    OldItemId = oldItemId,
                    NewItemId = newItemId,
                    RelistedAtUtc = DateTime.UtcNow,
                    ErrorMessage = errorMessage
                });

                await db.SaveChangesAsync(ct);
            }
        }
    }
}
