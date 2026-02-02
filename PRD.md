# Emit — Product Requirement Document (PRD)

## 1. Overview

Emit is a .NET 10.0+ library that provides a transactional outbox pattern. Instead of directly performing operations against external systems (e.g., producing a Kafka event), Emit first persists the operation in an outbox within the user's database as part of an ACID transaction. This guarantees that the user's data and the outbox entries are committed atomically — both succeed or both roll back.

A background worker then processes the outbox, performing the actual external operations (e.g., producing to Kafka) with retry logic, ordering guarantees, and resilience.

### Core Principles

- **Transactional safety**: User data and outbox entries are committed in a single database transaction.
- **Ordering guarantees**: Entries within the same group are processed strictly in sequence order.
- **Provider-agnostic core**: The outbox schema and engine are not tied to any specific external system.
- **Drop-in replacement**: For Kafka, users replace Confluent's producer registration with Emit's — their application code (calls to `Produce`/`ProduceAsync`) requires no changes.
- **Exhaustive logging**: The library uses `ILogger` at all levels (Error, Warning, Information, Debug, Trace). A user setting their logger to verbose should see detailed output about every step the library takes.

---

## 2. Project Structure

### 2.1 NuGet Packages

| Package | Description |
|---------|-------------|
| **Emit** | Core library. Contains contracts, outbox engine, worker, resilience policies, and abstractions shared across all packages. Nothing database-specific or external-system-specific lives here. |
| **Emit.Provider.Kafka** | Kafka outbox provider. Implements the fake `IProducer<TKey, TValue>` that serializes calls into outbox entries. Also provides the worker-side logic to produce to Kafka using real Confluent producers. Takes a direct dependency on the `Confluent.Kafka` package. |
| **Emit.Persistence.MongoDB** | MongoDB persistence provider. Manages the outbox collection, sequence counter collection, global lease document, and all MongoDB-specific query logic. |
| **Emit.Persistence.PostgreSQL** | PostgreSQL persistence provider. Same responsibilities as MongoDB but using PostgreSQL (Entity Framework Core preferred over raw Npgsql, consistent with the recurringthings project). |

### 2.2 Constraints

- **One persistence provider only.** The user cannot register both MongoDB and PostgreSQL. The library must enforce this at registration time with a clear error.
- **Multiple outbox providers allowed.** The user can register Kafka and future providers (e.g., RabbitMQ) simultaneously.
- **.NET 10.0+ target** for all Emit packages. Transitive dependencies (Transactional, Confluent.Kafka, etc.) may target .NET 8.0+ — this is fine as long as .NET 8.0 remains a supported runtime.
- **Use `Directory.Packages.props`** for centralized dependency version management.
- **Avoid locking users into specific transitive package versions.** Users of the library may be using slightly different versions of shared dependencies.
- **No commercial or restrictively licensed libraries.**

### 2.3 Repository Structure

Follow the same conventions as [recurringthings](https://github.com/ChuckNovice/recurringthings):

- `.github/workflows/ci.yml` — CI pipeline (build, unit tests, format check, integration tests with service containers)
- `.github/workflows/publish.yml` — Tag-driven NuGet publish
- `src/Emit/` — Core library
- `src/Emit.Provider.Kafka/` — Kafka provider
- `src/Emit.Persistence.MongoDB/` — MongoDB persistence
- `src/Emit.Persistence.PostgreSQL/` — PostgreSQL persistence
- `tests/Emit.Tests/` — Unit tests
- `tests/Emit.IntegrationTests/` — Integration tests with base class pattern
- `Directory.Packages.props` — Centralized package versions
- `Emit.slnx` — Solution file
- `CLAUDE.md` — Claude Code guidance
- `.editorconfig` — Code style enforcement
- `.env.example` — Environment variable template for local development

### 2.4 Documentation Structure

Each project has its own README:

| Project | README Location | Content Focus |
|---------|-----------------|---------------|
| Core | `README.md` (repo root) | Engine features, outbox concepts, supported providers, links to provider READMEs |
| Kafka | `src/Emit.Provider.Kafka/README.md` | Kafka-specific setup, producer registration, serializer configuration |
| MongoDB | `src/Emit.Persistence.MongoDB/README.md` | MongoDB-specific setup, collection schema, indexes, sharding, counter collection |
| PostgreSQL | `src/Emit.Persistence.PostgreSQL/README.md` | PostgreSQL-specific setup, table schema, migrations, sequences |

---

## 3. Transaction Management

### 3.1 Dependency on Transactional Library

Emit depends on the [Transactional](https://github.com/ChuckNovice/transactional) library for database transaction abstractions. Emit does **not** create database transactions directly using raw library APIs.

Key interfaces consumed:
- `ITransactionContext` — Base transaction context, passed to repository methods.
- `IMongoTransactionContext` — MongoDB-specific, exposes `Session`.
- `IPostgresTransactionContext` — PostgreSQL-specific, exposes `Transaction`.
- `IMongoTransactionManager` / `IPostgresTransactionManager` — Creates transactions via `BeginTransactionAsync`.

### 3.2 Future Dependency: OnCommitted Callback

The Transactional library does not currently support registering a callback for when a transaction is successfully committed. Emit's design **assumes this capability will exist**. Specifically:

- The synchronous `Produce` method with a delivery callback requires the callback to fire when `ITransactionContext.CommitAsync()` succeeds.
- This is documented as a dependency on a future Transactional feature.

### 3.3 Optional Transaction Usage

Transactions are optional. A user may call `Produce`/`ProduceAsync` without a transaction context for edge cases where:
- They have no transaction on hand.
- They cannot create one.
- The event is non-critical and they want a best-effort enqueue.

In this case, the outbox entry is written directly (not within a transaction). The user should not need a separate registration or API to handle this case.

---

## 4. Outbox Schema

The outbox entry model is provider-agnostic. The persistence providers implement the actual storage, but the schema is defined by the core library.

### 4.1 Outbox Entry Fields

| Field | Type | Description |
|-------|------|-------------|
| **Id** | Provider-native (ObjectId / UUID) | Unique identifier for the entry. |
| **ProviderId** | `string` | Identifier of the outbox provider (e.g., `"kafka"`). Each provider exposes a well-known ID. Used to dispatch entries to the correct provider during processing. |
| **RegistrationKey** | `string` | The key name used during `AddKafka(...)` registration. A well-known sentinel value (e.g., `"__default__"`) indicates non-keyed registration. Used by the worker to resolve the correct real producer. |
| **GroupKey** | `string` | Determines sequential processing order. Entries in the same group are processed strictly in sequence. For Kafka: `cluster_identifier:topic_name`. The provider decides the grouping value. |
| **Sequence** | `long` | Monotonically increasing number within a group, assigned at enqueue time. Guarantees strict ordering. See §4.2 for generation strategy. |
| **Status** | `enum` | One of: `Pending`, `Completed`, `Failed`. See §4.3 for lifecycle. |
| **EnqueuedAt** | `DateTime` (UTC) | When the entry was added to the outbox. |
| **CompletedAt** | `DateTime?` (UTC) | When the entry was successfully processed. `CompletedAt - EnqueuedAt` provides time-in-outbox metrics. |
| **RetryCount** | `int` | Number of times processing has been attempted. |
| **LastAttemptedAt** | `DateTime?` (UTC) | When the last processing attempt occurred. Used by retry backoff calculations. |
| **LatestError** | `string?` | The latest error message. Set on failure, cleared (nulled) on success. Provides quick visibility into why an entry is stuck without scanning the attempts list. |
| **Attempts** | `List<OutboxAttempt>` | List of failed processing attempts. Capped to a configurable maximum (both MongoDB and PostgreSQL support efficient array/JSONB trimming). **Not populated on success** to avoid bloat. |
| **Payload** | `byte[]` | Opaque binary blob containing provider-specific data. Only the provider knows how to serialize/deserialize this. Serialized using MessagePack (see §4.4). For Kafka: contains key bytes, value bytes, headers, topic name, partition, etc. |
| **Properties** | `Dictionary<string, string>` | Arbitrary key-value metadata populated by the provider at enqueue time. Not used by the core engine for processing logic — purely for observability, metrics, and dashboards. For Kafka: `topic`, `cluster`, `valueType`, etc. |
| **CorrelationId** | `string?` | Optional trace/correlation ID for end-to-end tracing. Allows users to trace an outbox entry back to the business operation that created it. |

### 4.2 Sequence Number Generation

The sequence number must be globally ordered within a group across all application replicas.

**PostgreSQL:** Native `BIGINT GENERATED ALWAYS AS IDENTITY` or a database sequence. Zero overhead.

**MongoDB:** Atomic counter in a dedicated collection. One document per group key. Enqueue performs `FindOneAndUpdate` with `$inc` on the counter document, returning the next sequence number. This is one extra round-trip per enqueue within the same database.

Rationale for MongoDB counter collection:
- Timestamps are unreliable due to clock skew across replicas.
- `ObjectId` ordering is only approximate across machines.
- In-process counters (`Interlocked.Increment`) only guarantee ordering within a single process, not across replicas.
- The atomic counter is the only correct solution for strict cross-replica ordering.

### 4.3 Status Lifecycle

```
Pending ──→ Completed    (successful processing)
   │
   └──→ Failed           (processing error, retries exhausted or permanent failure)
           │
           └──→ Pending   (retry eligible based on backoff policy — conceptual, 
                           the status remains Failed but becomes retryable based
                           on LastAttemptedAt + backoff delay)
```

There is **no `Processing` status**. Since V1 uses a single active worker via global lease (§5), there is no concurrent access to entries. The worker that holds the global lease owns all entries. No per-entry lease or status transition to `Processing` is needed.

If the worker dies mid-flight, the entry remains `Pending` or `Failed`. The next worker picks it up naturally.

### 4.4 Payload Serialization Format

The provider-specific payload is serialized as `byte[]` using **MessagePack**.

Rationale (MessagePack over Protobuf):
- **No schema definition required.** Protobuf requires `.proto` files or annotated contracts. MessagePack works with plain C# objects and attributes, which is more natural for a library where each provider defines its own internal payload shape.
- **Faster serialization/deserialization.** MessagePack is consistently benchmarked as one of the fastest binary serializers for .NET.
- **Compact output.** Comparable to Protobuf for typical payloads.
- **Native .NET support.** The `MessagePack-CSharp` library is mature, well-maintained, and widely used in .NET.

The core library provides the MessagePack dependency. Providers define their payload types with MessagePack attributes.

### 4.5 Outbox Attempt Fields

Each entry in the `Attempts` list contains:

| Field | Type | Description |
|-------|------|-------------|
| **AttemptedAt** | `DateTime` (UTC) | When this attempt occurred. |
| **Reason** | `string` | Categorized reason for failure (e.g., `"BrokerUnreachable"`, `"SerializationError"`, `"ProducerNotRegistered"`). May be acted upon programmatically. |
| **Message** | `string` | Human-readable error message. |
| **ExceptionType** | `string` | Full type name of the exception (e.g., `"Confluent.Kafka.ProduceException"`). |

The attempts list is capped to a configurable maximum size. Both MongoDB (`$push` + `$slice`) and PostgreSQL (JSONB array trimming in a single UPDATE) support this efficiently.

Full exception objects are **not** stored — they are not reliably serializable and can be arbitrarily large. The exception type, message, and stack trace are logged via `ILogger` at the time of failure.

### 4.6 MongoDB Sharding

The `GroupKey` field serves as the sharding key for the outbox collection in MongoDB. This ensures that:
- Queries filtered by group key are routed to the correct shard.
- Processing a single group's entries does not cause scatter-gather operations.
- All entries in the same group reside on the same shard, enabling efficient sequential reads.

This mirrors the `TenantId` sharding strategy used in the recurringthings library. All MongoDB queries against the outbox **must** include the `GroupKey` as the first filter.

---

## 5. Outbox Worker

The outbox worker is implemented as a `BackgroundService` (`IHostedService`). It is responsible for polling the outbox, processing entries in order, and updating their status.

### 5.1 V1 Concurrency Model: Single Active Worker with Global Lease

**Design decision:** V1 uses a single active worker at a time, enforced by a global distributed lease in the outbox database. Multi-worker partitioned processing (range-based hashing, consumer-group-style rebalancing) is deferred to a future version.

**Rationale:** Multi-worker coordination requires a consensus protocol (leader election, rebalancing, heartbeating) that is extremely complex to implement correctly on top of a database. A single worker can process a massive throughput of events, and V1 prioritizes correctness and simplicity. The worker can still parallelize across groups in-memory.

**Future versions** may introduce range-partitioned multi-worker processing where workers hash group keys into ranges and acquire locks for ranges.

### 5.2 Global Lease

A single row/document in the database acts as the global lease.

**Fields:** `WorkerId` (string), `LeaseUntil` (DateTime UTC).

**Acquisition:** One atomic operation:
```
UPDATE lease SET WorkerId = X, LeaseUntil = now + duration
WHERE LeaseUntil < now OR WorkerId = X
```

If the update affects one row, Worker X holds the lease. If zero rows affected, another worker holds it. Worker X sleeps and retries.

This is a single atomic operation with no race condition.

### 5.3 Lease Renewal

The worker runs a background timer that renews the lease at a configurable interval, well under the lease duration.

**Configuration (all configurable with sensible defaults):**
- **Lease duration**: e.g., 60 seconds.
- **Renewal interval**: e.g., 15 seconds.
- **Validation**: Renewal interval must be significantly less than lease duration.

**Renewal failure handling:**
1. The renewal timer attempts the atomic update: SET `LeaseUntil = now + duration` WHERE `WorkerId = X`.
2. If the renewal fails, the worker **immediately fires a `CancellationToken`** shared with all processing tasks.
3. All components listening to the cancellation token have the remaining time until the lease expires (e.g., 45 seconds if renewal failed at T=15 of a 60-second lease) to finish their current work and stop.
4. If something doesn't stop in time, the lease expires and the next worker takes over. The worst case is a brief overlap where the old worker finishes writing `Completed` on an already-processed entry — harmless.

This is a **critical path** that must be implemented correctly.

### 5.4 Polling Interval

The worker polls the outbox at a configurable interval (default: 5 seconds). This interval governs how frequently the worker checks for new work when:
- The worker holds the lease and the outbox is empty (no pending entries).
- The worker has finished processing a batch and is checking for more work.

**Smart sleep on lease contention:** When a worker without the lease attempts to acquire it and fails, the lease query returns the current `LeaseUntil` value. The worker knows exactly how long the lease is held. Instead of retrying every 5 seconds, it sleeps for `LeaseUntil - now + small jitter` and retries immediately after. This avoids pointless polling when the worker already knows it cannot acquire the lease.

**After processing a batch with remaining work:** If the worker processed a full batch (batch size reached), it should immediately loop back for the next batch without waiting for the polling interval — there is likely more work queued.

### 5.5 Processing Flow

**Step 1: Acquire global lease.** Single atomic operation. If another worker holds it, sleep and retry.

**Step 2: Query group heads.** One query: "For each distinct group, give me the first non-completed entry ordered by sequence." Returns one entry per group — the head of each group's queue.

**Step 3: Evaluate group heads.** For each group head:
- If `Pending`: eligible for processing.
- If `Failed` and backoff has elapsed (based on `LastAttemptedAt` + retry policy): eligible for processing.
- If `Failed` and backoff has **not** elapsed: skip this group.
- Check in-memory circuit breaker (§5.7): if the circuit is open for this group, skip it.

Collect the list of eligible group keys.

**Step 4: Batch query.** Single query: "Give me the next N entries WHERE group key IN (eligible groups) AND status != Completed, ORDER BY group key, sequence." N is a configurable batch size. The database handles ordering.

**Step 5: In-memory processing.** Partition the batch by group. Process groups **in parallel** (async tasks, since groups are independent). Within each group, process entries **strictly sequentially**:

For each entry in sequence order:
1. Dispatch to the provider (e.g., Kafka provider produces the message using the real Confluent producer).
2. **On success**: Immediately update the entry to `Completed` with `CompletedAt = now`, clear `LatestError`. **This write must be confirmed before processing the next entry** — this prevents time-travel if the worker dies.
3. **On failure**: Update the entry to `Failed`, increment `RetryCount`, set `LastAttemptedAt`, record attempt in `Attempts` list (capped), set `LatestError`. **Stop processing this group for the current cycle.** Discard any remaining entries in the batch for this group.

**Step 6: Loop.** If groups still have remaining work beyond the batch, loop back to Step 4 for the next batch. Exclude any group that failed this cycle.

**Step 7: Lease renewal** runs continuously on a background timer throughout all steps (§5.3).

### 5.6 Handling Stale State from Previous Workers

Since V1 is single-worker, any entry with a state that implies in-progress work is guaranteed to be abandoned by a dead/previous worker. The new lease holder can safely process it. There is no `Processing` status that needs to be recovered.

### 5.7 Circuit Breaker

A group-level circuit breaker is held **in-memory** by the worker. It prevents wasteful retries against groups that are structurally broken (e.g., the producer registration was removed, the broker is permanently down).

**Behavior:**
- If a group fails N consecutive times (configurable threshold), the circuit opens.
- While open, the worker skips the group entirely for a configurable cooldown duration.
- After cooldown, the circuit moves to half-open: the worker attempts one entry. If it succeeds, the circuit closes. If it fails, the circuit reopens with the cooldown.

**In-memory by design:**
- The circuit breaker is a performance optimization, not a correctness mechanism.
- If it resets on restart, the worst case is a few wasted retries before it trips again.
- Each replica builds its own view independently.
- Zero additional database round-trips. Zero race conditions.

Circuit breaker configuration is part of the `ResiliencePolicy` (§6).

### 5.8 Group Blocking on Unresolvable Entries

If the worker encounters an outbox entry whose provider or producer registration is missing (e.g., `IProducer<string, PaymentProcessed>` was removed from the codebase), the entry **cannot be processed**.

**Behavior:**
- The entry is treated as a normal processing failure. The retry policy and circuit breaker apply.
- The group is **blocked** — no entries after the unresolvable one can be processed, preserving ordering.
- This is correct because in most cases, a topic has a single schema. If the group is `cluster:topic`, the next entry would fail for the same reason.

**Resolution paths:**
- The user redeploys with the registration restored → next retry succeeds → group unblocks and drains in order.
- The user manually intervenes (future: an administrative API to remove/acknowledge poisoned entries).

### 5.9 Completed Entries Cleanup

A separate background task (also a `BackgroundService`) periodically purges completed entries older than a configurable retention period.

**Configuration:**
- **Retention period**: Configurable. How long completed entries are kept (e.g., 7 days).
- **Cleanup interval**: Configurable, defaults to 1 hour.

The cleanup task uses the `CompletedAt` field to identify entries eligible for deletion.

---

## 6. Resilience Policy

### 6.1 ResiliencePolicy / ResiliencePolicyBuilder

A single `ResiliencePolicyBuilder` class that configures both retry and circuit breaker behavior. Reused at every registration level.

**Retry configuration:**
- Maximum retry count.
- Backoff strategy: exponential backoff, fixed interval (standard patterns — we are not reinventing the wheel).
- Maximum backoff delay (cap).

**Circuit breaker configuration:**
- Failure threshold (consecutive failures before the circuit opens).
- Cooldown duration (how long the circuit stays open before moving to half-open).

### 6.2 Override Hierarchy

The resilience policy can be set at multiple levels. The most specific non-null value wins:

1. **Producer-level** (most specific) — configured in `AddProducer<TKey, TValue>(...)`.
2. **Provider-level** — configured in `AddKafka(...)`.
3. **Global-level** — configured in `AddEmit(...)`.
4. **Built-in default** — sensible exponential backoff and circuit breaker defaults.

No level is mandatory. The library works out of the box with zero resilience configuration, using the built-in defaults.

---

## 7. Dependency Injection API

### 7.1 Registration Shape

```csharp
services.AddEmit(builder =>
{
    // Global resilience policy (optional)
    builder.ConfigureResilience(policy => { ... });

    // Exactly one persistence provider (required)
    builder.UseMongoDb((provider, options) =>
    {
        options.ConnectionString = "mongodb://localhost:27017";
        options.DatabaseName = "myapp";
        options.CollectionName = "outbox";       // outbox collection name
    });
    // OR
    builder.UsePostgreSql((provider, options) =>
    {
        options.ConnectionString = "Host=localhost;Database=myapp;...";
    });

    // One or more outbox providers
    // Default (unnamed) Kafka — producers registered as standard DI services
    builder.AddKafka((provider, kafka) =>
    {
        kafka.SchemaRegistry = new SchemaRegistryConfig { ... };  // Confluent's config object

        // Provider-level resilience (optional)
        kafka.ConfigureResilience(policy => { ... });

        kafka.AddProducer<string, OrderCreated>(producer =>
        {
            producer.ProducerConfig = new ProducerConfig { ... };  // Confluent's config object
            producer.KeySerializer = ...;   // Confluent's ISerializer<T>
            producer.ValueSerializer = ...;

            // Producer-level resilience (optional)
            producer.ConfigureResilience(policy => { ... });
        });

        kafka.AddProducer<string, PaymentProcessed>(producer =>
        {
            producer.ProducerConfig = new ProducerConfig { ... };
            producer.KeySerializer = ...;
            producer.ValueSerializer = ...;
        });
    });

    // Named Kafka — producers registered as keyed DI services
    builder.AddKafka("analytics", (provider, kafka) =>
    {
        kafka.SchemaRegistry = new SchemaRegistryConfig { ... };

        kafka.AddProducer<string, string>(producer =>
        {
            producer.ProducerConfig = new ProducerConfig { ... };
        });
    });
});
```

### 7.2 Producer Resolution by the User

- **Default (unnamed) `AddKafka`**: Producers are registered as **standard** DI services. The user injects `IProducer<string, OrderCreated>` via normal constructor injection. No `[FromKeyedServices]` needed. This is the common case and must be zero-friction.
- **Named `AddKafka("analytics", ...)`**: Producers are registered as **keyed** DI services. The user resolves with `[FromKeyedServices("analytics")] IProducer<string, string>`.

### 7.3 Internal Registration Key

Internally, all registrations (named and unnamed) use a key. The unnamed/default registration uses a well-known sentinel constant (e.g., `"__default__"`). This sentinel is stored in the outbox entry's `RegistrationKey` field.

When the worker processes an entry:
- If `RegistrationKey` is the sentinel: resolve the real producer as a standard (non-keyed) service.
- Otherwise: resolve as a keyed service using the registration key.

**Critical implementation note:** The DI registration code path must be shared between keyed and non-keyed registrations. The only branching should be at the final DI registration call and the final resolution call. There should **not** be near-duplicate registration logic for the two cases.

### 7.4 What Gets Registered

For each `AddProducer<TKey, TValue>` call, the library registers:

**User-facing (fake) producer:**
- `IProducer<TKey, TValue>` — Our implementation that serializes the call (using the configured `ISerializer<TKey>` and `ISerializer<TValue>`) into an outbox entry and writes it to the outbox within the active `ITransactionContext` (or directly if no transaction).
- Registered as a standard service (default Kafka) or keyed service (named Kafka).

**Worker-side (real) producer factory:**
- A factory that lazily constructs the real Confluent `IProducer<byte[], byte[]>` on first use. Since serialization happens at enqueue time, the worker always produces raw bytes regardless of the original `TKey, TValue`.
- Keyed internally by the registration key. One real producer per Kafka registration (per cluster), not per `TKey, TValue`.
- **Lazy construction is critical:** The real Confluent producer spawns background threads (librdkafka) and attempts broker connections on `Build()`. In tests and in production when the outbox is empty, no broker connection should be attempted until the worker actually needs to produce.

### 7.5 `IServiceProvider` Parameter

All configuration callbacks include an `IServiceProvider` parameter (as in recurringthings), allowing users to resolve services registered before `AddEmit` (e.g., `IOptions<T>`, `IConfiguration`).

---

## 8. Kafka Provider (Emit.Provider.Kafka)

### 8.1 Producer Interface

Emit defines its own `IProducer<TKey, TValue>` interface that **mirrors** the shape of Confluent's `IProducer<TKey, TValue>` but does **not** implement Confluent's interface. Only methods that make sense for an outbox are exposed.

**Minimum required methods:**

- `void Produce(string topic, Message<TKey, TValue> message, Action<DeliveryReport<TKey, TValue>> deliveryHandler = null)` — Fire-and-forget with optional callback. The callback fires when `ITransactionContext.OnCommitted` is invoked (future Transactional feature). Without a transaction, the callback fires after the outbox write succeeds.
- `Task<DeliveryResult<TKey, TValue>> ProduceAsync(string topic, Message<TKey, TValue> message, CancellationToken cancellationToken = default)` — Async enqueue. Returns as soon as the outbox write succeeds within the transaction. The result indicates "accepted into outbox", **not** "delivered to Kafka".

**Not included (not applicable to outbox):** `Flush`, `Poll`, `InitTransactions`, `BeginTransaction`, `CommitTransaction`, `AbortTransaction`, `SendOffsetsToTransaction`.

### 8.2 Confluent Dependencies

`Emit.Provider.Kafka` takes a **direct dependency** on the `Confluent.Kafka` package. It reuses Confluent's types directly:

- `ProducerConfig` — Broker connection configuration. We do not redesign this.
- `SchemaRegistryConfig` — Schema registry configuration.
- `ISerializer<T>` / `IAsyncSerializer<T>` — Serializers configured by the user.
- `Message<TKey, TValue>` — The message type the user constructs.
- `DeliveryReport<TKey, TValue>` / `DeliveryResult<TKey, TValue>` — Return/callback types.

### 8.3 Serialization at Enqueue Time

When the user calls `Produce` or `ProduceAsync`, the fake producer:
1. Invokes the configured `ISerializer<TKey>` and `ISerializer<TValue>` to produce `byte[]` for key and value.
2. Packages the raw bytes, topic name, headers, partition (if specified), and other Kafka-specific metadata into a Kafka payload object.
3. Serializes that payload using MessagePack into the outbox entry's `Payload` field.
4. Populates the `Properties` dictionary with observability metadata (topic, cluster, value type name, etc.).
5. Sets the `GroupKey` to `cluster_identifier:topic_name`.
6. Writes the outbox entry within the active `ITransactionContext`, or directly if no transaction.

### 8.4 Worker-Side Processing

When the worker picks up a Kafka outbox entry:
1. Deserializes the `Payload` (MessagePack) to recover the Kafka-specific data (topic, key bytes, value bytes, headers, partition).
2. Resolves the correct real Confluent `IProducer<byte[], byte[]>` via the factory, using the `RegistrationKey` from the outbox entry.
3. Produces the raw bytes to Kafka using the real producer.
4. Reports success or failure back to the worker.

### 8.5 Multi-Cluster Support

Each `AddKafka(...)` call represents a distinct Kafka cluster with its own `ProducerConfig`, `SchemaRegistryConfig`, and set of producers. The registration key (default or named) differentiates them throughout the system.

---

## 9. CI/CD

### 9.1 CI Workflow (`ci.yml`)

Triggers on push to `main` and pull requests to `main`.

**Jobs:**

**`test` job:**
1. Checkout, setup .NET 10.0.
2. `dotnet restore`.
3. `dotnet build --no-restore`.
4. `dotnet test --no-build --filter 'Category!=Integration'` — unit tests only.
5. `dotnet format --verify-no-changes` — formatting check.

**`integration-test` job:**

Service containers:
- **MongoDB**: `mongo:7`, port 27017.
- **PostgreSQL**: `postgres:16`, port 5432.
- **No Kafka container.** Integration tests mock the Confluent producer via the factory. The real Confluent producer is never built in tests, so no broker connection is attempted.

Steps:
1. Checkout, setup .NET 10.0.
2. `dotnet restore`.
3. `dotnet build --no-restore`.
4. `dotnet test --no-build --filter 'Category=Integration'` with environment variables:
   - `MONGODB_CONNECTION_STRING=mongodb://localhost:27017`
   - `POSTGRES_CONNECTION_STRING=Host=localhost;Database=emit_test;Username=postgres;Password=password`

### 9.2 Publish Workflow (`publish.yml`)

Triggers on tags matching `v[0-9]+.[0-9]+.[0-9]+*`.

Steps:
1. Checkout, setup .NET 10.0.
2. `dotnet restore`.
3. `dotnet build -c Release --no-restore`.
4. `dotnet test -c Release --no-build --filter 'Category!=Integration'` — unit tests gate the publish.
5. Extract version from tag.
6. `dotnet pack -c Release --no-build -p:Version=<extracted>`.
7. `dotnet nuget push '**/*.nupkg'` to NuGet.org.

---

## 10. Testing Strategy

### 10.1 V1 Scope

**V1 does NOT include an exhaustive test suite.** The deliverable is:

- Test project scaffolding (unit test project, integration test project).
- Base integration test class with the provider-inheritance pattern (same as recurringthings).
- CI workflow wired up and passing with minimal placeholder tests.
- MongoDB and PostgreSQL integration test classes inheriting from the base.

Exhaustive tests will be written later, after manual review and design stabilization. This is an explicit decision to avoid writing 1000 tests before the design is finalized.

### 10.2 Test Conventions

Follow the recurringthings CLAUDE.md conventions:

- **xUnit** as the testing framework.
- **Moq** for mocking.
- **xUnit `Assert`** for assertions (not FluentAssertions — it is commercial).
- **Naming**: Given-When-Then format (e.g., `GivenPendingEntry_WhenWorkerProcesses_ThenStatusIsCompleted`).
- **AAA pattern**: All tests must have `// Arrange`, `// Act`, `// Assert` comments.
- **Tests are ground truth**: If a test fails, it reveals a bug in the implementation — do not modify tests to work around implementation bugs.
- **Integration tests** use shared base class inherited by MongoDB and PostgreSQL test classes.
- **Kafka producing is mocked** — the real Confluent `IProducer` is replaced with a mock via the factory pattern. Tests verify outbox behavior, not Kafka connectivity.

### 10.3 Integration Test Environment Variables

- `MONGODB_CONNECTION_STRING` — MongoDB connection string for integration tests.
- `POSTGRES_CONNECTION_STRING` — PostgreSQL connection string for integration tests.

---

## 11. Code Conventions

Carry over from recurringthings CLAUDE.md, adapted for Emit:

### 11.1 General

- **No hardcoded `TimeSpan` values anywhere in the codebase.** Every duration (lease time, renewal interval, polling interval, retry backoff, cleanup interval, retention period, circuit breaker cooldown, etc.) must be exposed as a configurable option with a sensible default. All configurable durations must be validated at registration time using FluentValidation with enforced minimums/maximums where applicable (e.g., lease duration minimum 30 seconds, polling interval minimum 1 second). If a user provides a value that would break the system, the validation fails with a clear error message at startup — not at runtime.
- File-scoped namespaces.
- Using directives inside namespace.
- Collection expressions `[]` instead of `Array.Empty` or `new[]`.
- Primary constructors preferred.
- `nameof()` instead of hardcoded strings in exceptions and logging.
- **Internal by default.** Use `internal` for implementation details. Only `public` for types that are part of the public API (domain models, interfaces, engine, configuration). Use `InternalsVisibleTo` for test access.
- **FluentValidation** for validation rules on configuration and complex objects.
- **Convention over attributes** — configure naming conventions globally (camelCase for JSON, BSON conventions for MongoDB) rather than per-property attributes.
- Run `dotnet format` before every commit.

### 11.2 DateTime Handling

- All DateTime fields stored in UTC.

### 11.3 Logging

- Use `ILogger` exhaustively throughout the library.
- **Error**: Processing failures, lease acquisition failures, unresolvable entries.
- **Warning**: Circuit breaker trips, retry attempts, approaching max retries, lease renewal approaching deadline.
- **Information**: Worker started/stopped, lease acquired/released, batch processing summaries.
- **Debug**: Individual entry processing, state transitions, outbox writes.
- **Trace**: Query details, serialization/deserialization steps, timer ticks.

A user setting their logger to verbose/trace should be able to see exactly what the library is doing at every step.

### 11.4 XML Documentation

Write `<summary>`, `<param>`, `<returns>`, `<exception>` on all public classes, methods, and properties.

### 11.5 MongoDB Specifics

- `GroupKey` is the sharding key. All MongoDB queries must include `GroupKey` as the first filter.
- Use BSON conventions / class maps instead of `[BsonElement]` attributes.
- Use a dedicated counter collection for sequence number generation (§4.2).

---

## 12. Dependencies

### 12.1 Core (Emit)

| Dependency | Purpose |
|------------|---------|
| `Transactional.Abstractions` | `ITransactionContext` interface |
| `Microsoft.Extensions.Hosting.Abstractions` | `BackgroundService` for the worker |
| `Microsoft.Extensions.DependencyInjection.Abstractions` | DI registration |
| `Microsoft.Extensions.Logging.Abstractions` | `ILogger` |
| `FluentValidation` | Configuration validation |
| `MessagePack` | Payload serialization |

### 12.2 Emit.Provider.Kafka

| Dependency | Purpose |
|------------|---------|
| `Emit` (core) | Core abstractions |
| `Confluent.Kafka` | `ProducerConfig`, `ISerializer<T>`, `Message<TKey, TValue>`, `DeliveryReport`, real `IProducer<byte[], byte[]>` |
| `Confluent.SchemaRegistry` | `SchemaRegistryConfig` |

### 12.3 Emit.Persistence.MongoDB

| Dependency | Purpose |
|------------|---------|
| `Emit` (core) | Core abstractions |
| `Transactional.MongoDB` | `IMongoTransactionManager`, `IMongoTransactionContext` |
| `MongoDB.Driver` | MongoDB client |

### 12.4 Emit.Persistence.PostgreSQL

| Dependency | Purpose |
|------------|---------|
| `Emit` (core) | Core abstractions |
| `Transactional.PostgreSQL` | `IPostgresTransactionManager`, `IPostgresTransactionContext` |
| `Microsoft.EntityFrameworkCore` | PostgreSQL persistence (preferred over raw Npgsql) |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | EF Core PostgreSQL provider |

### 12.5 Test Dependencies

| Dependency | Purpose |
|------------|---------|
| `xunit` | Test framework |
| `Moq` | Mocking |
| `Microsoft.NET.Test.Sdk` | Test runner |

---

## 13. Open Items / Future Versions

The following items were discussed and explicitly deferred:

| Item | Rationale |
|------|-----------|
| **Multi-worker partitioned processing** | Requires consensus protocol (leader election, rebalancing, heartbeating). Extremely complex to implement correctly on a database. Deferred to V2+. The design explored range-based hashing with consistent hashing, worker registries, and generation-based rebalancing — all preserved as future design direction. |
| **`ITransactionContext.OnCommitted` callback** | Required for `Produce` delivery callbacks. Depends on a future enhancement to the Transactional library. |
| **Administrative API for poisoned entries** | Manual intervention for permanently failed entries blocking a group. The current resolution path is redeployment with the missing registration restored. |
| **Additional outbox providers** | e.g., RabbitMQ. The core is designed to be provider-agnostic, so adding new providers should be straightforward. |
| **Keyed service resolution for additional providers** | Same pattern as Kafka — named registrations use keyed DI, unnamed use standard DI. |

---

## 14. Glossary

| Term | Definition |
|------|------------|
| **Outbox** | A database table/collection that stores pending operations to be performed against external systems. |
| **Outbox entry** | A single record in the outbox representing one operation (e.g., one Kafka message to produce). |
| **Group** | A logical grouping of outbox entries that must be processed in strict sequence order. For Kafka: `cluster:topic`. |
| **Provider** | An outbox provider that knows how to process entries for a specific external system (e.g., Kafka). |
| **Persistence provider** | A database-specific implementation for storing and querying outbox entries (MongoDB or PostgreSQL). |
| **Global lease** | A distributed lock mechanism ensuring only one worker processes the outbox at a time (V1). |
| **Circuit breaker** | An in-memory mechanism that stops retrying a group after repeated consecutive failures, with a cooldown before retrying. |
| **Sentinel key** | A well-known constant (e.g., `"__default__"`) stored in the outbox entry to indicate the producer was registered without a named key. |