using System.ComponentModel.DataAnnotations;

namespace inventory_management.Models;

public class CreateShelfUnitViewModel
{
    public Guid Id { get; set; }

    [Required]
    [RegularExpression("^[A-Za-z]+$", ErrorMessage = "Identifier must contain letters only.")]
    [Display(Name = "Unit Identifier")]
    public string Identifier { get; set; } = string.Empty;

    [Range(1, 9, ErrorMessage = "Shelf Count must be between 1 and 9.")]
    [Display(Name = "Shelf Count")]
    public int ShelfCount { get; set; } = 5;

    [Display(Name = "Storage Unit (optional)")]
    public Guid? StorageUnitId { get; set; }

    [Display(Name = "Status")]
    public ShelfStatus Status { get; set; } = ShelfStatus.ActiveInventory;

    public List<StorageUnitOptionViewModel> StorageUnits { get; set; } = [];

    // Per-shelf statuses (only populated/used in edit mode)
    public List<ShelfSlotStatusEditModel> ShelfStatuses { get; set; } = [];
}

public class ShelfSlotStatusEditModel
{
    public int ShelfNumber { get; set; }
    public ShelfStatus Status { get; set; } = ShelfStatus.ActiveInventory;
}
