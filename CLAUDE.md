# Emit

.NET 10.0+ transactional outbox library with retry logic and ordering guarantees.

## Commands

```bash
dotnet build                                                           # Build
dotnet format                                                          # Fix formatting (run before every commit)
dotnet test tests/Emit.UnitTests/Emit.UnitTests.csproj                # Unit tests
dotnet test tests/Emit.IntegrationTests/Emit.IntegrationTests.csproj  # Integration tests (needs Docker)
```

## Rules

- **Internal by default** — only `public` for public API types. `InternalsVisibleTo` for test projects only. Never make internals public to satisfy tests — fix the test or the design instead.
- **Abstractions in `src/Emit.Abstractions`** — interfaces, abstract classes, DTOs/records, enums. Implementations (workers, validators, DI, options) stay in `src/Emit`. Public service classes get a corresponding interface in Abstractions; XML docs go on the interface, implementation uses `<inheritdoc />`.
- **Project boundary isolation** — each project is agnostic of projects it doesn't reference. Core `Emit` must not mention Kafka, MongoDB, or PostgreSQL. Provider projects reference core but not each other.
- **No hardcoded durations** — all `TimeSpan` values configurable with sensible defaults
- **No commercial libraries** (FluentAssertions is banned)
- **Convention over attributes** — camelCase for JSON, BSON conventions for MongoDB
- **IValidateOptions pattern** — implement `IValidateOptions<T>` directly, register with `ValidateOnStart()`
- **Documentation stays current** — update XML docs, README files, and `/docs` pages for any code changes

## Code Style

- File-scoped namespaces, using directives inside namespace
- Latest C# features: primary constructors, collection expressions `[]`, pattern matching, records
- `ConfigureAwait(false)` in library code; `BackgroundService` + `PeriodicTimer` for workers
- XML docs on all public APIs — write for the consumer, not the maintainer. No code samples. No internal implementation details.
- Always use `nameof()` for any string referencing a code identifier
- All DateTime fields in UTC
- Member ordering: constants, events, static members, properties, fields, constructors, instance methods, static methods, dispose

## Testing

- **xUnit + Moq** — use xUnit `Assert`, NOT FluentAssertions
- Given-When-Then naming, AAA pattern (`// Arrange`, `// Act`, `// Assert`)
- Tests are ground truth — if a test fails, fix the implementation
- Only write meaningful tests — don't test the compiler, framework, or trivial property assignments
- `NullLogger<T>.Instance` for internal classes; `Options.Create(...)` to wrap options
- **Unit tests** — `tests/Emit.UnitTests/` with subfolders mirroring `src/` structure
- **Integration tests** — `tests/Emit.IntegrationTests/` with subfolders per provider. All infra starts via Testcontainers (Docker). Read `tests/Emit.IntegrationTests/INSTRUCT.md` before writing integration tests.
- **Compliance classes** — abstract test bases named `*Compliance` in `Emit/Integration/Compliance/`. Each provider inherits to prove correctness. Provider subclasses are named `{Provider}{Feature}Compliance` (e.g., `MongoDbDistributedLockCompliance`).
