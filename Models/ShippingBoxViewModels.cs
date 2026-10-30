using System.ComponentModel.DataAnnotations;

namespace inventory_management.Models;

public class ShippingBoxFormViewModel
{
    public Guid? Id { get; set; }

    [Required]
    [MaxLength(100)]
    [Display(Name = "Box Name")]
    public string Name { get; set; } = string.Empty;

    [Required]
    [Range(0.01, 999.99)]
    [Display(Name = "Length (in)")]
    public decimal LengthIn { get; set; }

    [Required]
    [Range(0.01, 999.99)]
    [Display(Name = "Width (in)")]
    public decimal WidthIn { get; set; }

    [Required]
    [Range(0.01, 999.99)]
    [Display(Name = "Height (in)")]
    public decimal HeightIn { get; set; }

    [Range(0, 999)]
    [Display(Name = "Weight (lbs)")]
    public int? WeightLbs { get; set; }

    [Range(0, 15.9)]
    [Display(Name = "Weight (oz)")]
    public decimal? WeightOz { get; set; }
}

public class ShippingBoxAddStockViewModel
{
    [Required]
    public Guid Id { get; set; }

    [Required]
    [Range(1, 9999)]
    public int Amount { get; set; }
}

public class ShippingBoxIndexViewModel
{
    public List<ShippingBoxRowViewModel> Boxes { get; set; } = new();
}

public class ShippingBoxRowViewModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal LengthIn { get; set; }
    public decimal WidthIn { get; set; }
    public decimal HeightIn { get; set; }
    public int? WeightLbs { get; set; }
    public decimal? WeightOz { get; set; }
    public int Quantity { get; set; }
}
