---
name: minimal-serializers-add-context
description: Add a MinimalJsonSerializerContext and DataContract models to a project.
---

# Add a Minimal context

```csharp
[MinimalJsonSerializerContext]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public partial class ContractsJsonSerializerContext : JsonSerializerContext;
```

Rules:
- One context per project.
- Public DTOs: `[DataContract]` + `[DataMember]`.
- Prefer records with init properties.
- Collections: declare `List<T>` / `IReadOnlyCollection<T>` as needed; arrays are still registered as roots.
- Do not mark Result wrappers.
