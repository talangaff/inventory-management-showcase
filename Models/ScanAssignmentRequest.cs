namespace inventory_management.Models;

public class ScanAssignmentRequest
{
    public string ItemBarcode { get; set; } = string.Empty;
    public string StorageBarcode { get; set; } = string.Empty;
}

public class UpdateShelfUnitRequest
{
    public Guid Id { get; set; }
    public int Status { get; set; }
    public List<ShelfSlotUpdateItem> Slots { get; set; } = [];
}

public class ShelfSlotUpdateItem
{
    public int ShelfNumber { get; set; }
    public int Status { get; set; }
}

public class UpdateShelfSlotRequest
{
    public Guid ShelfUnitId { get; set; }
    public int ShelfNumber { get; set; }
    public int Status { get; set; }
}

public class RemoveItemLocationRequest
{
    public string ItemId { get; set; } = string.Empty;
}
