using EndToEndTests.Helpers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.Playwright;
using Oidc.Idp;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using WebApi;
using WebApp;

namespace EndToEndTests;

// ── Per-app Kestrel factories ─────────────────────────────────────────────────

/// <summary>
/// Real Kestrel host for the OIDC IDP, wired to an isolated in-memory database and with
/// transport-security requirements disabled so tests can use plain HTTP.
/// </summary>
internal sealed class IdpE2EFactory : KestrelWebApplicationFactory<IdpMarker>
{
    private readonly string _dbName = $"oidc-e2e-{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureTestServices(services =>
        {
            // Swap in an isolated in-memory database (same pattern as IdpWebApplicationFactory).
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>));
            if (descriptor is not null)
                services.Remove(descriptor);

            services.AddDbContext<ApplicationDbContext>(options =>
            {
                options.UseInMemoryDatabase(_dbName);
                options.UseOpenIddict();
            });

            // Allow plain HTTP in tests – Kestrel here runs on http://.
            services.PostConfigure<OpenIddictServerAspNetCoreOptions>(options =>
            {
                options.DisableTransportSecurityRequirement = true;
            });
        });
    }
}

/// <summary>
/// Real Kestrel host for the WebApi.  Overrides JWT-bearer validation so that token
/// verification is performed against the test IDP rather than the hardcoded production URL.
/// </summary>
internal sealed class WebApiE2EFactory(string idpUrl) : KestrelWebApplicationFactory<WebApiMarker>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureTestServices(services =>
        {
            // Runs after JwtBearerPostConfigureOptions, so the ConfigurationManager and
            // Backchannel we create here supersede those created by the framework.
            services.PostConfigure<JwtBearerOptions>(
                JwtBearerDefaults.AuthenticationScheme,
                options =>
                {
                    options.Authority = idpUrl;
                    options.RequireHttpsMetadata = false;

                    var backchannel = new HttpClient(new HttpClientHandler())
                    {
                        Timeout = options.BackchannelTimeout
                    };
                    options.Backchannel = backchannel;
                    options.ConfigurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                        $"{idpUrl}/.well-known/openid-configuration",
                        new OpenIdConnectConfigurationRetriever(),
                        new HttpDocumentRetriever(backchannel) { RequireHttps = false });
                });

            // Override the "oidc" named HttpClient so the /api/userinfo proxy also goes to
            // the test IDP rather than https://localhost:5000.
            services.AddHttpClient("oidc")
                .ConfigureHttpClient(c => c.BaseAddress = new Uri(idpUrl));
        });
    }
}

/// <summary>
/// Real Kestrel host for the WebApp.  Overrides OIDC authority and WebApi HttpClient so
/// that the browser-facing app is fully wired to the other test servers.
/// </summary>
internal sealed class WebAppE2EFactory(string idpUrl, string webApiUrl)
    : KestrelWebApplicationFactory<WebAppMarker>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureTestServices(services =>
        {
            // Runs after OpenIdConnectPostConfigureOptions, so our ConfigurationManager
            // and Backchannel supersede those created by the framework.
            services.PostConfigure<OpenIdConnectOptions>(
                OpenIdConnectDefaults.AuthenticationScheme,
                options =>
            {
                options.Authority = idpUrl;
                options.MetadataAddress = $"{idpUrl}/.well-known/openid-configuration";
                options.RequireHttpsMetadata = false;

                var backchannel = new HttpClient(new HttpClientHandler())
                {
                    Timeout = options.BackchannelTimeout
                };
                options.Backchannel = backchannel;
                options.ConfigurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                    options.MetadataAddress,
                    new OpenIdConnectConfigurationRetriever(),
                    new HttpDocumentRetriever(backchannel) { RequireHttps = false });
            });

            // Override the "webapi" named HttpClient to point to the test WebApi server.
            services.AddHttpClient("webapi")
                .ConfigureHttpClient(c => c.BaseAddress = new Uri(webApiUrl));
        });
    }
}

// ── xunit fixture ─────────────────────────────────────────────────────────────

/// <summary>
/// xunit class fixture shared by all E2E test classes.  Starts the three real Kestrel
/// servers (IDP, WebApi, WebApp) and a headless Chromium browser for Playwright tests.
/// </summary>
public sealed class E2EFixture : IAsyncLifetime
{
    private IdpE2EFactory? _idpFactory;
    private WebApiE2EFactory? _webApiFactory;
    private WebAppE2EFactory? _webAppFactory;
    private IPlaywright? _playwright;

    /// <summary>Playwright browser instance shared across test contexts.</summary>
    public IBrowser Browser { get; private set; } = null!;

    /// <summary>Base URL of the running WebApp Kestrel server.</summary>
    public string WebAppUrl { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        // 1. Start the IDP on a random port.
        _idpFactory = new IdpE2EFactory();
        _idpFactory.CreateClient();
        var idpUrl = _idpFactory.ServerAddress;

        // 2. Start the WebApi, configured to validate JWTs against the test IDP.
        _webApiFactory = new WebApiE2EFactory(idpUrl);
        _webApiFactory.CreateClient();
        var webApiUrl = _webApiFactory.ServerAddress;

        // 3. Start the WebApp, configured to use the test IDP and WebApi.
        _webAppFactory = new WebAppE2EFactory(idpUrl, webApiUrl);
        _webAppFactory.CreateClient();
        WebAppUrl = _webAppFactory.ServerAddress;

        // 4. Update the IDP's registered public-client with the actual WebApp redirect URI.
        //    The ClientSeeder ran with the hardcoded placeholder URI; we fix it now that we
        //    know the real Kestrel port.
        await using (var scope = _idpFactory.Services.CreateAsyncScope())
        {
            var appManager = scope.ServiceProvider
                .GetRequiredService<IOpenIddictApplicationManager>();

            var app = await appManager.FindByClientIdAsync("public-client");
            if (app is not null)
            {
                var descriptor = new OpenIddictApplicationDescriptor();
                await appManager.PopulateAsync(descriptor, app);

                descriptor.RedirectUris.Clear();
                descriptor.RedirectUris.Add(new Uri($"{WebAppUrl}/signin-oidc"));

                descriptor.PostLogoutRedirectUris.Clear();
                descriptor.PostLogoutRedirectUris.Add(new Uri($"{WebAppUrl}/signout-callback-oidc"));

                await appManager.UpdateAsync(app, descriptor);
            }
        }

        // 5. Launch a headless Chromium browser for the tests.
        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync(
            new BrowserTypeLaunchOptions { Headless = true });
    }

    public async Task DisposeAsync()
    {
        if (Browser is not null)
            await Browser.CloseAsync();

        _playwright?.Dispose();
        _webAppFactory?.Dispose();
        _webApiFactory?.Dispose();
        _idpFactory?.Dispose();
    }
}
