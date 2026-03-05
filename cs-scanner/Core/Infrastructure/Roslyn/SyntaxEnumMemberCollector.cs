using System;
using System.Collections.Generic;
using ContractScanner.Core.Domain.Models;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ContractScanner.Core.Infrastructure.Roslyn;

internal static class SyntaxEnumMemberCollector
{
    public static EnumMemberInfo[]? Collect(EnumDeclarationSyntax enumDecl)
    {
        var members = new List<EnumMemberInfo>();
        long current = -1;
        var hasNumeric = true;

        foreach (var item in enumDecl.Members)
        {
            string value;
            if (item.EqualsValue is null)
            {
                if (hasNumeric)
                {
                    current++;
                    value = current.ToString();
                }
                else
                {
                    value = string.Empty;
                }
            }
            else
            {
                var exprText = item.EqualsValue.Value.ToString();
                if (TryParseIntegralLiteral(exprText, out var parsed))
                {
                    current = parsed;
                    hasNumeric = true;
                    value = parsed.ToString();
                }
                else
                {
                    hasNumeric = false;
                    value = exprText;
                }
            }

            members.Add(new EnumMemberInfo(item.Identifier.Text, value));
        }

        return members.Count > 0 ? members.ToArray() : null;
    }

    private static bool TryParseIntegralLiteral(string text, out long value)
    {
        var normalized = text.Replace("_", string.Empty, StringComparison.Ordinal).Trim();
        if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return long.TryParse(normalized.AsSpan(2), System.Globalization.NumberStyles.HexNumber, null, out value);
        }

        return long.TryParse(normalized, out value);
    }
}
