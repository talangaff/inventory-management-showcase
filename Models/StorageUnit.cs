namespace inventory_management.Models;

public class StorageUnit
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Identifier { get; set; } = string.Empty;
    public string BarcodeNumber { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; }
}

