using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;


public class SemanticValidator
{
    public ValidationResult Validate(SyntaxTree modifiedTree, Compilation originalCompilation)
    {
        var result = new ValidationResult { IsValid = true };

        try
        {
            var oldTree = originalCompilation.SyntaxTrees.FirstOrDefault(st => st.FilePath == modifiedTree.FilePath);
            if (oldTree == null)
            {
                return new ValidationResult
                {
                    IsValid = false,
                    Errors = new List<string> { "Original syntax tree not found in compilation" }
                };
            }

            var newCompilation = originalCompilation.ReplaceSyntaxTree(oldTree, modifiedTree);

            var baselineErrors = originalCompilation.GetDiagnostics()
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(ToDiagnosticKey)
                .ToHashSet(StringComparer.Ordinal);

            var newErrors = newCompilation.GetDiagnostics()
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .ToList();

            foreach (var diagnostic in newCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Warning))
            {
                result.Warnings.Add($"{diagnostic.Id}: {diagnostic.GetMessage()} at {diagnostic.Location}");
            }

            foreach (var diagnostic in newErrors)
            {
                var key = ToDiagnosticKey(diagnostic);
                if (!baselineErrors.Contains(key))
                {
                    result.IsValid = false;
                    result.Errors.Add($"{diagnostic.Id}: {diagnostic.GetMessage()} at {diagnostic.Location}");
                }
            }
        }
        catch (Exception ex)
        {
            result.IsValid = false;
            result.Errors.Add($"Validation error: {ex.Message}");
        }

        return result;
    }

    private static string ToDiagnosticKey(Diagnostic diagnostic)
    {
        var span = diagnostic.Location.GetLineSpan();
        var path = span.Path ?? string.Empty;
        return $"{diagnostic.Id}|{diagnostic.GetMessage()}|{path}|{span.StartLinePosition.Line}|{span.StartLinePosition.Character}";
    }
}

public class ValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}
