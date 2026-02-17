using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
namespace RefactorMCP.ConsoleApp.SyntaxWalkers
{

    internal class MethodAnalysisWalker : CSharpSyntaxWalker
    {
        private readonly HashSet<string> _instanceMembers;
        private readonly HashSet<string> _methodNames;
        private readonly string _methodName;

        public bool UsesInstanceMembers { get; private set; }
        public bool CallsOtherMethods { get; private set; }
        public bool IsRecursive { get; private set; }

        public MethodAnalysisWalker(HashSet<string> instanceMembers, HashSet<string> methodNames, string methodName)
        {
            _instanceMembers = instanceMembers;
            _methodNames = methodNames;
            _methodName = methodName;
        }

        public override void VisitIdentifierName(IdentifierNameSyntax node)
        {
            if (_instanceMembers.Contains(node.Identifier.ValueText))
            {
                var parent = node.Parent;
                if (parent is MemberAccessExpressionSyntax ma)
                {
                    if (ma.Name == node && ma.Expression is ThisExpressionSyntax)
                        UsesInstanceMembers = true;
                }
                else
                {
                    UsesInstanceMembers = true;
                }
            }

            if (_methodNames.Contains(node.Identifier.ValueText))
            {
                var parent = node.Parent;
                if (parent is not InvocationExpressionSyntax &&
                    (parent is not MemberAccessExpressionSyntax ||
                     (parent is MemberAccessExpressionSyntax ma && ma.Expression is ThisExpressionSyntax)))
                {
                    if (node.Identifier.ValueText == _methodName)
                        IsRecursive = true;
                    else
                        CallsOtherMethods = true;
                }
            }

            base.VisitIdentifierName(node);
        }

        public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            var invokedName = node.Expression switch
            {
                IdentifierNameSyntax id => id.Identifier.ValueText,
                MemberAccessExpressionSyntax ma => ma.Name.Identifier.ValueText,
                _ => null
            };

            if (invokedName != null && _methodNames.Contains(invokedName))
            {
                if (invokedName == _methodName)
                {
                    IsRecursive = true;
                }
                else
                {
                    CallsOtherMethods = true;
                }
            }
            base.VisitInvocationExpression(node);
        }
    }
}
