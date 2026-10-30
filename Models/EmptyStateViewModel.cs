namespace inventory_management.Models;

public class EmptyStateViewModel
{
    public string Icon { get; set; } = "inbox";
    public string Title { get; set; } = string.Empty;
    public string? Message { get; set; }
    public string? ActionText { get; set; }
    public string? ActionUrl { get; set; }
}
