using ModelContextProtocol.Server;
using ModelContextProtocol;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Caching.Memory;
using System.ComponentModel;
using System.IO;
using System.Collections.Generic;
using System.Threading;


[McpServerToolType]
public static class LoadSolutionTool
{
    [McpServerTool, Description("Start a new session by clearing caches then load a solution file and set the current directory")]
    public static async Task<string> LoadSolution(
        [Description("Absolute path to the solution file (.sln)")] string solutionPath,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        bool completed = false;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            ToolCallLogger.Log(nameof(LoadSolution), new Dictionary<string, string?> { ["status"] = "Starting", ["path"] = solutionPath });
            progress?.Report($"[0ms] Starting LoadSolution for {solutionPath}...");

            if (!File.Exists(solutionPath))
            {
                throw new McpException($"Error: Solution file not found at {solutionPath}");
            }

            RefactoringHelpers.ClearAllCaches();
            MoveMethodTool.ResetMoveHistory();

            var logDir = Path.Combine(Path.GetDirectoryName(solutionPath)!, ".refactor-mcp");
            ToolCallLogger.SetLogDirectory(logDir);
            
            // Re-log with the new directory set
            ToolCallLogger.Log(nameof(LoadSolution), new Dictionary<string, string?> { ["status"] = "LogDirectorySet", ["path"] = logDir });

            Directory.SetCurrentDirectory(Path.GetDirectoryName(solutionPath)!);
            progress?.Report($"[{sw.ElapsedMilliseconds}ms] Working directory set. Checking cache...");

            if (RefactoringHelpers.SolutionCache.TryGetValue(solutionPath, out SolutionSession? session))
            {
                var cachedProjects = session!.CurrentSolution.Projects.Select(p => p.Name).ToList();
                var msg = $"Successfully loaded solution '{Path.GetFileName(solutionPath)}' from cache with {cachedProjects.Count} projects. Duration: {sw.ElapsedMilliseconds}ms";
                ToolCallLogger.Log(nameof(LoadSolution), new Dictionary<string, string?> { ["status"] = "Success (Cache)", ["durationMs"] = sw.ElapsedMilliseconds.ToString() });
                completed = true;
                return msg;
            }

            progress?.Report($"[{sw.ElapsedMilliseconds}ms] Cache miss. Creating workspace...");
            var workspace = RefactoringHelpers.CreateWorkspace();
            ToolCallLogger.Log(nameof(LoadSolution), new Dictionary<string, string?> { ["status"] = "WorkspaceCreated", ["elapsedMs"] = sw.ElapsedMilliseconds.ToString() });
            
            progress?.Report($"[{sw.ElapsedMilliseconds}ms] Opening solution (this may take a while)...");
            var solution = await workspace.OpenSolutionAsync(solutionPath, progress: null, cancellationToken);
            ToolCallLogger.Log(nameof(LoadSolution), new Dictionary<string, string?> { ["status"] = "SolutionOpened", ["elapsedMs"] = sw.ElapsedMilliseconds.ToString() });
            
            var newSession = new SolutionSession(workspace, solution, solutionPath);
            RefactoringHelpers.SolutionCache.Set(solutionPath, newSession);

            var metricsDir = Path.Combine(Path.GetDirectoryName(solutionPath)!, ".refactor-mcp", "metrics");
            Directory.CreateDirectory(metricsDir);

            var projects = solution.Projects.Select(p => p.Name).ToList();
            var message = $"Successfully loaded solution '{Path.GetFileName(solutionPath)}' with {projects.Count} projects. Duration: {sw.ElapsedMilliseconds}ms";
            
            progress?.Report(message);
            ToolCallLogger.Log(nameof(LoadSolution), new Dictionary<string, string?> { ["status"] = "Success", ["durationMs"] = sw.ElapsedMilliseconds.ToString(), ["projectCount"] = projects.Count.ToString() });
            completed = true;
            return message;
        }
        catch (Exception ex)
        {
            ToolCallLogger.Log(nameof(LoadSolution), new Dictionary<string, string?> { ["status"] = "Error", ["error"] = ex.ToString() });
            completed = true;
            throw new McpException($"Error loading solution: {ex.Message}", ex);
        }
        finally
        {
            if (!completed)
            {
                 ToolCallLogger.Log(nameof(LoadSolution), new Dictionary<string, string?> { ["status"] = "Stopped/Unknown", ["durationMs"] = sw.ElapsedMilliseconds.ToString() });
            }
        }
    }
}
