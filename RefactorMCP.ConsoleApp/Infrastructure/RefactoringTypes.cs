namespace RefactorMCP.ConsoleApp.Infrastructure;

/// <summary>
/// Standardized error codes for programmatic handling
/// </summary>
public enum RefactoringErrorCode
{
    // Validation Errors (1000-1999)
    InvalidSolutionPath = 1001,
    SolutionNotFound = 1002,
    InvalidFilePath = 1003,
    FileNotFound = 1004,
    DirectoryNotFound = 1005,
    InvalidSelectionRangeFormat = 1006,
    SelectionRangeOutOfBounds = 1007,
    RequiredParameterMissing = 1008,
    InvalidParameterFormat = 1009,
    
    // Symbol Resolution Errors (2000-2999)
    SymbolNotFound = 2001,
    SymbolAmbiguous = 2002,
    SymbolNotRenamable = 2003,
    MultipleSymbolsFound = 2004,
    
    // Refactoring Errors (3000-3999)
    CannotExtractMethod = 3001,
    CannotIntroduceVariable = 3002,
    CannotIntroduceParameter = 3003,
    CannotIntroduceField = 3004,
    CannotDeleteSymbol = 3005,
    CannotMoveMember = 3006,
    
    // System Errors (9000-9999)
    InternalError = 9001,
    Timeout = 9002,
    InsufficientMemory = 9003,
    AccessDenied = 9004
}

/// <summary>
/// Standardized error response structure
/// </summary>
public class RefactoringErrorResponse
{
    public string Status { get; set; } = "error";
    public RefactoringErrorCode ErrorCode { get; set; }
    public string Message { get; set; } = string.Empty;
    public string ParameterName { get; set; } = string.Empty;
    public string? Details { get; set; }
    public string? StackTrace { get; set; }
}

/// <summary>
/// Standardized success response structure
/// </summary>
public class RefactoringSuccessResponse
{
    public string Status { get; set; } = "success";
    public string Operation { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public List<RefactoringChange> Changes { get; set; } = new();
    public string Summary { get; set; } = string.Empty;
    public List<string> Warnings { get; set; } = new();
    public RefactoringMetrics? Metrics { get; set; }
}

/// <summary>
/// Metrics for refactoring operations
/// </summary>
public class RefactoringMetrics
{
    public long DurationMs { get; set; }
    public int FilesChanged { get; set; }
    public int SymbolsProcessed { get; set; }
    public int LinesOfCodeAffected { get; set; }
}

/// <summary>
/// Individual change made during refactoring
/// </summary>
public class RefactoringChange
{
    public string Type { get; set; } = string.Empty; // "insert", "delete", "rename", "move", "modify"
    public string Description { get; set; } = string.Empty;
    public string? FilePath { get; set; }
    public int? StartLine { get; set; }
    public int? StartColumn { get; set; }
    public int? EndLine { get; set; }
    public int? EndColumn { get; set; }
    public string? OldContent { get; set; }
    public string? NewContent { get; set; }
}
