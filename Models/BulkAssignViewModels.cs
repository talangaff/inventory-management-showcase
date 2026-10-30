using System.ComponentModel.DataAnnotations;

namespace inventory_management.Models;

public class BulkAssignLocationViewModel
{
    [Required]
    public List<string> ItemIds { get; set; } = new();

    [Required]
    public Guid ShelfUnitId { get; set; }

    [Range(1, 9)]
    public int ShelfNumber { get; set; }
}

public class BulkAssignBoxViewModel
{
    [Required]
    public List<string> ItemIds { get; set; } = new();

    [Required]
    public Guid BoxId { get; set; }
}

public class BulkRemoveLocationViewModel
{
    [Required]
    public List<string> ItemIds { get; set; } = new();
}

/// <summary>Shared config for the "select items, bulk assign/remove location" toolbar
/// used on the main inventory page and every storage-location Details page.</summary>
public class BulkItemActionsConfigViewModel
{
    public List<ShelfUnitOptionViewModel> ShelfUnits { get; set; } = [];
    public List<BoxOptionViewModel> Boxes { get; set; } = [];
}
