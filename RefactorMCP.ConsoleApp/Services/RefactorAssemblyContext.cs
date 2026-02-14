using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.Logging;

namespace RefactorMCP.ConsoleApp.Services;

/// <summary>
/// Custom assembly loading context that forces loading of bundled dependencies
/// instead of system/GAC assemblies, particularly important for .NET 10 preview
/// environments where MSBuild 15.1.0.0 gets loaded from runtime instead of bundled 17.7.2
/// </summary>
public class RefactorAssemblyContext : AssemblyLoadContext
{
    private readonly string _basePath;
    private readonly ILogger _logger;

    public RefactorAssemblyContext(string basePath, ILogger logger) : base(isCollectible: false)
    {
        _basePath = basePath ?? throw new ArgumentNullException(nameof(basePath));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Priority 1: Try to load from bundled dependencies first
        string assemblyPath = Path.Combine(_basePath, $"{assemblyName.Name}.dll");
        if (File.Exists(assemblyPath))
        {
            try
            {
                var assembly = LoadFromAssemblyPath(assemblyPath);
                _logger.LogDebug("Loaded assembly {AssemblyName} from bundled path: {AssemblyPath}", 
                    assemblyName.Name, assemblyPath);
                return assembly;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load bundled assembly {AssemblyName} from {AssemblyPath}", 
                    assemblyName.Name, assemblyPath);
            }
        }

        // Priority 2: For critical MSBuild/CodeAnalysis assemblies, try more aggressive resolution
        if (IsCriticalAssembly(assemblyName.Name))
        {
            // Try to find in subdirectories
            var searchPaths = new[]
            {
                _basePath,
                Path.Combine(_basePath, "BuildHost-netcore"),
                Path.Combine(_basePath, "BuildHost-net472")
            };

            foreach (var searchPath in searchPaths)
            {
                var candidatePath = Path.Combine(searchPath, $"{assemblyName.Name}.dll");
                if (File.Exists(candidatePath))
                {
                    try
                    {
                        var assembly = LoadFromAssemblyPath(candidatePath);
                        _logger.LogDebug("Loaded critical assembly {AssemblyName} from fallback path: {AssemblyPath}", 
                            assemblyName.Name, candidatePath);
                        return assembly;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to load critical assembly {AssemblyName} from fallback path: {AssemblyPath}", 
                            assemblyName.Name, candidatePath);
                    }
                }
            }
        }

        // Priority 3: Let the default context handle it (GAC, runtime, etc.)
        _logger.LogDebug("Delegating assembly {AssemblyName} to default context", assemblyName.Name);
        return null;
    }

    private static bool IsCriticalAssembly(string assemblyName)
    {
        return assemblyName.StartsWith("Microsoft.Build", StringComparison.OrdinalIgnoreCase) ||
               assemblyName.StartsWith("Microsoft.CodeAnalysis", StringComparison.OrdinalIgnoreCase) ||
               assemblyName.Equals("MSBuildLocator", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Environment detection for SDK version and assembly loading strategy
/// </summary>
public static class SdkVersionDetector
{
    private static ILogger? _logger;

    public static void InitializeLogger(ILogger logger)
    {
        _logger = logger;
    }

    public static SdkEnvironment DetectEnvironment()
    {
        var runtimeVersion = Environment.Version;
        var isNet10Preview = runtimeVersion.Major >= 10;
        
        _logger?.LogInformation("Detected runtime: {RuntimeVersion}, IsNet10Preview: {IsNet10Preview}", 
            runtimeVersion, isNet10Preview);

        // Force assembly loading context for .NET 10+ to prevent version conflicts
        if (isNet10Preview)
        {
            return new SdkEnvironment 
            { 
                UseCustomAssemblyContext = true,
                TargetFramework = "net9.0", // Force target regardless of runtime
                ForceMsBuildVersion = "17.7.2",
                Reason = ".NET 10+ preview detected - using custom assembly context to prevent MSBuild version conflicts"
            };
        }

        return new SdkEnvironment 
        { 
            UseCustomAssemblyContext = false,
            TargetFramework = runtimeVersion.Major >= 9 ? "net9.0" : $"net{runtimeVersion.Major}.0",
            ForceMsBuildVersion = null,
            Reason = "Using default assembly context"
        };
    }
}

/// <summary>
/// Configuration for SDK environment and assembly loading
/// </summary>
public class SdkEnvironment
{
    public bool UseCustomAssemblyContext { get; init; }
    public string TargetFramework { get; init; } = string.Empty;
    public string? ForceMsBuildVersion { get; init; }
    public string Reason { get; init; } = string.Empty;
}
