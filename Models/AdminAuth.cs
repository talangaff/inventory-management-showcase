namespace inventory_management.Models;

public class AdminAuth
{
    public int Id { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public bool RequirePasswordChange { get; set; }
}
