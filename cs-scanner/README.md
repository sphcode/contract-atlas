# Contract Scanner (Roslyn)

C# scanner using Roslyn `MSBuildWorkspace` to extract:

- `ServiceContract` types
- `DataContract` types
- `DataMember` fields/properties with CLR type
- `enum` definitions with member values

## NuGet packages

- `Microsoft.CodeAnalysis.CSharp.Workspaces`
- `Microsoft.Build.Locator`

## Build & Run

```bash
dotnet run --project Cli/Cli.csproj -- <path/to/solution.sln|project.csproj> [--verbose]
```

## Outputs

Scanner writes two JSONL files at runtime working directory:

- `contracts.jsonl`
- `data-members.jsonl`

`contracts.jsonl` rows:

- `{"type":"ServiceContract","name":"Namespace.IMyService"}`
- `{"type":"DataContract","name":"Namespace.MyDto"}`
- `{"type":"Enum","name":"Namespace.Status","enumMembers":[{"name":"Active","value":"1"}]}`

`data-members.jsonl` rows (only `DataContract`):

- `{"type":"DataContract","name":"Namespace.MyDto","dataMembers":[{"name":"Id","type":"int"}]}`
