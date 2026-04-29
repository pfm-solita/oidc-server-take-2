using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace IntegrationTests;

/// <summary>
/// Shared fixture that owns both the test IDP and the test WebApi, ensuring
/// the WebApi is wired to validate tokens against the in-process IDP.
/// </summary>
public sealed class WebApiWithIdpFixture : IDisposable
{
    public IdpWebApplicationFactory IdpFactory { get; } = new();
    public WebApiWebApplicationFactory WebApiFactory { get; }

    public WebApiWithIdpFixture()
    {
        WebApiFactory = new WebApiWebApplicationFactory(IdpFactory);
    }

    public void Dispose()
    {
        WebApiFactory.Dispose();
        IdpFactory.Dispose();
    }
}

/// <summary>
/// Integration tests for the WebApi endpoints, including an end-to-end M2M
/// flow that obtains a real JWT from the test IDP and uses it against the API.
/// </summary>
public class WebApiTests(WebApiWithIdpFixture fixture) : IClassFixture<WebApiWithIdpFixture>
{
    // ── /api/time ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetTime_Anonymous_Returns200WithTimeField()
    {
        var client = fixture.WebApiFactory.CreateClient();

        var response = await client.GetAsync("/api/time");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.True(body.TryGetProperty("time", out var timeProp));
        Assert.False(string.IsNullOrEmpty(timeProp.GetString()));
    }

    // ── /api/userinfo ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetUserInfo_WithoutToken_Returns401()
    {
        var client = fixture.WebApiFactory.CreateClient();

        var response = await client.GetAsync("/api/userinfo");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetUserInfo_WithInvalidToken_Returns401()
    {
        var client = fixture.WebApiFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "not.a.valid.jwt");

        var response = await client.GetAsync("/api/userinfo");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// Full end-to-end M2M flow:
    ///   1. Obtain an access token from the test IDP (client credentials).
    ///   2. Call /api/userinfo on the WebApi using that token.
    ///   3. The WebApi validates the JWT (via the test IDP's JWKS) and proxies
    ///      the request to the IDP's /connect/userinfo endpoint.
    /// </summary>
    [Fact]
    public async Task GetUserInfo_WithM2MToken_Returns200WithSubjectClaim()
    {
        // 1. Get token from test IDP
        var idpClient = fixture.IdpFactory.CreateClient();
        var tokenResponse = await idpClient.PostAsync("/connect/token", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = "m2m-client",
                ["client_secret"] = "m2m-secret",
                ["scope"] = "api",
            }));

        Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);

        var token = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync())
            .RootElement.GetProperty("access_token").GetString();
        Assert.False(string.IsNullOrEmpty(token));

        // 2. Call WebApi /api/userinfo with the token
        var webApiClient = fixture.WebApiFactory.CreateClient();
        webApiClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var response = await webApiClient.GetAsync("/api/userinfo");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // 3. The userinfo response must contain the subject claim
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.True(body.TryGetProperty("sub", out var subProp));
        Assert.Equal("m2m-client", subProp.GetString());
    }
}
