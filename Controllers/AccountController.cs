using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace inventory_management.Controllers;

/// <summary>
/// Login, logout and password changes now live on the reverse proxy (see
/// Services/ForwardedAuthHandler.cs). These actions only keep old bookmarks working.
/// </summary>
public class AccountController : Controller
{
    [HttpGet]
    [AllowAnonymous]
    public IActionResult Login() => Redirect("/login");

    [HttpGet]
    [AllowAnonymous]
    public IActionResult ChangePassword() => Redirect("/account/password");

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Logout() => Redirect("/");
}
