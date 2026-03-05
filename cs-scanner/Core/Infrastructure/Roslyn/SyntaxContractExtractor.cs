using System;
using System.Collections.Generic;
using System.Linq;
using ContractScanner.Core.Domain.Models;
using Microsoft.CodeAnalysis;
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

            var fullName = GetFullTypeName(typeDecl);
            if (string.IsNullOrWhiteSpace(fullName))
            {
                continue;
            }

            var dataMembers = typeDecl is TypeDeclarationSyntax typeDeclaration
                ? CollectDataMembers(matchedType, typeDeclaration)
                : null;

            var enumMembers = typeDecl is EnumDeclarationSyntax enumDeclaration
                ? CollectEnumMembers(enumDeclaration)
                : null;

            var operations = typeDecl is TypeDeclarationSyntax serviceType
                ? CollectOperationContracts(matchedType, serviceType)
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

        if (HasAttribute(typeDecl.AttributeLists, "ServiceContract"))
        {
            return "ServiceContract";
        }

        if (HasAttribute(typeDecl.AttributeLists, "DataContract"))
        {
            return "DataContract";
        }

        return null;
    }

    private static DataMemberInfo[]? CollectDataMembers(string matchedType, TypeDeclarationSyntax typeDecl)
    {
        if (!string.Equals(matchedType, "DataContract", StringComparison.Ordinal))
        {
            return null;
        }

        var members = new List<DataMemberInfo>();
        foreach (var member in typeDecl.Members)
        {
            switch (member)
            {
                case PropertyDeclarationSyntax property when HasAttribute(property.AttributeLists, "DataMember"):
                    members.Add(new DataMemberInfo(property.Identifier.Text, property.Type.ToString()));
                    break;
                case FieldDeclarationSyntax field when HasAttribute(field.AttributeLists, "DataMember"):
                    foreach (var variable in field.Declaration.Variables)
                    {
                        members.Add(new DataMemberInfo(variable.Identifier.Text, field.Declaration.Type.ToString()));
                    }
                    break;
            }
        }

        return members.Count > 0 ? members.ToArray() : null;
    }

    private static EnumMemberInfo[]? CollectEnumMembers(EnumDeclarationSyntax enumDecl)
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

    private static OperationContractInfo[]? CollectOperationContracts(string matchedType, TypeDeclarationSyntax typeDecl)
    {
        if (!string.Equals(matchedType, "ServiceContract", StringComparison.Ordinal))
        {
            return null;
        }

        var operations = new List<OperationContractInfo>();
        foreach (var method in typeDecl.Members.OfType<MethodDeclarationSyntax>())
        {
            if (!HasAttribute(method.AttributeLists, "OperationContract"))
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
        var isOut = parameter.Modifiers.Any(static m => m.IsKind(SyntaxKind.OutKeyword));
        var isRef = parameter.Modifiers.Any(static m => m.IsKind(SyntaxKind.RefKeyword));
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
                if (!IsAttributeNameMatch(attr.Name.ToString(), "OperationContract"))
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

    private static bool HasAttribute(SyntaxList<AttributeListSyntax> attributeLists, string expectedName)
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

    private static bool IsAttributeNameMatch(string actualName, string expectedName)
    {
        return string.Equals(actualName, expectedName, StringComparison.Ordinal)
            || string.Equals(actualName, $"{expectedName}Attribute", StringComparison.Ordinal)
            || actualName.EndsWith($".{expectedName}", StringComparison.Ordinal)
            || actualName.EndsWith($".{expectedName}Attribute", StringComparison.Ordinal);
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

    private static string GetFullTypeName(BaseTypeDeclarationSyntax typeDecl)
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
