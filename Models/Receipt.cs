namespace inventory_management.Models;

/// <summary>One purchase receipt. TotalPrice is the final amount actually paid, tax included.
/// Each ReceiptItem's PreTaxPrice is entered before tax; the implied tax rate
/// (TotalPrice / sum of item PreTaxPrices) is computed and applied proportionally to each item
/// to produce ReceiptItem.AllocatedTotalPrice — see Services/ReceiptCalculator.cs.</summary>
public class Receipt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Username { get; set; } = string.Empty;
    public DateOnly ReceiptDate { get; set; }
    /// <summary>Final amount paid, including tax.</summary>
    public decimal TotalPrice { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
