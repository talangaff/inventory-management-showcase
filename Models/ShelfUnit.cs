namespace inventory_management.Models;

public enum ShelfStatus
{
    ActiveInventory = 0,
    Unlisted = 1,
    NeedsSorted = 2
}

public class ShelfUnit
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Identifier { get; set; } = string.Empty;
    public string BarcodeNumber { get; set; } = string.Empty;
    public int ShelfCount { get; set; }
    public Guid? StorageUnitId { get; set; }
    public ShelfStatus Status { get; set; } = ShelfStatus.ActiveInventory;

    public IReadOnlyList<string> GetShelfBarcodeNumbers()
    {
        if (string.IsNullOrWhiteSpace(BarcodeNumber))
        {
            return [];
        }

        var prefix = BarcodeNumber.EndsWith('0')
            ? BarcodeNumber[..^1]
            : BarcodeNumber;

        var count = Math.Clamp(ShelfCount, 1, 9);
        return Enumerable.Range(1, count)
            .Select(shelfNumber => $"{prefix}{shelfNumber}")
            .ToList();
    }
}
