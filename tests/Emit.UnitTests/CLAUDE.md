# Emit.UnitTests/

Consolidated unit tests for all Emit projects using xUnit, Moq, and Given-When-Then naming convention.

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `Emit.UnitTests.csproj` | Test project file with xUnit and Moq dependencies | Configure test dependencies |
| `MeasurementRecorder.cs` | Records the counter measurements one test makes on a meter, kept apart from other tests by a tag unique to the recorder | Assert what a component records in metrics |
| `GlobalUsings.cs` | Global using of the test helper namespace | Add a namespace every test file needs |
| `TestPipeline.cs` | Wraps a delegate into a pipeline, for terminals in unit tests | Give a unit test a pipeline terminal |
| `TestTransportContext.cs` | Minimal TransportContext for unit tests without a real transport | Build a consume context in a unit test |

## Subdirectories

| Directory | What | When to read |
| --------- | ---- | ------------ |
| `Emit/` | Core library unit tests: pipeline, consumer, DI, routing, tracing, error handling | Write or debug core library tests |
| `Emit.Abstractions/` | Abstractions unit tests: error handling, metrics, pipeline contracts | Write or debug abstractions-layer tests |
| `Emit.Mediator/` | Mediator unit tests: metrics, observer, DI registration | Write or debug mediator tests |
| `Emit.OpenTelemetry/` | OpenTelemetry integration unit tests | Write or debug OpenTelemetry registration tests |
| `Emit.Kafka/` | Kafka provider unit tests: consumer, DI, metrics, observability, serialization | Write or debug Kafka provider tests |
| `Emit.Kafka.AvroSerializer/` | Avro serializer unit tests | Write or debug Avro serializer tests |
| `Emit.Kafka.JsonSerializer/` | JSON Schema serializer unit tests | Write or debug JSON serializer tests |
| `Emit.Kafka.ProtobufSerializer/` | Protobuf serializer unit tests | Write or debug Protobuf serializer tests |
| `Emit.Kafka.HealthChecks/` | Kafka health check unit tests | Write or debug Kafka health check tests |
| `Emit.MongoDB/` | MongoDB persistence unit tests: repository, DI, BSON configuration | Write or debug MongoDB persistence tests |
| `Emit.MongoDB.HealthChecks/` | MongoDB health check unit tests | Write or debug MongoDB health check tests |
| `Emit.EntityFrameworkCore/` | EF Core persistence unit tests: repository, DI, model configuration | Write or debug EF Core persistence tests |
| `Emit.EntityFrameworkCore.HealthChecks/` | EF Core health check unit tests | Write or debug EF Core health check tests |
