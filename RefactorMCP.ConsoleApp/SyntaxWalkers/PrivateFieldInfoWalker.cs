using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
namespace RefactorMCP.ConsoleApp.SyntaxWalkers
{

    internal class PrivateFieldInfoWalker : CSharpSyntaxWalker
    {
        public Dictionary<string, TypeSyntax> Infos { get; } = new();

        public override void VisitFieldDeclaration(FieldDeclarationSyntax node)
        {
            if (IsPrivateOrImplicitPrivate(node))
            {
                foreach (var variable in node.Declaration.Variables)
                    Infos[variable.Identifier.ValueText] = node.Declaration.Type;
            }
            base.VisitFieldDeclaration(node);
        }

        private static bool IsPrivateOrImplicitPrivate(FieldDeclarationSyntax node)
        {
            if (node.Modifiers.Any(SyntaxKind.PrivateKeyword))
                return true;

            var hasExplicitAccessModifier = node.Modifiers.Any(SyntaxKind.PublicKeyword)
                || node.Modifiers.Any(SyntaxKind.ProtectedKeyword)
                || node.Modifiers.Any(SyntaxKind.InternalKeyword);

            return !hasExplicitAccessModifier;
        }
    }
}
