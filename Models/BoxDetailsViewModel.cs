namespace inventory_management.Models;

public class BoxDetailsViewModel
{
    public Guid BoxId { get; set; }
    public string BoxIdentifier { get; set; } = string.Empty;
    public string BoxBarcode { get; set; } = string.Empty;
    public string ShelfLocation { get; set; } = string.Empty;
    public Guid? ShelfUnitId { get; set; }
    public Guid? StorageUnitId { get; set; }
    public string StorageUnitName { get; set; } = string.Empty;
    public string StorageUnitIdentifier { get; set; } = string.Empty;
    public bool IsTransitBox { get; set; }
    public List<StorageItemViewModel> Items { get; set; } = new();
}
