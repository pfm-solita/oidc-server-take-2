using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Net.Http.Headers;

namespace WebApp.Pages;

[Authorize]
public class UserInfoModel(IHttpClientFactory httpClientFactory) : PageModel
{
    public string UserInfoJson { get; private set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync()
    {
        var token = await HttpContext.GetTokenAsync("access_token");
        if (string.IsNullOrEmpty(token))
            return Challenge();

        var client = httpClientFactory.CreateClient("webapi");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/userinfo");
        if (!response.IsSuccessStatusCode)
        {
            UserInfoJson = $"Error: received {(int)response.StatusCode} from WebApi.";
            return Page();
        }

        UserInfoJson = await response.Content.ReadAsStringAsync();
        return Page();
    }
}
