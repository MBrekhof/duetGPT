# GitHub Actions Workflows

## Playwright E2E Tests

Automated end-to-end tests run on every push to `main` and `develop` branches, as well as on pull requests.

### Required GitHub Secrets

Add the following secret to your GitHub repository:

1. Go to: **Settings** → **Secrets and variables** → **Actions**
2. Click **New repository secret**
3. Add:
   - **Name:** `ANTHROPIC_API_KEY`
   - **Value:** Your Anthropic API key

### Workflow Details

**File:** `playwright-tests.yml`

**Triggers:**
- Push to `main` or `develop` branches
- Pull requests to `main` branch
- Manual dispatch (via GitHub Actions tab)

**Environment:**
- **OS:** Ubuntu Latest
- **Runtime:** .NET 10.0
- **Database:** PostgreSQL 15 with pgvector extension
- **Browser:** Chromium (via Playwright)

**Steps:**
1. Checkout code
2. Setup .NET 10.0 SDK
3. Setup Node.js 20 (for Playwright)
4. Restore NuGet packages
5. Build solution in Release mode
6. Install Playwright browsers with dependencies
7. Create test configuration with secrets
8. Start duetGPT application in background
9. Wait for application to be ready
10. Run Playwright tests
11. Stop application
12. Upload test results and screenshots (on failure)
13. Generate test summary in GitHub Actions UI

**Test Results:**
- Test results are uploaded as artifacts (TRX format)
- Screenshots are uploaded on test failure
- Summary is shown in the GitHub Actions run page

**Timeout:**
- Workflow timeout: 20 minutes
- Test timeout: 2 minutes per test (configured in tests)

### Running Tests Manually

You can trigger the workflow manually:

1. Go to **Actions** tab in GitHub
2. Select **Playwright E2E Tests** workflow
3. Click **Run workflow**
4. Select branch and click **Run workflow**

### Viewing Results

**In GitHub Actions:**
- Click on the workflow run
- Check the **Test Report Summary** at the bottom
- Download artifacts to view detailed results or screenshots

**Artifacts Available:**
- `test-results` - Complete test results (TRX files) and Playwright output
- `playwright-screenshots` - Screenshots of failures (only on test failure)

### Local Development

To run the same tests locally:

```bash
# Terminal 1 - Start application
cd duetGPT
dotnet run

# Terminal 2 - Run tests
cd duetGPT.Tests
dotnet test
```

Or use the PowerShell script:

```powershell
.\run-tests.ps1
```

### Troubleshooting

**Authentication Failures:**
- Ensure `ANTHROPIC_API_KEY` secret is properly set in GitHub repository settings
- The key must be a valid Anthropic API key

**Database Connection Issues:**
- PostgreSQL service runs in Docker container
- Connection string: `Host=localhost;Port=5432;Database=duetgpt_test;Username=postgres;Password=test_password`

**Application Start Failures:**
- Check the "Start application" step logs
- Verify SSL certificate configuration
- Ensure all dependencies are properly restored

**Test Timeouts:**
- Default timeout is 30 seconds per test operation
- Increase timeout in test code if needed for slower operations

### Configuration

The workflow uses a test-specific configuration:
- Environment: `Testing`
- Database: Separate test database (`duetgpt_test`)
- Logging: Minimal (Warning level only)
- URL: `https://localhost:44391`

This ensures tests don't interfere with any development or production data.
