using Xunit;
using System.Threading.Tasks;

namespace RefactorMCP.Tests.Tools;

public class RenameSymbolAmbiguityTests : TestBase
{
    [Fact]
    public async Task RenameSymbol_AmbiguousFieldAndLocal_RequiresExplicitPosition()
    {
        const string testCode = """
public class TestClass
{
    private int value = 42; // Field

    public void Method()
    {
        int value = 123; // Local variable
        value = 456; // Ambiguous reference
    }
}
""";

        var testFile = Path.Combine(TestOutputPath, "AmbiguityTest.cs");
        await File.WriteAllTextAsync(testFile, testCode);

        // Try to rename without explicit position - should fail with ambiguity error
        var result = await RenameSymbolTool.RenameSymbol(
            SolutionPath,
            testFile,
            "value", // name only, no position
            "newValue");

        Assert.Contains("ambiguous", result.ToLower());
        Assert.Contains("line and column", result.ToLower());
    }

    [Fact]
    public async Task RenameSymbol_ParameterShadowingField_RequiresExplicitPosition()
    {
        const string testCode = """
public class TestClass
{
    private string name = "field"; // Field

    public void Method(string name) // Parameter
    {
        name = "parameter"; // Ambiguous reference
    }
}
""";

        var testFile = Path.Combine(TestOutputPath, "ShadowingTest.cs");
        await File.WriteAllTextAsync(testFile, testCode);

        // Try to rename without explicit position - should fail with ambiguity error
        var result = await RenameSymbolTool.RenameSymbol(
            SolutionPath,
            testFile,
            "name", // name only, no position
            "newName");

        Assert.Contains("ambiguous", result.ToLower());
        Assert.Contains("line and column", result.ToLower());
    }

    [Fact]
    public async Task RenameSymbol_WithExplicitPosition_ResolvesAmbiguity()
    {
        const string testCode = """
public class TestClass
{
    private int value = 42; // Field

    public void Method()
    {
        int value = 123; // Local variable
        value = 456; // Line 7, column 9 - should target local
    }
}
""";

        var testFile = Path.Combine(TestOutputPath, "ExplicitPositionTest.cs");
        await File.WriteAllTextAsync(testFile, testCode);

        // Rename with explicit position targeting the local variable
        var result = await RenameSymbolTool.RenameSymbol(
            SolutionPath,
            testFile,
            "value",
            "newValue",
            7, 9); // Line 7, column 9 - targets local variable

        Assert.Contains("Successfully renamed", result);
        
        var fileContent = await File.ReadAllTextAsync(testFile);
        Assert.Contains("int newValue = 123", fileContent);
        Assert.Contains("newValue = 456", fileContent);
        Assert.Contains("private int value = 42", fileContent); // Field unchanged
    }
}
