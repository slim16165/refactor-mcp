using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

/// <summary>
/// Enhanced ExtractMethod rewriter with data flow analysis integration
/// </summary>
public class ExtractMethodRewriter : CSharpSyntaxRewriter
{
    private static readonly SyntaxKind[] DisallowedControlFlow = new[] { 
        SyntaxKind.ReturnStatement, SyntaxKind.BreakStatement, SyntaxKind.ContinueStatement, 
        SyntaxKind.GotoStatement, SyntaxKind.YieldReturnStatement, SyntaxKind.YieldBreakStatement };

    private readonly MethodDeclarationSyntax _containingMethod;
    private readonly ClassDeclarationSyntax _containingClass;
    private readonly List<StatementSyntax> _statementsToExtract;
    private readonly string _methodName;
    private readonly DataFlowAnalysisResult _dataFlow;

    public ExtractMethodRewriter(
        MethodDeclarationSyntax containingMethod,
        ClassDeclarationSyntax containingClass,
        List<StatementSyntax> statementsToExtract,
        string methodName,
        DataFlowAnalysisResult dataFlow)
    {
        _containingMethod = containingMethod;
        _containingClass = containingClass;
        _statementsToExtract = statementsToExtract;
        _methodName = methodName;
        _dataFlow = dataFlow;
    }

    public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        if (node.Span != _containingMethod.Span)
            return base.VisitMethodDeclaration(node);

        // Validate control flow before proceeding
        if (ContainsDisallowedControlFlow(_statementsToExtract))
            throw new McpException("Error: Cannot extract a region containing return/break/continue/goto/yield. Narrow the selection (v2 limitation).");

        // Create the new extracted method
        var extractedMethod = CreateExtractedMethod();
        
        // Replace extracted statements with method call
        var methodCall = CreateMethodCall();
        var newBody = ReplaceStatementsWithCall(node.Body!, methodCall);

        return node.WithBody(newBody)
                  .WithAdditionalAnnotations(Formatter.Annotation);
    }

    public override SyntaxNode? VisitClassDeclaration(ClassDeclarationSyntax node)
    {
        if (node.Span != _containingClass.Span)
            return base.VisitClassDeclaration(node);

        // Visit members first to replace statements in the existing method
        var visitedNode = (ClassDeclarationSyntax)base.VisitClassDeclaration(node)!;
        
        // Add the extracted method to the class
        var extractedMethod = CreateExtractedMethod();
        var newMembers = visitedNode.Members.Add(extractedMethod);

        return visitedNode.WithMembers(newMembers)
                        .WithAdditionalAnnotations(Formatter.Annotation);
    }

    private MethodDeclarationSyntax CreateExtractedMethod()
    {
        // Determine return type and parameters based on data flow analysis
        var isAsync = _statementsToExtract.Any(s => s.DescendantNodesAndSelf().OfType<AwaitExpressionSyntax>().Any());
        var returnType = DetermineReturnType(_dataFlow, _containingMethod.ReturnType, isAsync);
        var parameters = CreateParameters();
        var body = SyntaxFactory.Block(_statementsToExtract);

        // Add return statement if method produces output
        if (_dataFlow.HasOutputs)
        {
            body = AddReturnStatement(body);
        }

        var modifiers = new List<SyntaxToken> { SyntaxFactory.Token(SyntaxKind.PrivateKeyword) };
        if (isAsync)
        {
            modifiers.Add(SyntaxFactory.Token(SyntaxKind.AsyncKeyword));
        }

        var method = SyntaxFactory.MethodDeclaration(
                returnType,
                SyntaxFactory.Identifier(_methodName))
            .WithParameterList(parameters)
            .WithBody(body)
            .WithModifiers(SyntaxFactory.TokenList(modifiers))
            .WithAttributeLists(SyntaxFactory.List<AttributeListSyntax>())
            .WithConstraintClauses(SyntaxFactory.List<TypeParameterConstraintClauseSyntax>())
            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.None));

        return method.WithAdditionalAnnotations(Formatter.Annotation);
    }

    private static TypeSyntax DetermineReturnType(DataFlowAnalysisResult dataFlow, TypeSyntax containingReturnType, bool isAsync)
    {
        // v2: return inside selection is disallowed -> return type depends ONLY on outputs + async
        TypeSyntax baseType = SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.VoidKeyword));

        if (dataFlow.HasOutputs)
        {
            baseType = dataFlow.OutputVariables.Count == 1
                ? SyntaxFactory.ParseTypeName(dataFlow.VariableTypes[dataFlow.OutputVariables[0]])
                : SyntaxFactory.IdentifierName("object");
        }

        return WrapInTaskIfNeeded(baseType, isAsync);
    }

    private static TypeSyntax WrapInTaskIfNeeded(TypeSyntax baseType, bool isAsync)
    {
        if (!isAsync) return baseType;

        if (IsTaskType(baseType)) return baseType;

        if (IsVoid(baseType))
            return SyntaxFactory.IdentifierName("Task");

        return SyntaxFactory.GenericName("Task")
            .WithTypeArgumentList(SyntaxFactory.TypeArgumentList(
                SyntaxFactory.SingletonSeparatedList(baseType)));
    }

    private static bool IsVoid(TypeSyntax t)
        => t is PredefinedTypeSyntax pts && pts.Keyword.IsKind(SyntaxKind.VoidKeyword);

    private static bool IsTaskType(TypeSyntax t)
        => (t is IdentifierNameSyntax ins && ins.Identifier.Text == "Task")
           || (t is GenericNameSyntax gns && gns.Identifier.Text == "Task");

    private static bool ContainsDisallowedControlFlow(IEnumerable<StatementSyntax> statements)
        => statements.SelectMany(s => s.DescendantNodesAndSelf())
            .Any(n => DisallowedControlFlow.Contains(n.Kind()));

    private ParameterListSyntax CreateParameters()
    {
        var parameters = new List<ParameterSyntax>();

        foreach (var inputVar in _dataFlow.InputVariables)
        {
            // Skip if the variable is declared inside the extraction region
            if (_dataFlow.DeclaredInsideRegion.Contains(inputVar))
                continue;

            string typeName = "object";
            if (_dataFlow.VariableTypes.TryGetValue(inputVar, out var type))
            {
                typeName = type;
            }

            var parameter = SyntaxFactory.Parameter(
                attributeLists: new SyntaxList<AttributeListSyntax>(),
                modifiers: new SyntaxTokenList(),
                type: SyntaxFactory.ParseTypeName(typeName),
                identifier: SyntaxFactory.Identifier(inputVar),
                @default: null);

            parameters.Add(parameter);
        }

        return SyntaxFactory.ParameterList(
            SyntaxFactory.SeparatedList(parameters));
    }

    private BlockSyntax AddReturnStatement(BlockSyntax body)
    {
        // Simple implementation - return the last output variable
        var lastOutput = _dataFlow.OutputVariables.LastOrDefault();
        if (lastOutput != null)
        {
            var returnStatement = SyntaxFactory.ReturnStatement(
                SyntaxFactory.IdentifierName(lastOutput));

            return body.AddStatements(returnStatement);
        }

        return body;
    }

    private BlockSyntax ReplaceStatementsWithCall(BlockSyntax originalBody, InvocationExpressionSyntax methodCall)
    {
        var newStatements = new List<StatementSyntax>();
        bool isAsync = _statementsToExtract.Any(s => s.DescendantNodesAndSelf().OfType<AwaitExpressionSyntax>().Any());

        ExpressionSyntax finalCall = methodCall;
        if (isAsync)
        {
            finalCall = SyntaxFactory.AwaitExpression(methodCall);
        }

        StatementSyntax callStatement;
        if (_dataFlow.HasOutputs && _dataFlow.OutputVariables.Count == 1)
        {
            // Handle single output variable assignment
            var outputVar = _dataFlow.OutputVariables[0];
            var assignment = SyntaxFactory.AssignmentExpression(
                SyntaxKind.SimpleAssignmentExpression,
                SyntaxFactory.IdentifierName(outputVar),
                finalCall);
            
            callStatement = SyntaxFactory.ExpressionStatement(assignment);
        }
        else
        {
            callStatement = SyntaxFactory.ExpressionStatement(finalCall);
        }
        
        bool replaced = false;
        foreach (var statement in originalBody.Statements)
        {
            if (_statementsToExtract.Any(s => s.Span == statement.Span))
            {
                if (!replaced)
                {
                    newStatements.Add(callStatement);
                    replaced = true;
                }
            }
            else
            {
                newStatements.Add(statement);
            }
        }

        return SyntaxFactory.Block(newStatements);
    }

    private InvocationExpressionSyntax CreateMethodCall()
    {
        var arguments = new List<ArgumentSyntax>();

        foreach (var inputVar in _dataFlow.InputVariables)
        {
            if (_dataFlow.DeclaredInsideRegion.Contains(inputVar))
                continue;

            var argument = SyntaxFactory.Argument(
                SyntaxFactory.IdentifierName(inputVar));
            arguments.Add(argument);
        }

        var argumentList = SyntaxFactory.ArgumentList(
            SyntaxFactory.SeparatedList(arguments));

        return SyntaxFactory.InvocationExpression(
                SyntaxFactory.IdentifierName(_methodName),
                argumentList)
            .WithAdditionalAnnotations(Formatter.Annotation);
    }
}
