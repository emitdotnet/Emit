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

## Key Constraints

- **Real dependencies only** — never mock databases or message brokers
- **Test isolation** — unique database/topic per test class, never share mutable state
- **No wall-clock dependencies** — use unit tests with time abstraction instead
- **No `[Collection]`** — use `IClassFixture<T>` for parallel execution
- **Process-global listeners** — `ActivityListener` and `MeterListener` capture across all parallel tests; filter by topic tag

---

## MongoDB Transaction Caveat

MongoDB atomic operations (e.g., `FindOneAndUpdate`) do NOT roll back when a session is aborted. PostgreSQL rolls back everything. Test rollback behavior accordingly per provider.
