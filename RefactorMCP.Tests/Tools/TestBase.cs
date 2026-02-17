using System;
using System.IO;

namespace RefactorMCP.Tests;

public abstract class TestBase : IDisposable
{
    protected static readonly string SolutionPath = TestUtilities.GetSolutionPath();
    protected static readonly string ExampleFilePath = Path.Combine(Path.GetDirectoryName(SolutionPath)!, "RefactorMCP.Tests", "Tools", "ExampleCode.cs");
    private static readonly string TestOutputRoot =
        Path.Combine(Path.GetDirectoryName(SolutionPath)!, "RefactorMCP.Tests", "TestOutput");

    protected string TestOutputPath { get; }

    static TestBase()
    {
        // Disable file logging during tests to prevent file locking issues
        Environment.SetEnvironmentVariable("REFACTOR_MCP_DISABLE_LOGGING", "true");
    }

    /// <summary>
    /// Normalizes line endings for consistent test assertions across platforms
    /// </summary>
    protected static string NormalizeLineEndings(string text) => text.Replace("\r\n", "\n");

    protected TestBase()
    {
        Directory.CreateDirectory(TestOutputRoot);
        TestOutputPath = Path.Combine(TestOutputRoot, Guid.NewGuid().ToString());
        Directory.CreateDirectory(TestOutputPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(TestOutputPath))
            Directory.Delete(TestOutputPath, true);
    }
}
