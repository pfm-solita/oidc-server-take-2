using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Oidc.Idp;
using OpenIddict.Server.AspNetCore;

namespace IntegrationTests;

/// <summary>
/// In-process test server for the Oidc.Idp project.
/// Each factory instance gets its own isolated in-memory database so that
/// multiple instances running in the same test process do not collide when
/// the ClientSeeder tries to seed the same client IDs.
/// </summary>
public class IdpWebApplicationFactory : WebApplicationFactory<IdpMarker>
{
    // A unique name per factory instance keeps EF Core's in-memory databases isolated.
    private readonly string _databaseName = $"oidc-test-{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureTestServices(services =>
        {
            // Remove the existing DbContext registration (which uses the shared "oidc" name)
            // and replace it with one backed by a per-instance in-memory database.
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>));
            if (descriptor is not null)
                services.Remove(descriptor);

            services.AddDbContext<ApplicationDbContext>(options =>
            {
                options.UseInMemoryDatabase(_databaseName);
                options.UseOpenIddict();
            });

            // Allow HTTP in the test environment – the WebApplicationFactory test server
            // does not use HTTPS, so OpenIddict's transport-security requirement must be relaxed.
            services.PostConfigure<OpenIddictServerAspNetCoreOptions>(options =>
            {
                options.DisableTransportSecurityRequirement = true;
            });
        });
    }
}
