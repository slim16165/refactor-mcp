using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;


/// <summary>
/// Analizza il flusso di dati per identificare dipendenze tra variabili
/// </summary>
public class DataFlowAnalyzer
{
    /// <summary>
    /// Analizza il flusso di dati di un blocco di codice
    /// </summary>
    public DataFlowAnalysisResult Analyze(SyntaxNode block, SemanticModel semanticModel)
    {
        var result = new DataFlowAnalysisResult();

        var dataFlow = semanticModel.AnalyzeDataFlow(block);
        PopulateFromDataFlow(result, dataFlow);
        CollectVariableUsage(result, semanticModel, block);
        Normalize(result);

        return result;
    }

    /// <summary>
    /// Analizza il flusso di dati su un range di statement contigui.
    /// </summary>
    public DataFlowAnalysisResult AnalyzeStatements(IReadOnlyList<StatementSyntax> statements, SemanticModel semanticModel)
    {
        var result = new DataFlowAnalysisResult();
        if (statements.Count == 0)
        {
            return result;
        }

        var dataFlow = semanticModel.AnalyzeDataFlow(statements.First(), statements.Last());
        PopulateFromDataFlow(result, dataFlow);

        foreach (var statement in statements)
        {
            CollectVariableUsage(result, semanticModel, statement);
        }

        Normalize(result);
        return result;
    }

    private void PopulateFromDataFlow(DataFlowAnalysisResult result, DataFlowAnalysis? dataFlow)
    {
        if (dataFlow == null || !dataFlow.Succeeded)
        {
            return;
        }

        foreach (var symbol in dataFlow.DataFlowsIn)
        {
            if ((symbol.Kind == SymbolKind.Local || symbol.Kind == SymbolKind.Parameter) && symbol.Name != "this")
            {
                result.InputVariables.Add(symbol.Name);
                if (symbol is ILocalSymbol local)
                    result.VariableTypes[symbol.Name] = local.Type.ToDisplayString();
                else if (symbol is IParameterSymbol param)
                    result.VariableTypes[symbol.Name] = param.Type.ToDisplayString();
            }
        }

        foreach (var symbol in dataFlow.DataFlowsOut)
        {
            if (symbol.Kind == SymbolKind.Local || symbol.Kind == SymbolKind.Parameter)
            {
                result.OutputVariables.Add(symbol.Name);
            }
        }

        foreach (var symbol in dataFlow.AlwaysAssigned)
        {
            if (symbol.Kind == SymbolKind.Local && !result.OutputVariables.Contains(symbol.Name))
            {
                result.LocalVariables.Add(symbol.Name);
            }
        }

        foreach (var symbol in dataFlow.Captured)
        {
            if (symbol.Kind == SymbolKind.Local || symbol.Kind == SymbolKind.Parameter)
            {
                result.CapturedVariables.Add(symbol.Name);
            }
        }

        foreach (var symbol in dataFlow.VariablesDeclared)
        {
            if (symbol.Kind == SymbolKind.Local)
            {
                result.DeclaredInsideRegion.Add(symbol.Name);
            }
        }
    }

    private void CollectVariableUsage(DataFlowAnalysisResult result, SemanticModel semanticModel, SyntaxNode block)
    {
        var walker = new VariableUsageWalker(semanticModel);
        walker.Visit(block);

        foreach (var variable in walker.UsedVariables)
        {
            if (!result.InputVariables.Contains(variable.Name) &&
                !result.OutputVariables.Contains(variable.Name) &&
                !result.LocalVariables.Contains(variable.Name))
            {
                result.InputVariables.Add(variable.Name);
                result.VariableTypes[variable.Name] = variable.Type;
            }
        }
    }

    private static void Normalize(DataFlowAnalysisResult result)
    {
        result.InputVariables = result.InputVariables.Distinct(StringComparer.Ordinal).OrderBy(v => v).ToList();
        result.OutputVariables = result.OutputVariables.Distinct(StringComparer.Ordinal).OrderBy(v => v).ToList();
        result.LocalVariables = result.LocalVariables.Distinct(StringComparer.Ordinal).OrderBy(v => v).ToList();
        result.CapturedVariables = result.CapturedVariables.Distinct(StringComparer.Ordinal).OrderBy(v => v).ToList();
        result.DeclaredInsideRegion = result.DeclaredInsideRegion.Distinct(StringComparer.Ordinal).OrderBy(v => v).ToList();
    }

    /// <summary>
    /// Walker per identificare variabili usate
    /// </summary>
    private class VariableUsageWalker : CSharpSyntaxWalker
    {
        private readonly SemanticModel _semanticModel;
        private readonly HashSet<(string Name, string Type)> _usedVariables = new();

        public HashSet<(string Name, string Type)> UsedVariables => _usedVariables;

        public VariableUsageWalker(SemanticModel semanticModel)
        {
            _semanticModel = semanticModel;
        }

        public override void VisitIdentifierName(Microsoft.CodeAnalysis.CSharp.Syntax.IdentifierNameSyntax node)
        {
            var symbol = _semanticModel.GetSymbolInfo(node).Symbol;
            if (symbol != null && (symbol.Kind == SymbolKind.Local || symbol.Kind == SymbolKind.Parameter) && symbol.Name != "this")
            {
                string type = "object";
                if (symbol is ILocalSymbol local) type = local.Type.ToDisplayString();
                else if (symbol is IParameterSymbol param) type = param.Type.ToDisplayString();
                _usedVariables.Add((symbol.Name, type));
            }
            base.VisitIdentifierName(node);
        }
    }
}

/// <summary>
/// Risultato dell'analisi del flusso di dati
/// </summary>
public class DataFlowAnalysisResult
{
    public List<string> InputVariables { get; set; } = new();
    public Dictionary<string, string> VariableTypes { get; set; } = new();
    public List<string> OutputVariables { get; set; } = new();
    public List<string> LocalVariables { get; set; } = new();
    public List<string> CapturedVariables { get; set; } = new();
    public List<string> DeclaredInsideRegion { get; set; } = new();

    public bool HasInputs => InputVariables.Count > 0;
    public bool HasOutputs => OutputVariables.Count > 0;
    public bool HasLocals => LocalVariables.Count > 0;
    public bool HasCaptures => CapturedVariables.Count > 0;
}
