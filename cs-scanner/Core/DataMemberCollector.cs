using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ContractScanner.Core;

internal static class DataMemberCollector
{
    private const string DataMemberAttribute = "global::System.Runtime.Serialization.DataMemberAttribute";
    private static readonly SymbolDisplayFormat MemberTypeFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions:
            SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
            SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers |
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    public static DataMemberInfo[]? CollectMembers(string contractType, INamedTypeSymbol typeSymbol)
    {
        if (!string.Equals(contractType, "DataContract", StringComparison.Ordinal))
        {
            return null;
        }

        var members = new List<DataMemberInfo>();
        foreach (var memberSymbol in typeSymbol.GetMembers())
        {
            if (memberSymbol is IFieldSymbol or IPropertySymbol)
            {
                foreach (var attr in memberSymbol.GetAttributes())
                {
                    if (IsDataMemberAttribute(attr))
                    {
                        members.Add(new DataMemberInfo(
                            memberSymbol.Name,
                            GetMemberTypeName(memberSymbol)));
                        break;
                    }
                }
            }
        }

        return members.Count > 0 ? members.ToArray() : null;
    }

    private static bool IsDataMemberAttribute(AttributeData attribute)
    {
        var symbolName = attribute.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (string.Equals(symbolName, DataMemberAttribute, StringComparison.Ordinal))
        {
            return true;
        }

        var syntax = attribute.ApplicationSyntaxReference?.GetSyntax() as AttributeSyntax;
        if (syntax is null)
        {
            return false;
        }

        var syntaxName = syntax.Name.ToString();
        return string.Equals(syntaxName, "DataMember", StringComparison.Ordinal)
            || string.Equals(syntaxName, "DataMemberAttribute", StringComparison.Ordinal)
            || syntaxName.EndsWith(".DataMember", StringComparison.Ordinal)
            || syntaxName.EndsWith(".DataMemberAttribute", StringComparison.Ordinal);
    }

    private static string GetMemberTypeName(ISymbol memberSymbol)
    {
        var typeSymbol = memberSymbol switch
        {
            IFieldSymbol field => field.Type,
            IPropertySymbol property => property.Type,
            _ => null
        };

        return typeSymbol?.ToDisplayString(MemberTypeFormat) ?? "unknown";
    }
}
