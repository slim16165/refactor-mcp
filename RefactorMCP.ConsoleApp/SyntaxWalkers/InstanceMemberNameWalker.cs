using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
namespace RefactorMCP.ConsoleApp.SyntaxWalkers
{

    internal class InstanceMemberNameWalker : NameCollectorWalker
    {
        public override void VisitFieldDeclaration(FieldDeclarationSyntax node)
        {
            if (node.Modifiers.Any(SyntaxKind.StaticKeyword))
            {
                base.VisitFieldDeclaration(node);
                return;
            }

            foreach (var variable in node.Declaration.Variables)
                Add(variable.Identifier.ValueText);
            base.VisitFieldDeclaration(node);
        }

        public override void VisitPropertyDeclaration(PropertyDeclarationSyntax node)
        {
            if (node.Modifiers.Any(SyntaxKind.StaticKeyword))
            {
                base.VisitPropertyDeclaration(node);
                return;
            }

            Add(node.Identifier.ValueText);
            base.VisitPropertyDeclaration(node);
        }
    }
}
