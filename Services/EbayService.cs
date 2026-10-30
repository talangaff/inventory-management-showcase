using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using inventory_management.Data;
using inventory_management.Models;

namespace inventory_management.Services;

public interface IEbayService
{
    bool IsConfigured();
    string BuildAuthorizeUrl(string state);
    Task ExchangeCodeForTokensAsync(string code);
    Task<IReadOnlyList<EbayListingViewModel>> GetActiveListingsAsync();
    Task<PagedEbayListingsResult> GetActiveListingsPageAsync(int pageNumber, int pageSize = 25);
    Task<IReadOnlyList<OrderListItemViewModel>> GetRecentOrdersAsync(int daysBack = 90, IReadOnlyDictionary<string, string>? knownPhotos = null, ISet<string>? cancelledOrderIds = null);
    Task<EbayItemDetailsViewModel?> GetItemDetailsAsync(string itemId);
    /// <summary>Updates the Buy-It-Now start price on eBay via ReviseItem. Throws on failure.</summary>
    Task UpdateItemPriceAsync(string itemId, decimal newPrice);
    /// <summary>
    /// LOCKED INVARIANT — see SyncInventoryListingsFromEbayAsync in InventoryController.
    /// Writes a listing's permanent local Barcode into eBay's Custom Label (SKU) field via
    /// ReviseItem. This is what lets sync recognize an item after eBay silently relists it
    /// under a brand-new ItemID (native "automatically relist" listings do this without any
    /// other trace) and keep the same barcode instead of minting a new one. Never remove this
    /// call or repurpose the SKU field for anything else. Throws on failure (best-effort caller
    /// should catch and log, not fail the whole sync).
    /// </summary>
    Task UpdateItemSkuAsync(string itemId, string sku);
    /// <summary>
    /// Uploads a tracking number to eBay via the Fulfillment REST API.
    /// Returns true on success, false if the scope is missing (403) or the call otherwise fails.
    /// </summary>
    Task<bool> UploadTrackingAsync(string username, string orderId, string trackingNumber, string carrierCode);
    /// <summary>
    /// Returns the set of order IDs that eBay considers fulfilled (FULFILLED or PARTIALLY_FULFILLED)
    /// via the Fulfillment REST API. Used to auto-detect orders shipped outside this app (e.g.
    /// eBay International Shipping / GSP) whose tracking the Trading API's GetOrders misses.
    /// Returns an empty set on any error.
    /// </summary>
    Task<HashSet<string>> GetFulfilledOrderIdsAsync();
    /// <summary>Best-effort lookup of eBay's total fee and actual shipping label cost for a
    /// batch of orders via the Finance REST API (requires the sell.finances OAuth scope, not
    /// requested by default — see Models/EbayOptions.cs). eBay's orderId filter on this endpoint
    /// doesn't reliably match in practice, so this fetches every transaction (SALE and
    /// SHIPPING_LABEL) over the last 90 days and matches orderId client-side instead — one
    /// batched call rather than one per order. Returns an empty dict on any error, including a
    /// missing scope (403), so it's always safe to call; feeds RecentOrder.EbayFee /
    /// ShippingLabelCost -> the matching SaleRecord fields. An order absent from the result, or
    /// present with a null field, means that piece hasn't posted to eBay's ledger yet (e.g. not
    /// settled, or shipped without a label eBay tracks) — not that it's zero.</summary>
    Task<Dictionary<string, OrderFinanceData>> GetOrderFinanceDataAsync(IEnumerable<string> orderIds);
    /// <summary>Ends an active eBay listing via EndItem. Safe to call from background worker. Throws on failure.</summary>
    Task EndItemAsync(string username, string itemId);
    /// <summary>
    /// Relists a previously-ended eBay item via RelistItem and returns the new item ID.
    /// Safe to call from background worker. Throws on failure.
    /// </summary>
    Task<string> RelistItemAsync(string username, string itemId);
    Task<List<EbayDraftViewModel>> GetDraftListingsAsync();
    Task<EbayDraftViewModel?> GetDraftByIdAsync(string draftId);
    Task UpdateDraftAsync(string draftId, DraftUpdateInput input);
    Task<string> PublishDraftAsync(string draftId);
    void Disconnect();
    bool IsConnected();
    Task<List<EbayPromotionViewModel>> GetPromotionsAsync();
}

public class PagedEbayListingsResult
{
    public List<EbayListingViewModel> Items { get; set; } = [];
    public bool HasMore { get; set; }
}

/// <summary>Either field can be null independent of the other — a fee can post before a
/// shipping label transaction does, or vice versa. See IEbayService.GetOrderFinanceDataAsync.</summary>
public class OrderFinanceData
{
    public decimal? Fee { get; set; }
    public decimal? ShippingLabelCost { get; set; }
}

public class EbayService : IEbayService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ApplicationDbContext _dbContext;
    private readonly EbayOptions _options;

    public EbayService(
        IHttpClientFactory httpClientFactory,
        IHttpContextAccessor httpContextAccessor,
        ApplicationDbContext dbContext,
        IOptions<EbayOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _httpContextAccessor = httpContextAccessor;
        _dbContext = dbContext;
        _options = options.Value;
    }

    public bool IsConfigured()
    {
        return !string.IsNullOrWhiteSpace(_options.ClientId)
            && !string.IsNullOrWhiteSpace(_options.ClientSecret)
            && !string.IsNullOrWhiteSpace(_options.RuName);
    }

    public bool IsConnected()
    {
        var username = GetCurrentUsernameOrNull();
        if (string.IsNullOrWhiteSpace(username))
        {
            return false;
        }

        return GetStoredTokens(username) is not null;
    }

    public string BuildAuthorizeUrl(string state)
    {
        EnsureConfigured();

        var authBaseUrl = IsSandbox() ? "https://auth.sandbox.ebay.com" : "https://auth.ebay.com";
        return $"{authBaseUrl}/oauth2/authorize" +
               $"?client_id={Uri.EscapeDataString(_options.ClientId)}" +
               $"&redirect_uri={Uri.EscapeDataString(_options.RuName)}" +
               "&response_type=code" +
               $"&scope={Uri.EscapeDataString(_options.Scopes)}" +
               $"&state={Uri.EscapeDataString(state)}";
    }

    public async Task ExchangeCodeForTokensAsync(string code)
    {
        EnsureConfigured();

        var client = _httpClientFactory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, BuildIdentityUrl("/identity/v1/oauth2/token"));

        var basicAuth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basicAuth);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = _options.RuName
        });

        var response = await client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Token exchange failed: {content}");
        }

        var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(content)
            ?? throw new InvalidOperationException("Unable to parse token response.");

        SaveTokens(tokenResponse);
    }

    public async Task<IReadOnlyList<EbayListingViewModel>> GetActiveListingsAsync()
    {
        EnsureConfigured();

        var allItems = new List<EbayListingViewModel>();
        var pageNumber = 1;
        while (true)
        {
            var page = await GetActiveListingsPageAsync(pageNumber, 200);
            allItems.AddRange(page.Items);
            if (!page.HasMore)
            {
                break;
            }

            pageNumber++;
        }

        return allItems;
    }

    public async Task<PagedEbayListingsResult> GetActiveListingsPageAsync(int pageNumber, int pageSize = 25)
    {
        EnsureConfigured();

        if (pageNumber < 1)
        {
            pageNumber = 1;
        }

        if (pageSize < 1)
        {
            pageSize = 25;
        }

        var accessToken = await GetValidAccessTokenAsync();
        var responseXml = await CallGetMyeBaySellingAsync(accessToken, pageNumber, pageSize);
        XNamespace ns = "urn:ebay:apis:eBLBaseComponents";

        var ack = responseXml.Root?.Element(ns + "Ack")?.Value;
        if (!string.Equals(ack, "Success", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(ack, "Warning", StringComparison.OrdinalIgnoreCase))
        {
            var error = responseXml.Descendants(ns + "LongMessage").FirstOrDefault()?.Value
                ?? "eBay API returned an unknown error.";
            throw new InvalidOperationException(error);
        }

        var items = responseXml
            .Descendants(ns + "ActiveList")
            .Descendants(ns + "Item")
            .Select(item =>
            {
                var currentPriceEl = item.Element(ns + "SellingStatus")?.Element(ns + "CurrentPrice");
                decimal? currentPrice = null;
                if (currentPriceEl != null && decimal.TryParse(currentPriceEl.Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var price))
                {
                    currentPrice = price;
                }
                var currencyId = currentPriceEl?.Attribute("currencyID")?.Value;

                DateTime? endTime = null;
                var endTimeRaw = item.Element(ns + "ListingDetails")?.Element(ns + "EndTime")?.Value
                    ?? item.Element(ns + "EndTime")?.Value;
                if (!string.IsNullOrWhiteSpace(endTimeRaw) && DateTime.TryParse(endTimeRaw, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsedEnd))
                {
                    endTime = parsedEnd;
                }

                var listingType = item.Element(ns + "ListingDetails")?.Element(ns + "ListingType")?.Value
                    ?? item.Element(ns + "ListingType")?.Value;

                int? quantity = null;
                var quantityEl = item.Element(ns + "Quantity");
                if (quantityEl != null && int.TryParse(quantityEl.Value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var qty))
                    quantity = qty;

                int? quantitySold = null;
                var quantitySoldEl = item.Element(ns + "SellingStatus")?.Element(ns + "QuantitySold");
                if (quantitySoldEl != null && int.TryParse(quantitySoldEl.Value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var sold))
                    quantitySold = sold;

                return new EbayListingViewModel
                {
                    ItemId = item.Element(ns + "ItemID")?.Value ?? string.Empty,
                    // Mirrors our permanent local Barcode once we've pushed it — see UpdateItemSkuAsync.
                    Sku = item.Element(ns + "SKU")?.Value,
                    Title = item.Element(ns + "Title")?.Value ?? "(Untitled listing)",
                    CoverPhotoUrl =
                        item.Element(ns + "GalleryURL")?.Value
                        ?? item.Element(ns + "PictureDetails")?.Element(ns + "GalleryURL")?.Value
                        ?? item.Element(ns + "PictureDetails")?.Elements(ns + "PictureURL").Select(x => x.Value).FirstOrDefault(url => !string.IsNullOrWhiteSpace(url))
                        ?? item.Element(ns + "PictureDetails")?.Elements(ns + "ExternalPictureURL").Select(x => x.Value).FirstOrDefault(url => !string.IsNullOrWhiteSpace(url)),
                    CurrentPrice = currentPrice,
                    CurrencyId = currencyId,
                    EndTime = endTime,
                    ListingType = listingType,
                    QuantityAvailable = quantity.HasValue ? quantity.Value - (quantitySold ?? 0) : (int?)null
                };
            })
            .ToList();

        var hasMore = ParseHasMoreItems(responseXml, ns, pageNumber, items.Count, pageSize);

        return new PagedEbayListingsResult
        {
            Items = items,
            HasMore = hasMore
        };
    }

    public async Task<IReadOnlyList<OrderListItemViewModel>> GetRecentOrdersAsync(int daysBack = 90, IReadOnlyDictionary<string, string>? knownPhotos = null, ISet<string>? cancelledOrderIds = null)
    {
        EnsureConfigured();

        if (daysBack < 1)
        {
            daysBack = 90;
        }

        var accessToken = await GetValidAccessTokenAsync();
        var createTimeFromUtc = DateTime.UtcNow.AddDays(-daysBack);

        var pageNumber = 1;
        var allItems = new List<OrderListItemViewModel>();

        while (true)
        {
            var responseXml = await CallGetOrdersAsync(accessToken, createTimeFromUtc, DateTime.UtcNow, pageNumber, 200);
            XNamespace ns = "urn:ebay:apis:eBLBaseComponents";

            var ack = responseXml.Root?.Element(ns + "Ack")?.Value;
            if (!string.Equals(ack, "Success", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(ack, "Warning", StringComparison.OrdinalIgnoreCase))
            {
                var error = responseXml.Descendants(ns + "LongMessage").FirstOrDefault()?.Value
                    ?? "eBay API returned an unknown orders error.";
                throw new InvalidOperationException(error);
            }

            var orderItems = ParseOrderItems(responseXml, ns, cancelledOrderIds);
            allItems.AddRange(orderItems);

            var hasMore = ParseHasMoreOrders(responseXml, ns, pageNumber, orderItems.Count, 200);
            if (!hasMore)
            {
                break;
            }

            pageNumber++;
        }

        // Pre-fill photos from the caller-supplied cache before hitting the API.
        if (knownPhotos is { Count: > 0 })
        {
            foreach (var item in allItems)
            {
                if (string.IsNullOrWhiteSpace(item.ItemPhotoUrl) && !string.IsNullOrWhiteSpace(item.ItemId)
                    && knownPhotos.TryGetValue(item.ItemId, out var cached) && !string.IsNullOrWhiteSpace(cached))
                {
                    item.ItemPhotoUrl = cached;
                }
            }
        }

        await PopulateMissingOrderPhotosAsync(allItems, accessToken);

        return allItems
            .OrderByDescending(x => x.OrderCreatedAtUtc ?? DateTime.MinValue)
            .ThenBy(x => x.ItemId)
            .ToList();
    }

    public async Task<EbayItemDetailsViewModel?> GetItemDetailsAsync(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
        {
            return null;
        }

        try
        {
            var accessToken = await GetValidAccessTokenAsync();
            var itemXml = await CallGetItemAsync(accessToken, itemId);
            return ParseGetItemDetails(itemXml);
        }
        catch
        {
            return null;
        }
    }

    public async Task UpdateItemPriceAsync(string itemId, decimal newPrice)
    {
        var accessToken = await GetValidAccessTokenAsync();

        var client = _httpClientFactory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, BuildApiUrl("/ws/api.dll"));

        request.Headers.Add("X-EBAY-API-CALL-NAME", "ReviseItem");
        request.Headers.Add("X-EBAY-API-SITEID", "0");
        request.Headers.Add("X-EBAY-API-COMPATIBILITY-LEVEL", "1231");
        request.Headers.Add("X-EBAY-API-IAF-TOKEN", accessToken);

        var priceStr = newPrice.ToString("F2", CultureInfo.InvariantCulture);
        var xmlBody =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
            "<ReviseItemRequest xmlns=\"urn:ebay:apis:eBLBaseComponents\">" +
              "<Item>" +
                $"<ItemID>{System.Security.SecurityElement.Escape(itemId)}</ItemID>" +
                $"<StartPrice>{priceStr}</StartPrice>" +
              "</Item>" +
            "</ReviseItemRequest>";

        request.Content = new StringContent(xmlBody, Encoding.UTF8, "text/xml");

        var response = await client.SendAsync(request);
        var xml = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"ReviseItem failed: {xml}");
        }

        var doc = XDocument.Parse(xml);
        XNamespace ns = "urn:ebay:apis:eBLBaseComponents";
        var ack = doc.Root?.Element(ns + "Ack")?.Value;
        if (!string.Equals(ack, "Success", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(ack, "Warning", StringComparison.OrdinalIgnoreCase))
        {
            var errorMsg = doc.Descendants(ns + "LongMessage").FirstOrDefault()?.Value
                ?? doc.Descendants(ns + "ShortMessage").FirstOrDefault()?.Value
                ?? "Unknown eBay error";
            throw new InvalidOperationException($"eBay ReviseItem: {errorMsg}");
        }
    }

    public async Task UpdateItemSkuAsync(string itemId, string sku)
    {
        var accessToken = await GetValidAccessTokenAsync();

        var client = _httpClientFactory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, BuildApiUrl("/ws/api.dll"));

        request.Headers.Add("X-EBAY-API-CALL-NAME", "ReviseItem");
        request.Headers.Add("X-EBAY-API-SITEID", "0");
        request.Headers.Add("X-EBAY-API-COMPATIBILITY-LEVEL", "1231");
        request.Headers.Add("X-EBAY-API-IAF-TOKEN", accessToken);

        var xmlBody =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
            "<ReviseItemRequest xmlns=\"urn:ebay:apis:eBLBaseComponents\">" +
              "<Item>" +
                $"<ItemID>{System.Security.SecurityElement.Escape(itemId)}</ItemID>" +
                $"<SKU>{System.Security.SecurityElement.Escape(sku)}</SKU>" +
              "</Item>" +
            "</ReviseItemRequest>";

        request.Content = new StringContent(xmlBody, Encoding.UTF8, "text/xml");

        var response = await client.SendAsync(request);
        var xml = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"ReviseItem (SKU) failed: {xml}");
        }

        var doc = XDocument.Parse(xml);
        XNamespace ns = "urn:ebay:apis:eBLBaseComponents";
        var ack = doc.Root?.Element(ns + "Ack")?.Value;
        if (!string.Equals(ack, "Success", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(ack, "Warning", StringComparison.OrdinalIgnoreCase))
        {
            var errorMsg = doc.Descendants(ns + "LongMessage").FirstOrDefault()?.Value
                ?? doc.Descendants(ns + "ShortMessage").FirstOrDefault()?.Value
                ?? "Unknown eBay error";
            throw new InvalidOperationException($"eBay ReviseItem (SKU): {errorMsg}");
        }
    }

    public async Task EndItemAsync(string username, string itemId)
    {
        var accessToken = await GetValidAccessTokenAsync(username);

        var client = _httpClientFactory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, BuildApiUrl("/ws/api.dll"));

        request.Headers.Add("X-EBAY-API-CALL-NAME", "EndItem");
        request.Headers.Add("X-EBAY-API-SITEID", "0");
        request.Headers.Add("X-EBAY-API-COMPATIBILITY-LEVEL", "1231");
        request.Headers.Add("X-EBAY-API-IAF-TOKEN", accessToken);

        var xmlBody =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
            "<EndItemRequest xmlns=\"urn:ebay:apis:eBLBaseComponents\">" +
              $"<ItemID>{System.Security.SecurityElement.Escape(itemId)}</ItemID>" +
              "<EndingReason>NotAvailable</EndingReason>" +
            "</EndItemRequest>";

        request.Content = new StringContent(xmlBody, Encoding.UTF8, "text/xml");

        var response = await client.SendAsync(request);
        var xml = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"EndItem failed: {xml}");
        }

        var doc = XDocument.Parse(xml);
        XNamespace ns = "urn:ebay:apis:eBLBaseComponents";
        var ack = doc.Root?.Element(ns + "Ack")?.Value;
        if (!string.Equals(ack, "Success", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(ack, "Warning", StringComparison.OrdinalIgnoreCase))
        {
            var errorMsg = doc.Descendants(ns + "LongMessage").FirstOrDefault()?.Value
                ?? doc.Descendants(ns + "ShortMessage").FirstOrDefault()?.Value
                ?? "Unknown eBay error";
            throw new InvalidOperationException($"eBay EndItem: {errorMsg}");
        }
    }

    public async Task<string> RelistItemAsync(string username, string itemId)
    {
        var accessToken = await GetValidAccessTokenAsync(username);

        var client = _httpClientFactory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, BuildApiUrl("/ws/api.dll"));

        request.Headers.Add("X-EBAY-API-CALL-NAME", "RelistItem");
        request.Headers.Add("X-EBAY-API-SITEID", "0");
        request.Headers.Add("X-EBAY-API-COMPATIBILITY-LEVEL", "1231");
        request.Headers.Add("X-EBAY-API-IAF-TOKEN", accessToken);

        var xmlBody =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
            "<RelistItemRequest xmlns=\"urn:ebay:apis:eBLBaseComponents\">" +
              $"<ItemID>{System.Security.SecurityElement.Escape(itemId)}</ItemID>" +
            "</RelistItemRequest>";

        request.Content = new StringContent(xmlBody, Encoding.UTF8, "text/xml");

        var response = await client.SendAsync(request);
        var xml = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"RelistItem failed: {xml}");
        }

        var doc = XDocument.Parse(xml);
        XNamespace ns = "urn:ebay:apis:eBLBaseComponents";
        var ack = doc.Root?.Element(ns + "Ack")?.Value;
        if (!string.Equals(ack, "Success", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(ack, "Warning", StringComparison.OrdinalIgnoreCase))
        {
            var errorMsg = doc.Descendants(ns + "LongMessage").FirstOrDefault()?.Value
                ?? doc.Descendants(ns + "ShortMessage").FirstOrDefault()?.Value
                ?? "Unknown eBay error";
            throw new InvalidOperationException($"eBay RelistItem: {errorMsg}");
        }

        var newItemId = doc.Root?.Element(ns + "ItemID")?.Value;
        if (string.IsNullOrWhiteSpace(newItemId))
        {
            throw new InvalidOperationException("eBay RelistItem succeeded but returned no new ItemID.");
        }

        return newItemId;
    }

    private static EbayItemDetailsViewModel? ParseGetItemDetails(XDocument itemXml)
    {
        XNamespace ns = "urn:ebay:apis:eBLBaseComponents";

        var ack = itemXml.Root?.Element(ns + "Ack")?.Value;
        if (!string.Equals(ack, "Success", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(ack, "Warning", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var item = itemXml.Descendants(ns + "Item").FirstOrDefault();
        if (item is null)
        {
            return null;
        }

        var sellingStatus = item.Element(ns + "SellingStatus");
        var currentPriceEl = sellingStatus?.Element(ns + "CurrentPrice");
        decimal? currentPrice = null;
        if (currentPriceEl != null && decimal.TryParse(currentPriceEl.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var price))
        {
            currentPrice = price;
        }

        var currencyId = currentPriceEl?.Attribute(ns + "currencyID")?.Value ?? currentPriceEl?.Attribute("currencyID")?.Value;

        var quantityEl = item.Element(ns + "Quantity");
        int? quantity = null;
        if (quantityEl != null && int.TryParse(quantityEl.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var qty))
        {
            quantity = qty;
        }

        var quantitySoldEl = sellingStatus?.Element(ns + "QuantitySold");
        int? quantitySold = null;
        if (quantitySoldEl != null && int.TryParse(quantitySoldEl.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sold))
        {
            quantitySold = sold;
        }

        var viewItemUrl = item.Element(ns + "ListingDetails")?.Element(ns + "ViewItemURL")?.Value
            ?? item.Element(ns + "ViewItemURL")?.Value;

        var conditionDisplayName = item.Element(ns + "ConditionDisplayName")?.Value;
        var conditionDesc = item.Element(ns + "ConditionDescription")?.Value;
        if (string.IsNullOrWhiteSpace(conditionDisplayName) && !string.IsNullOrWhiteSpace(conditionDesc))
        {
            conditionDisplayName = conditionDesc;
        }
        else if (string.IsNullOrWhiteSpace(conditionDisplayName))
        {
            conditionDisplayName = item.Element(ns + "ConditionID")?.Value;
        }

        var listingStatus = sellingStatus?.Element(ns + "ListingStatus")?.Value;

        var listingType = item.Element(ns + "ListingDetails")?.Element(ns + "ListingType")?.Value
            ?? item.Element(ns + "ListingType")?.Value;

        DateTime? endTime = null;
        var endTimeRaw = item.Element(ns + "ListingDetails")?.Element(ns + "EndTime")?.Value
            ?? item.Element(ns + "EndTime")?.Value;
        if (!string.IsNullOrWhiteSpace(endTimeRaw) && DateTime.TryParse(endTimeRaw, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsedEnd))
        {
            endTime = parsedEnd;
        }

        return new EbayItemDetailsViewModel
        {
            CurrentPrice = currentPrice,
            CurrencyId = currencyId,
            Quantity = quantity,
            QuantitySold = quantitySold,
            ViewItemUrl = viewItemUrl,
            ConditionDisplayName = conditionDisplayName,
            ListingStatus = listingStatus,
            ListingType = listingType,
            EndTime = endTime
        };
    }

    private static bool ParseHasMoreItems(XDocument responseXml, XNamespace ns, int pageNumber, int itemCount, int pageSize)
    {
        var candidates = new[]
        {
            responseXml.Descendants(ns + "ActiveList").Elements(ns + "HasMoreItems").FirstOrDefault()?.Value,
            responseXml.Descendants(ns + "PaginationResult").Elements(ns + "HasMoreItems").FirstOrDefault()?.Value,
            responseXml.Descendants(ns + "HasMoreItems").FirstOrDefault()?.Value
        };

        foreach (var candidate in candidates)
        {
            if (bool.TryParse(candidate, out var parsed))
            {
                return parsed;
            }
        }

        var totalPagesRaw = responseXml.Descendants(ns + "PaginationResult").Elements(ns + "TotalNumberOfPages").FirstOrDefault()?.Value;
        if (int.TryParse(totalPagesRaw, out var totalPages) && totalPages > 0)
        {
            return pageNumber < totalPages;
        }

        var totalEntriesRaw = responseXml.Descendants(ns + "PaginationResult").Elements(ns + "TotalNumberOfEntries").FirstOrDefault()?.Value;
        if (int.TryParse(totalEntriesRaw, out var totalEntries) && totalEntries > 0)
        {
            return pageNumber * pageSize < totalEntries;
        }

        return itemCount >= pageSize;
    }

    private static bool ParseHasMoreOrders(XDocument responseXml, XNamespace ns, int pageNumber, int itemCount, int pageSize)
    {
        var candidates = new[]
        {
            responseXml.Descendants(ns + "HasMoreOrders").FirstOrDefault()?.Value,
            responseXml.Descendants(ns + "PaginationResult").Elements(ns + "HasMoreItems").FirstOrDefault()?.Value
        };

        foreach (var candidate in candidates)
        {
            if (bool.TryParse(candidate, out var parsed))
            {
                return parsed;
            }
        }

        var totalPagesRaw = responseXml.Descendants(ns + "PaginationResult").Elements(ns + "TotalNumberOfPages").FirstOrDefault()?.Value;
        if (int.TryParse(totalPagesRaw, out var totalPages) && totalPages > 0)
        {
            return pageNumber < totalPages;
        }

        var totalEntriesRaw = responseXml.Descendants(ns + "PaginationResult").Elements(ns + "TotalNumberOfEntries").FirstOrDefault()?.Value;
        if (int.TryParse(totalEntriesRaw, out var totalEntries) && totalEntries > 0)
        {
            return pageNumber * pageSize < totalEntries;
        }

        return itemCount >= pageSize;
    }

    private static List<OrderListItemViewModel> ParseOrderItems(XDocument responseXml, XNamespace ns, ISet<string>? cancelledOrderIds = null)
    {
        var result = new List<OrderListItemViewModel>();

        var orders = responseXml
            .Descendants(ns + "OrderArray")
            .Descendants(ns + "Order");

        foreach (var order in orders)
        {
            if (IsCancelledOrder(order, ns))
            {
                var cancelledId = order.Element(ns + "OrderID")?.Value ?? order.Element(ns + "ExtendedOrderID")?.Value;
                if (!string.IsNullOrEmpty(cancelledId))
                {
                    cancelledOrderIds?.Add(cancelledId);
                }
                continue;
            }

            var orderId =
                order.Element(ns + "OrderID")?.Value
                ?? order.Element(ns + "ExtendedOrderID")?.Value
                ?? string.Empty;

            var buyerName =
                order.Element(ns + "BuyerUserID")?.Value
                ?? order.Element(ns + "Buyer")?.Element(ns + "UserID")?.Value
                ?? "(Unknown buyer)";

            var createdAtRaw = order.Element(ns + "CreatedTime")?.Value;
            DateTime? createdAtUtc = null;
            if (DateTime.TryParse(createdAtRaw, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsedCreatedAt))
            {
                createdAtUtc = parsedCreatedAt;
            }

            var shipping = order.Element(ns + "ShippingAddress");
            var buyerStreet1 = shipping?.Element(ns + "Street1")?.Value;
            var buyerStreet2 = shipping?.Element(ns + "Street2")?.Value;
            var buyerCity = shipping?.Element(ns + "CityName")?.Value;
            var buyerState = shipping?.Element(ns + "StateOrProvince")?.Value;
            var buyerZip = shipping?.Element(ns + "PostalCode")?.Value;
            var buyerCountry = shipping?.Element(ns + "CountryName")?.Value
                ?? shipping?.Element(ns + "Country")?.Value;
            var buyerPhone = shipping?.Element(ns + "Phone")?.Value;

            // Only check top-level ShippingDetails (not inside TransactionArray) so that
            // transaction-level tracking for one item doesn't mark all other items as shipped.
            var orderHasTracking = order.Element(ns + "ShippingDetails")
                                       ?.Descendants(ns + "ShipmentTrackingDetails").Any() ?? false;
            var transactions = order
                .Descendants(ns + "TransactionArray")
                .Descendants(ns + "Transaction")
                .ToList();

            foreach (var transaction in transactions)
            {
                var item = transaction.Element(ns + "Item");
                var itemId =
                    item?.Element(ns + "ItemID")?.Value
                    ?? transaction.Element(ns + "ItemID")?.Value
                    ?? string.Empty;

                if (string.IsNullOrWhiteSpace(itemId))
                {
                    continue;
                }

                var itemName =
                    item?.Element(ns + "Title")?.Value
                    ?? transaction.Element(ns + "Title")?.Value
                    ?? "(Untitled item)";

                var itemPhotoUrl =
                    item?.Element(ns + "PictureDetails")?.Element(ns + "GalleryURL")?.Value
                    ?? item?.Element(ns + "GalleryURL")?.Value
                    ?? transaction.Element(ns + "PictureDetails")?.Element(ns + "GalleryURL")?.Value
                    ?? transaction.Element(ns + "GalleryURL")?.Value;

                var transactionHasTracking = transaction.Descendants(ns + "ShipmentTrackingDetails").Any();
                var orderLineItemId = transaction.Element(ns + "OrderLineItemID")?.Value;

                var quantityRaw = transaction.Element(ns + "QuantityPurchased")?.Value;
                var quantity = int.TryParse(quantityRaw, out var parsedQty) && parsedQty > 0 ? parsedQty : 1;

                // Transaction financials — already present in GetOrders at DetailLevel=ReturnAll,
                // no extra API call needed. Feeds RecentOrder -> SaleRecord via
                // SaleResolutionService. eBay's Final Value Fee is NOT here under managed
                // payments (see EbayService.GetOrderFeeAsync for that, via the Finance REST API).
                var transactionPriceEl = transaction.Element(ns + "TransactionPrice");
                var transactionPriceGross = ParseDecimal(transactionPriceEl?.Value);
                var currencyId = transactionPriceEl?.Attribute("currencyID")?.Value;

                var shippingCostEl = transaction
                    .Element(ns + "ShippingDetails")
                    ?.Element(ns + "ShippingServiceOptions")
                    ?.Element(ns + "ShippingServiceCost");
                var shippingCollected = ParseDecimal(shippingCostEl?.Value);

                var taxAmountEl = transaction.Element(ns + "Taxes")?.Element(ns + "TotalTaxAmount");
                var taxCollected = ParseDecimal(taxAmountEl?.Value);

                result.Add(new OrderListItemViewModel
                {
                    OrderId = !string.IsNullOrWhiteSpace(orderId) ? orderId : (orderLineItemId ?? string.Empty),
                    ItemId = itemId,
                    ItemName = itemName,
                    ItemPhotoUrl = itemPhotoUrl,
                    BuyerName = buyerName,
                    HasTracking = orderHasTracking || transactionHasTracking,
                    OrderCreatedAtUtc = createdAtUtc,
                    Quantity = quantity,
                    BuyerStreet1 = buyerStreet1,
                    BuyerStreet2 = buyerStreet2,
                    BuyerCity = buyerCity,
                    BuyerState = buyerState,
                    BuyerZip = buyerZip,
                    BuyerCountry = buyerCountry,
                    BuyerPhone = buyerPhone,
                    TransactionPriceGross = transactionPriceGross,
                    ShippingCollected = shippingCollected,
                    TaxCollected = taxCollected,
                    CurrencyId = currencyId
                });
            }
        }

        return result;
    }

    private async Task PopulateMissingOrderPhotosAsync(List<OrderListItemViewModel> orderItems, string accessToken)
    {
        var itemIdsMissingPhoto = orderItems
            .Where(x => string.IsNullOrWhiteSpace(x.ItemPhotoUrl) && !string.IsNullOrWhiteSpace(x.ItemId))
            .Select(x => x.ItemId)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (itemIdsMissingPhoto.Count == 0)
        {
            return;
        }

        var photoByItemId = new System.Collections.Concurrent.ConcurrentDictionary<string, string?>(StringComparer.Ordinal);
        var semaphore = new SemaphoreSlim(5);

        await Task.WhenAll(itemIdsMissingPhoto.Select(async itemId =>
        {
            await semaphore.WaitAsync();
            try
            {
                var itemXml = await CallGetItemAsync(accessToken, itemId);
                photoByItemId[itemId] = ParseItemPhotoUrl(itemXml);
            }
            catch
            {
                photoByItemId[itemId] = null;
            }
            finally
            {
                semaphore.Release();
            }
        }));

        foreach (var orderItem in orderItems)
        {
            if (!string.IsNullOrWhiteSpace(orderItem.ItemPhotoUrl) || string.IsNullOrWhiteSpace(orderItem.ItemId))
            {
                continue;
            }

            if (photoByItemId.TryGetValue(orderItem.ItemId, out var photoUrl) && !string.IsNullOrWhiteSpace(photoUrl))
            {
                orderItem.ItemPhotoUrl = photoUrl;
            }
        }
    }

    private static string? ParseItemPhotoUrl(XDocument itemXml)
    {
        XNamespace ns = "urn:ebay:apis:eBLBaseComponents";

        var ack = itemXml.Root?.Element(ns + "Ack")?.Value;
        if (!string.Equals(ack, "Success", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(ack, "Warning", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var item = itemXml.Descendants(ns + "Item").FirstOrDefault();
        if (item is null)
        {
            return null;
        }

        return
            item.Element(ns + "GalleryURL")?.Value
            ?? item.Element(ns + "PictureDetails")?.Element(ns + "GalleryURL")?.Value
            ?? item.Element(ns + "PictureDetails")?.Elements(ns + "PictureURL").Select(x => x.Value).FirstOrDefault(url => !string.IsNullOrWhiteSpace(url))
            ?? item.Element(ns + "PictureDetails")?.Elements(ns + "ExternalPictureURL").Select(x => x.Value).FirstOrDefault(url => !string.IsNullOrWhiteSpace(url));
    }

    private static decimal? ParseDecimal(string? value)
    {
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static bool IsCancelledOrder(XElement order, XNamespace ns)
    {
        static bool ContainsCancel(string? value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && value.Contains("cancel", StringComparison.OrdinalIgnoreCase);
        }

        var orderStatus = order.Element(ns + "OrderStatus")?.Value;
        if (ContainsCancel(orderStatus))
        {
            return true;
        }

        var cancelStatus = order.Element(ns + "CancelStatus")?.Value;
        if (ContainsCancel(cancelStatus))
        {
            return true;
        }

        var checkoutStatus = order.Element(ns + "CheckoutStatus")?.Element(ns + "Status")?.Value;
        if (ContainsCancel(checkoutStatus))
        {
            return true;
        }

        var transactionStatuses = order
            .Descendants(ns + "Transaction")
            .Select(t => t.Element(ns + "Status")?.Value);
        return transactionStatuses.Any(ContainsCancel);
    }

    public async Task<bool> UploadTrackingAsync(string username, string orderId, string trackingNumber, string carrierCode)
    {
        try
        {
            var accessToken = await GetValidAccessTokenAsync();
            var apiBase = IsSandbox() ? "https://api.sandbox.ebay.com" : "https://api.ebay.com";
            var url = $"{apiBase}/sell/fulfillment/v1/order/{Uri.EscapeDataString(orderId)}/shipping_fulfillment";

            var ebayCarrier = MapCarrierCode(carrierCode);
            var body = new
            {
                lineItems = Array.Empty<object>(),
                trackingNumber = trackingNumber,
                shippingCarrierCode = ebayCarrier,
                shippedDate = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
            };

            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

            var json = System.Text.Json.JsonSerializer.Serialize(body);
            var response = await client.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"));

            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                Console.Error.WriteLine($"[eBay] UploadTracking 403 — missing sell.fulfillment scope. Body: {errorBody}");
                return false;
            }

            if (response.StatusCode == System.Net.HttpStatusCode.Created || response.IsSuccessStatusCode)
            {
                return true;
            }

            var responseBody = await response.Content.ReadAsStringAsync();
            Console.Error.WriteLine($"[eBay] UploadTracking failed ({response.StatusCode}): {responseBody}");
            return false;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[eBay] UploadTracking exception: {ex.Message}");
            return false;
        }
    }

    public async Task<HashSet<string>> GetFulfilledOrderIdsAsync()
    {
        try
        {
            var accessToken = await GetValidAccessTokenAsync();
            var apiBase = IsSandbox() ? "https://api.sandbox.ebay.com" : "https://api.ebay.com";
            // filter uses literal braces per eBay docs; encoded here for safety
            var url = $"{apiBase}/sell/fulfillment/v1/order?filter=orderfulfillmentstatus%3A%7BFULFILLED%7D&limit=200";

            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            client.DefaultRequestHeaders.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

            var response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = System.Text.Json.JsonDocument.Parse(json);

            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (doc.RootElement.TryGetProperty("orders", out var orders))
            {
                foreach (var order in orders.EnumerateArray())
                {
                    // Add new-format orderId (e.g. "05-12345-12345")
                    if (order.TryGetProperty("orderId", out var idEl))
                    {
                        var id = idEl.GetString();
                        if (!string.IsNullOrWhiteSpace(id))
                            ids.Add(id);
                    }
                    // Also add legacy numeric orderId — the Trading API stores this format in the DB,
                    // so we need both to match regardless of which format was persisted.
                    if (order.TryGetProperty("legacyOrderId", out var legacyEl))
                    {
                        var legacyId = legacyEl.GetString();
                        if (!string.IsNullOrWhiteSpace(legacyId))
                            ids.Add(legacyId);
                    }
                }
            }
            return ids;
        }
        catch
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    // Trading API's GetOrders (used for everything else in this file) has no Final Value Fee
    // or actual-shipping-cost field under managed payments — those only exist via this Finance
    // REST endpoint, which needs the sell.finances scope (not in Ebay:Scopes by default; see
    // Models/EbayOptions.cs
    // manual reconnect step this requires). Confirmed directly against production 2026-08-20:
    // (1) this endpoint is hosted on apiz.ebay.com, not api.ebay.com like every other Sell API
    // in this file — the wrong host 404s even with a valid token; (2) its documented `orderId`
    // filter doesn't actually match real orders (returns 204 empty every time it was tried), so
    // this fetches every transaction over a date range instead and matches orderId client-side;
    // (3) the fee isn't a separate per-fee ledger row — each SALE transaction carries one
    // aggregate `totalFeeAmount` field directly; (4) the actual shipping cost (as opposed to
    // ShippingCollected, what the buyer paid) shows up as its own SHIPPING_LABEL transaction
    // with a plain `amount`, on whichever order the label was bought against — summed per order
    // in case more than one label was bought (e.g. a reshipment).
    public async Task<Dictionary<string, OrderFinanceData>> GetOrderFinanceDataAsync(IEnumerable<string> orderIds)
    {
        var wanted = new HashSet<string>(orderIds, StringComparer.Ordinal);
        var result = new Dictionary<string, OrderFinanceData>(StringComparer.Ordinal);
        if (wanted.Count == 0)
        {
            return result;
        }

        try
        {
            var accessToken = await GetValidAccessTokenAsync();
            var apiBase = IsSandbox() ? "https://apiz.sandbox.ebay.com" : "https://apiz.ebay.com";

            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            client.DefaultRequestHeaders.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

            var end = DateTime.UtcNow;
            var start = end.AddDays(-90);
            // No transactionType filter here — a combined-value filter syntax isn't proven
            // against this endpoint (only single-value filters are used elsewhere in this
            // file), and a plain date-range fetch is already confirmed (2026-08-20) to return
            // every transaction type, including both SALE and SHIPPING_LABEL, unfiltered.
            var filter = $"transactionDate:[{start:yyyy-MM-ddTHH:mm:ss.fffZ}..{end:yyyy-MM-ddTHH:mm:ss.fffZ}]";
            var url = $"{apiBase}/sell/finances/v1/transaction?filter={Uri.EscapeDataString(filter)}&limit=200";

            // Bounded pagination: caps total pages so an unbounded "next" chain (or a bug in
            // it) can't loop forever. Unlike the old fee-only lookup, this can't stop early on
            // "found everything" — a wanted order's fee and shipping-label rows can be on
            // different pages (different transaction dates), so every page must be scanned.
            for (var page = 0; page < 10 && !string.IsNullOrEmpty(url); page++)
            {
                var response = await client.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                    {
                        Console.Error.WriteLine("[eBay] GetOrderFinanceData 403 — missing sell.finances scope. Reconnect eBay after adding it to Ebay:Scopes.");
                    }
                    break;
                }

                var json = await response.Content.ReadAsStringAsync();
                using var doc = System.Text.Json.JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("transactions", out var transactions))
                {
                    foreach (var txn in transactions.EnumerateArray())
                    {
                        if (!txn.TryGetProperty("orderId", out var orderIdEl))
                        {
                            continue;
                        }
                        var oid = orderIdEl.GetString();
                        if (oid is null || !wanted.Contains(oid))
                        {
                            continue;
                        }
                        if (!txn.TryGetProperty("transactionType", out var typeEl))
                        {
                            continue;
                        }
                        var type = typeEl.GetString();

                        if (type == "SALE"
                            && txn.TryGetProperty("totalFeeAmount", out var feeEl)
                            && feeEl.TryGetProperty("value", out var feeValueEl)
                            && decimal.TryParse(feeValueEl.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var feeValue))
                        {
                            var data = result.TryGetValue(oid, out var existingFee) ? existingFee : new OrderFinanceData();
                            data.Fee = feeValue;
                            result[oid] = data;
                        }
                        else if (type == "SHIPPING_LABEL"
                            && txn.TryGetProperty("amount", out var amountEl)
                            && amountEl.TryGetProperty("value", out var amountValueEl)
                            && decimal.TryParse(amountValueEl.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var labelValue))
                        {
                            var data = result.TryGetValue(oid, out var existingLabel) ? existingLabel : new OrderFinanceData();
                            data.ShippingLabelCost = (data.ShippingLabelCost ?? 0) + Math.Abs(labelValue);
                            result[oid] = data;
                        }
                    }
                }

                url = doc.RootElement.TryGetProperty("next", out var nextEl) ? nextEl.GetString() : null;
            }
        }
        catch
        {
        }

        return result;
    }

    private static string MapCarrierCode(string easyPostCarrier)
    {
        return easyPostCarrier.ToUpperInvariant() switch
        {
            "USPS" => "USPS",
            "UPS" => "UPS",
            "FEDEX" => "FedEx",
            "DHL" => "DHL",
            _ => easyPostCarrier
        };
    }

    public async Task<List<EbayDraftViewModel>> GetDraftListingsAsync()
    {
        var accessToken = await GetValidAccessTokenAsync();
        var apiBase = IsSandbox() ? "https://api.sandbox.ebay.com" : "https://api.ebay.com";
        var url = $"{apiBase}/sell/listing/v1_beta/item_draft?marketplace_id=EBAY_US&limit=200";

        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await client.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // eBay returns 404 when there are no drafts instead of an empty list
            return new List<EbayDraftViewModel>();
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"GetDraftListings failed ({response.StatusCode}): {body}");
        }

        var doc = JsonDocument.Parse(body);
        var drafts = new List<EbayDraftViewModel>();

        if (!doc.RootElement.TryGetProperty("itemDrafts", out var itemDraftsEl))
        {
            return drafts;
        }

        foreach (var item in itemDraftsEl.EnumerateArray())
        {
            drafts.Add(ParseDraftItem(item));
        }

        return drafts;
    }

    public async Task<EbayDraftViewModel?> GetDraftByIdAsync(string draftId)
    {
        var accessToken = await GetValidAccessTokenAsync();
        var apiBase = IsSandbox() ? "https://api.sandbox.ebay.com" : "https://api.ebay.com";
        var url = $"{apiBase}/sell/listing/v1_beta/item_draft/{Uri.EscapeDataString(draftId)}";

        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await client.GetAsync(url);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var body = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(body);
        return ParseDraftItem(doc.RootElement);
    }

    public async Task UpdateDraftAsync(string draftId, DraftUpdateInput input)
    {
        var accessToken = await GetValidAccessTokenAsync();
        var apiBase = IsSandbox() ? "https://api.sandbox.ebay.com" : "https://api.ebay.com";
        var url = $"{apiBase}/sell/listing/v1_beta/item_draft/{Uri.EscapeDataString(draftId)}";

        var patchBody = BuildDraftUpdateBody(input);
        var json = JsonSerializer.Serialize(patchBody, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });

        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var request = new HttpRequestMessage(new HttpMethod("PATCH"), url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        var response = await client.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"UpdateDraft failed ({response.StatusCode}): {body}");
        }
    }

    public async Task<string> PublishDraftAsync(string draftId)
    {
        var accessToken = await GetValidAccessTokenAsync();
        var apiBase = IsSandbox() ? "https://api.sandbox.ebay.com" : "https://api.ebay.com";
        var url = $"{apiBase}/sell/listing/v1_beta/item_draft/{Uri.EscapeDataString(draftId)}/publish";

        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await client.PostAsync(url, new StringContent("{}", Encoding.UTF8, "application/json"));
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"PublishDraft failed ({response.StatusCode}): {body}");
        }

        // Response contains the published listingId
        var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("listingId", out var listingIdEl))
        {
            return listingIdEl.GetString() ?? draftId;
        }

        return draftId;
    }

    private static EbayDraftViewModel ParseDraftItem(JsonElement item)
    {
        var vm = new EbayDraftViewModel();

        if (item.TryGetProperty("listingId", out var lid))
            vm.DraftId = lid.GetString() ?? string.Empty;

        if (item.TryGetProperty("product", out var product))
        {
            if (product.TryGetProperty("title", out var title))
                vm.Title = title.GetString();

            if (product.TryGetProperty("description", out var desc))
                vm.Description = desc.GetString();

            if (product.TryGetProperty("imageUrls", out var images) && images.ValueKind == JsonValueKind.Array)
            {
                foreach (var img in images.EnumerateArray())
                {
                    var imgUrl = img.GetString();
                    if (!string.IsNullOrWhiteSpace(imgUrl))
                    {
                        vm.CoverPhotoUrl = imgUrl;
                        break;
                    }
                }
            }

            if (product.TryGetProperty("categoryId", out var catId))
                vm.CategoryId = catId.GetString();
        }

        if (item.TryGetProperty("condition", out var cond))
            vm.ConditionId = cond.GetString();

        if (item.TryGetProperty("conditionDescription", out var condDesc))
            vm.ConditionDescription = condDesc.GetString();

        if (item.TryGetProperty("pricingSummary", out var pricing))
        {
            if (pricing.TryGetProperty("price", out var priceEl))
            {
                if (priceEl.TryGetProperty("value", out var priceVal))
                {
                    var priceStr = priceVal.ValueKind == JsonValueKind.String
                        ? priceVal.GetString()
                        : priceVal.GetRawText();
                    if (decimal.TryParse(priceStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var price))
                        vm.Price = price;
                }
                if (priceEl.TryGetProperty("currency", out var curr))
                    vm.Currency = curr.GetString();
            }
        }

        if (item.TryGetProperty("packageWeightAndSize", out var pkg))
        {
            if (pkg.TryGetProperty("weight", out var weight))
            {
                if (weight.TryGetProperty("value", out var wVal))
                {
                    var wStr = wVal.ValueKind == JsonValueKind.String ? wVal.GetString() : wVal.GetRawText();
                    if (decimal.TryParse(wStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var wPounds))
                        vm.WeightPounds = wPounds;
                }
            }
            if (pkg.TryGetProperty("dimensions", out var dims))
            {
                if (dims.TryGetProperty("length", out var l) && decimal.TryParse(l.GetRawText(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var lv))
                    vm.LengthIn = lv;
                if (dims.TryGetProperty("width", out var w) && decimal.TryParse(w.GetRawText(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var wv))
                    vm.WidthIn = wv;
                if (dims.TryGetProperty("height", out var h) && decimal.TryParse(h.GetRawText(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var hv))
                    vm.HeightIn = hv;
            }
        }

        return vm;
    }

    private static object BuildDraftUpdateBody(DraftUpdateInput input)
    {
        var body = new Dictionary<string, object?>();

        if (!string.IsNullOrWhiteSpace(input.Description))
        {
            body["product"] = new Dictionary<string, object?>
            {
                ["description"] = input.Description
            };
        }

        if (!string.IsNullOrWhiteSpace(input.ConditionId))
            body["condition"] = input.ConditionId;

        if (!string.IsNullOrWhiteSpace(input.ConditionDescription))
            body["conditionDescription"] = input.ConditionDescription;

        if (input.Price.HasValue)
        {
            body["pricingSummary"] = new Dictionary<string, object?>
            {
                ["price"] = new Dictionary<string, object?>
                {
                    ["currency"] = input.Currency ?? "USD",
                    ["value"] = input.Price.Value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                }
            };
        }

        var hasDimensions = input.LengthIn.HasValue || input.WidthIn.HasValue || input.HeightIn.HasValue;
        if (input.WeightPounds.HasValue || hasDimensions)
        {
            var pkg = new Dictionary<string, object?>();

            if (input.WeightPounds.HasValue)
            {
                pkg["weight"] = new Dictionary<string, object?>
                {
                    ["value"] = input.WeightPounds.Value,
                    ["unit"] = "POUND"
                };
            }

            if (hasDimensions)
            {
                pkg["dimensions"] = new Dictionary<string, object?>
                {
                    ["length"] = input.LengthIn ?? 0m,
                    ["width"] = input.WidthIn ?? 0m,
                    ["height"] = input.HeightIn ?? 0m,
                    ["unit"] = "INCH"
                };
            }

            body["packageWeightAndSize"] = pkg;
        }

        return body;
    }

    public void Disconnect()
    {
        var username = GetCurrentUsernameOrThrow();
        var existing = _dbContext.UserEbayConnections.FirstOrDefault(x => x.Username == username);
        if (existing is null)
        {
            return;
        }

        _dbContext.UserEbayConnections.Remove(existing);
        _dbContext.SaveChanges();
    }

    private async Task<XDocument> CallGetMyeBaySellingAsync(string accessToken, int pageNumber, int entriesPerPage)
    {
        var client = _httpClientFactory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, BuildApiUrl("/ws/api.dll"));

        request.Headers.Add("X-EBAY-API-CALL-NAME", "GetMyeBaySelling");
        request.Headers.Add("X-EBAY-API-SITEID", "0");
        request.Headers.Add("X-EBAY-API-COMPATIBILITY-LEVEL", "1231");
        request.Headers.Add("X-EBAY-API-IAF-TOKEN", accessToken);

        var xmlBody =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
            "<GetMyeBaySellingRequest xmlns=\"urn:ebay:apis:eBLBaseComponents\">" +
              "<DetailLevel>ReturnAll</DetailLevel>" +
              "<ActiveList>" +
                "<Include>true</Include>" +
                "<Pagination>" +
                  $"<EntriesPerPage>{entriesPerPage}</EntriesPerPage>" +
                  $"<PageNumber>{pageNumber}</PageNumber>" +
                "</Pagination>" +
              "</ActiveList>" +
            "</GetMyeBaySellingRequest>";

        request.Content = new StringContent(xmlBody, Encoding.UTF8, "text/xml");

        var response = await client.SendAsync(request);
        var xml = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"GetMyeBaySelling failed: {xml}");
        }

        return XDocument.Parse(xml);
    }

    private async Task<XDocument> CallGetOrdersAsync(
        string accessToken,
        DateTime createTimeFromUtc,
        DateTime createTimeToUtc,
        int pageNumber,
        int entriesPerPage)
    {
        var client = _httpClientFactory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, BuildApiUrl("/ws/api.dll"));

        request.Headers.Add("X-EBAY-API-CALL-NAME", "GetOrders");
        request.Headers.Add("X-EBAY-API-SITEID", "0");
        request.Headers.Add("X-EBAY-API-COMPATIBILITY-LEVEL", "1231");
        request.Headers.Add("X-EBAY-API-IAF-TOKEN", accessToken);

        var xmlBody =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
            "<GetOrdersRequest xmlns=\"urn:ebay:apis:eBLBaseComponents\">" +
              "<DetailLevel>ReturnAll</DetailLevel>" +
              "<OrderRole>Seller</OrderRole>" +
              "<OrderStatus>All</OrderStatus>" +
              $"<CreateTimeFrom>{createTimeFromUtc:yyyy-MM-ddTHH:mm:ss.fffZ}</CreateTimeFrom>" +
              $"<CreateTimeTo>{createTimeToUtc:yyyy-MM-ddTHH:mm:ss.fffZ}</CreateTimeTo>" +
              "<Pagination>" +
                $"<EntriesPerPage>{entriesPerPage}</EntriesPerPage>" +
                $"<PageNumber>{pageNumber}</PageNumber>" +
              "</Pagination>" +
            "</GetOrdersRequest>";

        request.Content = new StringContent(xmlBody, Encoding.UTF8, "text/xml");

        var response = await client.SendAsync(request);
        var xml = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"GetOrders failed: {xml}");
        }

        return XDocument.Parse(xml);
    }

    private async Task<XDocument> CallGetItemAsync(string accessToken, string itemId)
    {
        var client = _httpClientFactory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, BuildApiUrl("/ws/api.dll"));

        request.Headers.Add("X-EBAY-API-CALL-NAME", "GetItem");
        request.Headers.Add("X-EBAY-API-SITEID", "0");
        request.Headers.Add("X-EBAY-API-COMPATIBILITY-LEVEL", "1231");
        request.Headers.Add("X-EBAY-API-IAF-TOKEN", accessToken);

        var xmlBody =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
            "<GetItemRequest xmlns=\"urn:ebay:apis:eBLBaseComponents\">" +
              "<DetailLevel>ReturnAll</DetailLevel>" +
              $"<ItemID>{System.Security.SecurityElement.Escape(itemId)}</ItemID>" +
            "</GetItemRequest>";

        request.Content = new StringContent(xmlBody, Encoding.UTF8, "text/xml");

        var response = await client.SendAsync(request);
        var xml = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"GetItem failed: {xml}");
        }

        return XDocument.Parse(xml);
    }

    public async Task<List<EbayPromotionViewModel>> GetPromotionsAsync()
    {
        try
        {
            var accessToken = await GetValidAccessTokenAsync();
            var apiBase = IsSandbox() ? "https://api.sandbox.ebay.com" : "https://api.ebay.com";
            var url = $"{apiBase}/sell/marketing/v1/promotion?marketplace_id=EBAY_US&promotion_status=ACTIVE&promotion_status=SCHEDULED&limit=200";

            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", accessToken);
            client.DefaultRequestHeaders.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

            var response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                Console.Error.WriteLine($"[eBay] GetPromotions failed ({response.StatusCode}): {errorBody}");
                return [];
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            var results = new List<EbayPromotionViewModel>();
            if (doc.RootElement.TryGetProperty("promotions", out var promotions))
            {
                foreach (var p in promotions.EnumerateArray())
                {
                    results.Add(new EbayPromotionViewModel
                    {
                        PromotionId = p.TryGetProperty("promotionId", out var id) ? id.GetString() ?? string.Empty : string.Empty,
                        Name = p.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty,
                        Description = p.TryGetProperty("description", out var desc) ? desc.GetString() ?? string.Empty : string.Empty,
                        PromotionType = p.TryGetProperty("promotionType", out var type) ? type.GetString() ?? string.Empty : string.Empty,
                        Status = p.TryGetProperty("status", out var status) ? status.GetString() ?? string.Empty : string.Empty,
                        StartDate = p.TryGetProperty("startDate", out var start) ? start.GetString() : null,
                        EndDate = p.TryGetProperty("endDate", out var end) ? end.GetString() : null,
                        PromotionHref = p.TryGetProperty("promotionHref", out var href) ? href.GetString() : null,
                    });
                }
            }

            return results;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[eBay] GetPromotions exception: {ex.Message}");
            return [];
        }
    }

    private Task<string> GetValidAccessTokenAsync() => GetValidAccessTokenAsync(GetCurrentUsernameOrThrow());

    private async Task<string> GetValidAccessTokenAsync(string username)
    {
        var tokens = GetStoredTokens(username) ?? throw new InvalidOperationException($"No eBay connection for user '{username}'.");

        if (tokens.ExpiresAtUtc > DateTime.UtcNow.AddMinutes(1))
        {
            return tokens.AccessToken;
        }

        if (string.IsNullOrWhiteSpace(tokens.RefreshToken))
        {
            throw new InvalidOperationException("No refresh token available. Please reconnect your eBay account.");
        }

        var client = _httpClientFactory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, BuildIdentityUrl("/identity/v1/oauth2/token"));

        var basicAuth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basicAuth);

        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = tokens.RefreshToken,
            ["scope"] = _options.Scopes
        });

        var response = await client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Refresh token failed: {content}");
        }

        var refreshed = JsonSerializer.Deserialize<TokenResponse>(content)
            ?? throw new InvalidOperationException("Unable to parse refresh token response.");

        // eBay may omit refresh_token on refresh responses, keep existing one if missing.
        if (string.IsNullOrWhiteSpace(refreshed.RefreshToken))
        {
            refreshed.RefreshToken = tokens.RefreshToken;
        }

        SaveTokens(refreshed, username);
        return refreshed.AccessToken;
    }

    private void SaveTokens(TokenResponse tokenResponse)
    {
        var username = GetCurrentUsernameOrThrow();
        SaveTokens(tokenResponse, username);
    }

    private void SaveTokens(TokenResponse tokenResponse, string username)
    {
        var stored = new StoredTokens
        {
            AccessToken = tokenResponse.AccessToken,
            RefreshToken = tokenResponse.RefreshToken,
            ExpiresAtUtc = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn)
        };

        var existing = _dbContext.UserEbayConnections.FirstOrDefault(x => x.Username == username);
        if (existing is null)
        {
            existing = new UserEbayConnection
            {
                Username = username
            };
            _dbContext.UserEbayConnections.Add(existing);
        }

        existing.AccessToken = stored.AccessToken;
        existing.RefreshToken = stored.RefreshToken;
        existing.ExpiresAtUtc = stored.ExpiresAtUtc;
        existing.UpdatedAtUtc = DateTime.UtcNow;

        _dbContext.SaveChanges();
    }

    private StoredTokens? GetStoredTokens(string username)
    {
        var existing = _dbContext.UserEbayConnections
            .AsNoTracking()
            .FirstOrDefault(x => x.Username == username);

        if (existing is null)
        {
            return null;
        }

        return new StoredTokens
        {
            AccessToken = existing.AccessToken,
            RefreshToken = existing.RefreshToken,
            ExpiresAtUtc = existing.ExpiresAtUtc
        };
    }

    private string GetCurrentUsernameOrThrow()
    {
        var context = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No active HTTP context.");

        if (context.User.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(context.User.Identity.Name))
        {
            throw new InvalidOperationException("You must be logged in to use eBay features.");
        }

        return context.User.Identity.Name;
    }

    private string? GetCurrentUsernameOrNull()
    {
        var context = _httpContextAccessor.HttpContext;
        if (context?.User.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        return context.User.Identity?.Name;
    }

    private string BuildIdentityUrl(string path)
    {
        var baseUrl = IsSandbox() ? "https://api.sandbox.ebay.com" : "https://api.ebay.com";
        return baseUrl + path;
    }

    private string BuildApiUrl(string path)
    {
        var baseUrl = IsSandbox() ? "https://api.sandbox.ebay.com" : "https://api.ebay.com";
        return baseUrl + path;
    }

    private bool IsSandbox()
    {
        return string.Equals(_options.Environment, "Sandbox", StringComparison.OrdinalIgnoreCase);
    }

    private void EnsureConfigured()
    {
        if (!IsConfigured())
        {
            throw new InvalidOperationException("Missing eBay settings. Fill Ebay:ClientId, Ebay:ClientSecret, and Ebay:RuName in configuration.");
        }
    }

    private class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("refresh_token")]
        public string RefreshToken { get; set; } = string.Empty;
    }

    private class StoredTokens
    {
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public DateTime ExpiresAtUtc { get; set; }
    }
}
