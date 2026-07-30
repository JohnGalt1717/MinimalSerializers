---
name: minimal-serializers-migrate
description: Migrate hand-written JsonSerializable context lists to MinimalSerializers.
---

# Migrate from manual JsonSerializable lists

1. Add package reference.
2. Replace manual `[JsonSerializable]` fan-out with `[MinimalJsonSerializerContext]` on the same partial class.
3. Keep `[JsonSourceGenerationOptions]` if present.
4. Ensure all serializable models have `[DataContract]` (Messages too).
5. Remove obsolete usings for manually listed nested types if unused.
6. Build once; fix MSJ diagnostics.
7. Run app tests; keep TypeInfoResolverChain insertion order.
