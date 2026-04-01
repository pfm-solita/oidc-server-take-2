using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace IntegrationTests;

/// <summary>
/// Integration tests for the Oidc.Idp token and discovery endpoints.
/// </summary>
public class IdpTokenTests(IdpWebApplicationFactory factory)
    : IClassFixture<IdpWebApplicationFactory>
{
    // ── Discovery ────────────────────────────────────────────────────────────

    [Fact]
    public async Task DiscoveryDocument_ReturnsExpectedEndpoints()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/.well-known/openid-configuration");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        Assert.Contains("/connect/token", root.GetProperty("token_endpoint").GetString());
        Assert.Contains("/connect/authorize", root.GetProperty("authorization_endpoint").GetString());
        Assert.Contains("/connect/userinfo", root.GetProperty("userinfo_endpoint").GetString());
    }

    // ── Client credentials ───────────────────────────────────────────────────

    [Fact]
    public async Task Token_ClientCredentials_ValidCredentials_ReturnsAccessToken()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsync("/connect/token", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = "m2m-client",
                ["client_secret"] = "m2m-secret",
                ["scope"] = "api",
            }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.True(body.TryGetProperty("access_token", out var tokenProp));
        Assert.False(string.IsNullOrEmpty(tokenProp.GetString()));
        Assert.Equal("Bearer", body.GetProperty("token_type").GetString(), ignoreCase: true);
    }

    [Fact]
    public async Task Token_ClientCredentials_WrongSecret_ReturnsUnauthorized()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsync("/connect/token", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = "m2m-client",
                ["client_secret"] = "wrong-secret",
                ["scope"] = "api",
            }));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Token_ClientCredentials_UnknownClient_ReturnsUnauthorized()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsync("/connect/token", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = "nonexistent-client",
                ["client_secret"] = "irrelevant",
                ["scope"] = "api",
            }));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
