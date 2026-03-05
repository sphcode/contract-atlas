# Contract Scanner (Roslyn)

C# scanner using Roslyn syntax model (`CSharpSyntaxTree`) to extract:

- `ServiceContract` types
- `DataContract` types
- `DataMember` fields/properties with CLR type
- `enum` definitions with member values
- `OperationContract` methods (name, parameters, return type)

## NuGet packages

- `Microsoft.CodeAnalysis.CSharp`

## Build & Run

```bash
dotnet run --project Cli/Cli.csproj -- <path/to/solution.sln|project.csproj|directory> [--verbose] [--recursive true|false]
```

Input modes:

- `sln`: discover `.csproj` entries from solution file, then scan `.cs` files under each project directory.
- `csproj`: scan `.cs` files under project directory.
- `directory`: scan `.cs` files directly from that directory.

Directory mode options:

- `--recursive true|false`: include subdirectories when scanning `.cs` files (default `true`).

## Outputs

Scanner writes two JSONL files at runtime working directory:

- `contracts.jsonl`
- `data-members.jsonl`

`contracts.jsonl` rows:

- `{"type":"ServiceContract","name":"Namespace.IMyService"}`
- `{"type":"DataContract","name":"Namespace.MyDto"}`
- `{"type":"Enum","name":"Namespace.Status","enumMembers":[{"name":"Active","value":"1"}]}`
- `{"type":"ServiceContract","name":"Namespace.IMyService","operationContracts":[{"name":"Ping","returnType":"Task<string>","effectiveReturnType":"string","isOneWay":false,"parameters":[{"name":"id","type":"int","isOut":false,"isRef":false,"isOptional":false}]}]}`

`data-members.jsonl` rows (only `DataContract`):

- `{"type":"DataContract","name":"Namespace.MyDto","dataMembers":[{"name":"Id","type":"int"}]}`

## Layered Structure

- `Core/Application`: scan orchestration and workflow (`ContractScanner`)
- `Core/Domain/Models`: scanner output models (`ScanResult`, `DataMemberInfo`, `EnumMemberInfo`)
- `Core/Infrastructure/InputDiscovery`: input path discovery (`ScanInputResolver`)
- `Core/Infrastructure/Roslyn`: Roslyn syntax parsing (`SyntaxContractExtractor`)
