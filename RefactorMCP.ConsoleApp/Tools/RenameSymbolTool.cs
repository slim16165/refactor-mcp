[McpServerToolType]
public static class RenameSymbolTool
{
    [McpServerTool, Description("Rename a symbol across the solution using Roslyn")]
    public static async Task<string> RenameSymbol(
        [Description("Absolute path to the solution file (.sln)")] string solutionPath,
        [Description("Path to the C# file containing the symbol")] string filePath,
        [Description("Current name of the symbol")] string oldName,
        [Description("New name for the symbol")] string newName,
        [Description("Line number of the symbol (1-based, optional)")] int? line = null,
        [Description("Column number of the symbol (1-based, optional)")] int? column = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var solution = await RefactoringHelpers.GetOrLoadSolution(solutionPath, cancellationToken);
            var document = RefactoringHelpers.GetDocumentByPath(solution, filePath);
            if (document == null)
                throw new McpException($"Error: File {filePath} not found in solution");

            var symbol = await FindSymbol(document, oldName, line, column, cancellationToken);
            if (symbol == null)
                throw new McpException($"Error: Symbol '{oldName}' not found");

            var options = new SymbolRenameOptions();
            var renamed = await Renamer.RenameSymbolAsync(solution, symbol, options, newName, cancellationToken);
            var changes = renamed.GetChanges(solution);
            foreach (var projectChange in changes.GetProjectChanges())
            {
                foreach (var id in projectChange.GetChangedDocuments())
                {
                    var newDoc = renamed.GetDocument(id)!;
                    var text = await newDoc.GetTextAsync(cancellationToken);
                    var encoding = await RefactoringHelpers.GetFileEncodingAsync(newDoc.FilePath!, cancellationToken);
                    await File.WriteAllTextAsync(newDoc.FilePath!, text.ToString(), encoding, cancellationToken);
                    RefactoringHelpers.UpdateSolutionCache(newDoc);
                }
            }

            return $"Successfully renamed '{oldName}' to '{newName}'";
        }
        catch (Exception ex)
        {
            throw new McpException($"Error renaming symbol: {ex.Message}", ex);
        }
    }

    private static async Task<ISymbol?> FindSymbol(Document document, string name, int? line, int? column, CancellationToken cancellationToken)
    {
        var model = await document.GetSemanticModelAsync(cancellationToken);
        var root = await document.GetSyntaxRootAsync(cancellationToken);
        if (model == null || root == null)
            return null;

        var position = await TryGetValidatedPositionAsync(document, line, column, cancellationToken);
        if (position.HasValue)
        {
            var symbolAtPosition = FindSymbolAtPosition(model, root, position.Value, name);
            if (symbolAtPosition != null)
                return symbolAtPosition;
        }

        var localOrParameter = FindLocalOrParameterSymbol(model, root, name);
        if (localOrParameter != null)
            return localOrParameter;

        var identifierSymbol = FindIdentifierSymbol(model, root, name);
        if (identifierSymbol != null)
            return identifierSymbol;

        var decls = await SymbolFinder.FindDeclarationsAsync(document.Project, name, false, cancellationToken);
        return decls.FirstOrDefault();
    }

    private static async Task<int?> TryGetValidatedPositionAsync(
        Document document,
        int? line,
        int? column,
        CancellationToken cancellationToken)
    {
        if (!line.HasValue && !column.HasValue)
            return null;

        if (!line.HasValue || !column.HasValue)
            throw new McpException("Error: line and column must both be provided");

        var text = await document.GetTextAsync(cancellationToken);
        if (line.Value <= 0 || line.Value > text.Lines.Count)
            throw new McpException($"Error: line {line.Value} is out of range (1-{text.Lines.Count})");

        var lineText = text.Lines[line.Value - 1];
        if (column.Value <= 0 || column.Value > lineText.Span.Length + 1)
            throw new McpException($"Error: column {column.Value} is out of range for line {line.Value} (1-{lineText.Span.Length + 1})");

        return lineText.Start + column.Value - 1;
    }

    private static ISymbol? FindSymbolAtPosition(SemanticModel model, SyntaxNode root, int position, string name)
    {
        var token = root.FindToken(position);
        var node = token.Parent;
        while (node != null)
        {
            var symbol = model.GetDeclaredSymbol(node) ?? model.GetSymbolInfo(node).Symbol;
            if (symbol != null && symbol.Name == name)
                return symbol;

            node = node.Parent;
        }

        return null;
    }

    private static ISymbol? FindLocalOrParameterSymbol(SemanticModel model, SyntaxNode root, string name)
    {
        var local = root.DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .FirstOrDefault(v => v.Identifier.ValueText == name);
        if (local != null)
        {
            var symbol = model.GetDeclaredSymbol(local);
            if (symbol != null)
                return symbol;
        }

        var parameter = root.DescendantNodes()
            .OfType<ParameterSyntax>()
            .FirstOrDefault(p => p.Identifier.ValueText == name);
        if (parameter != null)
        {
            var symbol = model.GetDeclaredSymbol(parameter);
            if (symbol != null)
                return symbol;
        }

        return null;
    }

    private static ISymbol? FindIdentifierSymbol(SemanticModel model, SyntaxNode root, string name)
    {
        foreach (var identifier in root.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (identifier.Identifier.ValueText != name)
                continue;

            var symbol = model.GetSymbolInfo(identifier).Symbol;
            if (symbol != null && symbol.Name == name)
                return symbol;
        }

        return null;
    }
}
