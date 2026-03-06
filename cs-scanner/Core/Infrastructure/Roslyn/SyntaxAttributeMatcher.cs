using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ContractScanner.Core.Infrastructure.Roslyn;

internal static class SyntaxAttributeMatcher
{
    public static bool HasAttribute(SyntaxList<AttributeListSyntax> attributeLists, string expectedName)
    {
        foreach (var list in attributeLists)
        {
            foreach (var attribute in list.Attributes)
            {
                if (IsAttributeNameMatch(attribute.Name.ToString(), expectedName))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static bool IsAttributeNameMatch(string actualName, string expectedName)
    {
        return string.Equals(actualName, expectedName, StringComparison.Ordinal)
            || string.Equals(actualName, $"{expectedName}Attribute", StringComparison.Ordinal)
            || actualName.EndsWith($".{expectedName}", StringComparison.Ordinal)
            || actualName.EndsWith($".{expectedName}Attribute", StringComparison.Ordinal);
    }
}
