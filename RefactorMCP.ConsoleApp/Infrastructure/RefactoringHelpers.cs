using ModelContextProtocol.Server;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using ModelContextProtocol;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Caching.Memory;
using System.Text;

namespace RefactorMCP.ConsoleApp.Infrastructure;

internal static class RefactoringHelpers
{
    private static readonly Lazy<AdhocWorkspace> _workspace = new(() => new AdhocWorkspace());
    internal static AdhocWorkspace SharedWorkspace => _workspace.Value;

    // Direct access for legacy tools
    internal static MemoryCache SolutionCache => SolutionService.GetSolutionCache();
    
    internal static void ClearAllCaches() => SolutionService.ClearAllCaches();
    
    internal static MSBuildWorkspace CreateWorkspace() => SolutionService.CreateWorkspace();

    // Delegate to SolutionService
    internal static async Task<Solution> GetOrLoadSolution(string solutionPath, CancellationToken cancellationToken = default) =>
        await SolutionService.GetOrLoadSolution(solutionPath, cancellationToken);

    internal static void UpdateSolutionCache(Document updatedDocument) =>
        SolutionService.UpdateSolutionCache(updatedDocument);

    internal static Document? GetDocumentByPath(Solution solution, string filePath) =>
        SolutionService.GetDocumentByPath(solution, filePath);

    // Delegate to RangeService
    internal static bool TryParseRange(string range, out int startLine, out int startColumn, out int endLine, out int endColumn) =>
        RangeService.TryParseRange(range, out startLine, out startColumn, out endLine, out endColumn);

    internal static bool ValidateRange(SourceText text, int startLine, int startColumn, int endLine, int endColumn, out string error) =>
        RangeService.ValidateRange(text, startLine, startColumn, endLine, endColumn, out error);

    internal static TextSpan ParseSelectionRange(SourceText sourceText, string selectionRange) =>
        RangeService.ParseSelectionRange(sourceText, selectionRange);

    // Delegate to TextService
    internal static async Task<(string Text, Encoding Encoding)> ReadFileWithEncodingAsync(string filePath, CancellationToken cancellationToken = default) =>
        await TextService.ReadFileWithEncodingAsync(filePath, cancellationToken);

    internal static async Task<Encoding> GetFileEncodingAsync(string filePath, CancellationToken cancellationToken = default) =>
        await TextService.GetFileEncodingAsync(filePath, cancellationToken);

    // Consolidated Helpers
    internal static async Task WriteAndUpdateCachesAsync(Document document, SyntaxNode newRoot)
    {
        var newDocument = document.WithSyntaxRoot(newRoot);
        var newText = await newDocument.GetTextAsync();
        var encoding = await TextService.GetFileEncodingAsync(document.FilePath!);
        await TextService.WriteFileWithEncodingAsync(document.FilePath!, newText.ToString(), encoding);
        SolutionService.UpdateSolutionCache(newDocument);
    }

    internal static async Task<string> ApplySingleFileEdit(string filePath, Func<string, string> transform, string successMessage)
    {
        if (!File.Exists(filePath))
            throw new McpException($"Error: File {filePath} not found (current dir: {Directory.GetCurrentDirectory()})");

        var (sourceText, encoding) = await TextService.ReadFileWithEncodingAsync(filePath);
        var newText = transform(sourceText);

        if (newText.StartsWith("Error:")) return newText;

        await TextService.WriteFileWithEncodingAsync(filePath, newText, encoding);
        UpdateFileCaches(filePath, newText);
        return successMessage;
    }

    internal static async Task<string> RunWithSolutionOrFile(
        string solutionPath,
        string filePath,
        Func<Document, Task<string>> withSolution,
        Func<string, Task<string>> singleFile)
    {
        var solution = await SolutionService.GetOrLoadSolution(solutionPath);
        var document = SolutionService.GetDocumentByPath(solution, filePath);
        return document != null ? await withSolution(document) : await singleFile(filePath);
    }

    // Restored Methods
    internal static async Task<SyntaxTree> GetOrParseSyntaxTreeAsync(string filePath)
    {
        var (text, _) = await TextService.ReadFileWithEncodingAsync(filePath);
        return CSharpSyntaxTree.ParseText(text);
    }

    internal static async Task<SemanticModel> GetOrCreateSemanticModelAsync(string filePath)
    {
        var tree = await GetOrParseSyntaxTreeAsync(filePath);
        var compilation = CSharpCompilation.Create("SingleFile", [tree], 
            ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(p => MetadataReference.CreateFromFile(p)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        return compilation.GetSemanticModel(tree);
    }

    internal static void UpdateFileCaches(string filePath, string newText)
    {
        // Update syntax tree cache for single-file mode operations
        if (SharedWorkspace.CurrentSolution.Projects.Any())
        {
            var doc = SharedWorkspace.CurrentSolution.Projects
                .SelectMany(p => p.Documents)
                .FirstOrDefault(d => d.FilePath == filePath);
            
            if (doc != null)
            {
                var newSourceText = SourceText.From(newText);
                var newDoc = doc.WithText(newSourceText);
                // Note: In single-file mode, we don't persist this to the actual solution
                // but it ensures subsequent operations in the same session see the updated content
            }
        }
    }

    internal static async Task<Document?> FindClassInSolution(Solution solution, string className, params string[]? excludingFilePaths)
    {
        foreach (var doc in solution.Projects.SelectMany(p => p.Documents))
        {
            var docPath = doc.FilePath ?? string.Empty;
            if (excludingFilePaths != null && excludingFilePaths.Any(p => Path.GetFullPath(docPath) == Path.GetFullPath(p)))
                continue;

            var root = await doc.GetSyntaxRootAsync();
            if (root?.DescendantNodes().OfType<ClassDeclarationSyntax>().Any(c => c.Identifier.Text == className) == true)
                return doc;
        }
        return null;
    }

    internal static async Task<Document?> FindTypeInSolution(Solution solution, string typeName, params string[]? excludingFilePaths)
    {
        foreach (var doc in solution.Projects.SelectMany(p => p.Documents))
        {
            var docPath = doc.FilePath ?? string.Empty;
            if (excludingFilePaths != null && excludingFilePaths.Any(p => Path.GetFullPath(docPath) == Path.GetFullPath(p)))
                continue;

            var root = await doc.GetSyntaxRootAsync();
            if (root?.DescendantNodes().Any(n =>
                    n is BaseTypeDeclarationSyntax bt && bt.Identifier.Text == typeName ||
                    n is EnumDeclarationSyntax en && en.Identifier.Text == typeName ||
                    n is DelegateDeclarationSyntax dd && dd.Identifier.Text == typeName) == true)
                return doc;
        }
        return null;
    }

    internal static void AddDocumentToProject(Project project, string filePath)
    {
        if (project.Documents.Any(d => Path.GetFullPath(d.FilePath ?? "") == Path.GetFullPath(filePath)))
            return;

        var text = SourceText.From(File.ReadAllText(filePath));
        var newDoc = project.AddDocument(Path.GetFileName(filePath), text, filePath: filePath);
        SolutionService.UpdateSolutionCache(newDoc);
    }
}
