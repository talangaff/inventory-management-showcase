using System.ComponentModel.DataAnnotations;

namespace inventory_management.Models;

public class BoxIndexViewModel
{
    public List<BoxListItemViewModel> Boxes { get; set; } = [];
}

public class BoxListItemViewModel
{
    public Guid Id { get; set; }
    public string BoxIdentifier { get; set; } = string.Empty;
    public string BoxBarcode { get; set; } = string.Empty;
    public string ShelfUnitIdentifier { get; set; } = string.Empty;
    public int ShelfNumber { get; set; }
    public string ShelfBarcode { get; set; } = string.Empty;
    public Guid? StorageUnitId { get; set; }
    public string StorageUnitIdentifier { get; set; } = string.Empty;
    public bool IsTransitBox { get; set; }
    public int ItemCount { get; set; }
}

public class BoxFormViewModel
{
    public Guid? Id { get; set; }

    [Required]
    [RegularExpression("^[A-Za-z]+$", ErrorMessage = "Box identifier must contain letters only.")]
    [Display(Name = "Box Identifier")]
    public string BoxIdentifier { get; set; } = string.Empty;

    // "shelf" or "storage_unit"
    public string LocationType { get; set; } = "shelf";

    [Display(Name = "Shelving Unit")]
    public Guid ShelfUnitId { get; set; }

    [Range(0, 9)]
    [Display(Name = "Shelf Number")]
    public int ShelfNumber { get; set; } = 1;

    [Display(Name = "Storage Unit")]
    public Guid? StorageUnitId { get; set; }

    public List<BoxShelfUnitOptionViewModel> ShelfUnits { get; set; } = [];
    public List<StorageUnitOptionViewModel> StorageUnits { get; set; } = [];
}

public class BoxShelfUnitOptionViewModel
{
    public Guid Id { get; set; }
    public string Identifier { get; set; } = string.Empty;
    public int ShelfCount { get; set; }
    public string BarcodePrefix { get; set; } = string.Empty;
}

public class AssignInventoryBoxViewModel
{
    [Required]
    public string ItemId { get; set; } = string.Empty;

    [Required]
    public string ItemTitle { get; set; } = string.Empty;

    [Required]
    public Guid BoxId { get; set; }
}
