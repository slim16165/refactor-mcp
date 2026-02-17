using Microsoft.CodeAnalysis;
using RefactorMCP.ConsoleApp.Tools;
using RefactorMCP.Tests.Infrastructure;
using Xunit.Abstractions;

namespace RefactorMCP.Tests.Tools;

/// <summary>
/// Snapshot tests for IntroduceVariable tool
/// </summary>
public class IntroduceVariableSnapshotTests : TestBase
{
    private readonly SnapshotTestHarness _harness;

    public IntroduceVariableSnapshotTests(ITestOutputHelper output)
        : base()
    {
        _harness = new SnapshotTestHarness(output, nameof(IntroduceVariableSnapshotTests));
    }

    [Fact]
    public async Task IntroduceVariable_SimpleExpression_CreatesSnapshot()
    {
        await using var workspace = await SnapshotTestHarness.CreateIsolatedWorkspaceAsync(
            "TestProject",
            ("Program.cs", @"
using System;

class Program
{
    static void Main()
    {
        Console.WriteLine(""Hello "" + ""World"");
    }
}
"));

        await _harness.VerifyRefactoringAsync(
            () => IntroduceVariableTool.IntroduceVariable(
                workspace.SolutionFile,
                Path.Combine(workspace.WorkspaceDirectory, "Program.cs"),
                "4:26-4:41", // "Hello " + "World""
                "greeting"),
            "SimpleExpression");
    }

    [Fact]
    public async Task IntroduceVariable_ComplexExpression_CreatesSnapshot()
    {
        await using var workspace = await SnapshotTestHarness.CreateIsolatedWorkspaceAsync(
            "TestProject",
            ("Calculator.cs", @"
public class Calculator
{
    public int Calculate(int a, int b)
    {
        return (a * b) + (a + b) / 2;
    }
}
"));

        await _harness.VerifyRefactoringAsync(
            () => IntroduceVariableTool.IntroduceVariable(
                workspace.SolutionFile,
                Path.Combine(workspace.WorkspaceDirectory, "Calculator.cs"),
                "5:16-5:37", // "(a * b) + (a + b) / 2"
                "result"),
            "ComplexExpression");
    }

    [Fact]
    public async Task IntroduceVariable_MultipleFiles_CreatesSnapshot()
    {
        await using var workspace = await SnapshotTestHarness.CreateIsolatedWorkspaceAsync(
            "TestProject",
            ("Utils.cs", @"
public static class Utils
{
    public static string FormatMessage(string message)
    {
        return message.Trim().ToUpper();
    }
}
"),
            ("Program.cs", @"
using System;

class Program
{
    static void Main()
    {
        var formatted = Utils.FormatMessage(""  hello world  "");
        Console.WriteLine(formatted);
    }
}
"));

        await _harness.VerifyRefactoringAsync(
            () => IntroduceVariableTool.IntroduceVariable(
                workspace.SolutionFile,
                Path.Combine(workspace.WorkspaceDirectory, "Program.cs"),
                "6:33-6:48", // "Utils.FormatMessage(...)"
                "cleanMessage"),
            "MultipleFiles");
    }

    [Fact]
    public async Task IntroduceVariable_ErrorCases_CreatesSnapshot()
    {
        await using var workspace = await SnapshotTestHarness.CreateIsolatedWorkspaceAsync(
            "TestProject",
            ("Program.cs", @"
class Program
{
    static void Main()
    {
        int x = 42;
    }
}
"));

        await _harness.VerifyRefactoringAsync(
            () => IntroduceVariableTool.IntroduceVariable(
                workspace.SolutionFile,
                Path.Combine(workspace.WorkspaceDirectory, "Program.cs"),
                "4:15-4:16", // "42" - valid range but not a complex expression
                "value"),
            "ErrorCases");
    }

    [Fact]
    public async Task IntroduceVariable_InvalidRange_CreatesSnapshot()
    {
        await using var workspace = await SnapshotTestHarness.CreateIsolatedWorkspaceAsync(
            "TestProject",
            ("Program.cs", @"
class Program
{
    static void Main()
    {
        Console.WriteLine();
    }
}
"));

        await _harness.VerifyRefactoringAsync(
            () => IntroduceVariableTool.IntroduceVariable(
                workspace.SolutionFile,
                Path.Combine(workspace.WorkspaceDirectory, "Program.cs"),
                "10:1-10:2", // Out of bounds
                "invalid"),
            "InvalidRange");
    }
}
