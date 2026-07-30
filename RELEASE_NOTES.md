# Release Notes

## 1.0.0

- Initial release of MinimalSerializers.Json
- MSBuild pre-compile discovery of `[DataContract]` graphs
- Emits `[JsonSerializable]` roots including `T`, `T[]`, and `List<T>`
- `[MinimalJsonSerializerContext]` attribute and `AddMinimalJsonContext<T>()` helper
- Single-build guarantee with buildTransitive packaging
- Samples, tests, benchmarks, CI/release workflows
