using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using inventory_management.Services;

namespace inventory_management.Controllers;

public class PromotionsController : Controller
{
    private readonly IEbayService _ebayService;

    public PromotionsController(IEbayService ebayService)
    {
        _ebayService = ebayService;
    }

    [Authorize]
    public async Task<IActionResult> Index()
    {
        if (!_ebayService.IsConfigured() || !_ebayService.IsConnected())
        {
            return View(new List<inventory_management.Models.EbayPromotionViewModel>());
        }

        var promotions = await _ebayService.GetPromotionsAsync();
        return View(promotions);
    }
}
