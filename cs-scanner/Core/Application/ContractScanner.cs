using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ContractScanner.Core.Domain.Models;
using ContractScanner.Core.Infrastructure.InputDiscovery;
using ContractScanner.Core.Infrastructure.Roslyn;

namespace ContractScanner.Core.Application;

public sealed class ContractScanner
{
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
        log?.Invoke($"Start scanning: {inputPath}");
        log?.Invoke($"Resolved scan targets: count={targets.Count}");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var totalMatches = 0;

        foreach (var target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var files = ScanInputResolver.ResolveCSharpFilesFromTarget(target);
            log?.Invoke($"Scanning target: kind={target.Kind}, path={target.Path}, files={files.Count}");

            totalMatches += await ScanFilesAsync(files, seen, onResult, cancellationToken).ConfigureAwait(false);
        }

        log?.Invoke($"Scan complete: matchedContracts={totalMatches}");
    }

    private static async Task<int> ScanFilesAsync(
        IReadOnlyList<string> files,
        HashSet<string> seen,
        Func<ScanResult, Task> onResult,
        CancellationToken cancellationToken)
    {
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
