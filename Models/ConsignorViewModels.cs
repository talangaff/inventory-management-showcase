namespace inventory_management.Models;

public class ConsignorsIndexViewModel
{
    public List<ConsignorListItemViewModel> Consignors { get; set; } = [];
}

public class ConsignorListItemViewModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal DefaultSplitPercent { get; set; }
    public int ActiveListingCount { get; set; }
    public int SaleRecordCount { get; set; }
}

public class ConsignorFormRequest
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal DefaultSplitPercent { get; set; } = 50m;
}
