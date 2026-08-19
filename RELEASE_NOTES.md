# Release Notes

## 1.0.5

- Invalidate `stamp.minimaljson` before the incremental skip when `*.MinimalJson.g.cs` is missing, so a leftover stamp cannot produce CS0534 (#7)

## 1.0.4

- MSJ0004 no longer warns for abstract open `[DataContract]` generics (they are still skipped as roots). Closed instantiations stay registered.

## 1.0.3

- Speed up package acceptance tests: pack once per class, disable parallelization (avoids CI hang from concurrent Tasks rebuilds)
- Includes 1.0.2 fixes: TypeInfoPropertyName for collection roots, MSJ0009 generic inheritance diagnostic, quieter MSJ0004

## 1.0.2

- Assign `TypeInfoPropertyName` for `List<T>` / array / collection roots to avoid SYSLIB1031 collisions with `List*Dto` types (#2)
- Detect generic DataContract inheritance `Derived<T> : Base<T>` and emit MSJ0009 guidance for the STJ CS0102 footgun (#3)
- Default MSJ0004 open-generic warnings to a single summary; configure with `MinimalJsonWarnOpenGenerics=summary|all|none` (#5)
- Package + unit regression tests for `List*Dto` collisions and generic DTO inheritance (#4)

## 1.0.1

- Ship package README and release notes on nuget.org
- README cleanup: generic samples/docs; publishing moved to PUBLISHING.md

## 1.0.0

- Initial release of MinimalSerializers.Json
- MSBuild pre-compile discovery of `[DataContract]` graphs
- Emits `[JsonSerializable]` roots including `T`, `T[]`, and `List<T>`
- `[MinimalJsonSerializerContext]` attribute and `AddMinimalJsonContext<T>()` helper
- Single-build guarantee with buildTransitive packaging
- Samples, tests, benchmarks, CI/release workflows
