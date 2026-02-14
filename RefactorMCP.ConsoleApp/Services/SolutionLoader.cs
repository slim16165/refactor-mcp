using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;

namespace RefactorMCP.ConsoleApp.Services;

/// <summary>
/// Robust solution loader with timeout, retry, and progress reporting
/// </summary>
public class SolutionLoader
{
    private readonly ILogger<SolutionLoader> _logger;
    private readonly RetryPolicy _retryPolicy;
    private readonly SolutionLoaderConfiguration _configuration;
    private readonly RefactorAssemblyContext? _customAssemblyContext;

    public SolutionLoader(
        ILogger<SolutionLoader> logger,
        RetryPolicy retryPolicy,
        SolutionLoaderConfiguration configuration,
        RefactorAssemblyContext? customAssemblyContext = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _retryPolicy = retryPolicy ?? throw new ArgumentNullException(nameof(retryPolicy));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _customAssemblyContext = customAssemblyContext;
    }

    /// <summary>
    /// Loads a solution with timeout, retry, and progress reporting
    /// </summary>
    public async Task<Solution> LoadSolutionAsync(
        string solutionPath, 
        IProgress<SolutionLoadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return await _retryPolicy.ExecuteWithRetryAsync(
            () => LoadSolutionInternalAsync(solutionPath, progress, cancellationToken),
            $"LoadSolution({Path.GetFileName(solutionPath)})",
            cancellationToken
        );
    }

    private async Task<Solution> LoadSolutionInternalAsync(
        string solutionPath,
        IProgress<SolutionLoadProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(solutionPath))
        {
            throw new FileNotFoundException($"Solution file not found: {solutionPath}");
        }

        var absoluteSolutionPath = Path.GetFullPath(solutionPath);
        var solutionDirectory = Path.GetDirectoryName(absoluteSolutionPath) ?? ".";

        _logger.LogInformation("Loading solution: {SolutionPath}", absoluteSolutionPath);

        // Create timeout token
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_configuration.Timeout);

        try
        {
            progress?.Report(new SolutionLoadProgress 
            { 
                Phase = "Creating workspace", 
                PercentComplete = 5,
                Details = "Initializing MSBuild workspace"
            });

            // Create workspace with custom assembly context if available
            MSBuildWorkspace workspace;
            if (_customAssemblyContext != null)
            {
                _logger.LogDebug("Using custom assembly context for workspace creation");
                workspace = MSBuildWorkspace.Create();
            }
            else
            {
                workspace = MSBuildWorkspace.Create();
            }

            // Configure workspace properties
            workspace.SkipUnrecognizedProjects = true;
            workspace.LoadMetadataForReferencedProjects = false;

            progress?.Report(new SolutionLoadProgress 
            { 
                Phase = "Opening solution", 
                PercentComplete = 20,
                Details = $"Loading {Path.GetFileName(solutionPath)}"
            });

            // Load solution with timeout
            var solution = await workspace.OpenSolutionAsync(absoluteSolutionPath, cancellationToken: timeoutCts.Token);

            progress?.Report(new SolutionLoadProgress 
            { 
                Phase = "Validating solution", 
                PercentComplete = 70,
                Details = "Checking solution integrity"
            });

            // Validate solution loaded correctly
            if (_configuration.ValidateSolutionAfterLoad)
            {
                await ValidateSolutionAsync(solution, timeoutCts.Token);
            }

            var projectCount = solution.ProjectIds.Count;
            var documentCount = await Task.WhenAll(solution.ProjectIds.Select(async projectId => 
            {
                try
                {
                    var project = solution.GetProject(projectId);
                    return await Task.FromResult(project?.DocumentIds.Count ?? 0);
                }
                catch
                {
                    return await Task.FromResult(0);
                }
            }));

            var totalDocuments = documentCount.Sum();

            progress?.Report(new SolutionLoadProgress 
            { 
                Phase = "Complete", 
                PercentComplete = 100,
                Details = $"Loaded {projectCount} projects with {totalDocuments} documents"
            });

            _logger.LogInformation("Solution loaded successfully: {ProjectCount} projects, {DocumentCount} documents in {Duration}ms", 
                projectCount, totalDocuments, timeoutCts.Token.IsCancellationRequested ? "TIMEOUT" : "SUCCESS");

            return solution;
        }
        catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested)
        {
            var timeoutMessage = $"LoadSolution timed out after {_configuration.Timeout.TotalSeconds}s for {solutionPath}";
            _logger.LogError(timeoutMessage);
            throw new TimeoutException(timeoutMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load solution: {SolutionPath}", solutionPath);
            throw;
        }
    }

    private async Task ValidateSolutionAsync(Solution solution, CancellationToken cancellationToken)
    {
        // Basic validation checks
        if (solution.ProjectIds.Count == 0)
        {
            throw new InvalidOperationException("Solution loaded but contains no projects");
        }

        // Check if we can access at least one project
        var firstProjectId = solution.ProjectIds.First();
        var firstProject = solution.GetProject(firstProjectId);
        
        if (firstProject == null)
        {
            throw new InvalidOperationException("Failed to access first project in solution");
        }

        // Try to access project information
        try
        {
            _ = firstProject.Name;
            _ = firstProject.FilePath;
            
            // Async validation
            await Task.Delay(10, cancellationToken); // Small delay to ensure async context
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Solution validation failed: {ex.Message}", ex);
        }

        _logger.LogDebug("Solution validation passed: {ProjectCount} projects", solution.ProjectIds.Count);
    }

    /// <summary>
    /// Gets solution information without loading the full solution
    /// </summary>
    public async Task<SolutionInfo> GetSolutionInfoAsync(string solutionPath, CancellationToken cancellationToken = default)
    {
        return await _retryPolicy.ExecuteWithRetryAsync(
            () => GetSolutionInfoInternalAsync(solutionPath, cancellationToken),
            $"GetSolutionInfo({Path.GetFileName(solutionPath)})",
            cancellationToken
        );
    }

    private async Task<SolutionInfo> GetSolutionInfoInternalAsync(string solutionPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(solutionPath))
        {
            throw new FileNotFoundException($"Solution file not found: {solutionPath}");
        }

        try
        {
            // Read solution file to get basic info without full workspace load
            var solutionContent = await File.ReadAllTextAsync(solutionPath, cancellationToken);
            var lines = solutionContent.Split('\n', '\r');
            
            var projectCount = lines.Count(line => line.Contains("Project("));
            var solutionName = Path.GetFileNameWithoutExtension(solutionPath);
            
            return new SolutionInfo
            {
                Name = solutionName,
                Path = solutionPath,
                ProjectCount = projectCount,
                FileSize = new FileInfo(solutionPath).Length,
                LastModified = File.GetLastWriteTime(solutionPath)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get solution info for: {SolutionPath}", solutionPath);
            throw;
        }
    }
}

/// <summary>
/// Basic information about a solution
/// </summary>
public class SolutionInfo
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public int ProjectCount { get; set; }
    public long FileSize { get; set; }
    public DateTime LastModified { get; set; }
}
