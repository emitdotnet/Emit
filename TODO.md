# TODO

## Distributed Locks

- **Expose expiration on `IDistributedLock`** — The lock handle does not surface the absolute expiration timestamp. Callers have no way to know when the lock will expire without tracking it themselves. Consider adding an `ExpiresAt` property (or similar) to `IDistributedLock`.

- **Add absolute-time overload to `ExtendAsync`** — `ExtendAsync(TimeSpan ttl)` only supports relative durations. Add an overload that accepts an absolute `DateTimeOffset` for scenarios where the caller needs the lock until a specific point in time.

## Daemon System

- **Custom daemon sample** — Provide a sample project demonstrating how to build a custom `IDaemonAgent` that plugs into the leader election and daemon coordination system. Currently `OutboxDaemon` is the only implementation and it's internal. Users need a clear example showing registration, the assignment lifecycle callbacks (`StartAsync`/`StopAsync`), and how to react to the `assignmentToken` cancellation.

- **Review internal visibility for extensibility** — `IDaemonAgent` and related types may need to be made public (or have their accessibility adjusted) so users can actually implement custom daemons. Audit the daemon registration path end-to-end and open up whatever is needed without exposing implementation details.
