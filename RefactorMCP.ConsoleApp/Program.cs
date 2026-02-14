using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using System;
using System.ComponentModel;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Runtime.CompilerServices;
using RefactorMCP.ConsoleApp.Infrastructure;
using RefactorMCP.ConsoleApp.Services;

[assembly: InternalsVisibleTo("RefactorMCP.Tests")]



// Ensure MSBuild is registered before any Roslyn types are loaded
try
{
    if (!Microsoft.Build.Locator.MSBuildLocator.IsRegistered)
    {
        var instances = Microsoft.Build.Locator.MSBuildLocator.QueryVisualStudioInstances().ToList();
        
        // **CRITICAL FIX**: Force Visual Studio 2022 MSBuild for .NET 10+ stability
        var runtimeVersion = Environment.Version;
        var isNet10Preview = runtimeVersion.Major >= 10;
        
        Microsoft.Build.Locator.VisualStudioInstance? selectedInstance = null;
        
        if (isNet10Preview)
        {
            Console.Error.WriteLine("[MSBuild] .NET 10+ detected, forcing Visual Studio 2022 MSBuild");
            
            // Try Visual Studio 2022 Enterprise first
            var vs2022Path = @"C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\amd64";
            if (Directory.Exists(vs2022Path))
            {
                selectedInstance = instances.FirstOrDefault(i => 
                    i.MSBuildPath.Equals(vs2022Path, StringComparison.OrdinalIgnoreCase));
                
                if (selectedInstance != null)
                {
                    Console.Error.WriteLine($"[MSBuild] Using VS2022 Enterprise: {selectedInstance.Version}");
                }
            }
            
            // Fallback to JetBrains Rider
            if (selectedInstance == null)
            {
                var riderPath = @"C:\Program Files\JetBrains\JetBrains Rider 2025.3.2\tools\MSBuild\Current\Bin\amd64";
                if (Directory.Exists(riderPath))
                {
                    selectedInstance = instances.FirstOrDefault(i => 
                        i.MSBuildPath.Equals(riderPath, StringComparison.OrdinalIgnoreCase));
                    
                    if (selectedInstance != null)
                    {
                        Console.Error.WriteLine($"[MSBuild] Using JetBrains Rider: {selectedInstance.Version}");
                    }
                }
            }
        }
        
        // Support for deterministic overrides
        string? customPath = Environment.GetEnvironmentVariable("REFACTOR_MCP_MSBUILD_PATH");
        string? customKind = Environment.GetEnvironmentVariable("REFACTOR_MCP_MSBUILD_KIND");

        if (!string.IsNullOrEmpty(customPath))
        {
            selectedInstance = instances.FirstOrDefault(i => i.MSBuildPath.Equals(customPath, StringComparison.OrdinalIgnoreCase));
            if (selectedInstance == null)
            {
                Console.Error.WriteLine($"Warning: Custom MSBuild path '{customPath}' not found among detected instances.");
            }
        }

        if (selectedInstance == null && !string.IsNullOrEmpty(customKind))
        {
            var kind = customKind.ToLowerInvariant();
            selectedInstance = instances.Where(i => 
            {
                var typeStr = i.DiscoveryType.ToString().ToLowerInvariant();
                return (kind == "vs" && typeStr.Contains("visualstudio")) ||
                       (kind == "sdk" && typeStr.Contains("dotnet")) ||
                       (kind == "buildtools" && typeStr.Contains("buildtools"));
            }).OrderByDescending(i => i.Version).FirstOrDefault();
        }

        if (selectedInstance == null)
        {
            // Fallback to latest stable
            selectedInstance = instances
                .Where(i => !i.Version.ToString().Contains("preview") && !i.Version.ToString().Contains("rc"))
                .OrderByDescending(i => i.Version)
                .FirstOrDefault() ?? instances.OrderByDescending(i => i.Version).FirstOrDefault();
        }
        
        if (selectedInstance != null)
        {
            Console.Error.WriteLine($"[MSBuild] Selected: {selectedInstance.Name} ({selectedInstance.Version})");
            Console.Error.WriteLine($"[MSBuild] Path: {selectedInstance.MSBuildPath}");
            Microsoft.Build.Locator.MSBuildLocator.RegisterInstance(selectedInstance);
        }
        else
        {
            Console.Error.WriteLine("[MSBuild] No instances found! Attempting RegisterDefaults...");
            Microsoft.Build.Locator.MSBuildLocator.RegisterDefaults();
        }
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[MSBuild] Registration failed: {ex.Message}");
    Console.Error.WriteLine(ex.StackTrace);
}

// --------------------------------------------------------------------------------------
// CLI Argument Parsing & Execution Logic
// Refactor V2: Robust parsing, clean stdout/stderr separation.
// --------------------------------------------------------------------------------------

if (args.Length == 0)
{
    // Default mode: MCP Server Stdio
    // Check if input is redirected to warn users who run this interactively without piping
    if (!Console.IsInputRedirected)
    {
        Console.Error.WriteLine("Warning: Running in MCP Server mode but input is NOT redirected.");
        Console.Error.WriteLine("If you meant to use the CLI, use 'list-tools' or '--help'.");
        Console.Error.WriteLine("If you meant to start the server, just wait (it reads from stdin).");
    }
    await RunServerMode(Array.Empty<string>());
    return;
}

var command = args[0];
var commandLower = command.ToLowerInvariant();

switch (commandLower)
{
    case "--help":
    case "-h":
    case "/?":
        PrintHelp();
        return;

    case "--version":
    case "-v":
        PrintVersion();
        return;

    case "list-tools":
    case "list-tools-command": // Legacy alias
    case "list":
        RunListTools();
        return;

    case "doctor":
    case "diag":
        RunDoctor();
        return;

    case "mcp":
    case "--mcp-stdio":
    case "--server":
    case "--stdio":
        await RunServerMode(Array.Empty<string>());
        return;

    case "--json":
        if (args.Length < 3)
        {
            Console.Error.WriteLine("Usage: --json <tool> '<json_params>'");
            Environment.ExitCode = 2;
            return;
        }
        await RunJsonMode(args);
        return;

    default:
        // Handle unknown arguments
        if (command.StartsWith("-"))
        {
            // Unknown flag -> Warning but start server (robustness)
            Console.Error.WriteLine($"Warning: Unknown option '{command}'. Starting MCP server...");
            await RunServerMode(Array.Empty<string>());
        }
        else if (Console.IsInputRedirected)
        {
            // Likely launched by an MCP host: do not block server startup
            Console.Error.WriteLine($"Warning: Unknown command '{command}'. Starting MCP server (stdio) anyway.");
            await RunServerMode(Array.Empty<string>());
        }
        else
        {
            // Unknown positional -> Error (avoid hang)
            Console.Error.WriteLine($"Error: Unknown command '{command}'.");
            Console.Error.WriteLine("Did you mean 'list-tools'?");
            Console.Error.WriteLine("Use '--help' for usage information.");
            Environment.ExitCode = 2;
        }
        return;
}

// --------------------------------------------------------------------------------------
// Implementations
// --------------------------------------------------------------------------------------

static void PrintHelp()
{
    Console.WriteLine("RefactorMCP - C# Refactoring MCP Server & CLI");
    Console.WriteLine("Usage:");
    Console.WriteLine("  RefactorMCP.ConsoleApp [command] [options]");
    Console.WriteLine();
    Console.WriteLine("Commands:");
    Console.WriteLine("  (no args)             Start MCP server (stdio mode)");
    Console.WriteLine("  mcp                   Start MCP server explicitly");
    Console.WriteLine("  list-tools            List available refactoring tools");
    Console.WriteLine("  doctor                Check environment health");
    Console.WriteLine("  --json <tool> <args>  Run a tool with JSON params (one-shot)");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --help, -h            Show this help");
    Console.WriteLine("  --version, -v         Show version");
    Console.WriteLine();
    Console.WriteLine("Examples:");
    Console.WriteLine("  RefactorMCP.ConsoleApp list-tools");
    Console.WriteLine("  RefactorMCP.ConsoleApp --json extract-method '{\"Action\":\"Extract\", ...}'");
}

static void PrintVersion()
{
    var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown";
    Console.WriteLine($"RefactorMCP v{version}");
}

static void RunListTools()
{
    var toolNames = ToolRegistry.GetAvailableToolNames();
    Console.WriteLine("Available refactoring tools:");
    foreach (var name in toolNames)
    {
        Console.WriteLine($"  {name}");
    }
}

static void RunDoctor()
{
    Console.WriteLine("RefactorMCP Doctor");
    Console.WriteLine("------------------");
    Console.WriteLine($"OS: {Environment.OSVersion}");
    Console.WriteLine($"Runtime: {Environment.Version}");
    
    var dotnetPath = "dotnet";
    try 
    {
        // Try to get dotnet version
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = "--version",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        var p = System.Diagnostics.Process.Start(psi);
        if (p != null)
        {
            p.WaitForExit(2000);
            var output = p.StandardOutput.ReadToEnd().Trim();
            Console.WriteLine($"Dotnet SDK: {output}");
        }
    }
    catch
    {
        Console.WriteLine("Dotnet SDK: Not found or error running 'dotnet --version'");
    }

    Console.WriteLine($"Input Redirected: {Console.IsInputRedirected}");
    if (!Console.IsInputRedirected)
    {
        Console.Error.WriteLine("  [Info] Standard Input is NOT redirected. MCP Server mode requires piped input.");
    }
    else
    {
        Console.WriteLine("  [OK] Standard Input is redirected.");
    }

    Console.WriteLine("Done.");
}

async Task RunServerMode(string[] hostArgs)
{
    // IMPORTANT: Pass empty args to Host.CreateApplicationBuilder in server mode
    // to prevent it from trying to parse our own CLI args.
    var builder = Host.CreateApplicationBuilder(hostArgs);
    
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole(consoleLogOptions =>
    {
        // CRITICAL: All logs must go to stderr to not separate from MCP protocol on stdout
        consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace;
    });

    builder.Services
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithToolsFromAssembly()
        .WithResourcesFromAssembly()
        .WithPromptsFromAssembly();

    // Register custom services
    builder.Services.AddSingleton<SolutionLoaderConfiguration>();
    builder.Services.AddSingleton<RetryPolicy>();
    builder.Services.AddSingleton<SolutionLoader>();

    // Initialize ToolCallLogger and SolutionService after building the host
    var app = builder.Build();
    var appLoggerFactory = app.Services.GetRequiredService<ILoggerFactory>();

    // Setup custom assembly context for .NET 10+ environments
    var logger = appLoggerFactory.CreateLogger("AssemblyContextSetup");
    SdkVersionDetector.InitializeLogger(logger);
    var environment = SdkVersionDetector.DetectEnvironment();

    if (environment.UseCustomAssemblyContext)
    {
        logger.LogInformation("Setting up custom assembly context: {Reason}", environment.Reason);
        
        var basePath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".";
        var customContext = new RefactorAssemblyContext(basePath, logger);
        
        // Note: We can't replace the default AssemblyLoadContext, but we can use our custom context
        // for specific operations in the SolutionLoader and other components
        logger.LogInformation("Custom assembly context initialized for path: {BasePath}", basePath);
    }
    else
    {
        logger.LogInformation("Using default assembly context: {Reason}", environment.Reason);
    }

    var solutionLoader = app.Services.GetRequiredService<SolutionLoader>();
    var solutionServiceLogger = appLoggerFactory.CreateLogger("SolutionService");

    // Initialize static services
    ToolCallLogger.InitializeLogger(appLoggerFactory.CreateLogger("ToolCallLogger"));
    RefactorMCP.ConsoleApp.Infrastructure.SolutionService.Initialize(solutionLoader, solutionServiceLogger);

    await app.RunAsync();
}

static async Task RunJsonMode(string[] args)
{
    // args[0] is --json, args[1] is tool, args[2...] is json
    ToolCallLogger.RestoreFromEnvironment();
    var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

    var toolName = args[1];
    var json = string.Join(" ", args.Skip(2)).Trim();

    if (string.IsNullOrWhiteSpace(json))
    {
        Console.Error.WriteLine("Error: Missing JSON params.");
        Environment.ExitCode = 2;
        return;
    }

    Dictionary<string, JsonElement>? paramDict;
    try
    {
        // Fast JSON validity check with clearer error surface
        using var _ = JsonDocument.Parse(json);
        paramDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, jsonOptions);
        if (paramDict == null)
        {
            Console.Error.WriteLine("Error: Failed to parse parameters");
            Environment.ExitCode = 2;
            return;
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error parsing JSON: {ex.Message}");
        Environment.ExitCode = 2;
        return;
    }

    var method = ToolRegistry.GetTool(toolName);

    if (method == null)
    {
        Console.Error.WriteLine($"Unknown tool: {toolName}");
        var suggestions = ToolRegistry.SuggestTools(toolName);
        if (suggestions.Count > 0)
        {
            Console.Error.WriteLine($"Did you mean: {string.Join(", ", suggestions)}?");
        }
        Console.Error.WriteLine("List tools:");
        Console.Error.WriteLine("  RefactorMCP.ConsoleApp list-tools");
        Console.Error.WriteLine("  RefactorMCP.ConsoleApp --json list-tools \"{}\"");
        Environment.ExitCode = 2;
        return;
    }

    var parameters = method.GetParameters();
    var invokeArgs = new object?[parameters.Length];
    var rawValues = new Dictionary<string, string?>();
    
    // Parameter binding logic
    try 
    {
        for (int i = 0; i < parameters.Length; i++)
        {
            var p = parameters[i];
            if (paramDict.TryGetValue(p.Name!, out var value))
            {
                rawValues[p.Name!] = value.ToString();
                if (value.ValueKind == JsonValueKind.String)
                {
                    invokeArgs[i] = ConvertInput(value.GetString()!, p.ParameterType);
                }
                else
                {
                    invokeArgs[i] = value.Deserialize(p.ParameterType, jsonOptions);
                }
            }
            else if (p.HasDefaultValue)
            {
                rawValues[p.Name!] = null;
                invokeArgs[i] = p.DefaultValue;
            }
            else
            {
                Console.Error.WriteLine($"Error: Missing required parameter '{p.Name}'");
                Environment.ExitCode = 2;
                return;
            }
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Parameter binding error: {ex.Message}");
        Environment.ExitCode = 2;
        return;
    }

    try
    {
        var result = method.Invoke(null, invokeArgs);
        if (result is Task<string> taskStr)
        {
            Console.WriteLine(await taskStr);
        }
        else if (result is Task task)
        {
            await task;
            Console.WriteLine("Done");
        }
        else if (result != null)
        {
            Console.WriteLine(result.ToString());
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error executing tool: {ex.Message}");
        // We might validly return 0 even if tool fails logic, but if exception implies crash -> 1
        Environment.ExitCode = 1;
    }
    finally
    {
        if (!string.Equals(method.Name, nameof(LoadSolutionTool.LoadSolution)))
            ToolCallLogger.Log(method.Name, rawValues);
    }
}

static object? ConvertInput(string value, Type targetType)
{
    if (targetType == typeof(string))
        return value;
    if (targetType == typeof(string[]))
        return value.Split(',', StringSplitOptions.RemoveEmptyEntries);
    if (targetType == typeof(int))
        return int.Parse(value);
    if (targetType == typeof(bool))
        return bool.Parse(value);
    return Convert.ChangeType(value, targetType);
}
