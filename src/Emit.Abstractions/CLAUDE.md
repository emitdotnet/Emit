# Emit.Abstractions/

Shared abstractions for the Emit library: interfaces, models, pipeline contracts, and extension methods.

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `README.md` | Abstractions overview and package description | Understand what this package provides |
| `Emit.Abstractions.csproj` | Abstractions project file with Transactional dependency | Configure abstraction dependencies |
| `IOutboxRepository.cs` | Persistence contract for enqueueing, deleting, and fetching outbox entries | Implement a new persistence backend |
| `IOutboxProvider.cs` | Contract for providers that process outbox entries by delivering to external systems | Implement a new outbox provider |
| `IConsumer.cs` | Generic interface for handling consumed messages with a typed value | Implement consumer handlers |
| `IEventProducer.cs` | Generic interface for producing typed key-value messages | Implement a new producer or understand producing API |
| `EventProducerExtensions.cs` | Convenience overloads for IEventProducer accepting key, value, and headers directly | Understand producer extension API surface |
| `EventMessage.cs` | Provider-agnostic record with typed key, value, and optional string headers | Understand the message model passed to producers |
| `IDistributedLockProvider.cs` | Contract for acquiring distributed locks with TTL and timeout | Implement a new distributed lock backend |
| `IDistributedLock.cs` | Acquired lock handle supporting extend and release via disposal | Understand lock lifecycle |
| `DistributedLock.cs` | Default IDistributedLock implementation delegating to DistributedLockProviderBase | Understand lock handle behavior |
| `DistributedLockProviderBase.cs` | Abstract base with retry loop, exponential backoff, and jitter for lock acquisition | Implement a new lock provider or modify retry behavior |
| `IEmitContext.cs` | Scoped context interface providing access to the active transaction | Understand transaction context flow |
| `ITransactionContext.cs` | Base transaction context interface with commit, rollback, and state query members | Understand the core transaction contract before implementing a provider-specific context |
| `IRandomProvider.cs` | Interface for random number generation used in backoff jitter | Understand or mock randomness in tests |
| `Backoff.cs` | Abstract base with static factories (None, Exponential, Fixed) for retry delay strategies | Configure retry backoff in error policies or implement a custom backoff strategy |
| `ConsumerKind.cs` | Enum distinguishing Direct consumer handlers from content-based Router dispatchers | Understand consumer identity kind values used in tracing, metrics, and error handling |
| `ErrorActionBuilder.cs` | Base builder for configuring a terminal error action (DeadLetter, Discard) on a message | Understand how error actions are constructed inside error policy clauses |
| `MessageContext.cs` | Abstract envelope flowing through pipelines with MessageId, Timestamp, CancellationToken, Services, and Features | Understand pipeline context model |
| `ConsumeContext.cs` | Post-deserialization consume context carrying the typed message and parent TransportContext | Understand the consume pipeline context or implement consume middleware |
| `SendContext.cs` | Outbound context for producing messages with typed message data and mutable headers | Implement outbound middleware or understand producer pipeline |
| `TransportContext.cs` | Pre-deserialization transport context carrying raw bytes, headers, and provider metadata | Understand transport-level pipeline or implement transport middleware |
| `IFeatureCollection.cs` | Type-safe feature bag interface used by pipeline contexts for optional, extensible metadata | Add new pipeline features or access existing ones in middleware |
| `FeatureCollection.cs` | Dictionary-backed IFeatureCollection implementation used by message contexts | Understand how features are stored and retrieved on pipeline contexts |
| `IConsumerFlowControl.cs` | Interface for pausing and resuming message consumption while maintaining group membership | Implement circuit breakers or backpressure mechanisms |
| `IDeadLetterSink.cs` | Interface for producing messages to a dead letter destination by raw bytes and headers | Implement a DLQ producer or understand how failed messages are forwarded |
| `IInboundConfigurable.cs` | Leaf builder interface for registering typed inbound middleware (Use) and consumer filters (Filter) | Implement a new builder that supports per-consumer pipeline configuration |
| `IMessageValidator.cs` | Interface for validating incoming messages; return Fail for deterministic failures, throw for transient errors | Implement message validation or understand the validation contract |
| `IOutboundConfigurable.cs` | Leaf builder interface for registering typed outbound middleware (Use) on producer pipelines | Implement a new builder that supports per-producer pipeline configuration |
| `IResponseFeature.cs` | Feature interface for request-response patterns, allowing middleware to set a typed response | Implement request-response patterns or access response state in middleware |
| `MessageValidationResult.cs` | Result type for IMessageValidator with static Success singleton and Fail factory methods | Return or inspect validation outcomes in validators and validation middleware |
| `MessageValidationException.cs` | Exception thrown when a message fails validation; carries the list of validation error messages | Handle or inspect validation failures in error policies or dead-letter headers |
| `EmitEndpointAddress.cs` | Transport endpoint address as a URI with scheme, host, port, and entity name | Understand or construct endpoint addresses for producers and dead-letter sinks |
| `WellKnownHeaders.cs` | Well-known message header name constants for trace context and address propagation | Reference header names in middleware, producers, or consumers |
| `DeadLetterHeaders.cs` | Well-known dead-letter header constants and factory methods for failed messages | Implement dead-letter producers or read DLQ diagnostic headers |

## Subdirectories

| Directory | What | When to read |
| --------- | ---- | ------------ |
| `Daemon/` | Daemon assignment contracts: IDaemonAgent, IDaemonAssignmentPersistence, and assignment state model | Implement daemon-capable services or a new daemon persistence backend |
| `ErrorHandling/` | Error action enum, error clauses, error policies, and error policy builder | Implement error handling logic or configure error policies |
| `LeaderElection/` | Leader election contracts: ILeaderElectionService, ILeaderElectionPersistence, and heartbeat models | Implement leader election or a new leader election persistence backend |
| `Metrics/` | Metrics enrichment, lock metrics, and meter name constants | Add custom metric enrichment or reference meter names |
| `Models/` | OutboxEntry domain model | Create, query, or update outbox entries |
| `Observability/` | Observer interfaces for consume, produce, and outbox lifecycle events | Implement monitoring, logging, or custom instrumentation |
| `Pipeline/` | Middleware pipeline contracts: delegates, middleware interfaces, and builder interfaces | Implement middleware, understand pipeline architecture, or add new pipeline features |
| `Tracing/` | Activity source names, enrichment context, and activity enricher interface | Implement custom tracing enrichment or reference activity sources |
