using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using CoreScanner = ContractScanner.Core.Application.ContractScanner;

namespace ContractScanner.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("Usage: scanner <solution.sln|project.csproj|directory> [--verbose] [--recursive true|false]");
            return 1;
        }

        var inputPath = args[0];
        var verbose = args.Any(static arg => string.Equals(arg, "--verbose", StringComparison.OrdinalIgnoreCase));
        if (!TryParseRecursiveOption(args, out var recursive, out var recursiveError))
        {
            Console.Error.WriteLine(recursiveError);
            return 3;
        }

        // Hardcoded output paths (JSONL)
        var outputDirectory = Path.GetFullPath("output");
        var outputPath = Path.Combine(outputDirectory, "contracts.jsonl");
        var dataMembersPath = Path.Combine(outputDirectory, "data-members.jsonl");

        if (!File.Exists(inputPath) && !Directory.Exists(inputPath))
        {
            Console.Error.WriteLine($"Input path not found: {inputPath}");
            return 2;
        }

        Directory.CreateDirectory(outputDirectory);

        await using var outStream = new FileStream(
            outputPath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true);

        await using var outWriter = new StreamWriter(outStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        await using var dmStream = new FileStream(
            dataMembersPath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true);

        await using var dmWriter = new StreamWriter(dmStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var scanner = new CoreScanner();
        await scanner.ScanAsync(inputPath, async result =>
        {
            object contractRow = BuildContractRow(result);
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
        recursiveForDirectory: recursive,
        log: verbose ? static message => Console.Error.WriteLine($"[scanner] {message}") : null).ConfigureAwait(false);

        return 0;
    }

    private static bool TryParseRecursiveOption(string[] args, out bool recursive, out string? error)
    {
        recursive = true;
        error = null;

        for (var i = 1; i < args.Length; i++)
        {
            var arg = args[i];
            if (string.Equals(arg, "--recursive", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length)
                {
                    error = "Missing value for --recursive. Expected true or false.";
                    return false;
                }

                if (!bool.TryParse(args[i + 1], out recursive))
                {
                    error = $"Invalid value for --recursive: {args[i + 1]}. Expected true or false.";
                    return false;
                }

                i++;
                continue;
            }

            const string Prefix = "--recursive=";
            if (arg.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            {
                var value = arg.Substring(Prefix.Length);
                if (!bool.TryParse(value, out recursive))
                {
                    error = $"Invalid value for --recursive: {value}. Expected true or false.";
                    return false;
                }
            }
        }

        return true;
    }

    private static object BuildContractRow(ContractScanner.Core.Domain.Models.ScanResult result)
    {
        if (result.EnumMembers is { Length: > 0 })
        {
            return new
            {
                type = result.Type,
                name = result.Name,
                enumMembers = result.EnumMembers.Select(static m => new { name = m.Name, value = m.Value })
            };
        }

        if (result.OperationContracts is { Length: > 0 })
        {
            return new
            {
                type = result.Type,
                name = result.Name,
                operationContracts = result.OperationContracts.Select(static op => new
                {
                    name = op.Name,
                    returnType = op.ReturnType,
                    effectiveReturnType = op.EffectiveReturnType,
                    isOneWay = op.IsOneWay,
                    parameters = op.Parameters.Select(static p => new
                    {
                        name = p.Name,
                        type = p.Type,
                        isOut = p.IsOut,
                        isRef = p.IsRef,
                        isOptional = p.IsOptional
                    })
                })
            };
        }

        return new
        {
            type = result.Type,
            name = result.Name
        };
    }
}
