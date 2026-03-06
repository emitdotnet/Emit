# Emit.IntegrationTests/

Consolidated integration tests using compliance base classes and Testcontainers.

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `INSTRUCT.md` | Integration test architecture: compliance pattern, container fixture setup, naming rules | Write or modify any integration test |
| `Emit.IntegrationTests.csproj` | Test project file with xUnit, Testcontainers, and provider dependencies | Configure test dependencies |

## Subdirectories

| Directory | What | When to read |
| --------- | ---- | ------------ |
| `Emit/` | Shared base classes, container-agnostic interfaces, and compliance abstractions | Understand the compliance architecture or add a new compliance contract |
| `Emit.MongoDB/` | MongoDB compliance implementations and MongoDb container fixture | Write MongoDB persistence tests or debug MongoDB-specific behavior |
| `Emit.EntityFrameworkCore/` | PostgreSQL compliance implementations, container fixture, and test DbContext | Write EF Core / PostgreSQL persistence tests |
| `Emit.Kafka/` | Kafka compliance implementations and Kafka+Schema Registry container fixture | Write Kafka provider tests or debug Kafka-specific behavior |
