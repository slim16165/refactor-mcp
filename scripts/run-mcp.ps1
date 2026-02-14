$ErrorActionPreference = "Stop"
$root = Resolve-Path "$PSScriptRoot/.."
$project = Join-Path $root "RefactorMCP.ConsoleApp/RefactorMCP.ConsoleApp.csproj"

Write-Host "Starting Refactor-mcp Server..." -ForegroundColor Cyan
Write-Host "This process will wait for JSON-RPC messages on Stdin." -ForegroundColor Yellow
Write-Host "Press Ctrl+C to stop." -ForegroundColor Gray

dotnet run --project "$project" -- mcp
