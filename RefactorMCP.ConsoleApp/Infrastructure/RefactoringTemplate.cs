using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using ModelContextProtocol.Server;

namespace RefactorMCP.ConsoleApp.Infrastructure;

/// <summary>
/// Standardized template for refactoring tools with validation, execution, and structured output
/// </summary>
public static class RefactoringTemplate
{
    /// <summary>
    /// Standard template: Validate → Load → Apply → Return structured result
    /// </summary>
    public static async Task<string> ExecuteRefactoringAsync<TRequest>(
        TRequest request,
        Func<TRequest, ValidationResult> validateRequest,
        Func<TRequest, Document, Task<RefactoringResult>> applyWithSolution,
        Func<TRequest, string, Task<RefactoringResult>> applySingleFile,
        string operationName)
    {
        try
        {
            // 1. Validate request
            var validationResult = validateRequest(request);
            if (!validationResult.IsValid)
                return ResponseFormatter.FormatError(validationResult.Errors.ToArray());

            // 2. Extract common parameters
            var (solutionPath, filePath) = ExtractCommonPaths(request);

            // 3. Load solution/document
            var solution = await RefactoringHelpers.GetOrLoadSolution(solutionPath);
            var document = RefactoringHelpers.GetDocumentByPath(solution, filePath);

            // 4. Apply refactoring
            var result = document != null 
                ? await applyWithSolution(request, document)
                : await applySingleFile(request, filePath);

            // 5. Return structured success
            return ResponseFormatter.FormatSuccess(operationName, document?.FilePath ?? filePath, result);
        }
        catch (McpException ex)
        {
            return ResponseFormatter.FormatError(ex);
        }
        catch (Exception ex)
        {
            return ResponseFormatter.FormatError(ex, operationName);
        }
    }

    private static (string solutionPath, string filePath) ExtractCommonPaths<TRequest>(TRequest request)
    {
        var solutionPathProp = typeof(TRequest).GetProperty("SolutionPath");
        var filePathProp = typeof(TRequest).GetProperty("FilePath");
        
        var solutionPath = solutionPathProp?.GetValue(request)?.ToString() ?? "";
        var filePath = filePathProp?.GetValue(request)?.ToString() ?? "";
        
        return (solutionPath, filePath);
    }
}

/// <summary>
/// Result of request validation
/// </summary>
public class ValidationResult
{
    public bool IsValid { get; set; } = true;
    public List<string> Errors { get; set; } = new();
}

/// <summary>
/// Result of refactoring operation
/// </summary>
public class RefactoringResult
{
    public List<RefactoringChange> Changes { get; set; } = new();
    public string Summary { get; set; } = string.Empty;
    public List<string> Warnings { get; set; } = new();
}
