using Microsoft.Playwright;
using NUnit.Framework;
using System.Text.RegularExpressions;

namespace duetGPT.Tests;

/// <summary>
/// Tests for authentication and authorization
/// These tests run WITHOUT pre-authenticated storage state to test the login flow
/// </summary>
public class AuthenticationTests : UnauthenticatedTestBase
{
    [Test]
    public async Task ChatPageRequiresAuthentication()
    {
        // Try to access chat without login
        await Page.GotoAsync($"{BaseUrl}/chat-v2");

        // Should redirect to login page
        await Expect(Page).ToHaveURLAsync(new Regex(".*/Account/Login.*"));
    }

    [Test]
    public async Task CanLoginAndAccessChat()
    {
        // This test doesn't use storage state - it tests the login flow itself
        // Navigate to login page
        await Page.GotoAsync($"{BaseUrl}/Account/Login");

        // Wait for login form to load
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Fill in credentials (DevExpress custom components use Name attribute)
        // Look for input with name attribute containing "Email" or "Password"
        var emailInput = Page.Locator("input[name*='Email']").First;
        var passwordInput = Page.Locator("input[name*='Password']").First;

        await emailInput.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await emailInput.FillAsync("martin@brekhof.nl");
        await passwordInput.FillAsync("1Zaqwsx2!");

        // Click login button (DevExpress button with Text="Login")
        var loginButton = Page.GetByRole(AriaRole.Button, new() { Name = "Login" });
        await loginButton.ClickAsync();

        // Wait for redirect to chat page (not login page)
        await Page.WaitForURLAsync(new Regex(".*/($|chat-v2)"), new() { Timeout = 10000 });

        // Verify we're on the chat page
        Assert.That(Page.Url, Does.Not.Contain("/Account/Login"), "Should be redirected away from login page");
    }
}
