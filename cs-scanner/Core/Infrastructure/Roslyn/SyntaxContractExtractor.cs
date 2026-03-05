using System.Collections.Generic;
using System.Linq;
using ContractScanner.Core.Domain.Models;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ContractScanner.Core.Infrastructure.Roslyn;

internal static class SyntaxContractExtractor
{
    public static IReadOnlyList<ScanResult> Extract(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var root = syntaxTree.GetRoot();
        var results = new List<ScanResult>();

        foreach (var typeDecl in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
        {
            var matchedType = GetMatchedType(typeDecl);
            if (matchedType is null)
            {
                continue;
            }

            var fullName = SyntaxTypeNameResolver.GetFullTypeName(typeDecl);
            if (string.IsNullOrWhiteSpace(fullName))
            {
                continue;
            }

            var dataMembers = typeDecl is TypeDeclarationSyntax typeDeclaration
                ? SyntaxDataMemberCollector.Collect(matchedType, typeDeclaration)
                : null;

            var enumMembers = typeDecl is EnumDeclarationSyntax enumDeclaration
                ? SyntaxEnumMemberCollector.Collect(enumDeclaration)
                : null;

            var operations = typeDecl is TypeDeclarationSyntax serviceType
                ? SyntaxOperationContractCollector.Collect(matchedType, serviceType)
                : null;

            results.Add(new ScanResult(matchedType, fullName, dataMembers, enumMembers, operations));
        }

        return results;
    }

    private static string? GetMatchedType(BaseTypeDeclarationSyntax typeDecl)
    {
        if (typeDecl is EnumDeclarationSyntax)
        {
            return "Enum";
        }

        if (SyntaxAttributeMatcher.HasAttribute(typeDecl.AttributeLists, "ServiceContract"))
        {
            return "ServiceContract";
        }

        if (SyntaxAttributeMatcher.HasAttribute(typeDecl.AttributeLists, "DataContract"))
        {
            return "DataContract";
        }

        return null;
    }
}
