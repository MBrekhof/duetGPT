using Microsoft.Playwright;
using NUnit.Framework;
using System.Text.RegularExpressions;

namespace duetGPT.Tests;

/// <summary>
/// One-time setup that runs before all tests to authenticate and save the session
/// </summary>
[SetUpFixture]
public class AuthenticationSetup
{
    private static string StorageStatePath => Path.Combine(Path.GetTempPath(), "playwright-auth-state.json");
    public static string BaseUrl => "https://localhost:44391";

    [OneTimeSetUp]
    public async Task GlobalSetup()
    {
        Console.WriteLine("=== Running Global Authentication Setup ===");
        TestContext.Progress.WriteLine("=== Running Global Authentication Setup ===");

        IPlaywright? playwright = null;
        IBrowser? browser = null;

        try
        {
            // Create Playwright instance
            playwright = await Playwright.CreateAsync();
            Console.WriteLine("Playwright instance created");
            TestContext.Progress.WriteLine("Playwright instance created");

            browser = await playwright.Chromium.LaunchAsync(new()
            {
                Headless = true
            });
            Console.WriteLine("Browser launched");
            TestContext.Progress.WriteLine("Browser launched");

            var context = await browser.NewContextAsync(new()
            {
                IgnoreHTTPSErrors = true // Ignore SSL certificate errors for localhost
            });

            var page = await context.NewPageAsync();

            // Navigate to login page
            Console.WriteLine($"Navigating to {BaseUrl}/Account/Login");
            TestContext.Progress.WriteLine($"Navigating to {BaseUrl}/Account/Login");
            await page.GotoAsync($"{BaseUrl}/Account/Login", new() { WaitUntil = WaitUntilState.NetworkIdle });
            Console.WriteLine($"Current URL: {page.Url}");
            TestContext.Progress.WriteLine($"Current URL: {page.Url}");

            // Fill in login credentials
            var emailInput = page.Locator("input[name*='Email']").First;
            var passwordInput = page.Locator("input[name*='Password']").First;

            await emailInput.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5000 });
            await emailInput.FillAsync("martin@brekhof.nl");
            Console.WriteLine("Email filled");
            TestContext.Progress.WriteLine("Email filled");

            await passwordInput.FillAsync("1Zaqwsx2!");
            Console.WriteLine("Password filled");
            TestContext.Progress.WriteLine("Password filled");

            // Click login button
            var loginButton = page.GetByRole(AriaRole.Button, new() { Name = "Login" });
            await loginButton.ClickAsync();
            Console.WriteLine("Login button clicked");
            TestContext.Progress.WriteLine("Login button clicked");

            // Wait for redirect to home/chat page
            await page.WaitForURLAsync(new Regex(".*/($|chat-v2)"), new() { Timeout = 15000 });
            Console.WriteLine($"Redirected to: {page.Url}");
            TestContext.Progress.WriteLine($"Redirected to: {page.Url}");

            // Wait a bit for the page to fully load
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            // Save the authenticated state
            await context.StorageStateAsync(new()
            {
                Path = StorageStatePath
            });

            Console.WriteLine($"=== Authentication successful! State saved to: {StorageStatePath} ===");
            TestContext.Progress.WriteLine($"=== Authentication successful! State saved to: {StorageStatePath} ===");
        }
        catch (Exception ex)
        {
            var errorMsg = $"=== Authentication setup failed: {ex.Message}\nStack: {ex.StackTrace} ===";
            Console.WriteLine(errorMsg);
            TestContext.Progress.WriteLine(errorMsg);
            throw;
        }
        finally
        {
            if (browser != null)
            {
                await browser.CloseAsync();
                Console.WriteLine("Browser closed");
            }
            playwright?.Dispose();
        }
    }

    [OneTimeTearDown]
    public void GlobalTeardown()
    {
        // Clean up the storage state file
        if (File.Exists(StorageStatePath))
        {
            File.Delete(StorageStatePath);
            Console.WriteLine("=== Cleaned up authentication state file ===");
        }
    }

    /// <summary>
    /// Gets the path to the saved storage state file
    /// </summary>
    public static string GetStorageStatePath()
    {
        if (!File.Exists(StorageStatePath))
        {
            throw new FileNotFoundException(
                "Authentication state file not found. Global setup may have failed.",
                StorageStatePath);
        }
        return StorageStatePath;
    }

    /// <summary>
    /// Checks if authentication state is available
    /// </summary>
    public static bool HasStorageState()
    {
        return File.Exists(StorageStatePath);
    }
}
