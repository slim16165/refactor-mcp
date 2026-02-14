# RefactorMCP

RefactorMCP is a Model Context Protocol (MCP) server that exposes robust Roslyn-based C# refactoring tools. 
Designed for stability, it supports both human interactive CLI usage and agentic automation.

## Features
- **MCP Server**: Stdio-based server for IDEs and AI agents (Windsurf, Cursor, Claude Desktop).
- **CLI Tools**: `list-tools`, `doctor`, and JSON-based one-shot invocation.
- **Robustness**: Normalized tool naming (kebab-case), fuzzy matching, and strict stdout/stderr discipline.

## Modes

| Mode | Command | Description |
|------|---------|-------------|
| **MCP Server** | `refactor-mcp` (or `mcp`) | Starts the JSON-RPC server on Stdio. **Default**. |
| **List Tools** | `refactor-mcp list-tools` | Lists all available tools in kebab-case. |
| **JSON Runner**| `refactor-mcp --json <tool> <params>`| Executes a single tool with JSON parameters. |
| **Doctor** | `refactor-mcp doctor` | Diagnoses environment issues (.NET, MSBuild, Input Redirection). |

---

## Quick Start

### 1. Build & Run (Source)
```bash
cd RefactorMCP.ConsoleApp
dotnet run
# NOTE: Without arguments, this starts the server and waits for input.
# It might look "stuck" - this is normal behavior for MCP servers!
```

### 2. Interactive CLI
```bash
# List available tools
dotnet run -- list-tools

# Check environment
dotnet run -- doctor

# Run a specific tool (example)
dotnet run -- --json extract-method "{\"path\": \"...\", \"selection\": \"...\"}"
```

### 3. Agent Integration (Windsurf / Claude Desktop / Antigravity)
Configure your MCP client to run the server.

**Example Configuration (mcp_config.json):**
```json
{
  "mcpServers": {
    "refactor-mcp": {
      "command": "C:\\path\\to\\RefactorMCP.ConsoleApp\\bin\\Release\\net10.0\\win-x64\\RefactorMCP.ConsoleApp.exe",
      "args": [],
      "env": {},
      "disabled": false
    }
  }
}
```

**Important Notes:**
- Always use **absolute paths** to the executable
- **Do NOT** include a `\publish\` subdirectory in the path unless you've explicitly published to that location
- Correct path format: `...\bin\Release\net10.0\win-x64\RefactorMCP.ConsoleApp.exe`
- Common mistake: `...\bin\Release\net10.0\win-x64\publish\RefactorMCP.ConsoleApp.exe` (incorrect)

**Verification:**
After configuration, restart your IDE/agent and verify with:
1. Check tool discovery: The server should expose 36 refactoring tools
2. Test basic operation: Try `cleanup-usings` or `extract-method` on a C# file
3. Expected performance: Operations should complete in < 2 seconds

**Note:** Always use absolute paths.

---

## Troubleshooting

### Windows: timeouts e process launch (agent-safe)

**CMD** e **PowerShell** NON sono intercambiabili.

#### Timeout
- In **Linux/macOS** esiste `timeout 10s <cmd>`.
- In **Windows CMD** `timeout` è un comando di *sleep* (non kill):  
  `cmd /c timeout /t 5 /nobreak`
- In **PowerShell**, per killare dopo N secondi usa `Wait-Process` + `Stop-Process`:

```powershell
$exe = "RefactorMCP.ConsoleApp\bin\Debug\net9.0\RefactorMCP.ConsoleApp.exe"
$p = Start-Process -FilePath $exe -PassThru -NoNewWindow
if (-not (Wait-Process -Id $p.Id -Timeout 2 -ErrorAction SilentlyContinue)) { Stop-Process -Id $p.Id -Force }
```

#### `start /B` (CMD) vs `Start-Process` (PowerShell)

* `start /B` è **CMD-only**:

  ```cmd
  cmd /c start "" /B dotnet run --project RefactorMCP.ConsoleApp\RefactorMCP.ConsoleApp.csproj -- mcp
  ```
* In PowerShell usa:

  ```powershell
  Start-Process dotnet -ArgumentList @("run","--project","RefactorMCP.ConsoleApp/RefactorMCP.ConsoleApp.csproj","--","mcp") -NoNewWindow
  ```

#### “Sembra bloccato”

Se lanci senza argomenti, parte il **server MCP su stdio** e attende input su STDIN.
Questo è normale.
Per test non-interattivi usa sempre `--json`:

```powershell
dotnet run --project RefactorMCP.ConsoleApp/RefactorMCP.ConsoleApp.csproj -- --json list-tools "{}"
```

### Output Discipline
- **STDOUT**: Reserved strictly for MCP protocol messages and Tool results (in JSON mode).
- **STDERR**: All logs, debug info, MSBuild warnings, and errors go here.
- **Implication**: Agents consuming JSON output should **only** parse Stdout.

---

## Helper Scripts
Located in `/scripts` for easier usage:
- `run-mcp.ps1`: Starts the server with a banner (PowerShell).
- `run-json.ps1`: Runs a tool with JSON params, handling quoting safely.
- `run-json.cmd`: CMD wrapper for JSON mode.

## Development

### Build
```bash
dotnet build
```

### Smoke Tests
```powershell
./scripts/smoke-tests.ps1
```

## Contributing
- Run `dotnet format` before committing.
- Ensure `smoke-tests.ps1` passes.
- Keep `Program.cs` logic minimal; delegate to Services.

## License
[Mozilla Public License 2.0](https://www.mozilla.org/MPL/2.0/)
