param(
    [Parameter(Mandatory=$true)]
    [string]$Tool,
    
    [Parameter(Mandatory=$false)]
    [string]$JsonParams = "{}"
)

# Usage: .\scripts\run-json.ps1 list-tools
# Usage: .\scripts\run-json.ps1 extract-method '{"path":"..."}'

$ErrorActionPreference = "Stop"
$root = Resolve-Path "$PSScriptRoot/.."
$project = Join-Path $root "RefactorMCP.ConsoleApp/RefactorMCP.ConsoleApp.csproj"

# We use simple quoting for the JSON param to avoid double-parsing issues
dotnet run --project "$project" -- --json $Tool $JsonParams
