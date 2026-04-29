using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;

namespace Oidc.Idp.Controllers;

public class AccountController : Controller
{
    [HttpGet("~/account/login")]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }

    [HttpPost("~/account/login")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(string username, string password, string? returnUrl = null)
    {
        if (string.IsNullOrEmpty(username) || username != password)
        {
            ModelState.AddModelError(string.Empty, "Invalid credentials. Username and password must be equal.");
            ViewData["ReturnUrl"] = returnUrl;
            return View();
        }

        var identity = new ClaimsIdentity("Cookie");
        identity.AddClaim(new Claim(ClaimTypes.Name, username));

        await HttpContext.SignInAsync("Cookie", new ClaimsPrincipal(identity));

        return Redirect(returnUrl ?? "/");
    }

    [HttpPost("~/account/logout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync("Cookie");
        return Redirect("/");
    }
}
