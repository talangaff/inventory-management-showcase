using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using inventory_management.Models;

namespace inventory_management.Services;

/// <summary>
/// Login is owned by an upstream app. Nginx runs every request past that
/// app's /auth/check first (auth_request) and only forwards it here, with
/// X-Forwarded-User set, once that check passes. This handler turns that header into the
/// signed-in user.
///
/// The header is only trusted from loopback — Kestrel listens on 127.0.0.1 and nginx
/// overwrites any client-supplied value — and the identity is always the configured
/// Auth:Username, because every table is keyed by it (per-user data isolation, see the README).
/// </summary>
public class ForwardedAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "ForwardedAuth";
    public const string HeaderName = "X-Forwarded-User";

    private readonly AuthOptions _authOptions;

    public ForwardedAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IOptions<AuthOptions> authOptions)
        : base(options, logger, encoder)
    {
        _authOptions = authOptions.Value;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var remote = Context.Connection.RemoteIpAddress;
        if (remote == null || !IPAddress.IsLoopback(remote))
            return Task.FromResult(AuthenticateResult.NoResult());

        if (string.IsNullOrWhiteSpace(Request.Headers[HeaderName]))
            return Task.FromResult(AuthenticateResult.NoResult());

        var identity = new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.Name, _authOptions.Username) }, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    // Normally unreachable (nginx already sent the browser to log in), but if a
    // request does get here unauthenticated, hand it to the upstream login.
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var returnUrl = Request.PathBase + Request.Path + Request.QueryString;
        Response.Redirect("/login?ReturnUrl=" + Uri.EscapeDataString(returnUrl));
        return Task.CompletedTask;
    }
}
