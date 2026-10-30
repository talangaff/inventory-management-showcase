namespace inventory_management.Models;

public class AuthOptions
{
    public const string SectionName = "Auth";

    public string Username { get; set; } = "admin";
    public string Password { get; set; } = "changeme";
}
