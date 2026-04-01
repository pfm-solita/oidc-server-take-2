using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text.Json.Serialization;

namespace WebApp.Pages;

public class IndexModel(IHttpClientFactory httpClientFactory) : PageModel
{
    public string CurrentTime { get; private set; } = string.Empty;

    public async Task OnGetAsync()
    {
        var client = httpClientFactory.CreateClient("webapi");
        try
        {
            var result = await client.GetFromJsonAsync<TimeResponse>("/api/time");
            CurrentTime = result?.Time ?? "unavailable";
        }
        catch (Exception ex)
        {
            CurrentTime = $"unavailable ({ex.GetType().Name})";
        }
    }

    private record TimeResponse([property: JsonPropertyName("time")] string Time);
}
