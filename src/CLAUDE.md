# src/

## Subdirectories

| Directory | What | When to read |
| --------- | ---- | ------------ |
| `Emit.Abstractions/` | Shared abstractions: interfaces, models, pipeline contracts, and extension methods | Implement new providers or persistence backends, understand core contracts |
| `Emit/` | Core library: configuration, DI registration, pipeline builder, background workers | Implement core features, modify outbox processing, or change configuration |
| `Emit.Mediator/` | In-process mediator: request/response dispatching with middleware pipeline | Implement mediator handlers, configure mediator middleware |
| `Emit.OpenTelemetry/` | OpenTelemetry integration for Emit metrics and tracing | Register Emit metrics and tracing with OpenTelemetry |
| `Emit.Kafka/` | Kafka provider: producer/consumer implementations, serialization, DI registration | Add Kafka features, configure producers/consumers, or troubleshoot Kafka delivery |
| `Emit.Kafka.AvroSerializer/` | Avro serializer extension for Kafka topic builder with schema registry support | Configure Avro serialization for Kafka topics |
| `Emit.Kafka.JsonSerializer/` | JSON Schema serializer extension for Kafka topic builder with schema registry support | Configure JSON Schema serialization for Kafka topics |
| `Emit.Kafka.ProtobufSerializer/` | Protobuf serializer extension for Kafka topic builder with schema registry support | Configure Protobuf serialization for Kafka topics |
| `Emit.MongoDB/` | MongoDB persistence: repository, BSON mapping, collection schema, distributed locking | Implement MongoDB queries, configure collections, or troubleshoot persistence |
| `Emit.MongoDB.HealthChecks/` | ASP.NET Core health check for MongoDB connectivity | Add MongoDB health checks to an application |
| `Emit.EntityFrameworkCore/` | EF Core persistence: repository, model builder, table schema, distributed locking | Implement EF Core queries, configure entity model, or troubleshoot persistence |
| `Emit.EntityFrameworkCore.HealthChecks/` | ASP.NET Core health check for EF Core / PostgreSQL connectivity | Add EF Core health checks to an application |
| `Emit.Kafka.HealthChecks/` | ASP.NET Core health check for Kafka broker connectivity | Add Kafka health checks to an application |
| `Emit.Testing/` | Test helpers: MessageSink and SinkConsumer for capturing messages in integration tests | Write integration or end-to-end tests that assert on consumed messages |
