using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ContractScanner.Core.Infrastructure.Roslyn;

internal static class SyntaxTypeNameResolver
{
    public static string GetFullTypeName(BaseTypeDeclarationSyntax typeDecl)
    {
        var typeNames = new Stack<string>();
        typeNames.Push(typeDecl.Identifier.Text);

        for (SyntaxNode? parent = typeDecl.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is TypeDeclarationSyntax parentType)
            {
                typeNames.Push(parentType.Identifier.Text);
                continue;
            }

            if (parent is NamespaceDeclarationSyntax ns)
            {
                return $"{ns.Name}.{string.Join(".", typeNames)}";
            }

            if (parent is FileScopedNamespaceDeclarationSyntax fileNs)
            {
                return $"{fileNs.Name}.{string.Join(".", typeNames)}";
            }
        }

        return string.Join(".", typeNames);
    }
}
