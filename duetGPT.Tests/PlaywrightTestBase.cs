using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace duetGPT.Tests;

/// <summary>
/// Base class for all Playwright tests providing common setup
/// </summary>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
public class PlaywrightTestBase : PageTest
{
    /// <summary>
    /// Base URL for the application (adjust if using different port)
    /// Uses HTTP in CI/Testing environment, HTTPS in Development
    /// </summary>
    protected string BaseUrl => Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Testing"
        ? "http://localhost:5000"
        : "https://localhost:44391";

    /// <summary>
    /// Override to provide browser context options including authentication state
    /// </summary>
    public override BrowserNewContextOptions ContextOptions()
    {
        var options = base.ContextOptions();

        // Ignore HTTPS errors for localhost testing
        options.IgnoreHTTPSErrors = true;

        // Load the authentication state if it exists
        if (AuthenticationSetup.HasStorageState())
        {
            options.StorageStatePath = AuthenticationSetup.GetStorageStatePath();
        }

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
