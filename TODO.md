# TODO

## PostgreSQL

- **Review outbox behavior with and without an explicit transaction** — The transactional outbox must work correctly whether the caller uses an explicit DB transaction (begin → produce → commit) or simply adds business data and the outbox entry to the `DbContext` and calls `SaveChangesAsync` directly. Audit the EF Core outbox enqueue path end-to-end for both flows, clarify which patterns are supported in docs, and ensure the sample projects demonstrate the idiomatic approach for each persistence backend.

- **Evaluate stored functions vs inline SQL** — Explore the trade-offs of moving PostgreSQL queries (outbox group heads, CAS leader election, node cleanup, daemon assignment upserts) into database functions rather than embedding SQL strings in application code. Consider versioning, migration tooling, debuggability, and whether the performance characteristics change.
