# Emit.Testing/

Test helpers for asserting on messages flowing through the Emit consumer pipeline.

## Files

| File | What | When to read |
| ---- | ---- | ------------ |
| `README.md` | Package description and usage overview | Understand what this package provides |
| `MessageSink.cs` | Thread-safe collector that captures consumed messages in order; pair with SinkConsumer and assert after waiting | Write integration or end-to-end tests that assert on consumed messages |
| `SinkConsumer.cs` | Generic IConsumer implementation that forwards every message to a registered MessageSink singleton | Register as the consumer in test DI configuration |
| `Emit.Testing.csproj` | Project file (DevelopmentDependency = true) | Configure package metadata or dependencies |
