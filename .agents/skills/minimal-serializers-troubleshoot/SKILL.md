---
name: minimal-serializers-troubleshoot
description: Troubleshoot missing JsonTypeInfo, multi-build myths, and generation failures.
---

# Troubleshoot

| Symptom | Check |
|---------|-------|
| GetTypeInfo null | Is type `[DataContract]`? Did one build produce `*.MinimalJson.g.cs`? |
| Needs second build | Should not; file must be in Compile before CoreCompile. Inspect targets import. |
| No generated file | `MinimalJsonSerializerEnabled`? Task DLL path? Package restore? |
| ProjectReference sample | Explicit Import of props/targets required (NuGet buildTransitive won't apply). |
| Arrays fail | Ensure `MinimalJsonEmitArrays=true` (default). |
| Open generic | Only closed types are roots (MSJ0004). |
| Design-time missing | `MinimalJson_EnableDesignTime` default true. |

Never "fix" by adding generator order APIs — they do not exist.
