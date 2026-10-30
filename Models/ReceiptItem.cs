namespace inventory_management.Models;

/// <summary>One line item on a Receipt. A manually entered name is optional — the line is
/// primarily identified by its price and (once assigned) the inventory item it maps to.
/// PreTaxPrice is entered by the user; AllocatedTotalPrice is computed by
/// Services/ReceiptCalculator.cs (this item's proportional share of the parent Receipt's
/// tax-included TotalPrice) and is what gets pushed to InventoryListing.PurchaseCost on
/// assignment. Editing this item's price, another item's price, or the receipt's total
/// recomputes AllocatedTotalPrice for every item on the receipt and re-pushes it to any
/// already-assigned listing's PurchaseCost.</summary>
public class ReceiptItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Username { get; set; } = string.Empty;
    public Guid ReceiptId { get; set; }
    public string? Name { get; set; }
    public decimal PreTaxPrice { get; set; }
    /// <summary>This item's proportional share of the receipt's tax-included total. Recomputed
    /// by ReceiptCalculator whenever this receipt's items or total change.</summary>
    public decimal AllocatedTotalPrice { get; set; }
    /// <summary>Null until this line is assigned to an inventory item. See
    /// InventoryListing.Id (NOT the mutable eBay ItemId).</summary>
    public Guid? AssignedListingId { get; set; }
    public DateTime? AssignedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
