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
            solutionPath = ToolParameterValidator.ValidateSolutionPath(solutionPath);
            filePath = ToolParameterValidator.ValidateFilePath(filePath);
            ToolParameterValidator.ValidateRequiredString(oldName, nameof(oldName));
            ToolParameterValidator.ValidateRequiredString(newName, nameof(newName));

            var solution = await RefactoringHelpers.GetOrLoadSolution(solutionPath, cancellationToken);
            var document = RefactoringHelpers.GetDocumentByPath(solution, filePath);
            if (document == null)
                throw new McpException($"Error: File {filePath} not found in solution");

            var symbol = await FindSymbol(document, oldName, line, column, cancellationToken);
            if (symbol == null)
            {
                var positionInfo = line.HasValue && column.HasValue 
                    ? $" at line {line}, column {column}" 
                    : "";
                throw new McpException($"Error: Symbol '{oldName}' not found{positionInfo}. Verify the symbol name and location.");
            }

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
        // First try to find the exact token at position
        var token = root.FindToken(position);
        if (token != default && token.Span.Contains(position))
        {
            // Check if the token itself matches the name
            if (token.Text == name)
            {
                // Walk up the syntax tree to find a node that can provide symbol info
                var currentNode = token.Parent;
                while (currentNode != null)
                {
                    var symbolInfo = model.GetSymbolInfo(currentNode);
                    if (symbolInfo.Symbol != null && symbolInfo.Symbol.Name == name)
                        return symbolInfo.Symbol;
                    
                    var declaredSymbol = model.GetDeclaredSymbol(currentNode);
                    if (declaredSymbol != null && declaredSymbol.Name == name)
                        return declaredSymbol;
                        
                    currentNode = currentNode.Parent;
                }
            }

            // Walk up the syntax tree to find containing symbols
            var node = token.Parent;
            while (node != null)
            {
                var symbol = model.GetDeclaredSymbol(node) ?? model.GetSymbolInfo(node).Symbol;
                if (symbol != null && symbol.Name == name)
                    return symbol;

                node = node.Parent;
            }
        }

        // Fallback: use LookupSymbols which is much more efficient and accurate
        // than scanning nearby nodes manually
        var candidateSymbols = model.LookupSymbols(position, name: name);
        
        // Prefer symbols that are actually accessible from this position and in source
        var accessibleSymbols = candidateSymbols
            .Where(s => s.CanBeReferencedByName && 
                       (s.Kind == SymbolKind.Local || 
                        s.Kind == SymbolKind.Parameter || 
                        s.Kind == SymbolKind.Field || 
                        s.Kind == SymbolKind.Property || 
                        s.Kind == SymbolKind.Method))
            .Where(s => s.Locations.Any(l => l.IsInSource && l.SourceTree == root.SyntaxTree))
            .OrderBy(s => {
                // Prefer symbols with smaller containing spans (more local)
                var containingSpan = s.Locations.FirstOrDefault()?.SourceSpan ?? default;
                return containingSpan.Length;
            })
            .ThenBy(s => {
                // Then by distance from position
                var symbolSpan = s.Locations.FirstOrDefault()?.SourceSpan ?? default;
                return Math.Abs(symbolSpan.Start - position);
            })
            .ToList(); // Materialize to count

        // Ambiguity check: if multiple symbols with same name are found, require explicit line/column
        if (accessibleSymbols.Count > 1)
        {
            throw new McpException(
                $"Error: Found {accessibleSymbols.Count} symbols named '{name}' at this location. " +
                "Rename operation is ambiguous. Please provide explicit line and column numbers " +
                "to specify which symbol to rename. Available candidates: " +
                string.Join(", ", accessibleSymbols.Select(s => $"{s.Kind} {s.ContainingType?.Name ?? s.Name}")));
        }

        return accessibleSymbols.FirstOrDefault();
    }

    private static ISymbol? FindLocalOrParameterSymbol(SemanticModel model, SyntaxNode root, string name)
    {
        // Try to find local variables with better scope awareness
        var locals = root.DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Where(v => v.Identifier.ValueText == name)
            .OrderBy(v => Math.Abs(v.Span.Start - root.FullSpan.Start)) // Prefer closest to root
            .ToList();

        foreach (var local in locals)
        {
            var symbol = model.GetDeclaredSymbol(local);
            if (symbol != null && symbol.Name == name)
                return symbol;
        }

        // Try to find parameters with scope awareness
        var parameters = root.DescendantNodes()
            .OfType<ParameterSyntax>()
            .Where(p => p.Identifier.ValueText == name)
            .OrderBy(p => Math.Abs(p.Span.Start - root.FullSpan.Start)) // Prefer closest to root
            .ToList();

        foreach (var parameter in parameters)
        {
            var symbol = model.GetDeclaredSymbol(parameter);
            if (symbol != null && symbol.Name == name)
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
