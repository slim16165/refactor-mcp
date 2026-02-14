using Microsoft.Build.Locator;

[McpServerToolType]
public static class DiagnosticTools
{
    [McpServerTool, Description("Diagnose the runtime environment, MSBuild registration, and loaded assemblies.")]
    public static string DiagnoseEnvironment()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Environment Diagnosis");
        sb.AppendLine($"- **Runtime**: {Environment.Version}");
        sb.AppendLine($"- **OS**: {Environment.OSVersion}");
        sb.AppendLine($"- **Process Architecture**: {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");
        sb.AppendLine($"- **Working Directory**: {Environment.CurrentDirectory}");
        
        sb.AppendLine("\n## MSBuild Locator");
        sb.AppendLine($"- **Is Registered**: {MSBuildLocator.IsRegistered}");
        if (MSBuildLocator.IsRegistered)
        {
            try 
            {
                var instances = MSBuildLocator.QueryVisualStudioInstances();
                sb.AppendLine("- **Detected Instances**:");
                foreach (var i in instances)
                {
                    sb.AppendLine($"  - {i.Name} ({i.Version}) @ {i.MSBuildPath} [{i.DiscoveryType}]");
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"- *Error querying instances: {ex.Message}*");
            }
        }

        sb.AppendLine("\n## Loaded Assemblies (Key Components)");
        var assemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => {
                var name = a.GetName().Name ?? "";
                return name.Contains("CodeAnalysis") || 
                       name.Contains("Build") || 
                       name.Contains("ModelContextProtocol") ||
                       name.Contains("RefactorMCP");
            })
            .OrderBy(a => a.GetName().Name);

        foreach (var a in assemblies)
        {
            var name = a.GetName();
            var location = "unknown";
            try { location = a.Location; } catch { }
            sb.AppendLine($"- **{name.Name}**: {name.Version} (Location: {location})");
        }

        return sb.ToString();
    }
}
