namespace inventory_management.Models;

public class DraftIndexViewModel
{
    public bool IsConnected { get; set; }
    public string? ErrorMessage { get; set; }
    public List<DraftListItemViewModel> Drafts { get; set; } = [];
}

public class DraftListItemViewModel
{
    public string DraftId { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? CoverPhotoUrl { get; set; }
    public string? ConditionId { get; set; }
    public decimal? Price { get; set; }
    public string? Currency { get; set; }
    public DateTime SyncedAtUtc { get; set; }
}

public class DraftEditViewModel
{
    public bool IsConnected { get; set; }
    public string? ErrorMessage { get; set; }

    public string DraftId { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? CoverPhotoUrl { get; set; }
    public string? CategoryId { get; set; }

    public string? ConditionId { get; set; }
    public string? ConditionDescription { get; set; }
    public decimal? Price { get; set; }
    public string Currency { get; set; } = "USD";
    public decimal? WeightPounds { get; set; }
    public decimal? LengthIn { get; set; }
    public decimal? WidthIn { get; set; }
    public decimal? HeightIn { get; set; }
    public string? Description { get; set; }

    public string DescriptionTemplate { get; set; } = string.Empty;

    public string? NextDraftId { get; set; }
    public int DraftPosition { get; set; }
    public int TotalDrafts { get; set; }
}

public class DraftSaveRequest
{
    public string DraftId { get; set; } = string.Empty;
    public string? ConditionId { get; set; }
    public string? ConditionDescription { get; set; }
    public decimal? Price { get; set; }
    public string? Currency { get; set; }
    public decimal? WeightPounds { get; set; }
    public decimal? LengthIn { get; set; }
    public decimal? WidthIn { get; set; }
    public decimal? HeightIn { get; set; }
    public string? Description { get; set; }
}

public class EbayDraftViewModel
{
    public string DraftId { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? CoverPhotoUrl { get; set; }
    public string? ConditionId { get; set; }
    public string? ConditionDescription { get; set; }
    public decimal? Price { get; set; }
    public string? Currency { get; set; }
    public decimal? WeightPounds { get; set; }
    public decimal? LengthIn { get; set; }
    public decimal? WidthIn { get; set; }
    public decimal? HeightIn { get; set; }
    public string? CategoryId { get; set; }
}

public class DraftUpdateInput
{
    public string? ConditionId { get; set; }
    public string? ConditionDescription { get; set; }
    public decimal? Price { get; set; }
    public string? Currency { get; set; }
    public decimal? WeightPounds { get; set; }
    public decimal? LengthIn { get; set; }
    public decimal? WidthIn { get; set; }
    public decimal? HeightIn { get; set; }
    public string? Description { get; set; }
}
