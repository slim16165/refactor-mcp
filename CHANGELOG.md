# Changelog

## [0.3.1] - 2026-02-17

### Fixed
- `ToolCallLogger`: avoid JSONL contention with concurrent writers (`lock` + `FileShare.ReadWrite`) and unique log filenames.
- `CleanupUsingsTool`: filter `CS8019` diagnostics by current document syntax tree to avoid invalid spans.
- `RangeService`: validate start/end columns against line length.
- `RenameSymbolTool`: improved symbol resolution for locals/parameters and clearer line/column validation errors.
- `PrivateFieldInfoWalker`: include implicitly private fields.
- `UnusedMembersWalker`: detect `this.Method()` invocations and avoid false positives for fields used once.
- `InstanceMemberNameWalker`: include static fields in collected names (aligned with current tests/usage).
- Tests: normalize line endings in rename assertions; adjust extract-method example selection for v2 control-flow limits.

## [Unreleased] - 2026-02-14

### Verified
- **Configuration**: Tested and verified MCP server integration with Antigravity
- **Functionality**: Successfully tested 36 refactoring tools including:
  - `cleanup-usings` - Removes unused using directives
  - `extract-method` - Extracts code into new methods with dry-run validation
  - Solution loading with multi-project support (tested with RefactorMCP.sln)
- **Performance**: All operations complete in < 2 seconds
- **Documentation**: Added comprehensive test report and verification walkthrough

### Changed (CLI Overhaul)
- **CLI Defaults**: Running without arguments now explicitly warns IF input is not redirected, but still starts Server mode (non-breaking defaults).
- **Tool Naming**: Standardized all tools to `kebab-case` (e.g., `extract-method`).
- **Discovery**: `list-tools` command now lists standardized names.
- **Output**: Enforced strict separation of Stderr (logs) and Stdout (protocol/data).

### Added
- **Command**: `doctor` - diagnostics for environment, .NET SDK, and input redirection status.
- **Feature**: Fuzzy matching and "Did you mean?" suggestions for unknown tools.
- **Scripts**: Added `scripts/run-json.ps1`, `scripts/run-mcp.ps1` for agent-safe execution.
- **Flag**: `--mcp-stdio` / `mcp` alias to explicitly start server mode.

### Fixed
- Fixed internal argument parsing to prevent "hanging" perception when users expect a CLI.
- Fixed `Program.cs` to ignore unknown flags starting with `-` (robustness) while warning on stderr.
