namespace inventory_management.Models;

public class EbayOptions
{
    public const string SectionName = "Ebay";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string RuName { get; set; } = string.Empty;
    public string Environment { get; set; } = "Sandbox";
    public string Scopes { get; set; } = "https://api.ebay.com/oauth/api_scope";
}
