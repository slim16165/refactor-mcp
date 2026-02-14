using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.Extensions.Logging;
using RefactorMCP.ConsoleApp.Services;

namespace RefactorMCP.ConsoleApp.Infrastructure;

internal sealed class SolutionSession : IDisposable
{
    public MSBuildWorkspace Workspace { get; }
    public string SolutionPath { get; }
    public Solution CurrentSolution { get; set; }

    public SolutionSession(MSBuildWorkspace workspace, Solution solution, string solutionPath)
    {
        Workspace = workspace;
        CurrentSolution = solution;
        SolutionPath = solutionPath;
    }

    public void Dispose() => Workspace.Dispose();
}

internal static class SolutionService
{
    private static MemoryCache SolutionCache = new(new MemoryCacheOptions());
    private static SolutionLoader? _loader;
    private static ILogger? _logger;

    public static void Initialize(SolutionLoader loader, ILogger logger)
    {
        _loader = loader;
        _logger = logger;
    }

    public static MemoryCache GetSolutionCache() => SolutionCache;

    public static void ClearAllCaches()
    {
        SolutionCache.Dispose();
        SolutionCache = new(new MemoryCacheOptions());
    }

    public static MSBuildWorkspace CreateWorkspace()
    {
        var host = MefHostServices.Create(MSBuildMefHostServices.DefaultAssemblies);
        var workspace = MSBuildWorkspace.Create(host);
        workspace.WorkspaceFailed += (_, e) =>
        {
            var msg = e.Diagnostic.Message;
            _logger?.LogWarning("[WSF001] Workspace Diagnostic: {Msg}", msg);
        };
        return workspace;
    }

    public static async Task<Solution> GetOrLoadSolution(string solutionPath, CancellationToken cancellationToken = default)
    {
        if (SolutionCache.TryGetValue(solutionPath, out SolutionSession? session))
        {
            return session!.CurrentSolution;
        }

        Solution solution;
        MSBuildWorkspace? workspace = null;

        if (_loader != null)
        {
            solution = await _loader.LoadSolutionAsync(solutionPath, null, cancellationToken);
            // Note: Since we don't have direct access to the workspace from SolutionLoader easily 
            // (it might be internal or disposed), we might have issues if we need to update the solution.
            // But Solution objects contain the Workspace reference usually if loaded via MSBuildWorkspace.
            if (solution.Workspace is MSBuildWorkspace mbw)
            {
                workspace = mbw;
            }
        }
        else
        {
            workspace = CreateWorkspace();
            solution = await workspace.OpenSolutionAsync(solutionPath, progress: null, cancellationToken);
        }
        
        if (workspace != null)
        {
            var newSession = new SolutionSession(workspace, solution, solutionPath);
            var cacheEntryOptions = new MemoryCacheEntryOptions()
                .RegisterPostEvictionCallback((key, value, reason, state) =>
                {
                    if (value is IDisposable disposable) disposable.Dispose();
                });

            SolutionCache.Set(solutionPath, newSession, cacheEntryOptions);
        }
        
        return solution;
    }

    public static void UpdateSolutionCache(Document updatedDocument)
    {
        var solutionPath = updatedDocument.Project.Solution.FilePath;
        if (!string.IsNullOrEmpty(solutionPath))
        {
            if (SolutionCache.TryGetValue(solutionPath, out SolutionSession? session))
            {
                session!.CurrentSolution = updatedDocument.Project.Solution;
            }

            if (!string.IsNullOrEmpty(updatedDocument.FilePath))
            {
                _ = MetricsProvider.RefreshFileMetrics(solutionPath!, updatedDocument.FilePath!);
            }
        }
    }

    public static Document? GetDocumentByPath(Solution solution, string filePath)
    {
        var normalizedPath = Path.GetFullPath(filePath);
        return solution.Projects
            .SelectMany(p => p.Documents)
            .FirstOrDefault(d => Path.GetFullPath(d.FilePath ?? "") == normalizedPath);
    }
}
