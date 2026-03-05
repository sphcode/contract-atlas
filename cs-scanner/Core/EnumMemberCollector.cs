using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace ContractScanner.Core;

internal static class EnumMemberCollector
{
    public static EnumMemberInfo[]? Collect(INamedTypeSymbol typeSymbol)
    {
        if (typeSymbol.TypeKind != TypeKind.Enum)
        {
            return null;
        }

        var items = new List<EnumMemberInfo>();
        foreach (var member in typeSymbol.GetMembers())
        {
            if (member is not IFieldSymbol fieldSymbol || !fieldSymbol.HasConstantValue)
            {
                continue;
            }

            if (fieldSymbol.ConstantValue is null || fieldSymbol.Name == "value__")
            {
                continue;
            }

            items.Add(new EnumMemberInfo(fieldSymbol.Name, fieldSymbol.ConstantValue.ToString()!));
        }

        return items.Count > 0 ? items.ToArray() : null;
    }
}
