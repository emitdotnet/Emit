# TODO

## Daemon System

- **Custom daemon sample** — Provide a sample project demonstrating how to build a custom `IDaemonAgent` that plugs into the leader election and daemon coordination system. Currently `OutboxDaemon` is the only implementation and it's internal. Users need a clear example showing registration, the assignment lifecycle callbacks (`StartAsync`/`StopAsync`), and how to react to the `assignmentToken` cancellation.

- **Review internal visibility for extensibility** — `IDaemonAgent` and related types may need to be made public (or have their accessibility adjusted) so users can actually implement custom daemons. Audit the daemon registration path end-to-end and open up whatever is needed without exposing implementation details.

## Node Identity

- **Static node ID assigned at startup** — The application should generate a single `nodeId` once at startup and keep it stable for the lifetime of the process. This ID should be propagated as a dimension on all metrics instruments and as a tag on all OTel activities, so every observation is attributable to its origin node. The same ID should be used by the node heartbeat worker instead of being generated at heartbeat registration time. Consider whether it also belongs as a field on outbox entries (e.g. to track which node enqueued a message). The ID should be surfaced through a well-known service (e.g. `INodeIdentity`) so all subsystems reference a single source of truth.

## PostgreSQL

- **Review outbox behavior with and without an explicit transaction** — The transactional outbox must work correctly whether the caller uses an explicit DB transaction (begin → produce → commit) or simply adds business data and the outbox entry to the `DbContext` and calls `SaveChangesAsync` directly. Audit the EF Core outbox enqueue path end-to-end for both flows, clarify which patterns are supported in docs, and ensure the sample projects demonstrate the idiomatic approach for each persistence backend.

- **Evaluate stored functions vs inline SQL** — Explore the trade-offs of moving PostgreSQL queries (outbox group heads, CAS leader election, node cleanup, daemon assignment upserts) into database functions rather than embedding SQL strings in application code. Consider versioning, migration tooling, debuggability, and whether the performance characteristics change.

## Consumer Middleware

- **FluentValidation convenience layer** — The validation middleware is functional but requires users to implement `IMessageValidator<T>` manually. Add an optional `Emit.FluentValidation` package (or integration point) that bridges FluentValidation's `IValidator<T>` into the Emit validation pipeline so users can register FluentValidation validators directly.

## Kafka Producers

- **Support tombstone events (null values) and null keys** — Kafka uses null-valued messages as tombstones to signal deletion in log-compacted topics. Producers should support sending messages with a null value and/or a null key. This likely requires allowing `TValue` (and `TKey`) to be nullable in `IEventProducer<TKey, TValue>` and `EventMessage<TKey, TValue>`, and ensuring the serialization pipeline handles nulls correctly.
