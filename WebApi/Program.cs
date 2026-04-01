using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication.JwtBearer;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = "https://localhost:5000";
        options.Audience = "WebApi";
    });

builder.Services.AddAuthorization();

builder.Services.AddHttpClient("oidc", client =>
{
    client.BaseAddress = new Uri("https://localhost:5000");
});

var app = builder.Build();

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/time", () => Results.Ok(new { time = DateTime.UtcNow.ToString("o") }));

app.MapGet("/api/userinfo", async (HttpContext httpContext, IHttpClientFactory httpClientFactory) =>
{
    var authHeader = httpContext.Request.Headers.Authorization.ToString();
    if (!authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        return Results.Unauthorized();

    var token = authHeader["Bearer ".Length..].Trim();

    var client = httpClientFactory.CreateClient("oidc");
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    var response = await client.GetAsync("/connect/userinfo");
    var content = await response.Content.ReadAsStringAsync();

    return Results.Content(content, "application/json");
}).RequireAuthorization();

app.Run();
