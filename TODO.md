# TODO

## Daemon System

- **Custom daemon sample** — Provide a sample project demonstrating how to build a custom `IDaemonAgent` that plugs into the leader election and daemon coordination system. Currently `OutboxDaemon` is the only implementation and it's internal. Users need a clear example showing registration, the assignment lifecycle callbacks (`StartAsync`/`StopAsync`), and how to react to the `assignmentToken` cancellation.

- **Review internal visibility for extensibility** — `IDaemonAgent` and related types may need to be made public (or have their accessibility adjusted) so users can actually implement custom daemons. Audit the daemon registration path end-to-end and open up whatever is needed without exposing implementation details.

## Kafka Producers

- **Support tombstone events (null values) and null keys** — Kafka uses null-valued messages as tombstones to signal deletion in log-compacted topics. Producers should support sending messages with a null value and/or a null key. This likely requires allowing `TValue` (and `TKey`) to be nullable in `IEventProducer<TKey, TValue>` and `EventMessage<TKey, TValue>`, and ensuring the serialization pipeline handles nulls correctly.
