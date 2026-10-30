using System.ComponentModel.DataAnnotations;

namespace inventory_management.Models;

public class AssignInventoryLocationViewModel
{
    [Required]
    public string ItemId { get; set; } = string.Empty;

    [Required]
    public string ItemTitle { get; set; } = string.Empty;

    [Required]
    public Guid ShelfUnitId { get; set; }

    [Range(1, 9)]
    public int ShelfNumber { get; set; }
}
