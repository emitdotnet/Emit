# CLAUDE.md

Guidance for Claude Code when working with this repository. See `PRD.md` for detailed design decisions.

## Project Overview

Emit is a .NET 8.0+ transactional outbox library. It persists operations (e.g., Kafka messages) in an outbox within the user's database as part of an ACID transaction, then a background worker processes them with retry logic and ordering guarantees.

## Critical Rules

1. **Documentation stays current** - Update XML docs and README files for any code changes
2. **Run `dotnet format` before every commit**
3. **No commercial libraries** (e.g., FluentAssertions is banned)
4. **Convention over attributes** - Configure naming conventions globally (camelCase for JSON, BSON conventions for MongoDB)
5. **Use FluentValidation** for validation rules on configuration and complex objects
6. **Internal by default** - Only `public` for public API types; use `InternalsVisibleTo` for test access
7. **No hardcoded durations** - All `TimeSpan` values must be configurable with sensible defaults

## Commands

```bash
dotnet build                                    # Build
dotnet test                                     # All tests
dotnet test --filter 'Category!=Integration'   # Unit tests only
dotnet format                                   # Fix formatting
dotnet format --verify-no-changes              # Check formatting
dotnet pack -c Release                          # Pack NuGet
```

## Key Dependencies

- **Confluent.Kafka** - Kafka producer/consumer
- **Transactional** - Transaction context abstractions (`ITransactionContext`, `IMongoTransactionContext`, `IPostgresTransactionContext`)
- **MessagePack** - Payload serialization
- **FluentValidation** - Configuration validation
- **MongoDB.Driver** / **Entity Framework Core** - Persistence providers
- **xUnit + Moq** - Testing (use xUnit `Assert`, NOT FluentAssertions)

## Code Style

- File-scoped namespaces, using directives inside namespace
- Collection expressions `[]` instead of `Array.Empty` or `new[]`
- Prefer primary constructors
- XML documentation on all public APIs
- Use `nameof()` instead of hardcoded strings
- All DateTime fields in UTC

## Testing

- Given-When-Then naming (e.g., `GivenPendingEntry_WhenWorkerProcesses_ThenStatusIsCompleted`)
- AAA pattern with `// Arrange`, `// Act`, `// Assert` comments
- **Tests are ground truth** - If a test fails, fix the implementation, not the test
- Integration tests require `MONGODB_CONNECTION_STRING` and `POSTGRES_CONNECTION_STRING` env vars
- Kafka producing is mocked - tests verify outbox behavior, not Kafka connectivity