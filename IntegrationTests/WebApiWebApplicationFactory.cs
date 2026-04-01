using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using WebApi;

namespace IntegrationTests;

/// <summary>
/// In-process test server for the WebApi project, wired to an <see cref="IdpWebApplicationFactory"/>
/// so that JWT validation and the "oidc" named HttpClient both route through the test IDP.
/// </summary>
public class WebApiWebApplicationFactory(IdpWebApplicationFactory idpFactory)
    : WebApplicationFactory<WebApiMarker>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureTestServices(services =>
        {
            // Override JWT bearer so that all backchannel traffic (discovery document, JWKS)
            // is routed through the in-process test IDP.
            //
            // NOTE: JwtBearerPostConfigureOptions (built-in, registered before ConfigureTestServices)
            // already creates options.Backchannel and options.ConfigurationManager with the
            // original "https://localhost:5000" authority by the time our PostConfigure runs.
            // We must therefore explicitly replace both objects here rather than just setting
            // BackchannelHttpHandler and Authority, which would be too late.
            services.PostConfigure<JwtBearerOptions>(
                JwtBearerDefaults.AuthenticationScheme,
                options =>
                {
                    var handler = idpFactory.Server.CreateHandler();
                    var authority = idpFactory.Server.BaseAddress.ToString().TrimEnd('/');

                    options.Authority = authority;
                    options.RequireHttpsMetadata = false;

                    // Replace the Backchannel so HTTP requests go through the test server.
                    options.Backchannel = new HttpClient(handler) { Timeout = options.BackchannelTimeout };

                    // Replace the ConfigurationManager with one that uses the test-IDP address
                    // and the new Backchannel.  This supersedes the one created by
                    // JwtBearerPostConfigureOptions (which still points to the original authority).
                    options.ConfigurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                        $"{authority}/.well-known/openid-configuration",
                        new OpenIdConnectConfigurationRetriever(),
                        new HttpDocumentRetriever(options.Backchannel) { RequireHttps = false });
                });

            // Override the "oidc" named HttpClient so that the /api/userinfo proxy
            // also reaches the test IDP instead of https://localhost:5000.
            services.AddHttpClient("oidc")
                .ConfigureHttpClient(c => c.BaseAddress = idpFactory.Server.BaseAddress)
                .ConfigurePrimaryHttpMessageHandler(() => idpFactory.Server.CreateHandler());
        });
    }
}
