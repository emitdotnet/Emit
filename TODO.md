# TODO

## PostgreSQL

- **Review outbox behavior with and without an explicit transaction** — The transactional outbox must work correctly whether the caller uses an explicit DB transaction (begin → produce → commit) or simply adds business data and the outbox entry to the `DbContext` and calls `SaveChangesAsync` directly. Audit the EF Core outbox enqueue path end-to-end for both flows, clarify which patterns are supported in docs, and ensure the sample projects demonstrate the idiomatic approach for each persistence backend.

- **Evaluate stored functions vs inline SQL** — Explore the trade-offs of moving PostgreSQL queries (outbox group heads, CAS leader election, node cleanup, daemon assignment upserts) into database functions rather than embedding SQL strings in application code. Consider versioning, migration tooling, debuggability, and whether the performance characteristics change.

## Kafka Startup Advisory

- **Design a startup advisory system** — Design and implement a provider-agnostic advisory that runs at application startup, inspects the registered configuration, and logs structured recommendations to help users identify misconfiguration, missing best-practice settings, or potentially dangerous setups. The advisory should be a DI singleton driven by descriptor objects registered at build time (one per producer topic, one per consumer group), so it has access to the full topology without relying on generic type parameters or internal builder state. The Kafka advisory should eventually replace `StartupDiagnosticsLogger` for consumer groups and extend it to cover producers (e.g. warning when an outbox producer has not set `Acks.All`).
