using System;
using System.IO;
using Microsoft.CodeAnalysis.Text;
using ModelContextProtocol;
using RefactorMCP.ConsoleApp.Infrastructure;
using Xunit;

namespace RefactorMCP.Tests.Infrastructure;

public class ToolParameterValidatorTests
{
    // --- ValidateSolutionPath ---

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateSolutionPath_NullOrEmpty_Throws(string? path)
    {
        var ex = Assert.Throws<McpException>(() => ToolParameterValidator.ValidateSolutionPath(path!));
        Assert.Contains("solutionPath is required", ex.Message);
    }

    [Fact]
    public void ValidateSolutionPath_WrongExtension_Throws()
    {
        var ex = Assert.Throws<McpException>(() => ToolParameterValidator.ValidateSolutionPath("project.csproj"));
        Assert.Contains("must be a .sln file", ex.Message);
    }

    [Fact]
    public void ValidateSolutionPath_FileNotFound_Throws()
    {
        var ex = Assert.Throws<McpException>(() => ToolParameterValidator.ValidateSolutionPath(@"C:\nonexistent\fake.sln"));
        Assert.Contains("Solution file not found", ex.Message);
    }

    [Fact]
    public void ValidateSolutionPath_ValidPath_ReturnsFullPath()
    {
        var solutionPath = TestUtilities.GetSolutionPath();
        var result = ToolParameterValidator.ValidateSolutionPath(solutionPath);
        Assert.Equal(Path.GetFullPath(solutionPath), result);
    }

    // --- ValidateFilePath ---

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateFilePath_NullOrEmpty_Throws(string? path)
    {
        var ex = Assert.Throws<McpException>(() => ToolParameterValidator.ValidateFilePath(path!));
        Assert.Contains("filePath is required", ex.Message);
    }

    [Fact]
    public void ValidateFilePath_CustomParameterName_UsedInMessage()
    {
        var ex = Assert.Throws<McpException>(() => ToolParameterValidator.ValidateFilePath("", "targetFilePath"));
        Assert.Contains("targetFilePath is required", ex.Message);
    }

    [Fact]
    public void ValidateFilePath_FileNotFound_Throws()
    {
        var ex = Assert.Throws<McpException>(() => ToolParameterValidator.ValidateFilePath(@"C:\nonexistent\file.cs"));
        Assert.Contains("File not found", ex.Message);
    }

    [Fact]
    public void ValidateFilePath_ExistingFile_ReturnsFullPath()
    {
        var solutionPath = TestUtilities.GetSolutionPath();
        var result = ToolParameterValidator.ValidateFilePath(solutionPath);
        Assert.Equal(Path.GetFullPath(solutionPath), result);
    }

    // --- ValidateRequiredString ---

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateRequiredString_NullOrEmpty_Throws(string? value)
    {
        var ex = Assert.Throws<McpException>(() => ToolParameterValidator.ValidateRequiredString(value!, "fieldName"));
        Assert.Contains("fieldName is required", ex.Message);
    }

    [Fact]
    public void ValidateRequiredString_ValidString_DoesNotThrow()
    {
        ToolParameterValidator.ValidateRequiredString("MyClass", "sourceClass");
    }

    // --- ValidateStringArray ---

    [Fact]
    public void ValidateStringArray_Null_Throws()
    {
        var ex = Assert.Throws<McpException>(() => ToolParameterValidator.ValidateStringArray(null!, "methodNames"));
        Assert.Contains("methodNames is required", ex.Message);
    }

    [Fact]
    public void ValidateStringArray_Empty_Throws()
    {
        var ex = Assert.Throws<McpException>(() => ToolParameterValidator.ValidateStringArray(Array.Empty<string>(), "methodNames"));
        Assert.Contains("methodNames is required", ex.Message);
    }

    [Fact]
    public void ValidateStringArray_ContainsEmptyEntry_Throws()
    {
        var ex = Assert.Throws<McpException>(() => ToolParameterValidator.ValidateStringArray(new[] { "Method1", "", "Method3" }, "methodNames"));
        Assert.Contains("methodNames[1] is empty", ex.Message);
    }

    [Fact]
    public void ValidateStringArray_ContainsWhitespaceEntry_Throws()
    {
        var ex = Assert.Throws<McpException>(() => ToolParameterValidator.ValidateStringArray(new[] { "Method1", "   " }, "methodNames"));
        Assert.Contains("methodNames[1] is empty", ex.Message);
    }

    [Fact]
    public void ValidateStringArray_ValidArray_ReturnsTrimmed()
    {
        var result = ToolParameterValidator.ValidateStringArray(new[] { " Method1 ", "Method2" }, "methodNames");
        Assert.Equal(new[] { "Method1", "Method2" }, result);
    }

    // --- SanitizeOptionalStringArray ---

    [Fact]
    public void SanitizeOptionalStringArray_Null_ReturnsEmpty()
    {
        var result = ToolParameterValidator.SanitizeOptionalStringArray(null);
        Assert.Empty(result);
    }

    [Fact]
    public void SanitizeOptionalStringArray_EmptyArray_ReturnsEmpty()
    {
        var result = ToolParameterValidator.SanitizeOptionalStringArray(Array.Empty<string>());
        Assert.Empty(result);
    }

    [Fact]
    public void SanitizeOptionalStringArray_FiltersEmptyEntries()
    {
        var result = ToolParameterValidator.SanitizeOptionalStringArray(new[] { "ILogger", "", "  ", "IConfig" });
        Assert.Equal(new[] { "ILogger", "IConfig" }, result);
    }

    [Fact]
    public void SanitizeOptionalStringArray_TrimsEntries()
    {
        var result = ToolParameterValidator.SanitizeOptionalStringArray(new[] { " ILogger ", " IConfig " });
        Assert.Equal(new[] { "ILogger", "IConfig" }, result);
    }

    // --- ValidateDistinctClasses ---

    [Fact]
    public void ValidateDistinctClasses_Same_Throws()
    {
        var ex = Assert.Throws<McpException>(() => ToolParameterValidator.ValidateDistinctClasses("MyClass", "MyClass"));
        Assert.Contains("must be different", ex.Message);
    }

    [Fact]
    public void ValidateDistinctClasses_Different_DoesNotThrow()
    {
        ToolParameterValidator.ValidateDistinctClasses("SourceClass", "TargetClass");
    }

    [Fact]
    public void ValidateDistinctClasses_DifferentCase_Throws()
    {
        var ex = Assert.Throws<McpException>(() => ToolParameterValidator.ValidateDistinctClasses("MyClass", "myClass"));
        Assert.Contains("must be different", ex.Message);
    }

    [Fact]
    public void ValidateDistinctClasses_DifferentCase_DoesNotThrow()
    {
        ToolParameterValidator.ValidateDistinctClasses("MyClass", "DifferentClass");
    }

    // --- NormalizeOptionalPath ---

    [Fact]
    public void NormalizeOptionalPath_Null_ReturnsNull()
    {
        var result = ToolParameterValidator.NormalizeOptionalPath(null);
        Assert.Null(result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeOptionalPath_EmptyOrWhitespace_ReturnsNull(string? path)
    {
        var result = ToolParameterValidator.NormalizeOptionalPath(path);
        Assert.Null(result);
    }

    [Fact]
    public void NormalizeOptionalPath_RelativePath_ReturnsFullPath()
    {
        var result = ToolParameterValidator.NormalizeOptionalPath("test.cs");
        Assert.Equal(Path.GetFullPath("test.cs"), result);
    }

    // --- ValidateTargetFilePath ---

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateTargetFilePath_NullOrEmpty_Throws(string? path)
    {
        var ex = Assert.Throws<McpException>(() => ToolParameterValidator.ValidateTargetFilePath(path!));
        Assert.Contains("targetFilePath is required", ex.Message);
    }

    [Fact]
    public void ValidateTargetFilePath_CustomParameterName_UsedInMessage()
    {
        var ex = Assert.Throws<McpException>(() => ToolParameterValidator.ValidateTargetFilePath("", "customPath"));
        Assert.Contains("customPath is required", ex.Message);
    }

    [Fact]
    public void ValidateTargetFilePath_NonexistentDirectory_Throws()
    {
        var nonExistingDir = Path.Combine(Path.GetTempPath(), "refactor-mcp-" + Guid.NewGuid().ToString("N"));
        var targetPath = Path.Combine(nonExistingDir, "file.cs");
        var ex = Assert.Throws<McpException>(() => ToolParameterValidator.ValidateTargetFilePath(targetPath));
        Assert.Contains("Directory for targetFilePath not found", ex.Message);
    }

    [Fact]
    public void ValidateTargetFilePath_ValidDirectory_ReturnsFullPath()
    {
        var tempDir = Path.GetTempPath();
        var targetPath = Path.Combine(tempDir, "test.cs");
        var result = ToolParameterValidator.ValidateTargetFilePath(targetPath);
        Assert.Equal(Path.GetFullPath(targetPath), result);
    }

    // --- ValidateSelectionRangeFormat ---

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateSelectionRangeFormat_NullOrEmpty_Throws(string? range)
    {
        var ex = Assert.Throws<McpException>(() => ToolParameterValidator.ValidateSelectionRangeFormat(range!));
        Assert.Contains("selectionRange is required", ex.Message);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("1:1")]
    [InlineData("abc:def-ghi:jkl")]
    [InlineData("1-2")]
    public void ValidateSelectionRangeFormat_InvalidFormat_Throws(string range)
    {
        var ex = Assert.Throws<McpException>(() => ToolParameterValidator.ValidateSelectionRangeFormat(range));
        Assert.Contains("Invalid selectionRange format", ex.Message);
    }

    [Theory]
    [InlineData("1:1-5:10")]
    [InlineData("10:1-15:20")]
    public void ValidateSelectionRangeFormat_ValidFormat_DoesNotThrow(string range)
    {
        ToolParameterValidator.ValidateSelectionRangeFormat(range);
    }

    // --- ValidateSelectionRange (with SourceText) ---

    [Fact]
    public void ValidateSelectionRange_ValidRange_DoesNotThrow()
    {
        var sourceText = SourceText.From("Line 1\nLine 2\nLine 3\nLine 4\nLine 5");
        ToolParameterValidator.ValidateSelectionRange("1:1-3:5", sourceText);
    }

    [Fact]
    public void ValidateSelectionRange_LineOutOfBounds_Throws()
    {
        var sourceText = SourceText.From("Line 1\nLine 2");
        var ex = Assert.Throws<McpException>(() => ToolParameterValidator.ValidateSelectionRange("1:1-5:10", sourceText));
        Assert.Contains("Invalid selection range", ex.Message);
        Assert.Contains("exceeds file length", ex.Message);
    }

    [Fact]
    public void ValidateSelectionRange_ColumnOutOfBounds_Throws()
    {
        var sourceText = SourceText.From("Short line");
        var ex = Assert.Throws<McpException>(() => ToolParameterValidator.ValidateSelectionRange("1:20-1:25", sourceText));
        Assert.Contains("Invalid selection range", ex.Message);
        Assert.Contains("column exceeds line length", ex.Message);
    }

    [Fact]
    public void ValidateSelectionRange_InvalidRange_Throws()
    {
        var sourceText = SourceText.From("Line 1\nLine 2");
        var ex = Assert.Throws<McpException>(() => ToolParameterValidator.ValidateSelectionRange("2:10-1:1", sourceText));
        Assert.Contains("Invalid selection range", ex.Message);
        Assert.Contains("start must precede end", ex.Message);
    }
}
