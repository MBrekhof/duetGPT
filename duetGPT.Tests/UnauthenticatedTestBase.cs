using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace duetGPT.Tests;

/// <summary>
/// Base class for tests that should run WITHOUT authentication
/// </summary>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
public class UnauthenticatedTestBase : PageTest
{
    /// <summary>
    /// Base URL for the application
    /// </summary>
    protected string BaseUrl => "https://localhost:44391";

    /// <summary>
    /// Override to provide browser context options WITHOUT authentication state
    /// </summary>
    public override BrowserNewContextOptions ContextOptions()
    {
        var options = base.ContextOptions();

        // Ignore HTTPS errors for localhost testing
        options.IgnoreHTTPSErrors = true;

        // Explicitly do NOT load storage state - test without authentication
        options.StorageStatePath = null;

        return options;
    }

    /// <summary>
    /// Setup method runs before each test
    /// </summary>
    [SetUp]
    public async Task Setup()
    {
        // Set default timeout
        Page.SetDefaultTimeout(30000); // 30 seconds

        // Navigate to home page before each test
        await Page.GotoAsync(BaseUrl);
    }

    /// <summary>
    /// Helper method to wait for navigation and loading
    /// </summary>
    protected async Task WaitForPageLoad()
    {
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }
}
