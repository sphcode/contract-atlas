using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ContractScanner.Core.Domain.Models;
using ContractScanner.Core.Infrastructure.InputDiscovery;
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
        string inputPath,
        Func<ScanResult, Task> onResult,
        bool recursiveForDirectory = true,
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        if (onResult is null)
        {
            throw new InvalidDataException("onResult callback is required.");
        }

        var targets = ScanInputResolver.Resolve(inputPath, recursiveForDirectory);

        var seen = new HashSet<string>();
        var totalMatches = 0;

        log?.Invoke($"Start scanning: {inputPath}");
        log?.Invoke($"Resolved scan targets: count={targets.Count}");

        MSBuildWorkspace? workspace = null;
        try
        {
            foreach (var target in targets)
            {
                if (target.Kind == ScanInputKind.CSharpDirectory)
                {
                    var fileMatches = await ScanDirectoryAsync(
                        target.Path,
                        target.Recursive,
                        onResult,
                        seen,
                        log,
                        cancellationToken).ConfigureAwait(false);
                    totalMatches += fileMatches;
                    continue;
                }

                workspace ??= CreateWorkspace(log);
                if (target.Kind == ScanInputKind.Solution)
                {
                    var matched = await ScanSolutionAsync(target.Path, workspace, onResult, seen, log, cancellationToken).ConfigureAwait(false);
                    totalMatches += matched;
                    continue;
                }

                var projectMatched = await ScanProjectFileAsync(target.Path, workspace, onResult, seen, log, cancellationToken).ConfigureAwait(false);
                totalMatches += projectMatched;
            }
        }
        finally
        {
            workspace?.Dispose();
        }

        log?.Invoke($"Scan complete: matchedContracts={totalMatches}");
    }

    private static MSBuildWorkspace CreateWorkspace(Action<string>? log)
    {
        var workspace = MSBuildWorkspace.Create();
        workspace.WorkspaceFailed += (_, args) =>
        {
            log?.Invoke($"Workspace {args.Diagnostic.Kind}: {args.Diagnostic.Message}");
        };
        return workspace;
    }

    private static async Task<int> ScanSolutionAsync(
        string solutionPath,
        MSBuildWorkspace workspace,
        Func<ScanResult, Task> onResult,
        HashSet<string> seen,
        Action<string>? log,
        CancellationToken cancellationToken)
    {
        var matched = 0;
        var solution = await workspace.OpenSolutionAsync(
            solutionPath,
            progress: null,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        log?.Invoke($"Loaded solution: {solution.FilePath ?? solutionPath}, projects={solution.Projects.Count()}");
        foreach (var project in solution.Projects)
        {
            log?.Invoke($"Scanning project: {project.Name}, documents={project.Documents.Count()}");
            var projectMatches = await ScanProjectAsync(project, onResult, seen, cancellationToken).ConfigureAwait(false);
            matched += projectMatches;
            log?.Invoke($"Project complete: {project.Name}, matchedContracts={projectMatches}");
        }

        return matched;
    }

    private static async Task<int> ScanProjectFileAsync(
        string projectPath,
        MSBuildWorkspace workspace,
        Func<ScanResult, Task> onResult,
        HashSet<string> seen,
        Action<string>? log,
        CancellationToken cancellationToken)
    {
        var project = await workspace.OpenProjectAsync(
            projectPath,
            progress: null,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        log?.Invoke($"Loaded project: {project.Name}, documents={project.Documents.Count()}");

        var matched = await ScanProjectAsync(project, onResult, seen, cancellationToken).ConfigureAwait(false);
        log?.Invoke($"Project complete: {project.Name}, matchedContracts={matched}");
        return matched;
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

    private static async Task<int> ScanDirectoryAsync(
        string directoryPath,
        bool recursive,
        Func<ScanResult, Task> onResult,
        HashSet<string> seen,
        Action<string>? log,
        CancellationToken cancellationToken)
    {
        var files = ScanInputResolver.ResolveCSharpFiles(directoryPath, recursive);
        log?.Invoke($"Scanning C# files from directory: {directoryPath}, recursive={recursive}, files={files.Count}");

        var matches = 0;
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var source = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
            var results = SyntaxContractExtractor.Extract(source);
            foreach (var result in results)
            {
                var key = $"{result.Type}|{result.Name}";
                if (!seen.Add(key))
                {
                    continue;
                }

                await onResult(result).ConfigureAwait(false);
                matches++;
            }
        }

        return matches;
    }
}
