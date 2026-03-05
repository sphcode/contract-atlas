using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ContractScanner.Core.Infrastructure.Roslyn;

internal static class ContractAttributeMatcher
{
    private const string ServiceContractAttribute = "global::System.ServiceModel.ServiceContractAttribute";
    private const string DataContractAttribute = "global::System.Runtime.Serialization.DataContractAttribute";

    public static (string Type, string Name)? GetMatch(INamedTypeSymbol typeSymbol)
    {
        foreach (var attribute in typeSymbol.GetAttributes())
        {
            if (IsAttributeMatch(attribute, ServiceContractAttribute, "ServiceContract"))
            {
                return ("ServiceContract", typeSymbol.Name);
            }

            if (IsAttributeMatch(attribute, DataContractAttribute, "DataContract"))
            {
                return ("DataContract", typeSymbol.Name);
            }
        }

        return null;
    }

    private static bool IsAttributeMatch(AttributeData attribute, string fullyQualifiedName, string simpleName)
    {
        var symbolName = attribute.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (string.Equals(symbolName, fullyQualifiedName, StringComparison.Ordinal))
        {
            return true;
        }

        var syntax = attribute.ApplicationSyntaxReference?.GetSyntax() as AttributeSyntax;
        if (syntax is null)
        {
            return false;
        }

        var syntaxName = syntax.Name.ToString();
        return string.Equals(syntaxName, simpleName, StringComparison.Ordinal)
            || string.Equals(syntaxName, $"{simpleName}Attribute", StringComparison.Ordinal)
            || syntaxName.EndsWith($".{simpleName}", StringComparison.Ordinal)
            || syntaxName.EndsWith($".{simpleName}Attribute", StringComparison.Ordinal);
    }
}
