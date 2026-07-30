---
name: minimal-serializers-install
description: Install MinimalSerializers.Json and verify buildTransitive generation is active.
---

# Install MinimalSerializers.Json

## Steps

1. `dotnet add package MinimalSerializers.Json`
2. Ensure no manual Target is required in the consumer csproj.
3. Add a partial context with `[MinimalJsonSerializerContext]`.
4. Mark DTOs with `[DataContract]` / `[DataMember]`.
5. `dotnet build` once.
6. Confirm `obj/**/minimaljson/*.MinimalJson.g.cs` exists and contains `[JsonSerializable]`.
7. Wire `options.AddMinimalJsonContext<TContext>()` or insert `TContext.Default`.

## Verify

- Build succeeds on a clean bin/obj.
- `GetTypeInfo(typeof(T))`, `T[]`, and `List<T>` are non-null with `TypeInfoResolver = context`.
