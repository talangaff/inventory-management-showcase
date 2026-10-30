namespace inventory_management.Models;

public class ShelfDetailsViewModel
{
    public Guid Id { get; set; }
    public string Identifier { get; set; } = string.Empty;
    public int ShelfCount { get; set; }
    public ShelfStatus Status { get; set; } = ShelfStatus.ActiveInventory;
    public List<ShelfSlotViewModel> Shelves { get; set; } = new();
}

public class ShelfSlotViewModel
{
    public int ShelfNumber { get; set; }
    public ShelfStatus Status { get; set; } = ShelfStatus.ActiveInventory;
    public List<StorageItemViewModel> DirectItems { get; set; } = new();
    public List<ShelfSlotBoxViewModel> Boxes { get; set; } = new();
}

public class ShelfSlotBoxViewModel
{
    public Guid BoxId { get; set; }
    public string BoxIdentifier { get; set; } = string.Empty;
    public string BoxBarcode { get; set; } = string.Empty;
    public List<StorageItemViewModel> Items { get; set; } = new();
}

public class ShelfSlotItemsViewModel
{
    public Guid ShelfUnitId { get; set; }
    public string ShelfUnitIdentifier { get; set; } = string.Empty;
    public int ShelfNumber { get; set; }
    public List<StorageItemViewModel> DirectItems { get; set; } = new();
    public List<ShelfSlotBoxViewModel> Boxes { get; set; } = new();
}
