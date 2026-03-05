using System;
using System.Collections.Generic;
using System.Linq;
using ContractScanner.Core.Domain.Models;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ContractScanner.Core.Infrastructure.Roslyn;

internal static class SyntaxOperationContractCollector
{
    public static OperationContractInfo[]? Collect(string matchedType, TypeDeclarationSyntax typeDecl)
    {
        if (!string.Equals(matchedType, "ServiceContract", StringComparison.Ordinal))
        {
            return null;
        }

        var operations = new List<OperationContractInfo>();
        foreach (var method in typeDecl.Members.OfType<MethodDeclarationSyntax>())
        {
            if (!SyntaxAttributeMatcher.HasAttribute(method.AttributeLists, "OperationContract"))
            {
                continue;
            }

            var returnType = method.ReturnType.ToString();
            var parameters = method.ParameterList.Parameters
                .Select(ToParameterInfo)
                .ToArray();
            var isOneWay = GetOperationIsOneWay(method);

            operations.Add(new OperationContractInfo(
                method.Identifier.Text,
                returnType,
                GetEffectiveReturnType(returnType),
                isOneWay,
                parameters));
        }

        return operations.Count > 0
            ? operations.OrderBy(static op => op.Name, StringComparer.Ordinal).ToArray()
            : null;
    }

    private static OperationParameterInfo ToParameterInfo(ParameterSyntax parameter)
    {
        var isOut = parameter.Modifiers.Any(static m => m.Kind() == SyntaxKind.OutKeyword);
        var isRef = parameter.Modifiers.Any(static m => m.Kind() == SyntaxKind.RefKeyword);
        return new OperationParameterInfo(
            parameter.Identifier.Text,
            parameter.Type?.ToString() ?? "unknown",
            isOut,
            isRef,
            parameter.Default is not null);
    }

    private static bool GetOperationIsOneWay(MethodDeclarationSyntax method)
    {
        foreach (var list in method.AttributeLists)
        {
            foreach (var attr in list.Attributes)
            {
                if (!SyntaxAttributeMatcher.IsAttributeNameMatch(attr.Name.ToString(), "OperationContract"))
                {
                    continue;
                }

                if (attr.ArgumentList is null)
                {
                    return false;
                }

                foreach (var arg in attr.ArgumentList.Arguments)
                {
                    var argName = arg.NameEquals?.Name.Identifier.Text;
                    if (!string.Equals(argName, "IsOneWay", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    return string.Equals(arg.Expression.ToString(), "true", StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        return false;
    }

    private static string GetEffectiveReturnType(string returnType)
    {
        var normalized = returnType.Trim();
        if (IsSimpleTypeMatch(normalized, "Task") || IsSimpleTypeMatch(normalized, "ValueTask"))
        {
            return "void";
        }

        if (TryGetSingleGenericArg(normalized, "Task", out var taskArg))
        {
            return taskArg;
        }

        if (TryGetSingleGenericArg(normalized, "ValueTask", out var valueTaskArg))
        {
            return valueTaskArg;
        }

        return normalized;
    }

    private static bool IsSimpleTypeMatch(string typeName, string candidate)
    {
        return string.Equals(typeName, candidate, StringComparison.Ordinal)
            || typeName.EndsWith($".{candidate}", StringComparison.Ordinal);
    }

    private static bool TryGetSingleGenericArg(string typeName, string genericName, out string arg)
    {
        var marker = $"{genericName}<";
        var index = typeName.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0)
        {
            marker = $".{genericName}<";
            index = typeName.IndexOf(marker, StringComparison.Ordinal);
            if (index < 0)
            {
                arg = string.Empty;
                return false;
            }
        }

        var start = typeName.IndexOf('<', index);
        var end = FindMatchingAngle(typeName, start);
        if (start < 0 || end < 0 || end <= start + 1)
        {
            arg = string.Empty;
            return false;
        }

        var content = typeName.Substring(start + 1, end - start - 1).Trim();
        if (string.IsNullOrEmpty(content) || content.Contains(',', StringComparison.Ordinal))
        {
            arg = string.Empty;
            return false;
        }

        arg = content;
        return true;
    }

    private static int FindMatchingAngle(string value, int leftIndex)
    {
        if (leftIndex < 0)
        {
            return -1;
        }

        var depth = 0;
        for (var i = leftIndex; i < value.Length; i++)
        {
            if (value[i] == '<')
            {
                depth++;
            }
            else if (value[i] == '>')
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return -1;
    }
}
