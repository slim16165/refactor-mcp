using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Text;

[McpServerToolType]
public static class ExtractMethodTool
{
    private static readonly DataFlowAnalyzer _dataFlowAnalyzer = new();
    private static readonly EdgeCaseDetector _edgeCaseDetector = new();
    private static readonly SemanticValidator _semanticValidator = new();

    [McpServerTool, Description("Extract a code block into a new method with advanced analysis and validation")]
    public static async Task<string> ExtractMethod(
        [Description("Absolute path to solution file (.sln)")] string solutionPath,
        [Description("Path to C# file")] string filePath,
        [Description("Range in format 'startLine:startColumn-endLine:endColumn'")] string selectionRange,
        [Description("Name for the new method")] string methodName,
        [Description("Perform dry run without making changes")] bool dryRun = false,
        [Description("Include detailed analysis in output")] bool verbose = false)
    {
        try
        {
            return await RefactoringHelpers.RunWithSolutionOrFile(
                solutionPath,
                filePath,
                doc => ExtractMethodWithSolution(doc, selectionRange, methodName, dryRun, verbose),
                path => ExtractMethodSingleFile(path, selectionRange, methodName, dryRun, verbose));
        }
        catch (Exception ex)
        {
            throw new McpException($"Error extracting method: {ex.Message}", ex);
        }
    }

    private static async Task<string> ExtractMethodWithSolution(Document document, string selectionRange, string methodName, bool dryRun, bool verbose)
    {
        var sourceText = await document.GetTextAsync();
        var syntaxRoot = await document.GetSyntaxRootAsync();
        var semanticModel = await document.GetSemanticModelAsync();
        var span = RefactoringHelpers.ParseSelectionRange(sourceText, selectionRange);

        var selectedNodes = syntaxRoot!.DescendantNodes()
            .Where(n => span.Contains(n.Span))
            .ToList();

        if (!selectedNodes.Any())
            throw new McpException("Error: No valid code selected");

        var containingMethod = selectedNodes.First().Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        if (containingMethod == null)
            throw new McpException("Error: Selected code is not within a method");

        if (containingMethod.Body == null)
        {
            if (containingMethod.ExpressionBody != null)
                throw new McpException("Error: Extraction from expression-bodied methods is not supported");
            throw new McpException("Error: Selected code is not within a block-bodied method");
        }

        var statementsToExtract = containingMethod.Body.Statements
            .Where(s => span.IntersectsWith(s.FullSpan))
            .ToList();

        if (!statementsToExtract.Any())
            throw new McpException("Error: Selected code does not contain extractable statements");

        // Enhanced analysis
        var compilation = (await document.Project.GetCompilationAsync())!;
        var analysisResult = await PerformPreExtractionAnalysis(
            statementsToExtract, containingMethod, semanticModel!, compilation, verbose);

        if (!analysisResult.IsValid)
        {
            var errorMsg = $"Extraction validation failed:\n{string.Join("\n", analysisResult.Errors)}";
            if (analysisResult.Warnings.Any())
                errorMsg += $"\nWarnings:\n{string.Join("\n", analysisResult.Warnings)}";
            throw new McpException(errorMsg);
        }

        if (dryRun)
        {
            var dryRunMsg = $"DRY RUN: Would extract method '{methodName}' from {selectionRange} in {document.FilePath}";
            if (verbose && analysisResult.Warnings.Any())
                dryRunMsg += $"\nWarnings:\n{string.Join("\n", analysisResult.Warnings)}";
            return dryRunMsg;
        }

        var containingClass = containingMethod.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault() ?? throw new McpException("Containing class not found");
        var rewriter = new ExtractMethodRewriter(
            containingMethod, containingClass, statementsToExtract, methodName, analysisResult.DataFlow!);
        var newRoot = rewriter.Visit(syntaxRoot);

        // Semantic validation
        var newSyntaxTree = newRoot!.SyntaxTree;
        compilation = (await document.Project.GetCompilationAsync())!;
        var validationResult = _semanticValidator.Validate(newSyntaxTree, compilation);
        
        if (!validationResult.IsValid)
        {
            throw new McpException($"Semantic validation failed:\n{string.Join("\n", validationResult.Errors)}");
        }

        var formattedRoot = Formatter.Format(newRoot, document.Project.Solution.Workspace);
        await RefactoringHelpers.WriteAndUpdateCachesAsync(document, formattedRoot);

        var successMsg = $"Successfully extracted method '{methodName}' from {selectionRange} in {document.FilePath}";
        if (verbose && analysisResult.Warnings.Any())
            successMsg += $"\nWarnings:\n{string.Join("\n", analysisResult.Warnings)}";
        if (validationResult.Warnings.Any())
            successMsg += $"\nValidation warnings:\n{string.Join("\n", validationResult.Warnings)}";

        return successMsg;
    }

    private static Task<string> ExtractMethodSingleFile(string filePath, string selectionRange, string methodName, bool dryRun, bool verbose)
    {
        return RefactoringHelpers.ApplySingleFileEdit(
            filePath,
            text => ExtractMethodInSource(text, selectionRange, methodName, dryRun, verbose),
            $"Successfully extracted method '{methodName}' from {selectionRange} in {filePath} (single file mode)");
    }

    public static string ExtractMethodInSource(string sourceText, string selectionRange, string methodName, bool dryRun = false, bool verbose = false)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(sourceText);
        var syntaxRoot = syntaxTree.GetRoot();
        var tree = syntaxTree;
        var compilation = CSharpCompilation.Create("SingleFile")
            .AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
            .AddSyntaxTrees(tree);
        var semanticModel = compilation.GetSemanticModel(tree);
        var text = SourceText.From(sourceText);
        var span = RefactoringHelpers.ParseSelectionRange(text, selectionRange);

        var selectedNodes = syntaxRoot.DescendantNodes()
            .Where(n => span.Contains(n.Span))
            .ToList();

        if (!selectedNodes.Any())
            throw new McpException("Error: No valid code selected");

        var containingMethod = selectedNodes.First().Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        if (containingMethod == null)
            throw new McpException("Error: Selected code is not within a method");

        if (containingMethod.Body == null)
        {
            if (containingMethod.ExpressionBody != null)
                throw new McpException("Error: Extraction from expression-bodied methods is not supported");
            throw new McpException("Error: Selected code is not within a block-bodied method");
        }

        var statementsToExtract = containingMethod.Body.Statements
            .Where(s => span.IntersectsWith(s.FullSpan))
            .ToList();

        if (!statementsToExtract.Any())
            throw new McpException("Error: Selected code does not contain extractable statements");

        // Enhanced analysis
        var analysisResult = PerformPreExtractionAnalysis(
            statementsToExtract, containingMethod, semanticModel, compilation, verbose).Result;

        if (!analysisResult.IsValid)
        {
            var errorMsg = $"Extraction validation failed:\n{string.Join("\n", analysisResult.Errors)}";
            if (analysisResult.Warnings.Any())
                errorMsg += $"\nWarnings:\n{string.Join("\n", analysisResult.Warnings)}";
            throw new McpException(errorMsg);
        }

        if (dryRun)
        {
            var dryRunMsg = $"DRY RUN: Would extract method '{methodName}' from {selectionRange}";
            if (verbose && analysisResult.Warnings.Any())
                dryRunMsg += $"\nWarnings:\n{string.Join("\n", analysisResult.Warnings)}";
            return dryRunMsg;
        }

        var containingClass = containingMethod.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault() ?? throw new McpException("Containing class not found");
        var rewriter = new ExtractMethodRewriter(
            containingMethod, containingClass, statementsToExtract, methodName, analysisResult.DataFlow!);
        var newRoot = rewriter.Visit(syntaxRoot);

        var formattedRoot = Formatter.Format(newRoot!, RefactoringHelpers.SharedWorkspace);
        return formattedRoot.ToFullString();
    }

    private static Task<ExtractionAnalysisResult> PerformPreExtractionAnalysis(
        List<StatementSyntax> statementsToExtract,
        MethodDeclarationSyntax containingMethod,
        SemanticModel semanticModel,
        Compilation compilation,
        bool verbose)
    {
        var result = new ExtractionAnalysisResult { IsValid = true };

        // Data flow analysis
        var dataFlow = _dataFlowAnalyzer.AnalyzeStatements(statementsToExtract, semanticModel);
        result.DataFlow = dataFlow;

        // Edge case detection
        var edgeCaseAnalysis = _edgeCaseDetector.Detect(
            statementsToExtract.First().Parent!, semanticModel);

        result.Warnings.AddRange(edgeCaseAnalysis.Warnings);

        // Validate extraction feasibility
        // Captured variables are allowed as long as they can be passed as parameters to the new method
        // The old logic was too restrictive - it only allowed variables that were already parameters of the containing method
        if (dataFlow.HasCaptures)
        {
            // For now, we'll allow most captured variables since they can be passed as parameters
            // In the future, we could add more sophisticated analysis for ref/out, unsafe contexts etc.
            // This is a much more permissive and practical approach than the previous overly restrictive validation
            result.Warnings.Add($"Extracted code captures {dataFlow.CapturedVariables.Count} variable(s): {string.Join(", ", dataFlow.CapturedVariables)}. These will be passed as parameters to the new method.");
        }

        if (edgeCaseAnalysis.HasBreakContinue)
        {
            result.IsValid = false;
            result.Errors.Add("Cannot extract code containing break/continue statements");
        }

        if (edgeCaseAnalysis.HasReturn && statementsToExtract.Count < containingMethod.Body!.Statements.Count)
        {
            result.Warnings.Add("Extracted code contains return statements - this may require manual adjustment of the caller's control flow.");
        }

        // Additional warnings for verbose mode
        if (verbose)
        {
            if (dataFlow.HasInputs)
                result.Warnings.Add($"Method will require {dataFlow.InputVariables.Count} input parameters");
            if (dataFlow.HasOutputs)
                result.Warnings.Add($"Method will need to return or modify {dataFlow.OutputVariables.Count} output variables");
            if (edgeCaseAnalysis.HasAsyncAwait)
                result.Warnings.Add("Extracted method will be async and should return Task");
            if (edgeCaseAnalysis.HasTryCatch)
                result.Warnings.Add("Extracted method contains try/catch - consider error handling strategy");
        }

        return Task.FromResult(result);
    }
}

public class ExtractionAnalysisResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public DataFlowAnalysisResult? DataFlow { get; set; }
}
