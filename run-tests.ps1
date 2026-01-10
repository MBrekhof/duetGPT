# PowerShell script to run duetGPT and execute Playwright tests

Write-Host "Starting duetGPT application..." -ForegroundColor Green

# Start the application in background with correct URL
$appProcess = Start-Process -FilePath "dotnet" -ArgumentList "run --project duetGPT/duetGPT.csproj --urls https://localhost:44391" -PassThru -NoNewWindow

# Wait for application to start (adjust time if needed)
Write-Host "Waiting for application to start (30 seconds)..." -ForegroundColor Yellow
Start-Sleep -Seconds 30

# Check if process is still running
if ($appProcess.HasExited) {
    Write-Host "ERROR: Application failed to start!" -ForegroundColor Red
    exit 1
}

Write-Host "Application started. Running tests..." -ForegroundColor Green

# Run the tests
Set-Location duetGPT.Tests
dotnet test --logger "console;verbosity=normal"
$testExitCode = $LASTEXITCODE

# Stop the application
Write-Host "Stopping application..." -ForegroundColor Yellow
Stop-Process -Id $appProcess.Id -Force

# Return test exit code
Write-Host "Tests completed with exit code: $testExitCode" -ForegroundColor $(if ($testExitCode -eq 0) { "Green" } else { "Red" })
exit $testExitCode
