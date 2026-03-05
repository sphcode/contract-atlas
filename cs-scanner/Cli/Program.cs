using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using CoreScanner = ContractScanner.Core.Application.ContractScanner;
using Microsoft.Build.Locator;

namespace ContractScanner.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("Usage: scanner <solutionOrProjectPath> [--verbose]");
            return 1;
        }

        var inputPath = args[0];
        var verbose = args.Any(static arg => string.Equals(arg, "--verbose", StringComparison.OrdinalIgnoreCase));

        // Hardcoded output paths (JSONL)
        var outputPath = Path.GetFullPath("contracts.jsonl");
        var dataMembersPath = Path.GetFullPath("data-members.jsonl");

        if (!File.Exists(inputPath))
        {
            Console.Error.WriteLine($"Input file not found: {inputPath}");
            return 2;
        }

        if (!MSBuildLocator.IsRegistered)
        {
            MSBuildLocator.RegisterDefaults();
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        Directory.CreateDirectory(Path.GetDirectoryName(dataMembersPath) ?? ".");

        await using var outStream = new FileStream(
            outputPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true);

        await using var outWriter = new StreamWriter(outStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        await using var dmStream = new FileStream(
            dataMembersPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true);

        await using var dmWriter = new StreamWriter(dmStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var scanner = new CoreScanner();
        await scanner.ScanAsync(inputPath, async result =>
        {
            object contractRow = result.EnumMembers is { Length: > 0 }
                ? new
                {
                    type = result.Type,
                    name = result.Name,
                    enumMembers = result.EnumMembers.Select(static m => new { name = m.Name, value = m.Value })
                }
                : new
                {
                    type = result.Type,
                    name = result.Name
                };
            var json = JsonSerializer.Serialize(contractRow);
            await outWriter.WriteLineAsync(json).ConfigureAwait(false);
            await outWriter.FlushAsync().ConfigureAwait(false);

            if (result.DataMembers is { Length: > 0 })
            {
                var dmJson = JsonSerializer.Serialize(new
                {
                    type = result.Type,
                    name = result.Name,
                    dataMembers = result.DataMembers.Select(static m => new { name = m.Name, type = m.Type })
                });
                await dmWriter.WriteLineAsync(dmJson).ConfigureAwait(false);
                await dmWriter.FlushAsync().ConfigureAwait(false);
            }
        },
        log: verbose ? static message => Console.Error.WriteLine($"[scanner] {message}") : null).ConfigureAwait(false);

        return 0;
    }
}
