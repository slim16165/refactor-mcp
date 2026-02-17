using Microsoft.CodeAnalysis;
using ModelContextProtocol.Server;
using System.Text.Json;

namespace RefactorMCP.ConsoleApp.Infrastructure;

/// <summary>
/// Helper for converting exceptions and results to structured responses
/// </summary>
public static class ResponseFormatter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    /// <summary>
    /// Convert McpException to structured error response
    /// </summary>
    public static string FormatError(McpException ex)
    {
        var errorCode = ExtractErrorCode(ex.Message);
        var parameterName = ExtractParameterName(ex.Message);
        
        var response = new RefactoringErrorResponse
        {
            ErrorCode = errorCode,
            Message = ex.Message,
            ParameterName = parameterName,
            Details = ex.InnerException?.Message
        };

        return JsonSerializer.Serialize(response, JsonOptions);
    }

    /// <summary>
    /// Convert error messages to structured error response
    /// </summary>
    public static string FormatError(params string[] errors)
    {
        var response = new RefactoringErrorResponse
        {
            ErrorCode = RefactoringErrorCode.InternalError,
            Message = string.Join("; ", errors),
            Details = null
        };

        return JsonSerializer.Serialize(response, JsonOptions);
    }

    /// <summary>
    /// Convert general exception to structured error response
    /// </summary>
    public static string FormatError(Exception ex, string operation = "unknown")
    {
        var response = new RefactoringErrorResponse
        {
            ErrorCode = RefactoringErrorCode.InternalError,
            Message = $"Unexpected error in {operation}: {ex.Message}",
            Details = ex.InnerException?.Message,
            StackTrace = ex.StackTrace
        };

        return JsonSerializer.Serialize(response, JsonOptions);
    }

    /// <summary>
    /// Convert successful refactoring result to structured response
    /// </summary>
    public static string FormatSuccess(
        string operation,
        string filePath,
        RefactoringResult result,
        long durationMs = 0,
        int filesChanged = 1,
        int symbolsProcessed = 1,
        int linesAffected = 0)
    {
        var response = new RefactoringSuccessResponse
        {
            Operation = operation,
            FilePath = filePath,
            Changes = result.Changes,
            Summary = result.Summary,
            Warnings = result.Warnings,
            Metrics = new RefactoringMetrics
            {
                DurationMs = durationMs,
                FilesChanged = filesChanged,
                SymbolsProcessed = symbolsProcessed,
                LinesOfCodeAffected = linesAffected
            }
        };

        return JsonSerializer.Serialize(response, JsonOptions);
    }

    /// <summary>
    /// Extract error code from exception message
    /// </summary>
    private static RefactoringErrorCode ExtractErrorCode(string message)
    {
        return message.ToLowerInvariant() switch
        {
            var m when m.Contains("solution file not found") => RefactoringErrorCode.SolutionNotFound,
            var m when m.Contains("invalid path") => RefactoringErrorCode.InvalidFilePath,
            var m when m.Contains("file not found") => RefactoringErrorCode.FileNotFound,
            var m when m.Contains("directory not found") => RefactoringErrorCode.DirectoryNotFound,
            var m when m.Contains("invalid selection range format") => RefactoringErrorCode.InvalidSelectionRangeFormat,
            var m when m.Contains("start column exceeds") || m.Contains("end column exceeds") || 
                 m.Contains("range exceeds file length") => RefactoringErrorCode.SelectionRangeOutOfBounds,
            var m when m.Contains("required") && m.Contains("cannot be empty") => RefactoringErrorCode.RequiredParameterMissing,
            var m when m.Contains("symbol not found") => RefactoringErrorCode.SymbolNotFound,
            var m when m.Contains("ambiguous") => RefactoringErrorCode.SymbolAmbiguous,
            var m when m.Contains("cannot extract method") => RefactoringErrorCode.CannotExtractMethod,
            var m when m.Contains("cannot introduce") => RefactoringErrorCode.CannotIntroduceVariable,
            _ => RefactoringErrorCode.InternalError
        };
    }

    /// <summary>
    /// Extract parameter name from exception message
    /// </summary>
    private static string ExtractParameterName(string message)
    {
        // Look for patterns like "Error: solutionPath is required" or "parameter 'solutionPath' is required"
        var match1 = System.Text.RegularExpressions.Regex.Match(message, @"Error:\s*(\w+)\s+is\s+required");
        if (match1.Success) return match1.Groups[1].Value;
        
        var match2 = System.Text.RegularExpressions.Regex.Match(message, @"parameter\s+'([^']+)'");
        return match2.Success ? match2.Groups[1].Value : string.Empty;
    }
}
