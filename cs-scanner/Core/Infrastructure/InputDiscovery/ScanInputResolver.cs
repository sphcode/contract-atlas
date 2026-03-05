using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ContractScanner.Core.Infrastructure.InputDiscovery;

internal enum ScanInputKind
{
    Solution,
    Project,
    CSharpDirectory,
}

internal readonly record struct ScanInputTarget(ScanInputKind Kind, string Path, bool Recursive = true);

internal static class ScanInputResolver
{
    private static readonly Regex SolutionProjectRegex = new(
        "^Project\\(\"\\{[^\\}]+\\}\"\\)\\s*=\\s*\"[^\"]+\",\\s*\"([^\"]+\\.csproj)\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static IReadOnlyList<ScanInputTarget> Resolve(string inputPath, bool recursiveForDirectory)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            throw new InvalidDataException("Solution, project, or directory path is required.");
        }

        var fullPath = Path.GetFullPath(inputPath);
        if (File.Exists(fullPath))
        {
            return new[] { ResolveFile(fullPath) };
        }

        if (!Directory.Exists(fullPath))
        {
            throw new InvalidDataException($"Input path not found: {inputPath}");
        }

        return new[] { new ScanInputTarget(ScanInputKind.CSharpDirectory, fullPath, recursiveForDirectory) };
    }

    public static IReadOnlyList<string> ResolveCSharpFilesFromTarget(ScanInputTarget target)
    {
        return target.Kind switch
        {
            ScanInputKind.Solution => ResolveCSharpFilesFromSolution(target.Path),
            ScanInputKind.Project => ResolveCSharpFilesFromProject(target.Path),
            ScanInputKind.CSharpDirectory => ResolveCSharpFilesFromDirectory(target.Path, target.Recursive),
            _ => Array.Empty<string>()
        };
    }

    private static ScanInputTarget ResolveFile(string fullPath)
    {
        if (fullPath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
        {
            return new ScanInputTarget(ScanInputKind.Solution, fullPath);
        }

        if (fullPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            return new ScanInputTarget(ScanInputKind.Project, fullPath);
        }

        throw new InvalidDataException("Input file must be a .sln or .csproj file.");
    }

    private static IReadOnlyList<string> ResolveCSharpFilesFromSolution(string solutionPath)
    {
        var solutionDirectory = Path.GetDirectoryName(solutionPath) ?? ".";
        var projects = File.ReadLines(solutionPath)
            .Select(static line => SolutionProjectRegex.Match(line))
            .Where(static match => match.Success)
            .Select(match => match.Groups[1].Value)
            .Select(path => path.Replace('\\', Path.DirectorySeparatorChar))
            .Select(path => Path.GetFullPath(Path.Combine(solutionDirectory, path)))
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (projects.Length == 0)
        {
            throw new InvalidDataException($"No .csproj entries found in solution: {solutionPath}");
        }

        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in projects)
        {
            foreach (var file in ResolveCSharpFilesFromProject(project))
            {
                files.Add(file);
            }
        }

        return files.OrderBy(static path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IReadOnlyList<string> ResolveCSharpFilesFromProject(string projectPath)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath);
        if (string.IsNullOrWhiteSpace(projectDirectory))
        {
            throw new InvalidDataException($"Cannot resolve project directory: {projectPath}");
        }

        return ResolveCSharpFilesFromDirectory(projectDirectory, recursive: true);
    }

    private static IReadOnlyList<string> ResolveCSharpFilesFromDirectory(string directoryPath, bool recursive)
    {
        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var files = Directory.EnumerateFiles(directoryPath, "*.cs", searchOption)
            .Where(static path => !IsGeneratedPath(path))
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (files.Length == 0)
        {
            throw new InvalidDataException("Input path does not contain any .cs files.");
        }

        return files;
    }

    private static bool IsGeneratedPath(string path)
    {
        var normalized = path.Replace('\\', '/');
        return normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase);
    }
}
