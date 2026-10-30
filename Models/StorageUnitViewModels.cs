using System.ComponentModel.DataAnnotations;

namespace inventory_management.Models;

public class StorageUnitIndexViewModel
{
    public List<StorageUnitListItemViewModel> StorageUnits { get; set; } = [];
}

public class StorageUnitListItemViewModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Identifier { get; set; } = string.Empty;
    public string BarcodeNumber { get; set; } = string.Empty;
    public int ShelfUnitCount { get; set; }
    public int BoxCount { get; set; }
    public int DirectItemCount { get; set; }
}

public class StorageUnitFormViewModel
{
    public Guid? Id { get; set; }

    [Required]
    [Display(Name = "Name")]
    public string Name { get; set; } = string.Empty;

    [Required]
    [RegularExpression("^[A-Za-z0-9\\-_]+$", ErrorMessage = "Identifier may contain letters, numbers, hyphens, and underscores only.")]
    [Display(Name = "Identifier")]
    public string Identifier { get; set; } = string.Empty;
}

public class StorageUnitDetailsViewModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Identifier { get; set; } = string.Empty;
    public string BarcodeNumber { get; set; } = string.Empty;
    public List<StorageUnitShelfSummaryViewModel> ShelfUnits { get; set; } = [];
    public List<StorageUnitBoxSummaryViewModel> Boxes { get; set; } = [];
    public List<StorageItemViewModel> DirectItems { get; set; } = [];
}

public class StorageUnitShelfSummaryViewModel
{
    public Guid Id { get; set; }
    public string Identifier { get; set; } = string.Empty;
    public int ShelfCount { get; set; }
    public int ItemCount { get; set; }
}

public class StorageUnitBoxSummaryViewModel
{
    public Guid Id { get; set; }
    public string BoxIdentifier { get; set; } = string.Empty;
    public int ItemCount { get; set; }
}

public class StorageUnitOptionViewModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Identifier { get; set; } = string.Empty;
}

public class ShelfIndexViewModel
{
    public IReadOnlyList<ShelfUnit> Units { get; set; } = [];
    public Dictionary<Guid, string> StorageUnitNameById { get; set; } = [];
    // ShelfUnitId -> ShelfNumber -> Status
    public Dictionary<Guid, Dictionary<int, ShelfStatus>> SlotStatusesByUnitId { get; set; } = [];
    // ShelfUnitId -> count of active-inventory items on the unit (direct + in boxes on it)
    public Dictionary<Guid, int> ItemCountByUnitId { get; set; } = [];
}
