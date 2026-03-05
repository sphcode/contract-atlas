using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ContractScanner.Core.Domain.Models;
using ContractScanner.Core.Infrastructure.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;

namespace ContractScanner.Core.Application;

public sealed class ContractScanner
{
    private static readonly SymbolDisplayFormat TypeNameFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

    public async Task ScanAsync(
        string solutionOrProjectPath,
        Func<ScanResult, Task> onResult,
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(solutionOrProjectPath))
        {
            throw new InvalidDataException("Solution or project path is required.");
        }

        if (onResult is null)
        {
            throw new InvalidDataException("onResult callback is required.");
        }

        using var workspace = MSBuildWorkspace.Create();
        workspace.WorkspaceFailed += (_, args) =>
        {
            log?.Invoke($"Workspace {args.Diagnostic.Kind}: {args.Diagnostic.Message}");
        };

        var seen = new HashSet<string>();
        var totalMatches = 0;

        log?.Invoke($"Start scanning: {solutionOrProjectPath}");

        if (solutionOrProjectPath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
        {
            var solution = await workspace.OpenSolutionAsync(
                solutionOrProjectPath,
                progress: null,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            log?.Invoke($"Loaded solution: {solution.FilePath ?? solutionOrProjectPath}, projects={solution.Projects.Count()}");
            foreach (var project in solution.Projects)
            {
                log?.Invoke($"Scanning project: {project.Name}, documents={project.Documents.Count()}");
                var projectMatches = await ScanProjectAsync(project, onResult, seen, cancellationToken).ConfigureAwait(false);
                totalMatches += projectMatches;
                log?.Invoke($"Project complete: {project.Name}, matchedContracts={projectMatches}");
            }
        }
        else if (solutionOrProjectPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            var project = await workspace.OpenProjectAsync(
                solutionOrProjectPath,
                progress: null,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            log?.Invoke($"Loaded project: {project.Name}, documents={project.Documents.Count()}");
            totalMatches = await ScanProjectAsync(project, onResult, seen, cancellationToken).ConfigureAwait(false);
            log?.Invoke($"Project complete: {project.Name}, matchedContracts={totalMatches}");
        }
        else
        {
            throw new InvalidDataException("Input path must be a .sln or .csproj file.");
        }

        log?.Invoke($"Scan complete: matchedContracts={totalMatches}");
    }

    private static async Task<int> ScanProjectAsync(
        Project project,
        Func<ScanResult, Task> onResult,
        HashSet<string> seen,
        CancellationToken cancellationToken)
    {
        var matches = 0;
        foreach (var document in project.Documents)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (document.SourceCodeKind != SourceCodeKind.Regular)
            {
                continue;
            }

            var syntaxRoot = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            if (syntaxRoot is null)
            {
                continue;
            }

            var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            if (semanticModel is null)
            {
                continue;
            }

            foreach (var typeDecl in syntaxRoot.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
            {
                var symbol = semanticModel.GetDeclaredSymbol(typeDecl, cancellationToken);
                if (symbol is not INamedTypeSymbol namedType)
                {
                    continue;
                }

                var matchedType = namedType.TypeKind == TypeKind.Enum
                    ? "Enum"
                    : ContractAttributeMatcher.GetMatch(namedType)?.Type;

                if (matchedType is null)
                {
                    continue;
                }

                var name = namedType.ToDisplayString(TypeNameFormat);
                var key = $"{matchedType}|{name}";
                if (!seen.Add(key))
                {
                    continue;
                }

                var membersArray = DataMemberCollector.CollectMembers(matchedType, namedType);
                var enumMembers = EnumMemberCollector.Collect(namedType);
                var operationContracts = ServiceContractOperationCollector.CollectOperations(matchedType, namedType);
                await onResult(new ScanResult(matchedType, name, membersArray, enumMembers, operationContracts)).ConfigureAwait(false);
                matches++;
            }
        }

        return matches;
    }
}
