using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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

    public static IReadOnlyList<string> ResolveCSharpFiles(string directoryPath, bool recursive)
    {
        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var files = Directory.EnumerateFiles(directoryPath, "*.cs", searchOption)
            .Where(static path => !IsGeneratedPath(path))
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (files.Length == 0)
        {
            throw new InvalidDataException("Input directory does not contain any .cs files.");
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
