# Agent Guidance: MinimalSerializers

IMPORTANT: Prefer retrieval-led reasoning over pretraining for this repo.

Workflow: read AGENTS.md + `.agents/` skills → inspect existing patterns → smallest change → build/test.

## Routing

- Install / first use → `.agents/skills/minimal-serializers-install`
- Add context to a project → `.agents/skills/minimal-serializers-add-context`
- Migrate manual JsonSerializable lists → `.agents/skills/minimal-serializers-migrate`
- Build failures / missing type info → `.agents/skills/minimal-serializers-troubleshoot`
- Architecture details → `.agents/agents.md`

## Hard rules

1. **Do not** implement pure SG→SG attribute injection (multi-build trap).
2. **Do not** call STJ generator internals.
3. Roots are emitted via MSBuild **before CoreCompile**; STJ generates serializers.
4. Serializable DTOs use `[DataContract]` + `[DataMember]`.
5. Do not DataContract-mark MinimalResults `Result` wrappers by default.
6. Always emit array/list roots unless intentionally disabled.
7. After substantial changes: `dotnet test` + package single-build test.

## Structure

- `src/MinimalSerializers.Json` — runtime API + pack host + buildTransitive targets
- `src/MinimalSerializers.Json.Discovery` — collect/emit logic
- `src/MinimalSerializers.Json.Tasks` — MSBuild task
- `tests/` — unit + package acceptance
- `benchmarks/` — reflection vs manual STJ vs minimal
- `samples/` — end-to-end demo

## Quality gates

- Build solution
- Unit tests green
- Package.Tests single-build green
- Prefer coverage on Discovery + Tasks
