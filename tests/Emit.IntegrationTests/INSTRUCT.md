# Integration Test Instructions

Follow these instructions exactly when writing integration tests.

---

## Architecture

Tests use **compliance classes** — abstract base classes that define `[Fact]` test methods. Each persistence provider inherits a compliance class to prove its implementation is correct. Compliance classes live in `Emit/Integration/Compliance/`.

```
DistributedLockCompliance (abstract, has [Fact] methods)
    ├── MongoDbDistributedLockCompliance
    └── PostgreSqlDistributedLockCompliance

LeaderElectionCompliance (abstract, has [Fact] methods)
    ├── MongoDbLeaderElectionCompliance
    └── PostgreSqlLeaderElectionCompliance

CircuitBreakerCompliance (abstract)        → KafkaCircuitBreakerComplianceTests
ExternalMessageCompliance (abstract)       → KafkaExternalMessageComplianceTests
ProduceConsumeCompliance (abstract)        → KafkaProduceConsumeComplianceTests
```

**Rules:**
- Shared test logic goes in the compliance base class, never duplicated across providers
- Provider-specific test logic goes only in the derived class
- New features get a new compliance class; new providers add a new derived class

---

## Container Fixtures

Containers are **static** (started once per test run, shared across all fixture instances via `Lazy<Task>`). Ryuk handles cleanup on process exit.

Test classes receive fixtures via `IClassFixture<T>`:

```csharp
[Trait("Category", "Integration")]
public class MongoDbDistributedLockCompliance
    : DistributedLockCompliance, IClassFixture<MongoDbContainerFixture>
{
    public MongoDbDistributedLockCompliance(MongoDbContainerFixture fixture) { ... }
}
```

**Never use `[Collection("...")]`** — it forces sequential execution. All test classes run in parallel; isolation comes from unique database names and topic names per class.

---

## Writing a New Compliance Class

1. Create an abstract class in `Emit/Integration/Compliance/` implementing `IAsyncLifetime`
2. Add `[Trait("Category", "Integration")]`
3. Define abstract members for the provider-specific dependency (e.g., `IDistributedLockProvider`)
4. Write `[Fact]` test methods using Given-When-Then naming and AAA comments
5. Use unique identifiers (`Guid.NewGuid()`) in all test data

## Adding a Provider Implementation

1. Create a class in the provider's subfolder inheriting the compliance class
2. Implement `IClassFixture<T>` with the appropriate container fixture
3. Accept the fixture via constructor, create a unique database name with `Guid.NewGuid()`
4. Wire up the provider's DI in the constructor (or `InitializeAsync` for PostgreSQL schema creation)
5. Implement `DisposeAsync` to drop the test database

---

## Shared Test Utilities

Use existing shared types — **never redefine** these in compliance or test files.

Types from `Emit.Testing` (package reference, `using Emit.Testing;`):

| Type | Purpose |
|------|---------|
| `MessageSink<T>` + `SinkConsumer<T>` | Capture individual consumed messages |
| `BatchSinkConsumer<T>` | Capture batch consumed messages |

Types from `Emit.IntegrationTests.Integration` (`using Emit.IntegrationTests.Integration;`):

| Type | Purpose |
|------|---------|
| `DlqCaptureConsumer` | Capture dead-lettered `byte[]` messages on DLQ topics |
| `AlwaysFailingConsumer` | `IConsumer<string>` that always throws — for error policy / DLQ tests |
| `AlwaysFailingBatchConsumer` | `IBatchConsumer<string>` that always throws — for batch error tests |
| `InvocationCounter` | Thread-safe counter for tracking handler invocations |
| `ConsumerToggle` | Toggle controlling `ToggleableConsumer` / `ToggleableBatchConsumer` failure |
| `ToggleableConsumer` | `IConsumer<string>` that throws or succeeds based on toggle — for circuit breaker tests |
| `ToggleableBatchConsumer` | `IBatchConsumer<string>` that throws or succeeds based on toggle — for batch circuit breaker tests |

```csharp
using Emit.IntegrationTests.Integration;  // for DlqCaptureConsumer, InvocationCounter, etc.
```

Use the shared polling helper from `Emit.IntegrationTests.Integration.TestHelpers`:

```csharp
using static Emit.IntegrationTests.Integration.TestHelpers;

await WaitUntilAsync(() => sink.ReceivedMessages.Count >= expected, "Messages not received");
```

For Kafka `string, string` topics, use `UseUtf8Serialization()` instead of 4 separate calls:

```csharp
kafka.Topic<string, string>(topic, t =>
{
    t.UseUtf8Serialization();
    t.Producer();
    t.ConsumerGroup(groupId, group => { ... });
});
```

For Kafka offset-commit synchronization, use `CommitAwaiter` from `Emit.Kafka.Tests.TestInfrastructure`.

---

## EF Core Test Naming Convention

Files in `Emit.EntityFrameworkCore/` use two prefixes:
- **`EfCore` prefix** — ORM-level features (outbox, transactions, unit of work, middleware)
- **`PostgreSql` prefix** — raw SQL persistence features (distributed locks, leader election, daemon assignment)

---

## Key Constraints

- **Real dependencies only** — never mock databases or message brokers
- **Test isolation** — unique database/topic per test class, never share mutable state
- **No wall-clock dependencies** — use unit tests with time abstraction instead
- **No `[Collection]`** — use `IClassFixture<T>` for parallel execution
- **Process-global listeners** — `ActivityListener` and `MeterListener` capture across all parallel tests; filter by topic tag

---

## MongoDB Transaction Caveat

MongoDB atomic operations (e.g., `FindOneAndUpdate`) do NOT roll back when a session is aborted. PostgreSQL rolls back everything. Test rollback behavior accordingly per provider.
