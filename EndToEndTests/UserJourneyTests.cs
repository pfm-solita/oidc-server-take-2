using Microsoft.Playwright;

namespace EndToEndTests;

/// <summary>
/// End-to-end browser tests for the complete OIDC authorization-code + PKCE user journey:
/// <list type="bullet">
///   <item>Home page is accessible without authentication.</item>
///   <item>Accessing a protected page redirects to the IDP login form.</item>
///   <item>Logging in with valid credentials completes the flow and shows user info.</item>
///   <item>The authenticated home page shows the correct navigation links.</item>
///   <item>Logging out clears the session and returns to the unauthenticated home page.</item>
/// </list>
/// Each test gets its own browser context (isolated cookies / session storage).
/// </summary>
public class UserJourneyTests(E2EFixture fixture)
    : IClassFixture<E2EFixture>, IAsyncLifetime
{
    private IBrowserContext _context = null!;
    private IPage _page = null!;

    // ── xunit per-test setup / teardown ──────────────────────────────────────

    public async Task InitializeAsync()
    {
        _context = await fixture.Browser.NewContextAsync();
        _page = await _context.NewPageAsync();
    }

    public async Task DisposeAsync() => await _context.CloseAsync();

    // ── helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Navigates to the UserInfo page (protected), fills in the IDP login form with
    /// <paramref name="username"/> (password == username as per the IDP's rule), and waits
    /// until the browser lands back on the WebApp.
    /// </summary>
    private async Task LoginAsync(string username)
    {
        await _page.GotoAsync($"{fixture.WebAppUrl}/UserInfo");

        // Should be on the IDP login page now.
        await _page.WaitForSelectorAsync("#username");
        await _page.FillAsync("#username", username);
        await _page.FillAsync("#password", username);
        await _page.ClickAsync("button[type=submit]");

        // Wait until the browser is back on the WebApp host.
        await _page.WaitForURLAsync(url => url.StartsWith(fixture.WebAppUrl));
    }

    // ── tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HomePage_WhenUnauthenticated_ShowsLoginLink()
    {
        await _page.GotoAsync(fixture.WebAppUrl);

        Assert.Equal("Home", await _page.TitleAsync());
        await Assertions.Expect(_page.GetByRole(AriaRole.Heading, new() { Name = "Home" })).ToBeVisibleAsync();
        await Assertions.Expect(_page.GetByText("Login and View User Info")).ToBeVisibleAsync();

        // Authenticated-only links must not be present.
        await Assertions.Expect(_page.GetByText("Logout")).ToBeHiddenAsync();
    }

    [Fact]
    public async Task NavigatingToProtectedPage_WhenUnauthenticated_RedirectsToIdpLogin()
    {
        await _page.GotoAsync($"{fixture.WebAppUrl}/UserInfo");

        // The browser should land on the IDP's login page.
        await Assertions.Expect(_page.GetByRole(AriaRole.Heading, new() { Name = "Login" })).ToBeVisibleAsync();
        await Assertions.Expect(_page.Locator("#username")).ToBeVisibleAsync();
        await Assertions.Expect(_page.Locator("#password")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Login_WithValidCredentials_CompletesOidcFlowAndShowsUserInfo()
    {
        await LoginAsync("alice");

        // Should be on the UserInfo page with the user's claims.
        await Assertions.Expect(_page.GetByRole(AriaRole.Heading, new() { Name = "User Info" })).ToBeVisibleAsync();

        var preContent = await _page.Locator("pre").InnerTextAsync();
        Assert.Contains("alice", preContent);
    }

    [Fact]
    public async Task AuthenticatedHomePage_ShowsUserInfoAndLogoutLinks()
    {
        await LoginAsync("bob");

        await _page.GotoAsync(fixture.WebAppUrl);

        await Assertions.Expect(_page.GetByText("View User Info")).ToBeVisibleAsync();
        await Assertions.Expect(_page.GetByText("Logout")).ToBeVisibleAsync();

        // The unauthenticated login prompt must not appear.
        await Assertions.Expect(_page.GetByText("Login and View User Info")).ToBeHiddenAsync();
    }

    [Fact]
    public async Task Logout_ClearsSession_ReturnsToUnauthenticatedHomePage()
    {
        await LoginAsync("carol");

        // Trigger logout via the logout page.
        await _page.GotoAsync($"{fixture.WebAppUrl}/Account/Logout");

        // Should land back on the home page and be logged out.
        await _page.WaitForURLAsync(url => url.StartsWith(fixture.WebAppUrl));
        await Assertions.Expect(_page.GetByRole(AriaRole.Heading, new() { Name = "Home" })).ToBeVisibleAsync();
        await Assertions.Expect(_page.GetByText("Login and View User Info")).ToBeVisibleAsync();
        await Assertions.Expect(_page.GetByText("Logout")).ToBeHiddenAsync();
    }
}
