using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Oidc.Idp.Controllers;

public class AuthorizationController(IOpenIddictScopeManager scopeManager) : Controller
{
    [HttpGet("~/connect/authorize")]
    [HttpPost("~/connect/authorize")]
    public async Task<IActionResult> Authorize()
    {
        var request = HttpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OpenIddict server request cannot be retrieved.");

        var result = await HttpContext.AuthenticateAsync("Cookie");
        if (!result.Succeeded)
        {
            return Challenge(
                authenticationSchemes: "Cookie",
                properties: new AuthenticationProperties
                {
                    RedirectUri = Request.PathBase + Request.Path + QueryString.Create(
                        Request.HasFormContentType
                            ? [.. Request.Form]
                            : [.. Request.Query])
                });
        }

        var username = result.Principal.Identity!.Name!;
        var subject = ComputeSubject(username);

        var identity = new ClaimsIdentity(
            authenticationType: "OpenIddict",
            nameType: Claims.Name,
            roleType: Claims.Role);

        identity.AddClaim(new Claim(Claims.Subject, subject).SetDestinations(Destinations.AccessToken, Destinations.IdentityToken));
        identity.AddClaim(new Claim(Claims.Name, username).SetDestinations(Destinations.AccessToken, Destinations.IdentityToken));
        identity.AddClaim(new Claim(Claims.Email, username).SetDestinations(Destinations.AccessToken, Destinations.IdentityToken));

        var principal = new ClaimsPrincipal(identity);

        var scopes = request.GetScopes();
        principal.SetScopes(scopes);
        principal.SetResources(await scopeManager.ListResourcesAsync(scopes).ToListAsync());

        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    [HttpPost("~/connect/token")]
    public async Task<IActionResult> Token()
    {
        var request = HttpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OpenIddict server request cannot be retrieved.");

        if (request.IsClientCredentialsGrantType())
        {
            var identity = new ClaimsIdentity(
                authenticationType: "OpenIddict",
                nameType: Claims.Name,
                roleType: Claims.Role);

            identity.AddClaim(new Claim(Claims.Subject, request.ClientId!).SetDestinations(Destinations.AccessToken));

            var principal = new ClaimsPrincipal(identity);
            principal.SetScopes(request.GetScopes());
            principal.SetResources(await scopeManager.ListResourcesAsync(request.GetScopes()).ToListAsync());

            return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        if (request.IsAuthorizationCodeGrantType())
        {
            var result = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            return SignIn(result.Principal!, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        throw new InvalidOperationException("The specified grant type is not supported.");
    }

    [HttpGet("~/connect/userinfo")]
    [HttpPost("~/connect/userinfo")]
    public async Task<IActionResult> Userinfo()
    {
        var claimsPrincipal = (await HttpContext.AuthenticateAsync(
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)).Principal!;

        var claims = new Dictionary<string, object>(StringComparer.Ordinal);

        var subject = claimsPrincipal.GetClaim(Claims.Subject);
        if (!string.IsNullOrEmpty(subject))
            claims[Claims.Subject] = subject;

        var name = claimsPrincipal.GetClaim(Claims.Name);
        if (!string.IsNullOrEmpty(name))
            claims[Claims.Name] = name;

        var email = claimsPrincipal.GetClaim(Claims.Email);
        if (!string.IsNullOrEmpty(email))
            claims[Claims.Email] = email;

        return Ok(claims);
    }

    [HttpGet("~/connect/logout")]
    [HttpPost("~/connect/logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync("Cookie");
        return SignOut(
            authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
            properties: new AuthenticationProperties
            {
                RedirectUri = "/"
            });
    }

    private static string ComputeSubject(string username)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(username));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
