namespace inventory_management.Models;

public class ShelfSlotStatus
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ShelfUnitId { get; set; }
    public int ShelfNumber { get; set; }
    public ShelfStatus Status { get; set; } = ShelfStatus.ActiveInventory;
}
