# MinimalSerializers — AI Onboarding

## Product thesis

Option A′: discover DataContract graphs and write compile-visible `[JsonSerializable]` attributes before CoreCompile so the stock System.Text.Json source generator runs in one build. Runtime speed equals manual STJ contexts.

## Architecture

1. Consumer references `MinimalSerializers.Json`.
2. `buildTransitive` targets run `GenerateMinimalJsonSerializerContexts`.
3. Task builds a Roslyn compilation from `@(Compile)` + `@(ReferencePath)`.
4. Discovery walks `[DataContract]` types and members.
5. Emitter writes `*.MinimalJson.g.cs` under `$(IntermediateOutputPath)minimaljson/`.
6. File is added to `@(Compile)` before CoreCompile.
7. STJ generator produces the real serializers.

### Local ProjectReference

NuGet `buildTransitive` is not imported for ProjectReferences. Samples/benchmarks explicitly Import:

`src/MinimalSerializers.Json/build/MinimalSerializers.Json.props|targets`

Repo layout resolves the task DLL from `src/MinimalSerializers.Json.Tasks/bin/...`.

### Pack layout

```
lib/netX/MinimalSerializers.Json.dll
buildTransitive/MinimalSerializers.Json.props
buildTransitive/MinimalSerializers.Json.targets
tasks/net8.0/MinimalSerializers.Json.Tasks.dll
tasks/net8.0/MinimalSerializers.Json.Discovery.dll
tasks/net8.0/Microsoft.CodeAnalysis*.dll
```

## Commands

```bash
dotnet build MinimalSerializers.slnx
dotnet test tests/MinimalSerializers.Json.Tests
dotnet test tests/MinimalSerializers.Json.Package.Tests
dotnet pack src/MinimalSerializers.Json -c Release -o artifacts/packages
dotnet run --project samples/Sample.Host
```

## Non-goals (v1)

- Full custom STJ serializer IR
- Generator execution ordering
- Auto polymorphism
- ProjectFulcrum integration (follow-up)
