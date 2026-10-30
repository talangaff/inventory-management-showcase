namespace inventory_management.Models;

public class Box
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Username { get; set; } = string.Empty;
    public string BoxIdentifier { get; set; } = string.Empty;
    public string BoxBarcode { get; set; } = string.Empty;
    public string ShelfUnitIdentifier { get; set; } = string.Empty;
    public int ShelfNumber { get; set; }
    public string ShelfBarcode { get; set; } = string.Empty;
    public Guid? StorageUnitId { get; set; }
    public ShelfStatus Status { get; set; } = ShelfStatus.ActiveInventory;
    public bool IsTransitBox { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
