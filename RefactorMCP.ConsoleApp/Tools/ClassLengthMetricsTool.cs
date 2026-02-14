[McpServerToolType]
public static class ClassLengthMetricsTool
{
    [McpServerTool, Description("Analyze line lengths of classes in all files of a solution")]
    public static async Task<string> ListClassLengths(
        [Description("Absolute path to the solution file (.sln)")] string solutionPath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var solution = await RefactoringHelpers.GetOrLoadSolution(solutionPath, cancellationToken);
            var results = new List<string>();

            foreach (var project in solution.Projects)
            {
                foreach (var document in project.Documents)
                {
                    if (document.FilePath == null) continue;
                    
                    var root = await document.GetSyntaxRootAsync(cancellationToken);
                    if (root == null) continue;

                    var classes = root.DescendantNodes().OfType<ClassDeclarationSyntax>();
                    var sourceText = await document.GetTextAsync(cancellationToken);

                    foreach (var @class in classes)
                    {
                        var span = @class.FullSpan;
                        var lines = sourceText.ToString().Substring(span.Start, span.Length).Split('\n').Length;
                        results.Add($"Class '{@class.Identifier.Text}': {lines} lines (in {document.Name})");
                    }
                }
            }

            return results.Count > 0 ? string.Join("\n", results) : "No classes found in solution.";
        }
        catch (Exception ex)
        {
            throw new McpException($"Error analyzing class lengths: {ex.Message}", ex);
        }
    }

    [McpServerTool, Description("Analyze line lengths of classes in a specific file")]
    public static async Task<string> ClassLengthMetrics(
        [Description("Path to the C# file")] string filePath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var (sourceText, _) = await RefactoringHelpers.ReadFileWithEncodingAsync(filePath, cancellationToken);
            var tree = CSharpSyntaxTree.ParseText(sourceText);
            var root = await tree.GetRootAsync(cancellationToken);

            var classes = root.DescendantNodes().OfType<ClassDeclarationSyntax>();
            var results = new List<string>();

            foreach (var @class in classes)
            {
                var span = @class.FullSpan;
                var lines = sourceText.Substring(span.Start, span.Length).Split('\n').Length;
                results.Add($"Class '{@class.Identifier.Text}': {lines} lines");
            }

            return results.Count > 0 ? string.Join("\n", results) : "No classes found in file.";
        }
        catch (Exception ex)
        {
            throw new McpException($"Error analyzing class lengths: {ex.Message}", ex);
        }
    }
}
