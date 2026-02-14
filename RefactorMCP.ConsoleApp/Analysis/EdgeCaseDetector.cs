using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;


/// <summary>
/// Rileva edge cases che potrebbero invalidare il refactoring
/// </summary>
public class EdgeCaseDetector
{
    public EdgeCaseAnalysis Detect(SyntaxNode node, SemanticModel semanticModel)
    {
        var analysis = new EdgeCaseAnalysis();
        var walker = new EdgeCaseWalker(semanticModel);
        walker.Visit(node);

        analysis.HasAsyncAwait = walker.HasAwait;
        analysis.HasUsing = walker.HasUsing;
        analysis.HasTryCatch = walker.HasTryCatch;
        analysis.HasReturn = walker.HasReturn;
        analysis.HasBreakContinue = walker.HasBreakContinue;
        analysis.HasLambdas = walker.HasLambdas;
        analysis.HasLocalFunctions = walker.HasLocalFunctions;

        if (analysis.HasAsyncAwait) analysis.DetectedEdgeCases.Add("async/await");
        if (analysis.HasUsing) analysis.DetectedEdgeCases.Add("using/disposables");
        if (analysis.HasTryCatch) analysis.DetectedEdgeCases.Add("try/catch");
        if (analysis.HasReturn) analysis.DetectedEdgeCases.Add("return statements");
        if (analysis.HasBreakContinue)
        {
            analysis.DetectedEdgeCases.Add("break/continue");
            analysis.Warnings.Add("Break/Continue statements detected. Extraction may be invalid if block is part of a loop.");
        }
        if (analysis.HasLambdas) analysis.DetectedEdgeCases.Add("lambdas/closures");
        if (analysis.HasLocalFunctions) analysis.DetectedEdgeCases.Add("local functions");

        return analysis;
    }

    private class EdgeCaseWalker : CSharpSyntaxWalker
    {
        private readonly SemanticModel _semanticModel;
        public bool HasAwait { get; private set; }
        public bool HasUsing { get; private set; }
        public bool HasTryCatch { get; private set; }
        public bool HasReturn { get; private set; }
        public bool HasBreakContinue { get; private set; }
        public bool HasLambdas { get; private set; }
        public bool HasLocalFunctions { get; private set; }

        public EdgeCaseWalker(SemanticModel semanticModel)
        {
            _semanticModel = semanticModel;
        }

        public override void VisitAwaitExpression(AwaitExpressionSyntax node) { HasAwait = true; base.VisitAwaitExpression(node); }
        public override void VisitUsingStatement(UsingStatementSyntax node) { HasUsing = true; base.VisitUsingStatement(node); }
        public override void VisitTryStatement(TryStatementSyntax node) { HasTryCatch = true; base.VisitTryStatement(node); }
        public override void VisitReturnStatement(ReturnStatementSyntax node) { HasReturn = true; base.VisitReturnStatement(node); }
        public override void VisitBreakStatement(BreakStatementSyntax node) { HasBreakContinue = true; base.VisitBreakStatement(node); }
        public override void VisitContinueStatement(ContinueStatementSyntax node) { HasBreakContinue = true; base.VisitContinueStatement(node); }
        public override void VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node) { HasLambdas = true; base.VisitParenthesizedLambdaExpression(node); }
        public override void VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node) { HasLambdas = true; base.VisitSimpleLambdaExpression(node); }
        public override void VisitLocalFunctionStatement(LocalFunctionStatementSyntax node) { HasLocalFunctions = true; base.VisitLocalFunctionStatement(node); }
    }
}

public class EdgeCaseAnalysis
{
    public bool HasAsyncAwait { get; set; }
    public bool HasUsing { get; set; }
    public bool HasTryCatch { get; set; }
    public bool HasReturn { get; set; }
    public bool HasBreakContinue { get; set; }
    public bool HasLambdas { get; set; }
    public bool HasLocalFunctions { get; set; }
    public List<string> DetectedEdgeCases { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}
