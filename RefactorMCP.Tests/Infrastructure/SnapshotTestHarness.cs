using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System.Text.Json;
using Xunit.Abstractions;

namespace RefactorMCP.Tests.Infrastructure;

/// <summary>
/// Snapshot testing infrastructure for reliable refactoring tests
/// </summary>
public class SnapshotTestHarness
{
    private readonly ITestOutputHelper _output;
    private readonly string _snapshotDirectory;

    public SnapshotTestHarness(ITestOutputHelper output, string testClassName)
    {
        _output = output;
        _snapshotDirectory = Path.Combine(
            Path.GetDirectoryName(typeof(SnapshotTestHarness).Assembly.Location)!,
            "Snapshots",
            testClassName);
        
        Directory.CreateDirectory(_snapshotDirectory);
    }

    /// <summary>
    /// Execute refactoring and compare with snapshot
    /// </summary>
    public async Task VerifyRefactoringAsync(
        Func<Task<string>> refactoringOperation,
        string snapshotName,
        string? customSnapshotDirectory = null)
    {
        // Execute refactoring
        var result = await refactoringOperation();
        
        // Parse result as structured JSON if possible
        var isStructured = TryParseStructuredResult(result, out var structured);
        
        // Get snapshot file path
        var snapshotDir = customSnapshotDirectory ?? _snapshotDirectory;
        var snapshotFile = Path.Combine(snapshotDir, $"{snapshotName}.snapshot.json");
        
        // Load or create snapshot
        if (File.Exists(snapshotFile))
        {
            var expected = await File.ReadAllTextAsync(snapshotFile);
            
            if (isStructured)
            {
                VerifyStructuredSnapshot(expected, structured!, snapshotFile);
            }
            else
            {
                VerifyTextSnapshot(expected, result, snapshotFile);
            }
        }
        else
        {
            // Create new snapshot
            await File.WriteAllTextAsync(snapshotFile, result);
            _output.WriteLine($"Created new snapshot: {snapshotFile}");
            
            // Fail to encourage snapshot review
            Assert.True(false, $"New snapshot created at {snapshotFile}. Review and re-run test.");
        }
    }

    /// <summary>
    /// Update snapshot (for when refactoring behavior intentionally changes)
    /// </summary>
    public async Task UpdateSnapshotAsync(
        Func<Task<string>> refactoringOperation,
        string snapshotName)
    {
        var result = await refactoringOperation();
        var snapshotFile = Path.Combine(_snapshotDirectory, $"{snapshotName}.snapshot.json");
        await File.WriteAllTextAsync(snapshotFile, result);
        _output.WriteLine($"Updated snapshot: {snapshotFile}");
    }

    /// <summary>
    /// Create isolated test workspace
    /// </summary>
    public static async Task<TestWorkspace> CreateIsolatedWorkspaceAsync(
        string projectName,
        params (string fileName, string content)[] files)
    {
        var workspaceDir = Path.Combine(Path.GetTempPath(), $"RefactorTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspaceDir);

        // Create project file
        var projectFile = Path.Combine(workspaceDir, $"{projectName}.csproj");
        await File.WriteAllTextAsync(projectFile, $@"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>");

        // Create source files
        foreach (var (fileName, content) in files)
        {
            var filePath = Path.Combine(workspaceDir, fileName);
            await File.WriteAllTextAsync(filePath, content);
        }

        // Create solution
        var solutionFile = Path.Combine(workspaceDir, $"{projectName}.sln");
        var solutionContent = $@"
Microsoft Visual Studio Solution File, Format Version 12.00
# Visual Studio Version 17
VisualStudioVersion = 17.0.31903.59
MinimumVisualStudioVersion = 10.0.40219.1

Project(""{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}"") = ""{projectName}"", ""{projectFile}"", ""{{GUID}}""
EndProject

Global
    GlobalSection(SolutionConfigurationPlatforms) = preSolution
        Debug|Any CPU = Debug|Any CPU
        Release|Any CPU = Release|Any CPU
    EndGlobalSection
    GlobalSection(ProjectConfigurationPlatforms) = postSolution
        {{{GUID}}}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
        {{{GUID}}}.Debug|Any CPU.Build.0 = Debug|Any CPU
        {{{GUID}}}.Release|Any CPU.ActiveCfg = Release|Any CPU
        {{{GUID}}}.Release|Any CPU.Build.0 = Release|Any CPU
    EndGlobalSection
EndGlobal";

        await File.WriteAllTextAsync(solutionFile, solutionContent.Replace("{GUID}", Guid.NewGuid().ToString("B").ToUpperInvariant()));

        return new TestWorkspace(workspaceDir, solutionFile);
    }

    private static bool TryParseStructuredResult(string result, out JsonElement? structured)
    {
        try
        {
            using var document = JsonDocument.Parse(result);
            structured = document.RootElement;
            return true;
        }
        catch
        {
            structured = null;
            return false;
        }
    }

    private static void VerifyStructuredSnapshot(string expected, JsonElement actual, string snapshotFile)
    {
        using var expectedDoc = JsonDocument.Parse(expected);
        var expectedElement = expectedDoc.RootElement;
        
        if (JsonElement.DeepEquals(expectedElement, actual))
            return;

        // Detailed comparison for structured results
        var differences = FindJsonDifferences(expectedElement, actual);
        var message = $"Snapshot mismatch in {snapshotFile}:\n{string.Join("\n", differences)}";
        
        Assert.True(false, message);
    }

    private static void VerifyTextSnapshot(string expected, string actual, string snapshotFile)
    {
        var normalizedExpected = NormalizeLineEndings(expected);
        var normalizedActual = NormalizeLineEndings(actual);
        
        if (normalizedExpected == normalizedActual)
            return;

        // Show diff for text results
        var message = $"Snapshot mismatch in {snapshotFile}.\nExpected:\n{normalizedExpected}\n\nActual:\n{normalizedActual}";
        Assert.True(false, message);
    }

    private static string NormalizeLineEndings(string text)
    {
        return text.Replace("\r\n", "\n").Replace('\r', '\n');
    }

    private static List<string> FindJsonDifferences(JsonElement expected, JsonElement actual)
    {
        var differences = new List<string>();
        
        if (expected.ValueKind != actual.ValueKind)
        {
            differences.Add($"Type mismatch: expected {expected.ValueKind}, got {actual.ValueKind}");
            return differences;
        }

        switch (expected.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in expected.EnumerateObject())
                {
                    if (!actual.TryGetProperty(property.Name, out var actualProperty))
                    {
                        differences.Add($"Missing property: {property.Name}");
                        continue;
                    }
                    
                    var propDifferences = FindJsonDifferences(property.Value, actualProperty);
                    differences.AddRange(propDifferences.Select(d => $"  {property.Name}.{d}"));
                }
                
                // Check for extra properties
                foreach (var property in actual.EnumerateObject())
                {
                    if (!expected.TryGetProperty(property.Name, out _))
                    {
                        differences.Add($"Extra property: {property.Name}");
                    }
                }
                break;
                
            case JsonValueKind.Array:
                var expectedArray = expected.EnumerateArray().ToList();
                var actualArray = actual.EnumerateArray().ToList();
                
                if (expectedArray.Count != actualArray.Count)
                {
                    differences.Add($"Array length mismatch: expected {expectedArray.Count}, got {actualArray.Count}");
                }
                
                for (int i = 0; i < Math.Min(expectedArray.Count, actualArray.Count); i++)
                {
                    var itemDifferences = FindJsonDifferences(expectedArray[i], actualArray[i]);
                    differences.AddRange(itemDifferences.Select(d => $"  [{i}].{d}"));
                }
                break;
                
            case JsonValueKind.String:
                var expectedStr = expected.GetString() ?? "";
                var actualStr = actual.GetString() ?? "";
                if (expectedStr != actualStr)
                {
                    differences.Add($"String mismatch: expected '{expectedStr}', got '{actualStr}'");
                }
                break;
                
            case JsonValueKind.Number:
                if (expected.GetDecimal() != actual.GetDecimal())
                {
                    differences.Add($"Number mismatch: expected {expected.GetDecimal()}, got {actual.GetDecimal()}");
                }
                break;
                
            case JsonValueKind.True:
            case JsonValueKind.False:
                if (expected.GetBoolean() != actual.GetBoolean())
                {
                    differences.Add($"Boolean mismatch: expected {expected.GetBoolean()}, got {actual.GetBoolean()}");
                }
                break;
        }

        return differences;
    }
}

/// <summary>
/// Isolated test workspace for snapshot testing
/// </summary>
public class TestWorkspace : IDisposable
{
    public string WorkspaceDirectory { get; }
    public string SolutionFile { get; }

    public TestWorkspace(string workspaceDirectory, string solutionFile)
    {
        WorkspaceDirectory = workspaceDirectory;
        SolutionFile = solutionFile;
    }

    public void Dispose()
    {
        if (Directory.Exists(WorkspaceDirectory))
        {
            try
            {
                Directory.Delete(WorkspaceDirectory, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }
}

/// <summary>
/// Extension methods for JsonElement comparison
/// </summary>
public static class JsonElementExtensions
{
    public static bool DeepEquals(this JsonElement left, JsonElement right)
    {
        if (left.ValueKind != right.ValueKind)
            return false;

        return left.ValueKind switch
        {
            JsonValueKind.Object => left.EnumerateObject().All(prop => 
                right.TryGetProperty(prop.Name, out var rightProp) && 
                DeepEquals(prop.Value, rightProp)),
                
            JsonValueKind.Array => left.EnumerateArray().Count() == right.EnumerateArray().Count() &&
                left.EnumerateArray().Zip(right.EnumerateArray(), (l, r) => DeepEquals(l, r)).All(b => b),
                
            JsonValueKind.String => left.GetString() == right.GetString(),
            JsonValueKind.Number => left.GetDecimal() == right.GetDecimal(),
            JsonValueKind.True => left.GetBoolean() == right.GetBoolean(),
            JsonValueKind.False => left.GetBoolean() == right.GetBoolean(),
            JsonValueKind.Null => true,
            
            _ => false
        };
    }
}
