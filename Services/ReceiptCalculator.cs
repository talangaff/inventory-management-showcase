using Microsoft.EntityFrameworkCore;
using inventory_management.Data;
using inventory_management.Models;

namespace inventory_management.Services;

// Shared tax-allocation logic for Receipts, used by every ReceiptsController action that can
// change a receipt's total or any of its items' pre-tax prices, so the math can't drift between
// call sites. TotalPrice on a Receipt is tax-included; each ReceiptItem.PreTaxPrice is entered
// before tax. The implied tax rate is TotalPrice / sum(PreTaxPrice), applied proportionally to
// each item to get AllocatedTotalPrice. Whenever this runs, it also re-pushes the recomputed
// AllocatedTotalPrice onto PurchaseCost for any item already assigned to an inventory listing —
// per the owner's explicit instruction that editing a receipt keeps already-assigned costs in sync.
public static class ReceiptCalculator
{
    public static async Task RecalculateAsync(ApplicationDbContext dbContext, Guid receiptId, string username)
    {
        var receipt = await dbContext.Receipts
            .FirstOrDefaultAsync(x => x.Username == username && x.Id == receiptId);
        if (receipt is null)
        {
            return;
        }

        var items = await dbContext.ReceiptItems
            .Where(x => x.Username == username && x.ReceiptId == receiptId)
            .ToListAsync();

        var nowUtc = DateTime.UtcNow;
        var sumPreTax = items.Sum(x => x.PreTaxPrice);
        var rate = sumPreTax > 0 ? receipt.TotalPrice / sumPreTax : 0m;

        var assignedListingIds = items
            .Where(x => x.AssignedListingId.HasValue)
            .Select(x => x.AssignedListingId!.Value)
            .ToList();
        var assignedListings = assignedListingIds.Count == 0
            ? new List<InventoryListing>()
            : await dbContext.InventoryListings
                .Where(x => x.Username == username && assignedListingIds.Contains(x.Id))
                .ToListAsync();
        var listingsById = assignedListings.ToDictionary(x => x.Id);

        foreach (var item in items)
        {
            item.AllocatedTotalPrice = Math.Round(item.PreTaxPrice * rate, 2);
            item.UpdatedAtUtc = nowUtc;

            if (item.AssignedListingId.HasValue && listingsById.TryGetValue(item.AssignedListingId.Value, out var listing))
            {
                listing.PurchaseCost = item.AllocatedTotalPrice;
                listing.UpdatedAtUtc = nowUtc;
            }
        }

        await dbContext.SaveChangesAsync();
    }
}
