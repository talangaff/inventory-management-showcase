namespace inventory_management.Models;

public class ShippingBox
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Username { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal LengthIn { get; set; }
    public decimal WidthIn { get; set; }
    public decimal HeightIn { get; set; }
    public int? WeightLbs { get; set; }
    public decimal? WeightOz { get; set; }
    public int Quantity { get; set; } = 0;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
