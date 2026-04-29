using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace EndToEndTests.Helpers;

/// <summary>
/// A <see cref="WebApplicationFactory{TEntryPoint}"/> variant that additionally starts a real
/// Kestrel HTTP server on a random loopback port so that a browser (e.g. Playwright) can
/// connect to the app via a real TCP socket.
///
/// <para>
/// The base-class <see cref="WebApplicationFactory{TEntryPoint}"/> creates a
/// <c>TestServer</c> (in-process, no TCP) and sets <see cref="ServerAddress"/> to its
/// loopback URL.  This subclass also starts Kestrel on port 0 and exposes the resulting
/// address via <see cref="ServerAddress"/> so callers can navigate to it with a browser.
/// </para>
/// </summary>
public class KestrelWebApplicationFactory<TEntry>(int port = 0)
    : WebApplicationFactory<TEntry>
    where TEntry : class
{
    private string? _serverAddress;

    /// <summary>
    /// The base address of the real Kestrel server (e.g. <c>http://127.0.0.1:PORT</c>).
    /// Available after the factory has been initialised (first call to
    /// <see cref="WebApplicationFactory{TEntryPoint}.CreateClient()"/>).
    /// </summary>
    public string ServerAddress =>
        _serverAddress ?? throw new InvalidOperationException(
            "Server not started yet. Call CreateClient() first.");

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // Build the TestServer host (used internally by WebApplicationFactory for CreateClient).
        var testHost = builder.Build();

        // Build a second host backed by real Kestrel on a random (or fixed) loopback port.
        builder.ConfigureWebHost(webHostBuilder =>
            webHostBuilder.UseKestrel(options =>
                options.Listen(System.Net.IPAddress.Loopback, port)));

        var kestrelHost = builder.Build();
        kestrelHost.Start();

        _serverAddress = kestrelHost.Services
            .GetRequiredService<IServer>()
            .Features
            .Get<IServerAddressesFeature>()!
            .Addresses
            .First();

        // Wrap both hosts so that both are stopped/disposed together.
        // WebApplicationFactory calls IHost.StartAsync on whatever is returned here;
        // we start only testHost at that point (kestrelHost is already running).
        return new CompositeHost(testHost, kestrelHost);
    }
}

/// <summary>
/// Wraps two <see cref="IHost"/> instances so that the factory lifecycle manages them both.
/// <see cref="Services"/> is delegated to <paramref name="testHost"/> so that the base
/// <see cref="WebApplicationFactory{T}"/> can still locate the <c>TestServer</c> for
/// <c>CreateClient()</c>.  The Kestrel host is stopped alongside the test host.
/// </summary>
internal sealed class CompositeHost(IHost testHost, IHost kestrelHost) : IHost
{
    // Expose the TestServer's service provider so WebApplicationFactory internals
    // (e.g. Server property, CreateClient) keep working unchanged.
    public IServiceProvider Services => testHost.Services;

    public void Dispose()
    {
        testHost.Dispose();
        kestrelHost.Dispose();
    }

    // kestrelHost was already started in CreateHost; only start testHost here.
    public Task StartAsync(CancellationToken cancellationToken = default) =>
        testHost.StartAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken = default) =>
        Task.WhenAll(
            testHost.StopAsync(cancellationToken),
            kestrelHost.StopAsync(cancellationToken));
}
