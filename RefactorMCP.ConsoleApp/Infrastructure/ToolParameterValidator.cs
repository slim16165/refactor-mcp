namespace RefactorMCP.ConsoleApp.Infrastructure;

internal static class ToolParameterValidator
{
    /// <summary>
    /// Helper to get full path with consistent exception handling.
    /// </summary>
    private static string GetFullPathOrThrow(string path, string parameterName)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new McpException($"Error: Invalid path for {parameterName} '{path}'. {ex.Message}", ex);
        }
    }
    /// <summary>
    /// Validates a solution path: non-empty, has .sln extension, file exists on disk.
    /// Returns the normalized full path.
    /// </summary>
    internal static string ValidateSolutionPath(string solutionPath)
    {
        if (string.IsNullOrWhiteSpace(solutionPath))
            throw new McpException("Error: solutionPath is required and cannot be empty.");

        if (!solutionPath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
            throw new McpException(
                $"Error: solutionPath must be a .sln file, but got '{Path.GetFileName(solutionPath)}'. " +
                "Provide the full path to the Visual Studio solution file.");

        string fullPath = GetFullPathOrThrow(solutionPath, "solutionPath");

        if (!File.Exists(fullPath))
            throw new McpException(
                $"Error: Solution file not found at '{fullPath}'. " +
                $"Current directory: {Directory.GetCurrentDirectory()}");

        return fullPath;
    }

    /// <summary>
    /// Validates a file path: non-empty, file exists on disk.
    /// Returns the normalized full path.
    /// </summary>
    internal static string ValidateFilePath(string filePath, string parameterName = "filePath")
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new McpException($"Error: {parameterName} is required and cannot be empty.");

        string fullPath = GetFullPathOrThrow(filePath, parameterName);

        if (!File.Exists(fullPath))
            throw new McpException(
                $"Error: File not found at '{fullPath}'. " +
                $"Current directory: {Directory.GetCurrentDirectory()}");

        return fullPath;
    }

    /// <summary>
    /// Validates a required string parameter is not null/empty/whitespace.
    /// </summary>
    internal static void ValidateRequiredString(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new McpException($"Error: {parameterName} is required and cannot be empty.");
    }

    /// <summary>
    /// Normalizes an optional path (may be null). Returns null if input is null/empty, otherwise returns full path.
    /// </summary>
    internal static string? NormalizeOptionalPath(string? path, string parameterName = "path")
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        
        return GetFullPathOrThrow(path, parameterName);
    }

    /// <summary>
    /// Validates a target file path: non-empty, directory exists. File may not exist (for creation).
    /// Returns the normalized full path.
    /// </summary>
    internal static string ValidateTargetFilePath(string targetFilePath, string parameterName = "targetFilePath")
    {
        if (string.IsNullOrWhiteSpace(targetFilePath))
            throw new McpException($"Error: {parameterName} is required and cannot be empty.");

        string fullPath = GetFullPathOrThrow(targetFilePath, parameterName);

        var directory = Path.GetDirectoryName(fullPath);

        if (string.IsNullOrWhiteSpace(directory))
            throw new McpException($"Error: {parameterName} '{targetFilePath}' has no valid directory.");

        if (!Directory.Exists(directory))
            throw new McpException(
                $"Error: Directory for {parameterName} not found at '{directory}'. " +
                $"Current directory: {Directory.GetCurrentDirectory()}");

        return fullPath;
    }

    /// <summary>
    /// Validates a string array: non-null, non-empty, no empty/whitespace entries.
    /// Returns the sanitized array (trimmed entries).
    /// </summary>
    internal static string[] ValidateStringArray(string[] values, string parameterName)
    {
        if (values == null || values.Length == 0)
            throw new McpException($"Error: {parameterName} is required and must contain at least one entry.");

        for (int i = 0; i < values.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(values[i]))
                throw new McpException(
                    $"Error: {parameterName}[{i}] is empty or whitespace. All entries must be non-empty.");
        }

        return values.Select(v => v.Trim()).ToArray();
    }

    /// <summary>
    /// Sanitizes an optional string array: if non-null and non-empty, filters blanks and trims.
    /// Returns empty array if null.
    /// </summary>
    internal static string[] SanitizeOptionalStringArray(string[]? values)
    {
        if (values == null || values.Length == 0)
            return Array.Empty<string>();

        return values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).ToArray();
    }

    /// <summary>
    /// Validates that two class names are not the same (case-insensitive for better UX).
    /// </summary>
    internal static void ValidateDistinctClasses(string sourceClass, string targetClass)
    {
        if (string.Equals(sourceClass, targetClass, StringComparison.OrdinalIgnoreCase))
            throw new McpException(
                $"Error: sourceClass and targetClass must be different (case-insensitive), but got '{sourceClass}' and '{targetClass}'.");
    }

    /// <summary>
    /// Pre-validates selectionRange format without needing SourceText.
    /// </summary>
    internal static void ValidateSelectionRangeFormat(string selectionRange)
    {
        if (string.IsNullOrWhiteSpace(selectionRange))
            throw new McpException(
                "Error: selectionRange is required. Use format 'startLine:startCol-endLine:endCol'.");

        if (!RangeService.TryParseRange(selectionRange, out _, out _, out _, out _))
            throw new McpException(
                $"Error: Invalid selectionRange format '{selectionRange}'. " +
                "Expected format: 'startLine:startColumn-endLine:endColumn' (e.g., '10:1-15:20').");
    }

    /// <summary>
    /// Validates selectionRange format and bounds against the provided SourceText.
    /// </summary>
    internal static void ValidateSelectionRange(string selectionRange, SourceText text)
    {
        if (string.IsNullOrWhiteSpace(selectionRange))
            throw new McpException(
                "Error: selectionRange is required. Use format 'startLine:startCol-endLine:endCol'.");

        if (!RangeService.TryParseRange(selectionRange, out var startLine, out var startColumn, out var endLine, out var endColumn))
            throw new McpException(
                $"Error: Invalid selectionRange format '{selectionRange}'. " +
                "Expected format: 'startLine:startColumn-endLine:endColumn' (e.g., '10:1-15:20').");

        if (!RangeService.ValidateRange(text, startLine, startColumn, endLine, endColumn, out var error))
            throw new McpException($"Error: Invalid selection range '{selectionRange}'. {error}");
    }
}
