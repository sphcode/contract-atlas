using System;
using System.Collections.Generic;
using System.Linq;
using ContractScanner.Core.Domain.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ContractScanner.Core.Infrastructure.Roslyn;

internal static class ServiceContractOperationCollector
{
    private const string OperationContractAttribute = "global::System.ServiceModel.OperationContractAttribute";
    private const string TaskType = "global::System.Threading.Tasks.Task";
    private const string ValueTaskType = "global::System.Threading.Tasks.ValueTask";

    private static readonly SymbolDisplayFormat TypeNameFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions:
            SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
            SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers |
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    public static OperationContractInfo[]? CollectOperations(string contractType, INamedTypeSymbol typeSymbol)
    {
        if (!string.Equals(contractType, "ServiceContract", StringComparison.Ordinal))
        {
            return null;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var methods = new List<IMethodSymbol>();

        AddMethods(typeSymbol, methods, seen);
        foreach (var iface in typeSymbol.AllInterfaces)
        {
            AddMethods(iface, methods, seen);
        }

        if (methods.Count == 0)
        {
            return null;
        }

        var operations = methods
            .OrderBy(static m => m.Name, StringComparer.Ordinal)
            .Select(ToOperationInfo)
            .ToArray();

        return operations.Length > 0 ? operations : null;
    }

    private static void AddMethods(INamedTypeSymbol typeSymbol, ICollection<IMethodSymbol> methods, ISet<string> seen)
    {
        foreach (var method in typeSymbol.GetMembers().OfType<IMethodSymbol>())
        {
            if (method.MethodKind != MethodKind.Ordinary)
            {
                continue;
            }

            if (!HasOperationContract(method))
            {
                continue;
            }

            var signature = method.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
            if (!seen.Add(signature))
            {
                continue;
            }

            methods.Add(method);
        }
    }

    private static bool HasOperationContract(IMethodSymbol methodSymbol)
    {
        foreach (var attr in methodSymbol.GetAttributes())
        {
            var attrName = attr.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (string.Equals(attrName, OperationContractAttribute, StringComparison.Ordinal))
            {
                return true;
            }

            var syntax = attr.ApplicationSyntaxReference?.GetSyntax() as AttributeSyntax;
            if (syntax is null)
            {
                continue;
            }

            var syntaxName = syntax.Name.ToString();
            if (string.Equals(syntaxName, "OperationContract", StringComparison.Ordinal)
                || string.Equals(syntaxName, "OperationContractAttribute", StringComparison.Ordinal)
                || syntaxName.EndsWith(".OperationContract", StringComparison.Ordinal)
                || syntaxName.EndsWith(".OperationContractAttribute", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static OperationContractInfo ToOperationInfo(IMethodSymbol methodSymbol)
    {
        var parameters = methodSymbol.Parameters
            .Select(ToParameterInfo)
            .ToArray();

        return new OperationContractInfo(
            methodSymbol.Name,
            methodSymbol.ReturnType.ToDisplayString(TypeNameFormat),
            GetEffectiveReturnType(methodSymbol.ReturnType),
            GetIsOneWay(methodSymbol),
            parameters);
    }

    private static OperationParameterInfo ToParameterInfo(IParameterSymbol parameterSymbol)
    {
        return new OperationParameterInfo(
            parameterSymbol.Name,
            parameterSymbol.Type.ToDisplayString(TypeNameFormat),
            parameterSymbol.RefKind == RefKind.Out,
            parameterSymbol.RefKind == RefKind.Ref,
            parameterSymbol.IsOptional);
    }

    private static string GetEffectiveReturnType(ITypeSymbol returnType)
    {
        if (returnType is not INamedTypeSymbol named)
        {
            return returnType.ToDisplayString(TypeNameFormat);
        }

        var fullName = named.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (string.Equals(fullName, TaskType, StringComparison.Ordinal)
            || string.Equals(fullName, ValueTaskType, StringComparison.Ordinal))
        {
            if (named.TypeArguments.Length == 1)
            {
                return named.TypeArguments[0].ToDisplayString(TypeNameFormat);
            }

            return "void";
        }

        return returnType.ToDisplayString(TypeNameFormat);
    }

    private static bool GetIsOneWay(IMethodSymbol methodSymbol)
    {
        foreach (var attr in methodSymbol.GetAttributes())
        {
            var attrName = attr.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (!string.Equals(attrName, OperationContractAttribute, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var namedArg in attr.NamedArguments)
            {
                if (!string.Equals(namedArg.Key, "IsOneWay", StringComparison.Ordinal))
                {
                    continue;
                }

                if (namedArg.Value.Value is bool value)
                {
                    return value;
                }
            }
        }

        return false;
    }
}
