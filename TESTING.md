# Playwright Testing Setup for duetGPT

This guide provides complete instructions for setting up and running Playwright E2E tests for duetGPT, enabling both manual testing and automated testing by Claude Code.

## Prerequisites

### 1. .NET SDK
- **Version**: .NET 9.0 (already installed for duetGPT)
- **Verify installation**:
  ```bash
  dotnet --version
  # Should show: 9.0.x
  ```

### 2. Node.js (for Playwright Browsers)
- **Minimum version**: Node.js 18+
- **Download**: [https://nodejs.org/](https://nodejs.org/)
- **Verify installation**:
  ```bash
  node --version  # Should show v18.x or higher
  npm --version   # Should show 9.x or higher
  ```

### 3. Operating System Support
- **Windows**: Windows 10+ (PowerShell or CMD)
- **macOS**: macOS 12+ (Terminal)
- **Linux**: Ubuntu 20.04+ / Debian 11+ (Bash)

---

## Quick Start Guide

### Step 1: Create Test Project

From the duetGPT solution root directory:

```bash
# Create new NUnit test project
dotnet new nunit -n duetGPT.Tests

# Navigate to test project
cd duetGPT.Tests

# Add Playwright NUnit integration
dotnet add package Microsoft.Playwright.NUnit --version 1.48.0

# Add Playwright core package
dotnet add package Microsoft.Playwright --version 1.48.0

# Build the project
dotnet build
```

### Step 2: Install Playwright Browsers

Playwright requires browser binaries (Chromium, Firefox, WebKit) to be installed:

**On Windows (PowerShell)**:
```powershell
pwsh bin/Debug/net9.0/playwright.ps1 install
```

**On Linux/macOS (Bash)**:
```bash
./bin/Debug/net9.0/playwright.sh install
```

**Install with system dependencies** (Linux only):
```bash
./bin/Debug/net9.0/playwright.sh install --with-deps
```

### Step 3: Add Test Project to Solution

From the solution root:
```bash
dotnet sln add duetGPT.Tests/duetGPT.Tests.csproj
```

---

## Creating Test Files

### Base Test Class

Create `PlaywrightTestBase.cs` in the `duetGPT.Tests` project:

```csharp
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
    /// </summary>
    protected string BaseUrl => "http://localhost:5000";

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
```

### Example Chat Tests

Create `ChatTests.cs` in the `duetGPT.Tests` project:

```csharp
using Microsoft.Playwright;
using NUnit.Framework;

namespace duetGPT.Tests;

/// <summary>
/// Tests for the chat interface functionality
/// </summary>
public class ChatTests : PlaywrightTestBase
{
    [Test]
    public async Task CanLoadChatPage()
    {
        // Navigate to chat page
        await Page.GotoAsync($"{BaseUrl}/chat-v2");

        // Verify DxAIChat component loaded
        var chatComponent = Page.Locator(".custom-ai-chat");
        await Expect(chatComponent).ToBeVisibleAsync();

        // Verify page title
        await Expect(Page).ToHaveTitleAsync(new Regex(".*duetGPT.*"));
    }

    [Test]
    public async Task CanSendMessage()
    {
        await Page.GotoAsync($"{BaseUrl}/chat-v2");

        // Wait for chat component to load
        await Page.WaitForSelectorAsync(".custom-ai-chat");

        // Find message input (adjust selector based on DxAIChat structure)
        var messageInput = Page.Locator("textarea, input[type='text']").First;
        await messageInput.FillAsync("Hello, test message");

        // Click send button
        var sendButton = Page.Locator("button:has-text('Send'), button[aria-label='Send']");
        await sendButton.ClickAsync();

        // Wait for response (Claude API call may take several seconds)
        await Page.WaitForTimeoutAsync(5000);

        // Verify message appears in chat
        var chatMessages = Page.Locator(".chat-message, [role='article']");
        await Expect(chatMessages).Not.ToBeEmptyAsync();
    }

    [Test]
    public async Task NewThreadButtonClearsChat()
    {
        await Page.GotoAsync($"{BaseUrl}/chat-v2");
        await Page.WaitForSelectorAsync(".custom-ai-chat");

        // Send an initial message
        var messageInput = Page.Locator("textarea, input[type='text']").First;
        await messageInput.FillAsync("Initial test message");
        await Page.Locator("button:has-text('Send')").ClickAsync();
        await Page.WaitForTimeoutAsync(3000);

        // Click New Thread button
        var newThreadButton = Page.Locator("button:has-text('New Thread')");
        await newThreadButton.ClickAsync();

        // Confirm in dialog popup
        var confirmButton = Page.Locator("button:has-text('Yes'), button:has-text('Confirm')");
        await confirmButton.ClickAsync();

        // Wait for UI to update
        await Page.WaitForTimeoutAsync(1000);

        // Verify chat is cleared (adjust selector based on your UI)
        var chatMessages = Page.Locator(".chat-message");
        var count = await chatMessages.CountAsync();
        Assert.That(count, Is.EqualTo(0), "Chat should be empty after new thread");
    }

    [Test]
    public async Task RAGToggleVisible()
    {
        await Page.GotoAsync($"{BaseUrl}/chat-v2");

        // Verify RAG toggle checkbox exists
        var ragToggle = Page.Locator("input[type='checkbox']:near(:text('RAG'))");
        await Expect(ragToggle).ToBeVisibleAsync();
    }

    [Test]
    public async Task ModelSelectionWorks()
    {
        await Page.GotoAsync($"{BaseUrl}/chat-v2");

        // Find model selector (DxComboBox or similar)
        var modelSelector = Page.Locator("select, [role='combobox']").First;
        await Expect(modelSelector).ToBeVisibleAsync();

        // Verify multiple models available
        var options = Page.Locator("option, [role='option']");
        var optionCount = await options.CountAsync();
        Assert.That(optionCount, Is.GreaterThan(0), "Should have model options");
    }
}
```

### Authentication Tests

Create `AuthenticationTests.cs`:

```csharp
using Microsoft.Playwright;
using NUnit.Framework;

namespace duetGPT.Tests;

/// <summary>
/// Tests for authentication and authorization
/// </summary>
public class AuthenticationTests : PlaywrightTestBase
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
        // Navigate to login page
        await Page.GotoAsync($"{BaseUrl}/Account/Login");

        // Fill in credentials (use test account)
        await Page.FillAsync("input[name='Email']", "test@example.com");
        await Page.FillAsync("input[name='Password']", "TestPassword123!");

        // Click login button
        await Page.ClickAsync("button[type='submit']");

        // Wait for redirect
        await WaitForPageLoad();

        // Verify redirected to home/chat page
        await Expect(Page).ToHaveURLAsync(new Regex(".*/(chat-v2)?$"));
    }
}
```

---

## Running Tests

### Start the Application

Before running tests, start the duetGPT application in a separate terminal:

```bash
cd duetGPT
dotnet run
```

Wait for the message: `Now listening on: http://localhost:5000`

### Run All Tests

In a second terminal:

```bash
cd duetGPT.Tests
dotnet test
```

### Run Specific Test

```bash
# Run single test by name
dotnet test --filter "CanSendMessage"

# Run all tests in a class
dotnet test --filter "FullyQualifiedName~ChatTests"

# Run tests matching pattern
dotnet test --filter "Name~Thread"
```

### Run with Verbose Output

```bash
dotnet test --logger "console;verbosity=detailed"
```

### Run in Specific Browser

By default, Playwright uses Chromium. To test other browsers:

```csharp
// In your test class, override browser selection
[Test]
public async Task TestInFirefox()
{
    var firefox = await Playwright.Firefox.LaunchAsync();
    var context = await firefox.NewContextAsync();
    var page = await context.NewPageAsync();
    // ... test code
}
```

---

## Configuration

### Optional: Playwright Configuration File

Create `playwright.config.json` in `duetGPT.Tests`:

```json
{
  "timeout": 30000,
  "retries": 1,
  "use": {
    "baseURL": "http://localhost:5000",
    "screenshot": "only-on-failure",
    "video": "retain-on-failure",
    "trace": "on-first-retry"
  },
  "projects": [
    {
      "name": "chromium",
      "use": {
        "browserName": "chromium"
      }
    },
    {
      "name": "firefox",
      "use": {
        "browserName": "firefox"
      }
    }
  ]
}
```

---

## Debugging Tests

### Take Screenshots

```csharp
[Test]
public async Task DebugTest()
{
    await Page.GotoAsync($"{BaseUrl}/chat-v2");

    // Take screenshot
    await Page.ScreenshotAsync(new()
    {
        Path = "screenshot.png",
        FullPage = true
    });
}
```

### Use Playwright Inspector

```bash
# Set environment variable before running tests
$env:PWDEBUG=1
dotnet test --filter "CanSendMessage"
```

This opens the Playwright Inspector for step-by-step debugging.

### View Test Artifacts

After test failures, check:
- `duetGPT.Tests/bin/Debug/net9.0/playwright-results/` for screenshots
- `duetGPT.Tests/bin/Debug/net9.0/videos/` for test videos (if enabled)

---

## CI/CD Integration

### GitHub Actions Workflow

Create `.github/workflows/playwright-tests.yml`:

```yaml
name: Playwright E2E Tests

on:
  push:
    branches: [ main, develop ]
  pull_request:
    branches: [ main ]

jobs:
  test:
    runs-on: ubuntu-latest
    timeout-minutes: 15

    services:
      postgres:
        image: postgres:15
        env:
          POSTGRES_PASSWORD: test_password
          POSTGRES_DB: duetgpt_test
        options: >-
          --health-cmd pg_isready
          --health-interval 10s
          --health-timeout 5s
          --health-retries 5
        ports:
          - 5432:5432

    steps:
    - name: Checkout code
      uses: actions/checkout@v4

    - name: Setup .NET
      uses: actions/setup-dotnet@v4
      with:
        dotnet-version: '9.0.x'

    - name: Restore dependencies
      run: dotnet restore

    - name: Build solution
      run: dotnet build --no-restore --configuration Release

    - name: Install Playwright browsers
      run: |
        cd duetGPT.Tests
        pwsh bin/Release/net9.0/playwright.ps1 install --with-deps

    - name: Start application in background
      run: |
        cd duetGPT
        dotnet run --no-build --configuration Release &
        echo $! > app.pid
        sleep 15  # Wait for app to start
      env:
        ASPNETCORE_ENVIRONMENT: Testing
        ConnectionStrings__DefaultConnection: "Host=localhost;Database=duetgpt_test;Username=postgres;Password=test_password"
        Anthropic__ApiKey: ${{ secrets.ANTHROPIC_API_KEY }}

    - name: Run Playwright tests
      run: dotnet test duetGPT.Tests/duetGPT.Tests.csproj --no-build --configuration Release --logger "trx;LogFileName=test-results.trx"

    - name: Stop application
      if: always()
      run: |
        if [ -f duetGPT/app.pid ]; then
          kill $(cat duetGPT/app.pid) || true
        fi

    - name: Upload test results
      if: always()
      uses: actions/upload-artifact@v4
      with:
        name: test-results
        path: |
          duetGPT.Tests/TestResults/test-results.trx
          duetGPT.Tests/bin/Release/net9.0/playwright-results/

    - name: Upload screenshots on failure
      if: failure()
      uses: actions/upload-artifact@v4
      with:
        name: playwright-screenshots
        path: duetGPT.Tests/bin/Release/net9.0/playwright-results/**/*.png
```

---

## Troubleshooting

### Issue: Browser not found

**Error**: `Executable doesn't exist at ...`

**Solution**: Run browser installation again:
```bash
pwsh bin/Debug/net9.0/playwright.ps1 install
```

### Issue: Connection refused

**Error**: `net::ERR_CONNECTION_REFUSED`

**Solution**: Ensure duetGPT application is running on `http://localhost:5000`:
```bash
cd duetGPT
dotnet run
```

### Issue: Test timeout

**Error**: `Test exceeded timeout of 30000ms`

**Solution**: Increase timeout in test or wait longer for Claude API:
```csharp
Page.SetDefaultTimeout(60000); // 60 seconds
```

### Issue: Element not found

**Error**: `Locator.click: Timeout ... waiting for locator(...)`

**Solution**:
1. Use Playwright Inspector to find correct selector: `$env:PWDEBUG=1`
2. Add wait before interaction:
   ```csharp
   await Page.WaitForSelectorAsync(".my-element", new() { State = WaitForSelectorState.Visible });
   ```

### Issue: Authentication required

**Solution**: Create test user or use mock authentication:
```csharp
// Set auth token in browser context
var context = await Browser.NewContextAsync(new()
{
    StorageState = "auth.json" // Saved login state
});
```

---

## Writing Effective Tests

### Best Practices

1. **Use data-testid attributes**: Add to important elements for stable selectors
   ```razor
   <button data-testid="send-message-btn">Send</button>
   ```
   ```csharp
   await Page.Locator("[data-testid='send-message-btn']").ClickAsync();
   ```

2. **Avoid hard-coded waits**: Use Playwright's auto-waiting
   ```csharp
   // BAD
   await Task.Delay(3000);

   // GOOD
   await Page.WaitForSelectorAsync(".result");
   ```

3. **Test user journeys, not implementation**: Focus on what users do
   ```csharp
   // User journey: Send message and get response
   await FillMessageAndSend("Hello");
   await WaitForResponse();
   await VerifyResponseVisible();
   ```

4. **Clean up after tests**: Reset state between tests
   ```csharp
   [TearDown]
   public async Task Cleanup()
   {
       // Clear browser storage
       await Context.ClearCookiesAsync();
   }
   ```

---

## Claude Code Integration

With Playwright installed, Claude Code can autonomously:

### 1. Run Tests
```bash
dotnet test duetGPT.Tests
```

### 2. Interpret Results
Claude Code reads test output and can:
- Identify which tests failed
- Read error messages and stack traces
- View screenshots of failures
- Suggest fixes based on errors

### 3. Write New Tests
Claude Code can create tests for new features:
```csharp
[Test]
public async Task NewFeature_WorksCorrectly()
{
    // Test implementation
}
```

### 4. Debug Failures
Using Playwright Inspector and screenshots:
```bash
$env:PWDEBUG=1
dotnet test --filter "FailingTest"
```

### 5. Verify Changes
After code modifications:
```bash
dotnet test
# Claude Code checks if all tests pass
```

---

## Next Steps

1. **Expand test coverage**: Add tests for all major features
2. **Add visual regression testing**: Use Playwright's screenshot comparison
3. **Integrate with CI/CD**: Run tests on every commit
4. **Add performance tests**: Measure page load times
5. **Test edge cases**: Error handling, network failures, timeouts

---

## Additional Resources

- **Playwright Documentation**: [https://playwright.dev/dotnet/](https://playwright.dev/dotnet/)
- **NUnit Documentation**: [https://docs.nunit.org/](https://docs.nunit.org/)
- **Playwright Best Practices**: [https://playwright.dev/dotnet/docs/best-practices](https://playwright.dev/dotnet/docs/best-practices)
- **DevExpress Blazor Testing**: [https://docs.devexpress.com/Blazor/403887/testing](https://docs.devexpress.com/Blazor/403887/testing)

---

## Summary

You've successfully set up Playwright E2E testing for duetGPT! This enables:
- ✅ Automated testing of chat functionality
- ✅ Regression prevention
- ✅ CI/CD integration
- ✅ Claude Code autonomous testing
- ✅ Confidence in deployments

Happy testing! 🧪
