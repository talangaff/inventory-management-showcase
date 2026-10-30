namespace inventory_management.Models;

public class UserDraftSettings
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string DescriptionTemplate { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; }
}
