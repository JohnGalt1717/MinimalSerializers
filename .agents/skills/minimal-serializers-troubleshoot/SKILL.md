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
| SYSLIB1031 / List*Dto | Collection roots use `TypeInfoPropertyName` (`ListOf_*` / `ArrayOf_*`). Prefer `GetTypeInfo(typeof(T))` over typed context members for collections. |
| Open generic | Only closed types are roots. MSJ0004 defaults to one summary; set `MinimalJsonWarnOpenGenerics=all\|summary\|none`. |
| CS0102 + generic DTO inheritance | `Derived<T> : Base<T>` DataContracts can break STJ source-gen. Expect **MSJ0009**; prefer composition/flattening. |
| Design-time missing | `MinimalJson_EnableDesignTime` default true. |

Never "fix" by adding generator order APIs — they do not exist.
