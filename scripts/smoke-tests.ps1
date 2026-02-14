$ErrorActionPreference = "Stop"

$root = Resolve-Path "$PSScriptRoot/.."
Push-Location $root

try {
    # Use random temp files to avoid collisions
    $stdoutFile = [System.IO.Path]::GetTempFileName()
    $stderrFile = [System.IO.Path]::GetTempFileName()

    function Test-Command {
        param($Name, $Command, $ExpectedExitCode, $ExpectedOutputPattern, $ExpectedStderrPattern)

        Write-Host "Running Test: $Name..." -NoNewline
        
        $process = Start-Process -FilePath "dotnet" -ArgumentList $Command -PassThru -NoNewWindow -Wait -RedirectStandardOutput $stdoutFile -RedirectStandardError $stderrFile
        
        $stdout = Get-Content $stdoutFile -ErrorAction SilentlyContinue | Out-String
        $stderr = Get-Content $stderrFile -ErrorAction SilentlyContinue | Out-String
        
        $failed = $false
        
        if ($process.ExitCode -ne $ExpectedExitCode) {
            Write-Host " FAILED (Exit Code)" -ForegroundColor Red
            Write-Host "  Expected: $ExpectedExitCode, Got: $($process.ExitCode)"
            Write-Host "  STDOUT: $stdout"
            Write-Host "  STDERR: $stderr"
            $failed = $true
        }
        elseif ($ExpectedOutputPattern -and $stdout -notmatch $ExpectedOutputPattern) {
            Write-Host " FAILED (Stdout Mismatch)" -ForegroundColor Red
            Write-Host "  Expected pattern: $ExpectedOutputPattern"
            Write-Host "  Got: $stdout"
            Write-Host "  STDERR: $stderr"
            $failed = $true
        }
        elseif ($ExpectedStderrPattern -and $stderr -notmatch $ExpectedStderrPattern) {
            Write-Host " FAILED (Stderr Mismatch)" -ForegroundColor Red
            Write-Host "  Expected pattern: $ExpectedStderrPattern"
            Write-Host "  Got: $stderr"
            Write-Host "  STDOUT: $stdout"
            $failed = $true
        }
        else {
            Write-Host " PASSED" -ForegroundColor Green
        }

        # Clear content for next run
        Clear-Content $stdoutFile -ErrorAction SilentlyContinue
        Clear-Content $stderrFile -ErrorAction SilentlyContinue
        
        if ($failed) {
            exit 1
        }
    }

    Write-Host "Building project..."
    dotnet build -v q
    if ($LASTEXITCODE -ne 0) { exit 1 }

    $project = "RefactorMCP.ConsoleApp/RefactorMCP.ConsoleApp.csproj"

    # 1. Version
    Test-Command -Name "Version" -Command "run --project $project --no-build -- --version" -ExpectedExitCode 0 -ExpectedOutputPattern "RefactorMCP v"

    # 2. Help
    Test-Command -Name "Help" -Command "run --project $project --no-build -- --help" -ExpectedExitCode 0 -ExpectedOutputPattern "Usage:"

    # 3. List Tools (Canonical)
    Test-Command -Name "List Tools" -Command "run --project $project --no-build -- list-tools" -ExpectedExitCode 0 -ExpectedOutputPattern "extract-method"

    # 3b. List Tools (Legacy -command)
    Test-Command -Name "List Tools (Legacy)" -Command "run --project $project --no-build -- list-tools-command" -ExpectedExitCode 0 -ExpectedOutputPattern "extract-method"

    # 4. JSON Mode (Unknown Tool)
    Test-Command -Name "JSON Mode (Unknown Tool)" -Command "run --project $project --no-build -- --json unknown-tool {}" -ExpectedExitCode 2 -ExpectedStderrPattern "Did you mean"

    # 5. Unknown Positional (Should fail with hint)
    # SKIPPED: In CI/Automated environments, Start-Process might trigger IsInputRedirected=true, 
    # causing the server to start (correct behavior) but hanging the test (Start-Process -Wait).
    # Test-Command -Name "Unknown Positional" -Command "run --project $project --no-build -- pizza" -ExpectedExitCode 2 -ExpectedStderrPattern "Did you mean"

    # 6. Doctor
    Test-Command -Name "Doctor" -Command "run --project $project --no-build -- doctor" -ExpectedExitCode 0 -ExpectedOutputPattern "RefactorMCP Doctor"

    Write-Host "All smoke tests passed!" -ForegroundColor Green
}
finally {
    Remove-Item $stdoutFile -ErrorAction SilentlyContinue
    Remove-Item $stderrFile -ErrorAction SilentlyContinue
    Pop-Location
}
